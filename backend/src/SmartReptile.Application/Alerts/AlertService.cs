using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Alerts;

/// <summary>
/// The alert lifecycle API of FR-12 (roadmap task 3.4): what a keeper can read, and the two things they can do.
/// </summary>
/// <remarks>
/// <b>What this owns.</b> The list, the detail, the response timeline, the acknowledgement and the resolution —
/// nothing else. Raising an alert is the evaluator's (3.2) and the derived signals' (3.3); delivering one is the
/// dispatcher's (3.5). That split is why every write here is one row plus one audit entry: an alert's <em>content</em>
/// is never edited by a human, only its ownership and its end.
/// <para>
/// <b>Scoping.</b> Every read and every write goes through <see cref="AlertService.LocateAsync"/>, which answers
/// null for an alert of somebody else's terrarium exactly as it does for one that does not exist. Callers therefore
/// get one answer — <c>404 not_found</c> — and cannot use the routes to learn which alert ids exist (BR-02.2).
/// Since 2026-10-25 that scope widens for an account holding <c>data.read.any</c>/<c>data.write.any</c>: the
/// administrator may open, acknowledge and resolve any account's alert, which is what makes the rows its audit list
/// already shows actionable rather than merely visible.
/// </para>
/// <para>
/// <b>Resolution re-arms the dwell key.</b> Closing a threshold alert clears the excursion window its
/// <c>EvaluationState</c> row still holds, so the alert cannot come back on the next sample; see
/// <see cref="AlertLifecycle.RearmsItsKey"/> for why that is a rule and not a detail. What a keeper who wants a
/// longer quiet period needs is a silence, which is time-boxed and visible (`MetricSilenceService`).
/// </para>
/// </remarks>
public sealed class AlertService(IAlertStore store, ITelemetryBroadcaster broadcaster, IClock clock)
{
    /// <summary>
    /// How many readings of an episode the detail carries. An hour at the default 15-second interval, or two hours
    /// at 30 — enough to draw the excursion that opened the alert, which is what §4.5 asks the field to be ("value
    /// series excerpt"). A multi-day episode is truncated to its newest points rather than refused, and the response
    /// says so.
    /// </summary>
    private const int SeriesExcerptMaxPoints = 240;

    /// <summary>Longest note a resolution may carry. The audit column holds more, but a note is a sentence.</summary>
    private const int ResolveNoteMaxLength = 500;

    /// <summary>Lists alerts of the caller's terrariums, newest first.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="query">Filters, page size and cursor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True for an administrator: the list then spans every account, which is the only read that reaches an alert on
    /// an ownerless terrarium (see <see cref="IAlertStore.ListAsync"/>).
    /// </param>
    public async Task<AlertOutcome> ListAsync(
        Guid userId,
        AlertQuery query,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Cursor is { Length: > 0 } cursor && !AlertCursor.TryDecode(cursor, out _))
        {
            return AlertOutcome.Failed(new AlertProblem(
                "invalid_cursor",
                "cursor is not one this API issued."));
        }

        if (query.FromUtc is { } from && query.ToUtc is { } to && to < from)
        {
            return AlertOutcome.Failed(new AlertProblem("invalid_range", "to must not precede from."));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, AlertPaging.MaxPageSize);

        // One row more than the page: the extra row is how "there is more" is known without a second count query.
        var rows = await store
            .ListAsync(userId, query, pageSize + 1, cancellationToken, mayReachAnyAccount)
            .ConfigureAwait(false);

        // The status bar's labels, from the same filters with the state dropped so the caller can switch between
        // them rather than being told only about the one already chosen.
        var counts = await store
            .CountByStateAsync(userId, query, cancellationToken, mayReachAnyAccount)
            .ConfigureAwait(false);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(Describe).ToArray();

        var next = hasMore && items.Length > 0
            ? AlertCursor.Encode(items[^1].TriggeredAt, items[^1].Id)
            : null;

        return new AlertOutcome { Page = new AlertPage(items, next, counts) };
    }

    /// <summary>One alert with an excerpt of the values it was judged on.</summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account read permission, which may open any account's alert (2026-10-25).
    /// The administrator's audit list already names those rows; a row that cannot be opened is a row that cannot be
    /// acted on, so the detail and the timeline take the same widening as the two writes.
    /// </param>
    public async Task<AlertOutcome> GetAsync(
        long alertId,
        Guid userId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var record = await LocateAsync(alertId, userId, mayReachAnyAccount, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return AlertOutcome.NotFound();
        }

        var alert = record.Alert;

        // A device-level alert (silence, sensor fault, clock skew) is not about a value, so there is no series to
        // ask for and an empty one is the honest answer rather than a lookup that cannot match.
        if (alert.Metric is not { } metric)
        {
            return new AlertOutcome { Alert = new AlertDetailView(Describe(record), [], SeriesTruncated: false) };
        }

        var from = alert.TriggeredAt;
        var to = alert.ResolvedAt ?? alert.LastObservedAt ?? clock.UtcNow;

        var points = await store
            .ReadEpisodeSeriesAsync(alert.TerrariumId, metric, from, to, SeriesExcerptMaxPoints + 1, cancellationToken)
            .ConfigureAwait(false);

        var truncated = points.Count > SeriesExcerptMaxPoints;
        var excerpt = truncated ? points.Skip(points.Count - SeriesExcerptMaxPoints).ToArray() : points;

        return new AlertOutcome
        {
            Alert = new AlertDetailView(Describe(record), excerpt, truncated),
        };
    }

    /// <summary>
    /// The response timeline: when it was raised, and what a human did about it.
    /// </summary>
    /// <remarks>
    /// Escalation entries are not here yet, and that is the design's own arrangement rather than a gap in this
    /// build: §02-design/05 §6 records escalations as <c>NotificationLog</c> rows carrying the alert id, so "the
    /// full response timeline of one incident" is assembled from the delivery records that task 3.5 writes. The
    /// alert row keeps no escalation instant because escalation edits the row in place — one row per episode is what
    /// DI-01's unique index is for.
    /// <remarks>
    /// Escalation entries come from the delivery records task 3.5 writes, which is the design's own arrangement:
    /// §02-design/05 §6 records escalations as <c>NotificationLog</c> rows carrying the alert id, and the alert row
    /// keeps no escalation instant because escalation edits the row in place — one row per episode is what DI-01's
    /// unique index is for. Only rows that reached a decision are listed (<c>Sent</c> and <c>Suppressed</c>): a
    /// failed attempt is recorded in the delivery history and would read as noise on a timeline whose subject is
    /// what happened to the incident, not to the push.
    /// </remarks>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account read permission (2026-10-25).</param>
    public async Task<AlertOutcome> TimelineAsync(
        long alertId,
        Guid userId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var record = await LocateAsync(alertId, userId, mayReachAnyAccount, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return AlertOutcome.NotFound();
        }

        var alert = record.Alert;
        var audit = await store.ReadAuditAsync(alertId, cancellationToken).ConfigureAwait(false);
        var deliveries = await store.ReadDeliveriesAsync(alertId, cancellationToken).ConfigureAwait(false);
        var entries = new List<AlertTimelineEntry>
        {
            // The vocabulary is the hub event's, so a client that handles alertChanged and a client that renders a
            // timeline are reading the same names for the same moves.
            new(AlertEvent.Opened, alert.TriggeredAt),
        };

        entries.AddRange(deliveries
            .Where(delivery => delivery.Status is NotificationStatus.Sent or NotificationStatus.Suppressed)
            .Select(delivery => new AlertTimelineEntry(
                AlertEvent.Notified,
                delivery.At,
                null,
                Describe(delivery),
                delivery.Title)));

        if (alert.AcknowledgedAt is { } acknowledgedAt)
        {
            entries.Add(new AlertTimelineEntry(
                AlertEvent.Acknowledged,
                acknowledgedAt,
                alert.AcknowledgedByUserId));
        }

        if (alert.ResolvedAt is { } resolvedAt)
        {
            // The reason and the keeper's own words come from the audit row rather than from a column: the alert
            // keeps the machine-readable reason, and the note is something a human wrote, which is what an audit
            // trail is for.
            var resolution = audit.FirstOrDefault(entry => entry.Action == AuditAction.AlertResolved);

            entries.Add(new AlertTimelineEntry(
                AlertEvent.Resolved,
                resolvedAt,
                alert.ResolvedByUserId,
                alert.ResolvedReason?.ToString(),
                NoteFrom(resolution?.AfterJson)));
        }

        return new AlertOutcome { Timeline = entries.OrderBy(entry => entry.At).ToArray() };
    }

    /// <summary>
    /// Takes ownership of an open alert.
    /// </summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in account; also the actor recorded.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account write permission, so may acknowledge another account's alert
    /// (2026-10-25). The audit row then names the caller as the actor, which is the whole record of "an administrator
    /// took ownership of a keeper's incident".
    /// </param>
    public async Task<AlertOutcome> AcknowledgeAsync(
        long alertId,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var record = await LocateAsync(alertId, userId, mayReachAnyAccount, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return AlertOutcome.NotFound();
        }

        var now = clock.UtcNow;

        // "Already acknowledged" and "already resolved" are one answer: in both cases the caller's request changed
        // nothing, and one code is one thing for a client to handle (TC-I-07's second acknowledgement).
        if (!AlertLifecycle.Acknowledge(record.Alert, userId, now))
        {
            return AlertOutcome.Failed(new AlertProblem("alert_not_open", "The alert is no longer open."));
        }

        store.AddAuditEntry(AuditLog.ForAlert(
            AuditAction.AlertAcknowledged,
            record.Alert.Id,
            record.Alert.DeviceId,
            userId,
            actor,
            now));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await BroadcastAsync([new AlertChange(record.Alert, AlertEvent.Acknowledged)], cancellationToken)
            .ConfigureAwait(false);

        return new AlertOutcome { Changed = Describe(record) };
    }

    /// <summary>
    /// Closes an alert with the reason the keeper gave, and re-arms the dwell key behind it.
    /// </summary>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in account; also the actor recorded.</param>
    /// <param name="request">Reason and note as the caller sent them.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account write permission, so may close another account's alert
    /// (2026-10-25). The dwell key it re-arms belongs to that other account's enclosure, which is the point: the
    /// excursion is re-armed wherever it lives, not only where the caller's own enclosures are.
    /// </param>
    public async Task<AlertOutcome> ResolveAsync(
        long alertId,
        Guid userId,
        ResolveAlertRequest request,
        AuditActor actor,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Parsed here rather than at the endpoint so every refusal this use case produces — an unknown reason, an
        // over-long note — reaches the client in the same field-level shape.
        if (request.Reason is not { } reasonText
            || !Enum.TryParse<ResolvedReason>(reasonText.Trim(), ignoreCase: true, out var reason))
        {
            const string Message = "reason is required and must be one of: Recovered, FalsePositive, SensorFault, Accepted.";

            return AlertOutcome.Failed(new AlertProblem(
                "validation_failed",
                Message,
                [new IdentityViolation("reason", "resolve_reason_invalid", Message)]));
        }

        var trimmedNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (trimmedNote is { Length: > ResolveNoteMaxLength })
        {
            return AlertOutcome.Failed(new AlertProblem(
                "validation_failed",
                "The note is too long.",
                [new IdentityViolation(
                    "note",
                    "note_too_long",
                    $"A note may be at most {ResolveNoteMaxLength} characters.")]));
        }

        var record = await LocateAsync(alertId, userId, mayReachAnyAccount, cancellationToken).ConfigureAwait(false);

        if (record is null)
        {
            return AlertOutcome.NotFound();
        }

        var now = clock.UtcNow;

        if (!AlertLifecycle.Resolve(record.Alert, userId, reason, now))
        {
            return AlertOutcome.Failed(new AlertProblem("alert_not_open", "The alert is no longer open."));
        }

        if (AlertLifecycle.RearmsItsKey(record.Alert) && record.Alert.Metric is { } metric)
        {
            var state = await store
                .FindExcursionStateAsync(record.Alert.TerrariumId, metric, record.Alert.Phase, cancellationToken)
                .ConfigureAwait(false);

            state?.Rearm();
        }

        store.AddAuditEntry(AuditLog.ForAlert(
            AuditAction.AlertResolved,
            record.Alert.Id,
            record.Alert.DeviceId,
            userId,
            actor,
            now,
            afterJson: JsonSerializer.Serialize(new { reason = reason.ToString(), note = trimmedNote })));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await BroadcastAsync([new AlertChange(record.Alert, AlertEvent.Resolved)], cancellationToken)
            .ConfigureAwait(false);

        return new AlertOutcome { Changed = Describe(record) };
    }

    /// <summary>
    /// The alert a request is about, from the scope the caller is allowed to see.
    /// </summary>
    /// <remarks>
    /// Two scopes, one answer. An ordinary keeper asks for their own alerts only, so an alert of somebody else's
    /// enclosure answers <c>null</c> — which the routes turn into <c>404</c>, and which is why those ids cannot be
    /// probed (BR-02.2). An account holding the cross-account permission asks for any account's alert, because the
    /// administrator's audit list already names those rows and a row that cannot be opened cannot be acted on
    /// (2026-10-25). Both scopes keep the join that applies the soft-delete filter, so an alert of a deleted
    /// enclosure stays a historical record for everybody.
    /// </remarks>
    /// <param name="alertId">Alert identity.</param>
    /// <param name="userId">Signed-in caller.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private Task<AlertRecord?> LocateAsync(
        long alertId,
        Guid userId,
        bool mayReachAnyAccount,
        CancellationToken cancellationToken) =>
        mayReachAnyAccount
            ? store.FindAnyAsync(alertId, cancellationToken)
            : store.FindOwnedAsync(alertId, userId, cancellationToken);

    /// <summary>
    /// Pushes committed moves, and treats a failed push as a lost push rather than a failed request.
    /// </summary>
    /// <remarks>
    /// The same rule the ingest fan-out follows (FR-09): the change is committed, so a SignalR outage must not turn
    /// a successful acknowledgement into a 500 the client would retry. Guarded even though the port requires
    /// implementations not to throw, because a broken hub registration would otherwise surface as "I could not
    /// acknowledge this alert" when the acknowledgement did happen.
    /// </remarks>
    private async Task BroadcastAsync(IReadOnlyList<AlertChange> changes, CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster
                .BroadcastAlertsAsync(changes.Select(AlertChangedPayload.From).ToArray(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Swallowed on purpose; the clients that missed it poll GET /alerts and see the same state.
        }
    }

    /// <summary>Projects a stored row onto the response shape.</summary>
    private static AlertView Describe(AlertRecord record)
    {
        var alert = record.Alert;

        return new AlertView(
            alert.Id,
            alert.TerrariumId,
            record.DevicePublicId,
            alert.Metric is { } metric ? MetricDictionary.Get(metric).ApiKey : null,
            AlertFamilies.ApiKey(alert.Family),
            alert.Severity.ToString(),
            alert.State.ToString(),
            alert.Source.ToString(),
            alert.Phase.ToString(),
            alert.TriggeringValue,
            alert.PeakValue,
            alert.BandMin,
            alert.BandMax,
            alert.TriggeredAt,
            alert.LastObservedAt,
            alert.AcknowledgedAt,
            alert.ResolvedAt,
            alert.ResolvedReason?.ToString(),
            alert.Duration is { } duration ? (long)Math.Round(duration.TotalSeconds) : null);
    }

    /// <summary>
    /// Names a delivery step the way a reader needs it: the channel it went out on, and — when policy withheld it —
    /// the reason, so the timeline and the report's suppression table agree.
    /// </summary>
    private static string Describe(NotificationAttemptRecord delivery) =>
        delivery.Status == NotificationStatus.Suppressed
            ? $"{delivery.Channel}: {delivery.Reason ?? NotificationReason.NoChannel}"
            : delivery.Step > 0
                ? $"{delivery.Channel}: escalation step {delivery.Step}"
                : $"{delivery.Channel}: {delivery.Reason ?? "sent"}";

    /// <summary>Reads the keeper's note back out of the audit row the resolution wrote.</summary>
    private static string? NoteFrom(string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(afterJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(afterJson);

            return document.RootElement.TryGetProperty("note", out var note) ? note.GetString() : null;
        }
        catch (JsonException)
        {
            // A row written by an older or newer shape is not worth failing a timeline over.
            return null;
        }
    }
}
