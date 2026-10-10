using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Evaluation;

/// <summary>
/// Writes the alert lifecycle the evaluator decided on: the row a new episode creates, and the three changes an
/// episode can take afterwards.
/// </summary>
/// <remarks>
/// Separated from <see cref="SampleEvaluator"/> because the two answer different questions — "what does this
/// reading mean?" is the decision, "which fields does that change?" is this — and because the field-by-field
/// version of the second is the part the alert card, the inbox and the report all read back. Kept static and
/// dependency-free so a test can assert one alert row without a store or a clock.
/// <para>
/// Evaluation writes alerts and <b>does not send them</b> (§02-design/03 §5): delivery is the dispatcher's, and a
/// severity change here is a row the notification layer will notice, not a push this class performs.
/// </para>
/// </remarks>
public static class ThresholdAlertWriter
{
    /// <summary>
    /// A new episode. The alert is dated from the reading that started it rather than from the reading that
    /// satisfied the dwell (§02-design/03 §4.2 note 1), and it carries the band in force at that moment, because
    /// editing the thresholds afterwards must not rewrite what this alert was judged against (UC-03 A4).
    /// </summary>
    /// <param name="band">Band in force.</param>
    /// <param name="context">Terrarium context the episode belongs to.</param>
    /// <param name="sample">Sample whose reading triggered the episode, for the device and the last-seen instant.</param>
    /// <param name="key">The <c>(metric, phase)</c> the episode is tracked under.</param>
    /// <param name="decision">The opening decision.</param>
    /// <param name="triggeringValue">Value of the first out-of-band reading.</param>
    /// <param name="peakValue">Worst value seen so far in the episode.</param>
    public static Alert Open(
        ThresholdBand band,
        EvaluationContext context,
        PersistedSample sample,
        (MetricCode Metric, ThresholdPhase Phase) key,
        ThresholdDecision decision,
        decimal triggeringValue,
        decimal peakValue)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(decision);

        var severity = decision.Severity ?? AlertSeverity.Warning;

        return new Alert
        {
            TerrariumId = context.TerrariumId,
            DeviceId = sample.DeviceId,
            Metric = key.Metric,
            Phase = key.Phase,
            Severity = severity,
            DedupeKey = Alert.DedupeKeyFor(context.TerrariumId, key.Metric, severity, key.Phase),
            State = AlertState.Open,
            Source = AlertSource.Threshold,
            TriggeringValue = triggeringValue,
            PeakValue = peakValue,
            BandMin = band.TargetMin,
            BandMax = band.TargetMax,
            TriggeredAt = decision.TriggeredAt ?? sample.RecordedAt,
            LastObservedAt = sample.RecordedAt,

            // Message stays null on purpose: §02-design/05 §7 renders the notification text from these fields and
            // localises it per user (ADR-013), so a sentence stored here would be a second, unlocalisable copy of
            // a message the clients already know how to build.
        };
    }

    /// <summary>
    /// The episode continues: the row's last-seen instant moves, and its peak moves only outward. The peak is the
    /// maximum for a hot excursion and the minimum for a cold one, because "worst" points in opposite directions.
    /// </summary>
    /// <param name="alert">The open alert row.</param>
    /// <param name="direction">Direction in force, from the state.</param>
    /// <param name="value">The reading being recorded.</param>
    /// <param name="observedAt">When that reading was taken.</param>
    public static void Touch(Alert alert, ViolationKind direction, decimal value, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.LastObservedAt = observedAt;
        alert.PeakValue = direction == ViolationKind.Cold
            ? Math.Min(alert.PeakValue ?? value, value)
            : Math.Max(alert.PeakValue ?? value, value);
    }

    /// <summary>
    /// The episode got worse. The row keeps its identity and its original <c>TriggeredAt</c> — the incident started
    /// when it started — but its severity, and therefore its dedupe key, become Critical.
    /// </summary>
    /// <param name="alert">The open alert row.</param>
    /// <param name="direction">Direction in force, from the state.</param>
    /// <param name="value">The critical reading being recorded.</param>
    /// <param name="observedAt">When that reading was taken.</param>
    public static void Escalate(Alert alert, ViolationKind direction, decimal value, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.Severity = AlertSeverity.Critical;
        alert.DedupeKey = Alert.DedupeKeyFor(alert.TerrariumId, alert.Metric, AlertSeverity.Critical, alert.Phase);
        Touch(alert, direction, value, observedAt);
    }

    /// <summary>
    /// The episode is over. <c>Recovered</c> is the only reason the engine writes: a human's <c>FalsePositive</c>
    /// or <c>Accepted</c> is the lifecycle API's (3.4), and inventing a third automatic reason would create an
    /// outcome the data does not support (§03-implementation/06 §1).
    /// </summary>
    /// <param name="alert">The open alert row.</param>
    /// <param name="observedAt">When the settling reading was taken.</param>
    public static void Resolve(Alert alert, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = observedAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
    }
}
