using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Domain.Evaluation;

/// <summary>What one evaluated reading means for the alert lifecycle (§02-design/03 §4.2).</summary>
public enum DecisionKind
{
    /// <summary>Nothing to report — inside the band, or outside it for less than the dwell time.</summary>
    None = 0,

    /// <summary>A new episode: dwell satisfied and nothing was open.</summary>
    Open = 1,

    /// <summary>The open episode got worse: the critical band has been held for its own dwell.</summary>
    Escalate = 2,

    /// <summary>The open episode continues; only its last-seen instant and peak move.</summary>
    Touch = 3,

    /// <summary>The open episode is over: three consecutive readings inside the band by the recovery margin.</summary>
    Resolve = 4,
}

/// <summary>
/// The decision one reading produces, with everything the caller needs to write it down.
/// </summary>
/// <param name="Kind">What to do.</param>
/// <param name="Severity">Severity to record: <see cref="AlertSeverity.Warning"/> or <see cref="AlertSeverity.Critical"/> on <see cref="DecisionKind.Open"/>, Critical on <see cref="DecisionKind.Escalate"/>.</param>
/// <param name="ObservedValue">The reading that produced the decision, for peak tracking on <see cref="DecisionKind.Touch"/>.</param>
/// <param name="TriggeredAt">
/// Start of the excursion on <see cref="DecisionKind.Open"/> — the first out-of-band reading, <b>not</b> the instant
/// the dwell expired. This is design note 1 of §02-design/03 §4.2: an alert that says "out of range for 9 min" is
/// telling the truth, one that says "4 min" is not.
/// </param>
public sealed record ThresholdDecision(
    DecisionKind Kind,
    AlertSeverity? Severity = null,
    decimal? ObservedValue = null,
    DateTimeOffset? TriggeredAt = null)
{
    /// <summary>Nothing to report.</summary>
    public static readonly ThresholdDecision None = new(DecisionKind.None);

    /// <summary>A new episode, back-dated to the reading that started it.</summary>
    public static ThresholdDecision Open(AlertSeverity severity, DateTimeOffset triggeredAt) =>
        new(DecisionKind.Open, severity, TriggeredAt: triggeredAt);

    /// <summary>The open episode escalated; the alert row keeps its identity.</summary>
    public static ThresholdDecision Escalate() =>
        new(DecisionKind.Escalate, AlertSeverity.Critical);

    /// <summary>The open episode continues with this reading.</summary>
    public static ThresholdDecision Touch(decimal value) =>
        new(DecisionKind.Touch, ObservedValue: value);

    /// <summary>The open episode recovered.</summary>
    public static ThresholdDecision Resolve() => new(DecisionKind.Resolve);

    /// <summary>Recovery ticks needed before an open episode is resolved (§02-design/03 §4.2, BR-11.4).</summary>
    public const int RecoveryTicks = 3;

    /// <summary>
    /// The dwell/hysteresis/escalation rule of §02-design/03 §4.2, as one pure function over a band, a value and
    /// the key's own state.
    /// </summary>
    /// <remarks>
    /// <b>Why this is a separate, static, dependency-free method.</b> It is the algorithm the whole product exists
    /// to get right, and it is the only part of the backend that can be exhaustively tested in milliseconds
    /// (`TC-U-10…20`). Everything it needs is an argument; nothing is fetched, logged or measured. The state it
    /// mutates is the caller's <see cref="EvaluationState"/>, deliberately: the design's tests assert the decision
    /// *and* the mutated state together, and a copy would let the two disagree.
    /// <para>
    /// <b>Three readings of the design, settled here and stated in the document.</b> First, the dwell comparisons
    /// measure against <paramref name="nowUtc"/> — the evaluation instant — while the instants recorded into the
    /// state are the sample's own <paramref name="observedAt"/>, exactly as the reference implementation in
    /// `03-implementation/03` §4 writes them; the two coincide for a live sample and differ for a back-filled one,
    /// where the excursion did last that long and the alert is for the record. Second, a reading inside the target
    /// band <b>ends the excursion</b> whether or not it met the recovery margin: the margin decides when an *open
    /// alert* may close, not when the dwell window re-arms, so a value hovering a hair inside the band cannot mean
    /// that the next excursion inherits the previous one's start. Third, escalation happens once per episode, which
    /// needs the severity the open alert already has — a fact the schema keeps on the alert, not on the state
    /// (§07-appendices/02 §3.9 has no severity column), so the caller passes it in rather than this method guessing.
    /// </para>
    /// </remarks>
    /// <param name="band">The band in force for this metric and phase.</param>
    /// <param name="value">The evaluated reading.</param>
    /// <param name="observedAt">When the reading was taken; the instant anything is dated with.</param>
    /// <param name="state">The key's state, mutated in place.</param>
    /// <param name="nowUtc">The evaluation instant the dwell windows are measured against.</param>
    /// <param name="openAlertSeverity">
    /// Severity of the alert <see cref="EvaluationState.OpenAlertId"/> points at, or null when no episode is open.
    /// </param>
    public static ThresholdDecision Decide(
        ThresholdBand band,
        decimal value,
        DateTimeOffset observedAt,
        EvaluationState state,
        DateTimeOffset nowUtc,
        AlertSeverity? openAlertSeverity = null)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(state);

        if (band.IsHot(value) || band.IsCold(value))
        {
            return OutsideTheBand(band, value, observedAt, state, nowUtc, openAlertSeverity);
        }

        // Inside the target band: the excursion is over, whether or not it produced an alert. Clearing the start
        // here is what makes the dwell window re-arm rather than accumulate across unrelated excursions.
        state.Violation = ViolationKind.None;
        state.FirstOutOfBandAt = null;
        state.CriticalSinceAt = null;

        state.ConsecutiveRecoveryTicks = band.IsRecovered(value) ? state.ConsecutiveRecoveryTicks + 1 : 0;

        return state.OpenAlertId is not null && state.ConsecutiveRecoveryTicks >= RecoveryTicks
            ? Resolve()
            : None;
    }

    private static ThresholdDecision OutsideTheBand(
        ThresholdBand band,
        decimal value,
        DateTimeOffset observedAt,
        EvaluationState state,
        DateTimeOffset nowUtc,
        AlertSeverity? openAlertSeverity)
    {
        state.Violation = band.IsHot(value) ? ViolationKind.Hot : ViolationKind.Cold;
        state.FirstOutOfBandAt ??= observedAt;

        // The critical excursion is its own dwell window, and it is consecutive: a single reading back inside the
        // critical band clears it, so "crit for one minute then not" cannot add up to a critical escalation.
        if (band.IsCritical(value))
        {
            state.CriticalSinceAt ??= observedAt;
        }
        else
        {
            state.CriticalSinceAt = null;
        }

        state.ConsecutiveRecoveryTicks = 0;

        var sustained = nowUtc - state.FirstOutOfBandAt.Value >= TimeSpan.FromMinutes(band.DwellWarnMinutes);
        var criticalSustained = state.CriticalSinceAt is { } since
            && nowUtc - since >= TimeSpan.FromMinutes(band.DwellCritMinutes);

        if (state.OpenAlertId is null)
        {
            return sustained
                ? Open(band.IsCritical(value) ? AlertSeverity.Critical : AlertSeverity.Warning,
                    state.FirstOutOfBandAt.Value)
                : None;
        }

        // Already open: escalate at most once per episode, then keep the row's last-seen instant and peak moving.
        if (criticalSustained && openAlertSeverity != AlertSeverity.Critical)
        {
            return Escalate();
        }

        return Touch(value);
    }
}
