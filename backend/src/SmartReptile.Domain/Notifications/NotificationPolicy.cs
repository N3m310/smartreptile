using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Notifications;

/// <summary>A quiet-hours window, as the pair of local times a user configured (BR-13.3).</summary>
/// <remarks>
/// The window is in the <b>user's</b> zone, not the terrarium's: the person who must not be woken is the person
/// whose clock the window names. It may cross midnight ("22:00–06:00"), which is why <see cref="Contains"/> is not
/// a pair of comparisons.
/// <para>
/// A pair whose two ends are equal is treated as <b>no window</b> rather than as all day. The profile route already
/// refuses half a pair (<c>quiet_hours_incomplete</c>), so the only way to reach this case is a deliberate
/// <c>22:00–22:00</c>, and reading that as "never notify" would turn a typo into a silent alarm system.
/// </para>
/// </remarks>
/// <param name="Start">Local time the window opens (inclusive).</param>
/// <param name="End">Local time the window closes (exclusive).</param>
public readonly record struct QuietHours(TimeOnly Start, TimeOnly End)
{
    /// <summary>Builds the window from a stored pair, or null when the pair is absent or empty.</summary>
    /// <param name="start">Stored start, if any.</param>
    /// <param name="end">Stored end, if any.</param>
    public static QuietHours? From(TimeOnly? start, TimeOnly? end) =>
        start is { } from && end is { } to && from != to ? new QuietHours(from, to) : null;

    /// <summary>True while <paramref name="localTime"/> is inside the window.</summary>
    /// <param name="localTime">Wall-clock time in the recipient's zone.</param>
    public bool Contains(TimeOnly localTime) =>
        Start < End
            ? localTime >= Start && localTime < End
            : localTime >= Start || localTime < End;
}

/// <summary>
/// Everything the matrix of §02-design/05 §4 needs to judge one alert for one recipient.
/// </summary>
/// <param name="Severity">Severity of the alert or escalation.</param>
/// <param name="MinSeverity">The recipient's <c>MinNotifySeverity</c>.</param>
/// <param name="Resolved">
/// True when this is a recovery notice: the alert has just resolved, so the decision is the user's opt-in rather
/// than the state of the habitat.
/// </param>
/// <param name="Silenced">True when an active silence window covers the alert's metric.</param>
/// <param name="Maintenance">True when the device is in <c>Maintenance</c> (FR-07).</param>
/// <param name="QuietHours">True when the recipient is inside their quiet hours right now.</param>
/// <param name="Coalesced">True when the terrarium has already reached its hourly cap (§5).</param>
public readonly record struct NotificationRequest(
    AlertSeverity Severity,
    AlertSeverity MinSeverity,
    bool Resolved,
    bool Silenced,
    bool Maintenance,
    bool QuietHours,
    bool Coalesced);

/// <summary>What policy decided, and why it says so in the record.</summary>
/// <param name="Send">True when at least one channel should attempt delivery.</param>
/// <param name="Reason">
/// Null on an ordinary send; one of the <see cref="NotificationReason"/> values otherwise. A send that carried a
/// bypass marker (<see cref="NotificationReason.BypassedQuietHours"/>) is a send <em>and</em> says why it was
/// allowed through — which is what TC-U-38 asks the record to keep.
/// </param>
public readonly record struct NotificationVerdict(bool Send, string? Reason)
{
    /// <summary>An ordinary send: no suppression, no bypass.</summary>
    public static NotificationVerdict Allowed() => new(true, null);

    /// <summary>A send that policy allowed through a window it would otherwise respect.</summary>
    /// <param name="reason">The bypass marker.</param>
    public static NotificationVerdict Bypassed(string reason) => new(true, reason);

    /// <summary>Nothing is sent, and the record says why.</summary>
    /// <param name="reason">The suppression reason.</param>
    public static NotificationVerdict Suppressed(string reason) => new(false, reason);
}

/// <summary>
/// The notification decision flow of §02-design/05 §4 as a pure function: one verdict per (alert, recipient),
/// with no I/O, no clock and no database.
/// </summary>
/// <remarks>
/// <b>This is the whole of "why was I not told".</b> The reasons it returns are the ones the report's suppression
/// table groups by, so the order of the checks is part of the contract rather than an implementation detail: a
/// silenced alert during quiet hours is <c>silenced_metric</c>, because the silence is the more specific decision a
/// human made. The order is §4's own flowchart.
/// <para>
/// <b>Critical bypasses quiet hours, Warning does not</b> (§1: "Push always; bypasses quiet hours"). That is the
/// one cell where severity changes <em>whether</em> rather than <em>how</em>, and it is why the verdict carries a
/// reason on a send.
/// </para>
/// <para>
/// The hourly cap is checked last, after quiet hours: an alert that would have been withheld by a window is not
/// evidence that the hour is busy, and counting it would let silent hours spend the recipient's budget.
/// </para>
/// </remarks>
public static class NotificationPolicy
{
    /// <summary>Judges one alert for one recipient.</summary>
    /// <param name="request">The matrix inputs.</param>
    /// <returns>The verdict, whose <see cref="NotificationVerdict.Reason"/> is what the record keeps.</returns>
    public static NotificationVerdict Judge(NotificationRequest request)
    {
        // §4: a resolution is a courtesy, and the courtesy is opt-in — so it stops here rather than at the
        // severity gate. v1 has no such preference (User has no column for one), which is what makes this branch
        // the honest answer for every resolution rather than a dead one: the record says "withheld by preference"
        // and the report can say "no recovery notices are sent in v1" without pretending they were attempted.
        if (request.Resolved)
        {
            return NotificationVerdict.Suppressed(NotificationReason.Preference);
        }

        if (request.Silenced)
        {
            return NotificationVerdict.Suppressed(NotificationReason.SilencedMetric);
        }

        if (request.Maintenance)
        {
            return NotificationVerdict.Suppressed(NotificationReason.Maintenance);
        }

        if (request.Severity < request.MinSeverity)
        {
            return NotificationVerdict.Suppressed(NotificationReason.BelowMinSeverity);
        }

        var bypassed = false;

        if (request.QuietHours)
        {
            if (request.Severity < AlertSeverity.Critical)
            {
                return NotificationVerdict.Suppressed(NotificationReason.QuietHours);
            }

            bypassed = true;
        }

        if (request.Coalesced)
        {
            return NotificationVerdict.Suppressed(NotificationReason.DigestCoalesced);
        }

        return bypassed
            ? NotificationVerdict.Bypassed(NotificationReason.BypassedQuietHours)
            : NotificationVerdict.Allowed();
    }
}

/// <summary>
/// The repeat, retry, escalation and digest rules of §02-design/05 §5–§6 and BR-13.5 — the arithmetic of the
/// notification layer, kept out of the dispatcher so it can be asserted without a clock or a database.
/// </summary>
/// <remarks>
/// <b>Retries are per channel, escalations are per alert.</b> A push that FCM refuses is retried on the push's own
/// schedule and its three attempts say nothing about the inbox row, which cannot fail; an escalation is a new
/// message to a human, so it is the alert's progress that advances it.
/// </remarks>
public static class NotificationCadence
{
    /// <summary>§5: an alert that is still open re-notifies at most this often.</summary>
    public static readonly TimeSpan RepeatCooldown = TimeSpan.FromMinutes(60);

    /// <summary>§5: notifications a terrarium may send within one hour before the cap coalesces them.</summary>
    public const int HourlyCap = 10;

    /// <summary>BR-13.5: attempts per channel, including the first.</summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// BR-13.5's backoff, as the delay <b>before</b> each retry: 5 s, 30 s, 2 min. The last entry is the wait after
    /// the final attempt, which is what makes the schedule's end instant 155 s rather than 35 s (TC-U-41).
    /// </summary>
    public static readonly TimeSpan[] RetryBackoff =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
    ];

    /// <summary>§6: the escalation ladder for a Critical alert, measured from <c>TriggeredAt</c>.</summary>
    public static readonly TimeSpan[] EscalationLadder =
    [
        TimeSpan.FromMinutes(30),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromMinutes(120),
    ];

    /// <summary>When attempt number <paramref name="attempt"/> is due, counting from <paramref name="firstAtUtc"/>.</summary>
    /// <param name="firstAtUtc">Instant of the first attempt.</param>
    /// <param name="attempt">Attempt number, 1-based.</param>
    public static DateTimeOffset AttemptScheduledAt(DateTimeOffset firstAtUtc, int attempt)
    {
        var elapsed = TimeSpan.Zero;

        for (var index = 1; index < attempt && index <= RetryBackoff.Length; index++)
        {
            elapsed += RetryBackoff[index - 1];
        }

        return firstAtUtc + elapsed;
    }

    /// <summary>
    /// The instant the schedule gives up: after the final attempt's backoff has been waited out, which is the
    /// "then <c>Failed</c> with <c>LastError</c>" of TC-U-41.
    /// </summary>
    /// <param name="firstAtUtc">Instant of the first attempt.</param>
    public static DateTimeOffset ScheduleEndsAt(DateTimeOffset firstAtUtc) =>
        AttemptScheduledAt(firstAtUtc, MaxAttempts) + RetryBackoff[^1];

    /// <summary>
    /// True when another attempt is due: the channel is under the attempt cap and the next backoff has elapsed.
    /// </summary>
    /// <param name="attemptsMade">Attempts already made, 1-based count.</param>
    /// <param name="lastAttemptAtUtc">Instant of the most recent attempt.</param>
    /// <param name="nowUtc">Current instant.</param>
    public static bool RetryDue(int attemptsMade, DateTimeOffset lastAttemptAtUtc, DateTimeOffset nowUtc) =>
        attemptsMade > 0
        && attemptsMade < MaxAttempts
        && nowUtc >= lastAttemptAtUtc + RetryBackoff[attemptsMade - 1];

    /// <summary>
    /// The next escalation step an alert owes, or 0 when none is due. Only Critical alerts escalate, escalation
    /// stops at the first acknowledgement, and a step that has already been delivered is never repeated — so a
    /// dispatcher that was down for two hours sends the *next* step rather than the three it missed.
    /// </summary>
    /// <param name="severity">Severity of the alert.</param>
    /// <param name="sinceTriggered">Elapsed time since <c>TriggeredAt</c>.</param>
    /// <param name="acknowledged">True when a human has taken ownership.</param>
    /// <param name="stepDelivered">Highest step already delivered (0 is the first delivery, 1–3 the ladder).</param>
    public static byte EscalationDue(
        AlertSeverity severity,
        TimeSpan sinceTriggered,
        bool acknowledged,
        byte stepDelivered)
    {
        if (severity != AlertSeverity.Critical || acknowledged)
        {
            return 0;
        }

        if (stepDelivered >= NotificationLog.MaxStep)
        {
            return 0;
        }

        var next = (byte)(stepDelivered + 1);

        return sinceTriggered >= EscalationLadder[next - 1] ? next : (byte)0;
    }

    /// <summary>
    /// True when the hourly cap has already been reached for a terrarium, which is what turns the eleventh
    /// notification of an hour into a digest entry (§5, TC-U-40).
    /// </summary>
    /// <param name="sentThisHour">Notifications already sent to the terrarium in the current hour.</param>
    public static bool CapReached(int sentThisHour) => sentThisHour >= HourlyCap;

    /// <summary>The start of the hour <paramref name="instant"/> falls in, in the zone the cap is counted in.</summary>
    /// <param name="instant">Instant to truncate.</param>
    public static DateTimeOffset HourStart(DateTimeOffset instant) =>
        new(instant.Year, instant.Month, instant.Day, instant.Hour, 0, 0, instant.Offset);
}

/// <summary>One alert counted into a digest, as the digest itself needs to see it.</summary>
/// <param name="Severity">Severity the alert carried.</param>
/// <param name="Metric">Metric it was about, or null for a device-level alert.</param>
/// <param name="PeakValue">Peak value of the episode, when it had one.</param>
public readonly record struct DigestSource(AlertSeverity Severity, MetricCode? Metric, decimal? PeakValue);

/// <summary>What §5's digest says: "one message: 7 warnings, 1 critical, worst 34.2 °C".</summary>
/// <param name="Total">Alerts coalesced into the hour.</param>
/// <param name="Warnings">How many of them were Warnings.</param>
/// <param name="Criticals">How many were Criticals.</param>
/// <param name="WorstMetric">The metric the hour was mostly about, or null when no entry named one.</param>
/// <param name="PeakValue">Highest peak among the entries for <paramref name="WorstMetric"/>.</param>
public readonly record struct DigestSummary(
    int Total,
    int Warnings,
    int Criticals,
    MetricCode? WorstMetric,
    decimal? PeakValue);

/// <summary>The digest's arithmetic, as a pure function (TC-U-45).</summary>
public static class NotificationDigest
{
    /// <summary>Counts an hour's coalesced alerts and names the worst of them.</summary>
    /// <param name="sources">The alerts withheld by the cap during the hour.</param>
    public static DigestSummary Summarise(IReadOnlyList<DigestSource> sources)
    {
        if (sources.Count == 0)
        {
            return new DigestSummary(0, 0, 0, null, null);
        }

        var warnings = sources.Count(source => source.Severity == AlertSeverity.Warning);
        var criticals = sources.Count(source => source.Severity == AlertSeverity.Critical);

        // "Worst metric" is the one the hour was mostly about, and the peak is the highest peak *of that metric*:
        // 34.2 °C and 80 %RH are not comparable numbers, so ranking peaks across metrics would produce a figure
        // that means nothing. A metric beats another by count first, then by peak, so the order is total.
        var worst = sources
            .Where(source => source.Metric is not null)
            .GroupBy(source => source.Metric!.Value)
            .Select(group => new
            {
                Metric = group.Key,
                Count = group.Count(),
                Peak = group.Max(source => source.PeakValue),
            })
            .OrderByDescending(entry => entry.Count)
            .ThenByDescending(entry => entry.Peak ?? decimal.MinValue)
            .FirstOrDefault();

        return new DigestSummary(sources.Count, warnings, criticals, worst?.Metric, worst?.Peak);
    }
}
