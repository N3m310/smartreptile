using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Application.Admin;

/// <summary>
/// The counts the administrator's dashboard shows in its header (TASKS 2026-10-14, item 13).
/// </summary>
/// <remarks>
/// One record of figures rather than eight calls to existing lists: the dashboard's first screen needs all of them at
/// once, and asking the database eight times for a header is how a dashboard becomes slower than the thing it
/// summarises.
/// </remarks>
/// <param name="Accounts">How many accounts exist.</param>
/// <param name="AccountsActive">How many of them may sign in.</param>
/// <param name="Terrariums">How many live enclosures exist across every account.</param>
/// <param name="TerrariumsWithDevice">How many of those have a board bound.</param>
/// <param name="Devices">How many boards exist, bound or not.</param>
/// <param name="DevicesOnline">Boards whose status is <c>Online</c>.</param>
/// <param name="AlertsOpen">Open alerts of both families.</param>
/// <param name="AlertsOpenEnclosure">Open alerts in the enclosure family.</param>
/// <param name="AlertsOpenSpecies">Open alerts in the species family.</param>
/// <param name="AlertsCritical">Open alerts at <c>Critical</c>, in either family.</param>
/// <param name="SpeciesProfiles">How many species profiles the shared catalogue holds.</param>
public sealed record AdminCounts(
    int Accounts,
    int AccountsActive,
    int Terrariums,
    int TerrariumsWithDevice,
    int Devices,
    int DevicesOnline,
    int AlertsOpen,
    int AlertsOpenEnclosure,
    int AlertsOpenSpecies,
    int AlertsCritical,
    int SpeciesProfiles);

/// <summary>One account's holdings, as the dashboard's get-all table lists them.</summary>
/// <param name="UserId">Account identity.</param>
/// <param name="Username">Login name.</param>
/// <param name="Email">Email address.</param>
/// <param name="Role">Code of the role the account holds.</param>
/// <param name="IsActive">False when the account is disabled.</param>
/// <param name="Terrariums">Live enclosures it owns.</param>
/// <param name="Devices">Boards bound to one of them.</param>
/// <param name="AlertsOpen">Open alerts of both families on them.</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="LastLoginAt">Last successful sign-in, if any.</param>
public sealed record AdminAccountRollup(
    Guid UserId,
    string Username,
    string Email,
    string Role,
    bool IsActive,
    int Terrariums,
    int Devices,
    int AlertsOpen,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>One enclosure of any account, with its owner and what is currently wrong with it.</summary>
/// <param name="Id">Enclosure identity.</param>
/// <param name="Name">Display name.</param>
/// <param name="OwnerId">Owning account.</param>
/// <param name="OwnerUsername">Owning account's login name.</param>
/// <param name="SpeciesProfileId">Species profile in force.</param>
/// <param name="SpeciesProfileName">That profile's common name.</param>
/// <param name="Status">Lifecycle status, as the REST surface spells it.</param>
/// <param name="DevicePublicId">Public id of the bound board, if one is.</param>
/// <param name="DeviceStatus">That board's status.</param>
/// <param name="DeviceLastSeenAt">When that board was last heard from.</param>
/// <param name="OpenAlerts">Open alerts of both families on this enclosure.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record AdminTerrariumView(
    Guid Id,
    string Name,
    Guid OwnerId,
    string OwnerUsername,
    Guid SpeciesProfileId,
    string SpeciesProfileName,
    string Status,
    string? DevicePublicId,
    string? DeviceStatus,
    DateTimeOffset? DeviceLastSeenAt,
    int OpenAlerts,
    DateTimeOffset CreatedAt);

/// <summary>One board in the deployment, bound or not.</summary>
/// <param name="Id">Board identity.</param>
/// <param name="PublicId">Public id clients see.</param>
/// <param name="DeviceName">Name the owner gave it.</param>
/// <param name="Status">Provisioning, online, offline or revoked.</param>
/// <param name="OwnerUsername">Login name of the account holding it, if any.</param>
/// <param name="TerrariumId">Enclosure it is bound to, if any.</param>
/// <param name="TerrariumName">That enclosure's name.</param>
/// <param name="FirmwareVersion">Firmware the board reported.</param>
/// <param name="LastSeenAt">Last time it published.</param>
/// <param name="SignalStrengthDbm">Last reported Wi-Fi signal.</param>
/// <param name="BatteryPct">Last reported battery level.</param>
public sealed record AdminDeviceView(
    Guid Id,
    string PublicId,
    string DeviceName,
    string Status,
    string? OwnerUsername,
    Guid? TerrariumId,
    string? TerrariumName,
    string FirmwareVersion,
    DateTimeOffset? LastSeenAt,
    int? SignalStrengthDbm,
    decimal? BatteryPct);

/// <summary>One alert of any account, which is what makes the two families reviewable across the deployment.</summary>
/// <param name="Id">Alert identity.</param>
/// <param name="Family">Family the row answers to, as the REST key.</param>
/// <param name="Severity">Current severity.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="Source">What produced it.</param>
/// <param name="Metric">Metric it is about as the REST key, if it is about one.</param>
/// <param name="DeviceId">Public id of the board involved.</param>
/// <param name="TerrariumId">Enclosure it belongs to.</param>
/// <param name="TerrariumName">That enclosure's name.</param>
/// <param name="OwnerUsername">Owning account's login name.</param>
/// <param name="TriggeredAt">Start of the episode.</param>
/// <param name="ResolvedAt">When it closed, if it has.</param>
public sealed record AdminAlertView(
    long Id,
    string Family,
    string Severity,
    string State,
    string Source,
    string? Metric,
    string DeviceId,
    Guid TerrariumId,
    string TerrariumName,
    string OwnerUsername,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? ResolvedAt);

/// <summary>What <c>GET /admin/overview</c> answers: the header's figures and the get-all table under them.</summary>
/// <param name="Counts">Deployment-wide totals.</param>
/// <param name="Accounts">Every account with its holdings, so one screen answers "who has what".</param>
/// <param name="Families">The alert families this build has, as the keys the alert list filters on.</param>
public sealed record AdminOverviewView(
    AdminCounts Counts,
    IReadOnlyList<AdminAccountRollup> Accounts,
    IReadOnlyList<AdminFamilyView> Families);

/// <summary>One alert family, described for the dashboard's two tabs.</summary>
/// <param name="Key">Key a client filters on.</param>
/// <param name="Metrics">Metric keys belonging to it, so a client can label the tab without a second call.</param>
/// <param name="AlertsOpen">Open alerts of this family across the deployment.</param>
public sealed record AdminFamilyView(string Key, IReadOnlyList<string> Metrics, int AlertsOpen);

/// <summary>One page of an administrative list, with the total so the dashboard can page it.</summary>
/// <typeparam name="T">Row type.</typeparam>
/// <param name="Items">Rows on this page.</param>
/// <param name="Total">How many rows match the same filter.</param>
/// <param name="Skip">Rows skipped to reach this page.</param>
/// <param name="Take">Page size this answer was asked for.</param>
public sealed record AdminPage<T>(IReadOnlyList<T> Items, int Total, int Skip, int Take);

/// <summary>
/// What an administrator's hard delete of an enclosure produced: whose it was, and which boards it released.
/// </summary>
/// <remarks>
/// The board identifiers come back rather than being closed inside the store, because a broker session is not a row:
/// the write is committed first, and only then are the devices disconnected. A session that outlives the commit by a
/// moment is harmless — ingest already refuses an unbound device — while a device disconnected before a commit that
/// then fails would be a live board with no explanation.
/// </remarks>
/// <param name="TerrariumId">The row that was removed.</param>
/// <param name="OwnerUserId">Account the enclosure belonged to.</param>
/// <param name="UnboundDevicePublicIds">Boards that were detached, as the API names them.</param>
public sealed record AdminTerrariumDeletion(
    Guid TerrariumId,
    Guid OwnerUserId,
    IReadOnlyList<string> UnboundDevicePublicIds);

/// <summary>
/// Everything the administrator's dashboard reads (TASKS 2026-10-14, item 13).
/// </summary>
/// <remarks>
/// Deliberately not built on the per-account stores: those all take an owner and scope to it, which is exactly what
/// this surface does not do. Every method here either ignores the owner or filters by something else, so a read that
/// widened a keeper's own list by accident would have to be written here rather than there — and every route that
/// reaches this interface names <c>Permissions.DataReadAny</c>.
/// <para>
/// The methods return the shapes the dashboard renders rather than tables, because the projection is where the
/// ownership join happens: doing it here keeps a wrong join in one place instead of spreading the same three-way
/// join across the service as well.
/// </para>
/// </remarks>
public interface IAdminStore
{
    /// <summary>The dashboard's header figures.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminCounts> CountAsync(CancellationToken cancellationToken);

    /// <summary>Every account with its holdings, ordered by username.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminAccountRollup>> ListAccountRollupsAsync(CancellationToken cancellationToken);

    /// <summary>A page of every account's enclosures, newest first.</summary>
    /// <param name="search">Matches enclosure name or owner username, case-insensitively.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminTerrariumView>> ListTerrariumsAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>How many enclosures match the same search.</summary>
    /// <param name="search">Matches enclosure name or owner username, case-insensitively.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountTerrariumsAsync(string? search, CancellationToken cancellationToken);

    /// <summary>A page of every board, last seen first, with the never-seen ones last.</summary>
    /// <param name="search">Matches public id, device name or owner username, case-insensitively.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminDeviceView>> ListDevicesAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>How many boards match the same search.</summary>
    /// <param name="search">Matches public id, device name or owner username, case-insensitively.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountDevicesAsync(string? search, CancellationToken cancellationToken);

    /// <summary>A page of every account's alerts, newest first.</summary>
    /// <param name="family">Only this family, or null for both.</param>
    /// <param name="state">Only this state, or null for every state.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AdminAlertView>> ListAlertsAsync(
        AlertFamily? family,
        AlertState? state,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>How many alerts match the same filters.</summary>
    /// <param name="family">Only this family, or null for both.</param>
    /// <param name="state">Only this state, or null for every state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountAlertsAsync(
        AlertFamily? family,
        AlertState? state,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes one enclosure outright, whatever account owns it, after unbinding its boards. Null when no live
    /// enclosure has that id, which the route answers as <c>404 not_found</c>.
    /// </summary>
    /// <remarks>
    /// A hard delete, unlike the keeper's own <c>DELETE /terrariums/{id}</c>: the row and everything cascading from
    /// it — readings, samples, alerts, summaries, overrides — are gone. It is what the administrator's "delete this
    /// enclosure" means (TASKS 2026-10-19, BE-02), and the two audit rows it stages on the same unit of work are what
    /// keeps the act answerable afterwards.
    /// </remarks>
    /// <param name="terrariumId">Enclosure to remove.</param>
    /// <param name="actingUserId">Administrator performing the delete; recorded on both audit rows.</param>
    /// <param name="actor">Request provenance for the audit rows.</param>
    /// <param name="occurredAt">When the delete happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AdminTerrariumDeletion?> DeleteTerrariumAsync(
        Guid terrariumId,
        Guid actingUserId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);
}
