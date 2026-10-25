using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Admin;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Admin;

/// <summary>
/// The <see cref="IAdminStore"/> adapter: the cross-account reads the administrator's dashboard is built on.
/// </summary>
/// <remarks>
/// <b>Why every projection is materialised before it is shaped.</b> The projected records carry the names the REST
/// surface spells — <c>TerrariumStatus.Active</c> as <c>"Active"</c>, a metric as its <c>tempC</c> key — and those
/// two translations are domain calls (<c>Enum.ToString</c>, <c>MetricDictionary.Get</c>) that a query provider cannot
/// translate. Each query below therefore selects the columns and the enumerations into an anonymous type, and only
/// then maps to the response record. The alternative — projecting the string from the database — would put the wire
/// vocabulary in a SQL projection, where the next person to add a metric would not look for it.
/// <para>
/// The second reason the response record is never the projection is that a provider can only push an ordering or a
/// predicate into SQL if it can see through the projection, and a constructor call is opaque; building the record
/// before paging is what turns a two-row page into a table scan.
/// </para>
/// </remarks>
public sealed class EfAdminStore(SmartReptileDbContext db) : IAdminStore
{
    /// <inheritdoc />
    public async Task<AdminCounts> CountAsync(CancellationToken cancellationToken)
    {
        // Counted in the database rather than by reading rows: the dashboard's header must stay one round trip each
        // and must not depend on how many enclosures exist.
        var accounts = await db.Users.CountAsync(cancellationToken).ConfigureAwait(false);
        var accountsActive = await db.Users
            .CountAsync(user => user.DisabledAt == null, cancellationToken)
            .ConfigureAwait(false);

        // Deleted enclosures are excluded by the context's own query filter (DI-10), which is the definition of
        // "live" this figure uses everywhere else.
        var terrariums = await db.Terrariums.CountAsync(cancellationToken).ConfigureAwait(false);
        var terrariumsWithDevice = await db.Terrariums
            .CountAsync(terrarium => db.Devices.Any(device => device.TerrariumId == terrarium.Id), cancellationToken)
            .ConfigureAwait(false);

        var devices = await db.Devices.CountAsync(cancellationToken).ConfigureAwait(false);
        var devicesOnline = await db.Devices
            .CountAsync(device => device.Status == DeviceStatus.Online, cancellationToken)
            .ConfigureAwait(false);

        var alertsOpen = await db.Alerts
            .CountAsync(alert => alert.State != AlertState.Resolved, cancellationToken)
            .ConfigureAwait(false);
        var alertsOpenEnclosure = await db.Alerts
            .CountAsync(
                alert => alert.State != AlertState.Resolved && alert.Family == AlertFamily.Enclosure,
                cancellationToken)
            .ConfigureAwait(false);
        var alertsOpenSpecies = await db.Alerts
            .CountAsync(
                alert => alert.State != AlertState.Resolved && alert.Family == AlertFamily.Species,
                cancellationToken)
            .ConfigureAwait(false);
        var alertsCritical = await db.Alerts
            .CountAsync(
                alert => alert.State != AlertState.Resolved && alert.Severity == AlertSeverity.Critical,
                cancellationToken)
            .ConfigureAwait(false);

        var speciesProfiles = await db.SpeciesProfiles.CountAsync(cancellationToken).ConfigureAwait(false);

        return new AdminCounts(
            accounts,
            accountsActive,
            terrariums,
            terrariumsWithDevice,
            devices,
            devicesOnline,
            alertsOpen,
            alertsOpenEnclosure,
            alertsOpenSpecies,
            alertsCritical,
            speciesProfiles);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminAccountRollup>> ListAccountRollupsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Users
            .Select(user => new
            {
                user.Id,
                user.Username,
                user.Email,
                RoleCode = user.Role!.Code,
                user.DisabledAt,
                user.CreatedAt,
                user.LastLoginAt,

                // Sub-selects rather than joins with a grouping: three independent counts over three tables, and a
                // join would multiply the rows before counting them.
                Terrariums = db.Terrariums.Count(terrarium => terrarium.UserId == user.Id),
                Devices = db.Devices.Count(device => db.Terrariums.Any(
                    terrarium => terrarium.Id == device.TerrariumId && terrarium.UserId == user.Id)),
                AlertsOpen = db.Alerts.Count(alert => alert.State != AlertState.Resolved
                    && db.Terrariums.Any(
                        terrarium => terrarium.Id == alert.TerrariumId && terrarium.UserId == user.Id)),
            })
            .OrderBy(row => row.Username)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new AdminAccountRollup(
                row.Id,
                row.Username,
                row.Email,
                row.RoleCode,
                row.DisabledAt is null,
                row.Terrariums,
                row.Devices,
                row.AlertsOpen,
                row.CreatedAt,
                row.LastLoginAt)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminTerrariumView>> ListTerrariumsAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        // One query, projected into an anonymous type rather than into the response record: a query provider can
        // only push an ordering or a predicate into SQL if it can see through the projection, and a constructor call
        // is opaque to it. Everything the row needs — the owner's name, the profile's name, the bound board and the
        // open-alert count — is a correlated sub-select, which the provider does translate.
        var rows = await FilterTerrariums(search)
            .OrderByDescending(terrarium => terrarium.CreatedAt)

            // Id breaks the tie, so two enclosures created in the same instant cannot swap places between two pages
            // and make one of them invisible.
            .ThenByDescending(terrarium => terrarium.Id)
            .Skip(skip)
            .Take(take)
            .Select(terrarium => new
            {
                terrarium.Id,
                terrarium.Name,
                OwnerId = terrarium.UserId,
                OwnerUsername = db.Users
                    .Where(user => user.Id == terrarium.UserId)
                    .Select(user => user.Username)
                    .FirstOrDefault(),
                terrarium.SpeciesProfileId,
                SpeciesProfileName = db.SpeciesProfiles
                    .Where(profile => profile.Id == terrarium.SpeciesProfileId)
                    .Select(profile => profile.Name)
                    .FirstOrDefault(),
                terrarium.Status,
                DevicePublicId = db.Devices
                    .Where(device => device.TerrariumId == terrarium.Id)
                    .Select(device => device.PublicId)
                    .FirstOrDefault(),
                DeviceStatus = db.Devices
                    .Where(device => device.TerrariumId == terrarium.Id)
                    .Select(device => (DeviceStatus?)device.Status)
                    .FirstOrDefault(),
                DeviceLastSeenAt = db.Devices
                    .Where(device => device.TerrariumId == terrarium.Id)
                    .Select(device => device.LastSeenAt)
                    .FirstOrDefault(),
                AlertsOpen = db.Alerts.Count(alert =>
                    alert.TerrariumId == terrarium.Id && alert.State != AlertState.Resolved),
                terrarium.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new AdminTerrariumView(
                row.Id,
                row.Name,
                row.OwnerId,

                // Null for an enclosure an administrator created and has not assigned yet.
                row.OwnerUsername,
                row.SpeciesProfileId,
                row.SpeciesProfileName,
                row.Status.ToString(),
                row.DevicePublicId,
                row.DeviceStatus?.ToString(),
                row.DeviceLastSeenAt,
                row.AlertsOpen,
                row.CreatedAt)),
        ];
    }

    /// <inheritdoc />
    public Task<int> CountTerrariumsAsync(string? search, CancellationToken cancellationToken) =>
        FilterTerrariums(search).CountAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminDeviceView>> ListDevicesAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await FilterDevices(search)

            // Last seen first, with the boards that have never published last: the first key says which of the two a
            // row is, because `null` sorting before a value is a collation's opinion rather than a rule.
            .OrderByDescending(device => device.LastSeenAt != null)
            .ThenByDescending(device => device.LastSeenAt)
            .ThenBy(device => device.PublicId)
            .Skip(skip)
            .Take(take)
            .Select(device => new
            {
                device.Id,
                device.PublicId,
                device.DeviceName,
                device.Status,
                OwnerUsername = db.Users
                    .Where(user => user.Id == device.UserId)
                    .Select(user => user.Username)
                    .FirstOrDefault(),
                device.TerrariumId,
                TerrariumName = db.Terrariums
                    .Where(terrarium => terrarium.Id == device.TerrariumId)
                    .Select(terrarium => terrarium.Name)
                    .FirstOrDefault(),
                device.FirmwareVersion,
                device.LastSeenAt,
                device.SignalStrengthDbm,
                device.BatteryPct,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new AdminDeviceView(
                row.Id,
                row.PublicId,
                row.DeviceName,
                row.Status.ToString(),
                row.OwnerUsername,
                row.TerrariumId,
                row.TerrariumName,
                row.FirmwareVersion,
                row.LastSeenAt,
                row.SignalStrengthDbm,
                row.BatteryPct)),
        ];
    }

    /// <inheritdoc />
    public Task<int> CountDevicesAsync(string? search, CancellationToken cancellationToken) =>
        FilterDevices(search).CountAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AdminAlertView>> ListAlertsAsync(
        AlertFamily? family,
        AlertState? state,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var rows = await FilterAlerts(family, state)
            .OrderByDescending(alert => alert.TriggeredAt)
            .ThenByDescending(alert => alert.Id)
            .Skip(skip)
            .Take(take)
            .Select(alert => new
            {
                alert.Id,
                alert.Family,
                alert.Severity,
                alert.State,
                alert.Source,
                alert.Metric,
                DevicePublicId = db.Devices
                    .Where(device => device.Id == alert.DeviceId)
                    .Select(device => device.PublicId)
                    .FirstOrDefault(),
                alert.TerrariumId,
                TerrariumName = db.Terrariums
                    .Where(terrarium => terrarium.Id == alert.TerrariumId)
                    .Select(terrarium => terrarium.Name)
                    .FirstOrDefault(),
                OwnerUsername = db.Terrariums
                    .Where(terrarium => terrarium.Id == alert.TerrariumId)
                    .Select(terrarium => db.Users
                        .Where(user => user.Id == terrarium.UserId)
                        .Select(user => user.Username)
                        .FirstOrDefault())
                    .FirstOrDefault(),
                alert.TriggeredAt,
                alert.ResolvedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(row => new AdminAlertView(
                row.Id,
                AlertFamilies.ApiKey(row.Family),
                row.Severity.ToString(),
                row.State.ToString(),
                row.Source.ToString(),
                row.Metric is { } metric ? MetricDictionary.Get(metric).ApiKey : null,
                row.DevicePublicId!,
                row.TerrariumId,
                row.TerrariumName!,
                row.OwnerUsername!,
                row.TriggeredAt,
                row.ResolvedAt)),
        ];
    }

    /// <inheritdoc />
    public Task<int> CountAlertsAsync(AlertFamily? family, AlertState? state, CancellationToken cancellationToken) =>
        FilterAlerts(family, state).CountAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AdminTerrariumDeletion?> DeleteTerrariumAsync(
        Guid terrariumId,
        Guid actingUserId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        // The context's query filter excludes soft-deleted rows, which is deliberate: an enclosure its owner already
        // removed answers 404 here rather than being deleted a second time.
        var terrarium = await db.Terrariums
            .FirstOrDefaultAsync(row => row.Id == terrariumId, cancellationToken)
            .ConfigureAwait(false);

        if (terrarium is null)
        {
            return null;
        }

        var devices = await db.Devices
            .Where(device => device.TerrariumId == terrariumId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The row's own state as the audit row's payload, captured before the delete: once the row is gone, nothing
        // else can say whose enclosure it was or what it was called.
        var terrariumJson = JsonSerializer.Serialize(
            new
            {
                terrarium.Id,
                terrarium.Name,
                terrarium.UserId,
                terrarium.SpeciesProfileId,
                Status = terrarium.Status.ToString(),
            },
            JsonSerializerOptions.Web);

        var unbound = new List<string>(devices.Count);

        foreach (var device in devices)
        {
            var beforeJson = JsonSerializer.Serialize(
                new
                {
                    device.Id,
                    device.PublicId,
                    device.TerrariumId,
                    Status = device.Status.ToString(),
                },
                JsonSerializerOptions.Web);

            // Back to Provisioning, not Revoked: the board is fine, it just has no enclosure. Credentials and the
            // owner are kept so the account can still revoke, rotate or re-claim it — the same rule the keeper's own
            // delete applies.
            device.TerrariumId = null;
            device.Status = DeviceStatus.Provisioning;
            device.ProvisionedAt = null;
            device.ClaimCode = null;
            device.ClaimCodeExpiresAt = null;

            unbound.Add(device.PublicId);

            db.AuditLogs.Add(AuditLog.ForDevice(
                AuditAction.DeviceUnbound,
                device.Id,
                device.PublicId,
                actingUserId,
                actor,
                occurredAt,
                beforeJson: beforeJson));
        }

        db.AuditLogs.Add(AuditLog.ForTerrarium(
            AuditAction.TerrariumDeleted,
            terrarium.Id,
            actingUserId,
            actor,
            occurredAt,
            beforeJson: terrariumJson));

        db.Terrariums.Remove(terrarium);

        // One unit of work: the unbinding, both audit rows and the delete commit together, so a failure leaves the
        // enclosure exactly as it was rather than with its boards already released.
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new AdminTerrariumDeletion(terrariumId, terrarium.UserId, unbound);
    }

    /// <summary>
    /// The enclosure query, filtered by the search both <c>list</c> and <c>count</c> use.
    /// </summary>
    /// <remarks>
    /// A filter over the entity rather than over the projected row, and deliberately: the projection is applied last
    /// (see <c>ListTerrariumsAsync</c>), so the predicate here is about columns the provider can index — the
    /// enclosure's own name, and an <c>EXISTS</c> over its owner's username.
    /// </remarks>
    /// <param name="search">Free text, or null.</param>
    private IQueryable<Terrarium> FilterTerrariums(string? search)
    {
        var query = db.Terrariums.AsQueryable();

        if (search is not null)
        {
            query = query.Where(terrarium =>
                terrarium.Name.Contains(search)
                || db.Users.Any(user => user.Id == terrarium.UserId && user.Username.Contains(search)));
        }

        return query;
    }

    /// <summary>The board query, filtered by the search both <c>list</c> and <c>count</c> use.</summary>
    /// <param name="search">Free text, or null.</param>
    private IQueryable<Device> FilterDevices(string? search)
    {
        var query = db.Devices.AsQueryable();

        if (search is not null)
        {
            query = query.Where(device =>
                device.PublicId.Contains(search)
                || device.DeviceName.Contains(search)
                || db.Users.Any(user => user.Id == device.UserId && user.Username.Contains(search)));
        }

        return query;
    }

    /// <summary>The alert query, filtered the way both <c>list</c> and <c>count</c> filter it.</summary>
    /// <param name="family">Only this family, or null for both.</param>
    /// <param name="state">Only this state, or null for every state.</param>
    private IQueryable<Alert> FilterAlerts(AlertFamily? family, AlertState? state)
    {
        var query = db.Alerts.AsQueryable();

        if (family is { } familyFilter)
        {
            query = query.Where(alert => alert.Family == familyFilter);
        }

        if (state is { } stateFilter)
        {
            query = query.Where(alert => alert.State == stateFilter);
        }

        return query;
    }
}
