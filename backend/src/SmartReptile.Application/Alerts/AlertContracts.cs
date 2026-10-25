using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Alerts;

/// <summary>
/// Paging constants of the convention `07-appendices/03` §2 states for list endpoints
/// (<c>?cursor=&amp;pageSize=</c>, default 50, max 200).
/// </summary>
public static class AlertPaging
{
    /// <summary>Page size when the caller does not ask for one.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Largest page a caller may ask for.</summary>
    public const int MaxPageSize = 200;
}

/// <summary>
/// The keyset cursor of <c>GET /alerts</c>: the position of the last row of the page the caller already has, so the
/// next page can start after it instead of using offset paging that repeats or skips rows as alerts arrive.
/// </summary>
/// <remarks>
/// Opaque on purpose — it is base64 of <c>triggeredAt|id</c> and nothing about its shape is a promise. The pair is
/// what makes the order total: two alerts can share a trigger instant (one batch can open two metrics at once), and
/// an identity alone would not say which of them came first.
/// </remarks>
public static class AlertCursor
{
    private const char Separator = '|';

    /// <summary>Encodes the position of one row.</summary>
    /// <param name="triggeredAt">Its trigger instant.</param>
    /// <param name="alertId">Its identity.</param>
    public static string Encode(DateTimeOffset triggeredAt, long alertId) =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{triggeredAt.UtcTicks}{Separator}{alertId}"));

    /// <summary>Reads a cursor back, or false when it is not one this build wrote.</summary>
    /// <param name="cursor">Value as it arrived in the query string.</param>
    /// <param name="position">Decoded position.</param>
    public static bool TryDecode(string? cursor, out (DateTimeOffset TriggeredAt, long AlertId) position)
    {
        position = default;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(Separator);

            if (parts.Length == 2
                && long.TryParse(parts[0], out var ticks)
                && long.TryParse(parts[1], out var alertId))
            {
                position = (new DateTimeOffset(ticks, TimeSpan.Zero), alertId);

                return true;
            }
        }
        catch (FormatException)
        {
            // A cursor a client made up is a bad request, not an incident: report it as "not a cursor" and let the
            // caller decide, rather than turning it into a 500.
        }

        return false;
    }
}

/// <summary>Filters of <c>GET /alerts</c>, already parsed out of the query string.</summary>
/// <param name="State">Only alerts in this lifecycle state.</param>
/// <param name="Severity">Only alerts at this severity.</param>
/// <param name="Metric">Only alerts about this metric; device-level alerts are excluded by any value.</param>
/// <param name="Family">
/// Only alerts of this family: <c>enclosure</c> for the enclosure's own humidity, light and board, <c>species</c>
/// for the animal's temperature and UV (TASKS 2026-10-14, item 1).
/// </param>
/// <param name="FromUtc">Only alerts triggered at or after this instant.</param>
/// <param name="ToUtc">Only alerts triggered at or before this instant.</param>
/// <param name="Cursor">Position to continue from, or null for the first page.</param>
/// <param name="PageSize">Rows requested, clamped to <see cref="AlertPaging.MaxPageSize"/>.</param>
/// <param name="InProgress">
/// Only alerts that have not been resolved yet — open and acknowledged together. The status bar's "cần xử lý" tab
/// means this, because an acknowledged alert is still the keeper's problem: ownership is not closure (BR-12.1), and a
/// tab that dropped one the moment it was picked up would hide exactly the rows somebody is working on.
/// </param>
public sealed record AlertQuery(
    AlertState? State = null,
    AlertSeverity? Severity = null,
    MetricCode? Metric = null,
    AlertFamily? Family = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Cursor = null,
    int PageSize = AlertPaging.DefaultPageSize,
    bool InProgress = false);

/// <summary>One alert as the list and the detail respond (FR-12, <c>07-appendices/03</c> §4.5).</summary>
/// <param name="Id">Identity, used by the ack/resolve/timeline routes.</param>
/// <param name="TerrariumId">Terrarium that raised it — the clients' key for everything else.</param>
/// <param name="DeviceId">Public device id, not the surrogate key, because that is the only one clients see.</param>
/// <param name="Metric">Metric the alert is about, as the REST key (<c>tempC</c>); null for a device-level alert.</param>
/// <param name="Family">
/// Which of the two alert families this row answers to, as the REST key (<c>enclosure</c> or <c>species</c>). Present
/// on every alert, including the device-level ones that carry no metric and could not otherwise be placed.
/// </param>
/// <param name="Severity">Current severity: <c>Info</c>, <c>Warning</c> or <c>Critical</c>.</param>
/// <param name="State">Current lifecycle state: <c>Open</c>, <c>Acknowledged</c> or <c>Resolved</c>.</param>
/// <param name="Source">What produced it.</param>
/// <param name="Phase">Phase the value was judged in.</param>
/// <param name="TriggeringValue">Value at the first out-of-band reading.</param>
/// <param name="PeakValue">Worst value of the episode so far.</param>
/// <param name="BandMin">Lower bound of the band in force when it fired.</param>
/// <param name="BandMax">Upper bound of that band.</param>
/// <param name="TriggeredAt">Start of the excursion, back-dated to the reading that began it.</param>
/// <param name="LastObservedAt">Newest sample seen while it was open.</param>
/// <param name="AcknowledgedAt">When a human took ownership.</param>
/// <param name="ResolvedAt">When it closed.</param>
/// <param name="ResolvedReason">Why it closed.</param>
/// <param name="DurationSeconds">Episode length in whole seconds; null while still open.</param>
public sealed record AlertView(
    long Id,
    Guid TerrariumId,
    string DeviceId,
    string? Metric,
    string Family,
    string Severity,
    string State,
    string Source,
    string Phase,
    decimal? TriggeringValue,
    decimal? PeakValue,
    decimal? BandMin,
    decimal? BandMax,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? LastObservedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolvedReason,
    long? DurationSeconds);

/// <summary>One point of the value series an alert's detail carries, so a card can draw the excursion.</summary>
/// <param name="At">Sample instant.</param>
/// <param name="Value">Measured value.</param>
public sealed record AlertSeriesPoint(DateTimeOffset At, decimal Value);

/// <summary>
/// The detail response: the alert and an excerpt of the values it was judged on.
/// </summary>
/// <remarks>
/// The contract also promises a <b>snapshot reference</b> (§4.5). It is absent here on purpose rather than sent as
/// null: `Alert.ThresholdSnapshotId` exists in `07-appendices/02` §3.8's sketch and in no built table, so a field
/// named after it would be a promise the database cannot keep. The band the alert carries is what explains it today,
/// and the reference arrives with 3.1's remaining half.
/// </remarks>
/// <param name="Alert">The alert itself.</param>
/// <param name="Series">Newest readings of the episode, oldest first.</param>
/// <param name="SeriesTruncated">True when older readings of the episode were left out.</param>
public sealed record AlertDetailView(
    AlertView Alert,
    IReadOnlyList<AlertSeriesPoint> Series,
    bool SeriesTruncated);

/// <summary>One step of an alert's response timeline (FR-12, <c>GET /alerts/{id}/timeline</c>).</summary>
/// <param name="Event">
/// What happened: <c>opened</c>, <c>escalated</c>, <c>acknowledged</c>, <c>resolved</c> or <c>notified</c>.
/// </param>
/// <param name="At">When it happened.</param>
/// <param name="ActorUserId">Who did it, when a user did.</param>
/// <param name="Reason">Resolution reason on a <c>resolved</c> entry; the channel or the suppression on a
/// <c>notified</c> one.</param>
/// <param name="Note">What the keeper wrote on a <c>resolved</c> entry; the delivery's own text on a
/// <c>notified</c> one.</param>
public sealed record AlertTimelineEntry(
    string Event,
    DateTimeOffset At,
    Guid? ActorUserId = null,
    string? Reason = null,
    string? Note = null);

/// <summary>
/// One delivery row of an alert, as the timeline needs it (roadmap task 3.5).
/// </summary>
/// <param name="At">Instant of the attempt.</param>
/// <param name="Channel">Channel the row records.</param>
/// <param name="Step">Escalation step: 0 is the first delivery, 1–3 the ladder of §02-design/05 §6.</param>
/// <param name="Status">What happened to the attempt.</param>
/// <param name="Reason">Suppression reason or bypass marker, when the row carries one.</param>
/// <param name="Title">Rendered title, so the timeline can show what was actually said.</param>
public sealed record NotificationAttemptRecord(
    DateTimeOffset At,
    NotificationChannel Channel,
    byte Step,
    NotificationStatus Status,
    string? Reason,
    string Title);

/// <summary>
/// How many alerts the current filters match, per lifecycle state — what the status bar is labelled with.
/// </summary>
/// <remarks>
/// Counted over every filter the caller sent <b>except</b> the state itself, because the numbers are how the caller
/// decides which state to look at. A device-level alert has no metric, so the count cannot come from the metric
/// dictionary and has to be the store's own query — the page and its labels therefore come from one place.
/// </remarks>
/// <param name="Open">Rows still open.</param>
/// <param name="Acknowledged">Rows a human took ownership of but has not closed.</param>
/// <param name="Resolved">Rows closed by recovery or by hand.</param>
public sealed record AlertCounts(int Open, int Acknowledged, int Resolved)
{
    /// <summary>Every row the filters match, whatever its state.</summary>
    public int Total => Open + Acknowledged + Resolved;
}

/// <summary>One page of alerts, newest first.</summary>
/// <param name="Items">The alerts.</param>
/// <param name="NextCursor">Cursor for the next page, or null when this page was the last.</param>
/// <param name="Counts">Per-state totals for the same filters, so the status bar can be labelled before paging.</param>
public sealed record AlertPage(IReadOnlyList<AlertView> Items, string? NextCursor, AlertCounts Counts);

/// <summary>An alert row with the one field of its device a client needs: the public id.</summary>
/// <param name="Alert">Stored row.</param>
/// <param name="DevicePublicId">Public device id the row's <c>DeviceId</c> points at.</param>
public sealed record AlertRecord(Alert Alert, string DevicePublicId);

/// <summary>
/// One alert that moved, and which way. Carried by the outcome of whatever committed it, so the push can happen
/// after the commit and by whoever owns the commit.
/// </summary>
/// <param name="Alert">The row as it now stands; its identity is set, which is why this is built after the commit.</param>
/// <param name="Event">Move from <see cref="AlertEvent"/>.</param>
public sealed record AlertChange(Alert Alert, string Event);

/// <summary>
/// An expected failure of an alert use case. Field-level violations reuse <see cref="IdentityViolation"/>, which
/// already means "a named field was refused, with a stable code".
/// </summary>
/// <param name="Code">Stable problem code, e.g. <c>alert_not_open</c>.</param>
/// <param name="Message">Message safe to show the user — never reveals whether a foreign alert exists.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
public sealed record AlertProblem(
    string Code,
    string Message,
    IReadOnlyList<IdentityViolation>? Errors = null);

/// <summary>
/// Outcome of an alert use case. At most one payload is set; a null <see cref="Problem"/> means success.
/// </summary>
public sealed record AlertOutcome
{
    /// <summary>Set by list.</summary>
    public AlertPage? Page { get; init; }

    /// <summary>Set by detail.</summary>
    public AlertDetailView? Alert { get; init; }

    /// <summary>Set by timeline.</summary>
    public IReadOnlyList<AlertTimelineEntry>? Timeline { get; init; }

    /// <summary>Set by the transitions, which answer with the alert as it now stands.</summary>
    public AlertView? Changed { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public AlertProblem? Problem { get; init; }

    /// <summary>True when there is no problem.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A failure the caller caused or should see.</summary>
    /// <param name="problem">What went wrong.</param>
    public static AlertOutcome Failed(AlertProblem problem) => new() { Problem = problem };

    /// <summary>
    /// The alert is not the caller's. One answer for "does not exist", "belongs to somebody else" and "belongs to a
    /// deleted terrarium", because telling them apart is exactly what BR-02.2 forbids.
    /// </summary>
    public static AlertOutcome NotFound() => Failed(new AlertProblem("not_found", "No such alert."));
}

/// <summary>
/// Persistence for the alert lifecycle (FR-12, roadmap task 3.4).
/// </summary>
/// <remarks>
/// A port of its own rather than a method on <c>ITerrariumStore</c>: reads here are about a table the terrarium read
/// surface only counts, and the writes change <c>EvaluationState</c> — which the evaluator owns but must not be the
/// only writer of.
/// </remarks>
public interface IAlertStore
{
    /// <summary>
    /// Alerts of the terrariums that belong to <paramref name="userId"/>, newest first, at most
    /// <paramref name="take"/> rows. The caller asks for one row more than the page it will show so it can tell a
    /// full page from the last one.
    /// </summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="query">Filters, including the cursor to continue from.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="anyAccount">
    /// When true the owner predicate is dropped and every account's alerts are returned — the administrator's read.
    /// It is also the only way an alert on an <b>ownerless</b> terrarium is ever returned: <c>Terrarium.UserId</c> is
    /// nullable (2026-10-23) and <c>NULL = &lt;account&gt;</c> matches nobody, so a scoped read cannot reach one.
    /// </param>
    Task<IReadOnlyList<AlertRecord>> ListAsync(
        Guid userId,
        AlertQuery query,
        int take,
        CancellationToken cancellationToken,
        bool anyAccount = false);

    /// <summary>
    /// How many alerts the same filters match, per lifecycle state — the labels of the status bar.
    /// </summary>
    /// <remarks>
    /// Every filter is applied <b>except</b> the state, for the reason the counts exist: they are how the caller picks
    /// a state. The cursor is ignored too, because a total that moved with the page number would be a page size.
    /// </remarks>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="query">Filters, with any state and cursor ignored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="anyAccount">When true, the owner predicate is dropped, exactly as <see cref="ListAsync"/> does.</param>
    Task<AlertCounts> CountByStateAsync(
        Guid userId,
        AlertQuery query,
        CancellationToken cancellationToken,
        bool anyAccount = false);

    /// <summary>One alert, provided it belongs to a terrarium of <paramref name="userId"/>; null otherwise.</summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AlertRecord?> FindOwnedAsync(long alertId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// One alert of any account, or null when it does not exist or its terrarium was deleted (2026-10-25).
    /// </summary>
    /// <remarks>
    /// The cross-account read the administrator's routes use. It is deliberately <b>not</b> scoped by owner and
    /// deliberately <b>not</b> unrestricted either: the join to <c>Terrarium</c> still applies that model's query
    /// filter, so an alert of a soft-deleted enclosure — which the administrator's audit list still shows — cannot
    /// be acknowledged or resolved through here. A closed enclosure's history is a record, not a queue.
    /// </remarks>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AlertRecord?> FindAnyAsync(long alertId, CancellationToken cancellationToken);

    /// <summary>
    /// Newest values one metric took inside the episode's window, oldest first, at most
    /// <paramref name="maxPoints"/> of them.
    /// </summary>
    /// <param name="terrariumId">Terrarium the readings belong to.</param>
    /// <param name="metric">Metric to read.</param>
    /// <param name="fromUtc">Start of the episode.</param>
    /// <param name="toUtc">End of the episode, or the current instant while it is open.</param>
    /// <param name="maxPoints">Cap, plus one when the caller wants to know whether it truncated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AlertSeriesPoint>> ReadEpisodeSeriesAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken);

    /// <summary>Audit rows written about one alert, oldest first.</summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AuditLog>> ReadAuditAsync(long alertId, CancellationToken cancellationToken);

    /// <summary>
    /// Delivery rows written about one alert, oldest first — what §02-design/05 §6 calls "the full response
    /// timeline of one incident", assembled from the records task 3.5 writes rather than from a second set of
    /// escalation columns on the alert.
    /// </summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationAttemptRecord>> ReadDeliveriesAsync(
        long alertId,
        CancellationToken cancellationToken);

    /// <summary>The dwell key one metric and phase is tracking, or null when nothing is being tracked for it.</summary>
    /// <param name="terrariumId">Terrarium the key belongs to.</param>
    /// <param name="metric">Metric of the key.</param>
    /// <param name="phase">Phase of the key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<EvaluationState?> FindExcursionStateAsync(
        Guid terrariumId,
        MetricCode metric,
        ThresholdPhase phase,
        CancellationToken cancellationToken);

    /// <summary>Stages an audit row. It commits with the change it describes, never on its own.</summary>
    /// <param name="entry">Row to stage.</param>
    void AddAuditEntry(AuditLog entry);

    /// <summary>Commits everything this scope staged.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
