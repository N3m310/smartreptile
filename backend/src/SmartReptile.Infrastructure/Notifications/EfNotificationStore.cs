using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Notifications;

/// <summary>
/// EF Core adapter for the dispatcher's port (FR-13, roadmap task 3.5).
/// </summary>
/// <remarks>
/// The sweep is assembled from a handful of indexed reads rather than one clever join, because the alternative —
/// a projection that carries recipients, silences, device state and delivery history in one statement — is a query
/// nobody can verify by reading. Each read is bounded by the candidate set of the pass, so the cost is proportional
/// to the alerts that could owe a message and not to the size of the fleet's history.
/// <para>
/// Silences are read with the same predicate 3.4's list route renders (<c>CancelledAt is null</c> and
/// <c>UntilUtc &gt; now</c>), and the metric rule is the domain's own <c>Covers</c>: a window that names no metric
/// covers the device-level alerts too, and a metric-named window does not.
/// </para>
/// </remarks>
public sealed class EfNotificationStore(SmartReptileDbContext db) : INotificationStore
{
    private const string DigestCoalesced = NotificationReason.DigestCoalesced;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationCandidate>> ListCandidatesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var alerts = await db.Alerts
            .Where(alert => alert.State != AlertState.Resolved
                || (alert.ResolvedAt != null && alert.ResolvedAt >= fromUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (alerts.Count == 0)
        {
            return [];
        }

        var terrariumIds = alerts.Select(alert => alert.TerrariumId).Distinct().ToArray();
        var alertIds = alerts.Select(alert => alert.Id).ToArray();

        var terrariums = await db.Terrariums
            .Where(terrarium => terrariumIds.Contains(terrarium.Id))
            .Select(terrarium => new { terrarium.Id, terrarium.Name, terrarium.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var inMaintenance = await db.Devices
            .Where(device => device.TerrariumId != null
                && terrariumIds.Contains(device.TerrariumId.Value)
                && device.Status == DeviceStatus.Maintenance)
            .Select(device => device.TerrariumId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var silences = await db.MetricSilences
            .Where(silence => terrariumIds.Contains(silence.TerrariumId)
                && silence.CancelledAt == null
                && silence.UntilUtc > nowUtc)
            .Select(silence => new { silence.TerrariumId, silence.Metric })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ownerIds = terrariums.Select(terrarium => terrarium.UserId).Distinct().ToArray();
        var ownerPreferences = await ReadPreferencesAsync(user => ownerIds.Contains(user.Id), cancellationToken)
            .ConfigureAwait(false);

        // §6's 30-minute step is the one that brings in "a Technician ... who was not notified". The role is global
        // rather than per-terrarium (the product has one account per enclosure), so the technicians are the enabled
        // accounts holding the role, and the first one is the one the ladder reaches for.
        var technicianPreferences = await ReadPreferencesAsync(
                user => user.DisabledAt == null
                    && user.Role!.Permissions.Any(grant => grant.PermissionCode == Permissions.AlertWrite),
                cancellationToken)
            .ConfigureAwait(false);

        var attempts = await db.NotificationLogs
            .Where(row => row.AlertId != null && alertIds.Contains(row.AlertId.Value))
            .Select(row => new
            {
                AlertId = row.AlertId!.Value,
                UserId = row.UserId,
                row.Channel,
                row.Step,
                row.Attempts,
                row.Status,
                row.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var candidates = new List<NotificationCandidate>(alerts.Count);

        foreach (var alert in alerts)
        {
            var terrarium = terrariums.FirstOrDefault(candidate => candidate.Id == alert.TerrariumId);

            if (terrarium is null)
            {
                continue;
            }

            var silenced = silences.Any(silence =>
                silence.TerrariumId == alert.TerrariumId
                && (alert.Metric is { } metric
                    ? silence.Metric is null || silence.Metric == metric
                    : silence.Metric is null));

            var recipients = new List<NotificationRecipient>();

            if (ownerPreferences.FirstOrDefault(user => user.Id == terrarium.UserId) is { } owner)
            {
                recipients.Add(ToRecipient(owner, isOwner: true));
            }

            recipients.AddRange(technicianPreferences
                .Where(technician => technician.Id != terrarium.UserId)
                .Select(technician => ToRecipient(technician, isOwner: false)));

            candidates.Add(new NotificationCandidate(
                alert.Id,
                alert.TerrariumId,
                terrarium.Name,
                alert.Severity,
                alert.State,
                alert.Source,
                alert.Metric,
                alert.TriggeringValue,
                alert.PeakValue,
                alert.BandMin,
                alert.BandMax,
                alert.TriggeredAt,
                alert.AcknowledgedAt,
                alert.ResolvedAt,
                inMaintenance.Contains(alert.TerrariumId),
                silenced,
                recipients,
                attempts
                    .Where(row => row.AlertId == alert.Id && row.UserId is { } userId)
                    .Select(row => new NotificationAttempt(
                        row.UserId!.Value,
                        row.Channel,
                        row.Step,
                        row.Attempts,
                        row.Status,
                        row.CreatedAt))
                    .ToArray()));
        }

        return candidates;
    }

    /// <inheritdoc />
    public Task<int> CountDeliveredAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        db.NotificationLogs.CountAsync(
            row => row.TerrariumId == terrariumId
                && row.Status == NotificationStatus.Sent
                && row.Channel != NotificationChannel.InApp
                && row.CreatedAt >= fromUtc
                && row.CreatedAt < toUtc,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationDigestInput>> ListDigestInputsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var coalesced = await db.NotificationLogs
            .Where(row => row.Status == NotificationStatus.Suppressed
                && row.SuppressedReason == DigestCoalesced
                && row.AlertId != null
                && row.TerrariumId != null
                && row.CreatedAt >= fromUtc
                && row.CreatedAt < toUtc)
            .Select(row => new { TerrariumId = row.TerrariumId!.Value, AlertId = row.AlertId!.Value })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (coalesced.Count == 0)
        {
            return [];
        }

        var terrariumIds = coalesced.Select(row => row.TerrariumId).Distinct().ToArray();
        var alertIds = coalesced.Select(row => row.AlertId).Distinct().ToArray();

        var alerts = await db.Alerts
            .Where(alert => alertIds.Contains(alert.Id))
            .Select(alert => new { alert.Id, alert.Severity, alert.Metric, alert.PeakValue })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var terrariums = await db.Terrariums
            .Where(terrarium => terrariumIds.Contains(terrarium.Id))
            .Select(terrarium => new { terrarium.Id, terrarium.Name, terrarium.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ownerIds = terrariums.Select(terrarium => terrarium.UserId).Distinct().ToArray();
        var owners = await ReadPreferencesAsync(user => ownerIds.Contains(user.Id), cancellationToken)
            .ConfigureAwait(false);

        var inputs = new List<NotificationDigestInput>();

        foreach (var terrariumId in terrariumIds)
        {
            var terrarium = terrariums.FirstOrDefault(candidate => candidate.Id == terrariumId);

            if (terrarium is null)
            {
                continue;
            }

            var sources = coalesced
                .Where(row => row.TerrariumId == terrariumId)
                .Join(alerts, row => row.AlertId, alert => alert.Id, (_, alert) => alert)
                .Select(alert => new DigestSource(alert.Severity, alert.Metric, alert.PeakValue))
                .ToArray();

            var recipients = owners
                .Where(owner => owner.Id == terrarium.UserId)
                .Select(owner => ToRecipient(owner, isOwner: true))
                .ToArray();

            if (sources.Length > 0 && recipients.Length > 0)
            {
                inputs.Add(new NotificationDigestInput(terrariumId, terrarium.Name, sources, recipients));
            }
        }

        return inputs;
    }

    /// <inheritdoc />
    public Task<bool> DigestExistsAsync(
        Guid terrariumId,
        DateTimeOffset coveredHourEndUtc,
        CancellationToken cancellationToken) =>
        db.NotificationLogs.AnyAsync(
            row => row.TerrariumId == terrariumId
                && row.AlertId == null
                && row.CreatedAt == coveredHourEndUtc,
            cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyList<NotificationLog> rows, CancellationToken cancellationToken)
    {
        db.NotificationLogs.AddRange(rows);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationLog>> ListInboxAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.NotificationLogs
            .Where(row => row.UserId == userId
                && row.Channel == NotificationChannel.InApp
                && row.Status == NotificationStatus.Sent);

        if (unreadOnly)
        {
            query = query.Where(row => !row.IsRead);
        }

        return await query
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<NotificationLog?> FindInboxRowAsync(
        Guid userId,
        long notificationId,
        CancellationToken cancellationToken) =>
        db.NotificationLogs.FirstOrDefaultAsync(
            row => row.Id == notificationId
                && row.UserId == userId
                && row.Channel == NotificationChannel.InApp
                && row.Status == NotificationStatus.Sent,
            cancellationToken);

    /// <inheritdoc />
    public async Task<int> MarkReadAsync(
        Guid userId,
        long? notificationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        // ExecuteUpdate rather than a tracked loop: "mark all read" can be hundreds of rows, and none of them needs
        // to be loaded to be changed. It also makes the write idempotent by construction — the filter is already
        // "not read yet", so a second call changes nothing.
        var query = db.NotificationLogs.Where(row => row.UserId == userId
            && row.Channel == NotificationChannel.InApp
            && row.Status == NotificationStatus.Sent
            && !row.IsRead);

        if (notificationId is { } id)
        {
            query = query.Where(row => row.Id == id);
        }

        return await query
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.IsRead, true)
                    .SetProperty(row => row.ReadAt, atUtc),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        db.NotificationLogs.CountAsync(
            row => row.UserId == userId
                && row.Channel == NotificationChannel.InApp
                && row.Status == NotificationStatus.Sent
                && !row.IsRead,
            cancellationToken);

    /// <inheritdoc />
    public async Task DisablePushAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The token is cleared as well as the channel: FCM said this destination no longer exists, so keeping it
        // would only make the next attempt fail the same way (TC-U-44).
        await db.Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(user => user.ChannelFcmEnabled, false)
                    .SetProperty(user => user.FcmToken, (string?)null),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationLog>> ListForAlertAsync(
        long alertId,
        CancellationToken cancellationToken) =>
        await db.NotificationLogs
            .Where(row => row.AlertId == alertId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<List<UserPreferences>> ReadPreferencesAsync(
        System.Linq.Expressions.Expression<Func<User, bool>> predicate,
        CancellationToken cancellationToken) =>
        await db.Users
            .Where(predicate)
            .Select(user => new UserPreferences(
                user.Id,
                user.PreferredLanguage,
                user.TimeZoneId,
                user.MinNotifySeverity,
                user.ChannelFcmEnabled,
                user.FcmToken,
                user.ChannelEmailEnabled,
                user.Email,
                user.QuietHoursStart,
                user.QuietHoursEnd))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static NotificationRecipient ToRecipient(UserPreferences preferences, bool isOwner) =>
        new(
            preferences.Id,
            preferences.PreferredLanguage,
            preferences.TimeZoneId,
            preferences.MinSeverity,
            preferences.PushEnabled,
            preferences.FcmToken,
            preferences.EmailEnabled,
            preferences.Email,
            QuietHours.From(preferences.QuietHoursStart, preferences.QuietHoursEnd),
            isOwner);

    private sealed record UserPreferences(
        Guid Id,
        string PreferredLanguage,
        string TimeZoneId,
        AlertSeverity MinSeverity,
        bool PushEnabled,
        string? FcmToken,
        bool EmailEnabled,
        string Email,
        TimeOnly? QuietHoursStart,
        TimeOnly? QuietHoursEnd);
}
