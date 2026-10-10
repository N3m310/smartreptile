using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Evaluation;

/// <summary>
/// The consumer of <see cref="IEvaluationQueue"/>: the point where a committed sample stops being a measurement
/// and starts being something the system has an opinion about.
/// </summary>
/// <remarks>
/// <b>What this does (FR-11, roadmap task 3.2).</b> For every plausible reading of every evaluable sample it
/// resolves the band in force, hands it to <see cref="ThresholdDecision.Decide"/>, and writes what the decision
/// says: a new alert, an escalation, a touch, or a recovery. The dwell window, the hysteresis and the escalation
/// rule all live in that one pure method; what is left here is bookkeeping — which key, which band, which row, and
/// which of them has to be saved first.
/// <para>
/// <b>The guard comes first, before any band is resolved.</b> §02-design/03 §4.2's opening line — a faulted or
/// physically impossible sample is stored as evidence and never judged (BR-11.1) — which is also why
/// <c>TC-I-03</c>'s "zero alerts created" holds for a stated reason rather than trivially.
/// </para>
/// <para>
/// <b>What this deliberately does not do.</b> It sends nothing: evaluation writes alerts and the dispatcher delivers
/// them (§02-design/03 §5, 3.5), so a new alert here is a row and a counter, not a push. It does not announce them
/// over SignalR either — <c>alertChanged</c> arrives with the lifecycle API that can also acknowledge and resolve
/// them (3.4) — and it does not derive the device-level signals of 3.3.
/// </para>
/// </remarks>
public sealed class SampleEvaluator(IEvaluationStore store, IClock clock)
{
    /// <summary>Advances the threshold engine for one committed batch.</summary>
    /// <param name="samples">Samples the ingest pipeline committed, in ingest order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<EvaluationOutcome> EvaluateAsync(
        IReadOnlyList<PersistedSample> samples,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return EvaluationOutcome.None;
        }

        var contexts = new Dictionary<Guid, EvaluationContext>();
        var advanced = 0;
        var skipped = 0;
        var opened = 0;
        var openedCritical = 0;
        var escalated = 0;
        var resolved = 0;

        foreach (var sample in samples)
        {
            // The design's first line, and the only one that needs no band: a faulted or physically impossible
            // sample is stored as evidence but must never move an excursion (BR-11.1).
            if (!QualityRules.IsEvaluable(sample.Flags))
            {
                skipped++;
                continue;
            }

            if (!contexts.TryGetValue(sample.TerrariumId, out var context))
            {
                context = await store.FindContextAsync(sample.TerrariumId, cancellationToken).ConfigureAwait(false);

                if (context is null)
                {
                    // The terrarium was deleted between the commit and this pass. Nothing to judge.
                    skipped++;
                    continue;
                }

                contexts[sample.TerrariumId] = context;
            }

            var local = clock.InZone(sample.RecordedAt, context.TimeZoneId);
            var localTimeOfDay = TimeOnly.FromDateTime(local.DateTime);
            var now = clock.UtcNow;

            foreach (var reading in sample.Readings)
            {
                if (!MetricDictionary.IsPlausible(reading.Code, reading.Value))
                {
                    skipped++;
                    continue;
                }

                // The band is resolved per reading, not per batch: a batch can straddle a phase change, and this is
                // the same resolver the read surface labels a card with (BR-10.3, BR-11.2).
                var effective = ThresholdResolver.Resolve(
                    reading.Code,
                    context.Overrides,
                    context.ProfileBands,
                    localTimeOfDay,
                    context.LightsOnLocalTime,
                    context.PhotoperiodHours);

                if (effective is null)
                {
                    // Nothing judges this metric at this phase, so there is no excursion to track and no row to keep.
                    continue;
                }

                var key = (effective.Metric, effective.Phase);

                if (!context.States.TryGetValue(key, out var state))
                {
                    state = new EvaluationState
                    {
                        TerrariumId = sample.TerrariumId,
                        Metric = key.Metric,
                        Phase = key.Phase,
                    };

                    store.AddState(state);

                    // Visible to the rest of this batch, so a second reading on the same key updates the staged row
                    // instead of staging a second one and colliding on the primary key.
                    context.States[key] = state;
                }
                else if (sample.SampleId <= state.LastEvaluatedSampleId)
                {
                    // Already judged. The watermark runs one way only: a replayed sample carries an id the evaluator
                    // has seen, and judging it again would count one dwell window twice.
                    continue;
                }

                var alert = Adopt(context, key, state);
                var decision = ThresholdDecision.Decide(
                    effective.Band,
                    reading.Value,
                    sample.RecordedAt,
                    state,
                    now,
                    alert?.Severity);

                switch (decision.Kind)
                {
                    case DecisionKind.Open:
                        {
                            var (triggering, peak) = await EpisodeValuesAsync(
                                context, key.Metric, decision.TriggeredAt!.Value, sample, reading.Value, state, cancellationToken)
                                .ConfigureAwait(false);

                            var openedAlert = ThresholdAlertWriter.Open(
                                effective.Band, context, sample, key, decision, triggering, peak);

                            store.AddAlert(openedAlert);
                            context.OpenAlerts.Add(openedAlert);

                            // Committed before the state points at it. The alert is the durable record and the pointer
                            // is the optimisation, so an interruption between the two leaves a state this pass can
                            // repair — an open alert the pointer does not name — rather than a state naming a row that
                            // was never written.
                            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                            state.OpenAlertId = openedAlert.Id;
                            opened++;

                            if (openedAlert.Severity == AlertSeverity.Critical)
                            {
                                openedCritical++;
                            }

                            break;
                        }

                    case DecisionKind.Escalate:
                        ThresholdAlertWriter.Escalate(alert!, state.Violation, reading.Value, sample.RecordedAt);
                        escalated++;
                        break;

                    case DecisionKind.Touch:
                        ThresholdAlertWriter.Touch(alert!, state.Violation, reading.Value, sample.RecordedAt);
                        break;

                    case DecisionKind.Resolve:
                        ThresholdAlertWriter.Resolve(alert!, sample.RecordedAt);
                        state.OpenAlertId = null;
                        resolved++;
                        break;

                    default:
                        break;
                }

                if (sample.SampleId > state.LastEvaluatedSampleId)
                {
                    state.LastEvaluatedSampleId = sample.SampleId;
                    advanced++;
                }
            }
        }

        if (advanced > 0)
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new EvaluationOutcome(advanced, skipped, opened, openedCritical, escalated, resolved);
    }

    /// <summary>
    /// The open episode for one key, with the state's pointer reconciled to it.
    /// </summary>
    /// <remarks>
    /// The pointer is an optimisation — <c>OpenAlertId</c> saves the lookup — so it is repaired here rather than
    /// trusted. It can be wrong two ways: a human resolved the alert through the lifecycle API while the state row
    /// still names it, and a crash between the alert insert and the pointer's commit. Adopting the row that is
    /// actually open fixes both, and it is what stops the evaluator opening a second alert for one episode only to
    /// be refused by the filtered unique index (DI-01).
    /// </remarks>
    private static Alert? Adopt(
        EvaluationContext context,
        (MetricCode Metric, ThresholdPhase Phase) key,
        EvaluationState state)
    {
        var open = context.OpenAlerts.FirstOrDefault(alert =>
            alert.State != AlertState.Resolved && alert.Metric == key.Metric && alert.Phase == key.Phase);

        // A newly opened alert has an identity only after its insert, and that is exactly where the evaluator sets
        // this itself, so the reconciliation never has to invent one.
        state.OpenAlertId = open is { Id: > 0 } ? open.Id : null;

        return open;
    }

    /// <summary>
    /// The first and the worst value of the episode so far, read back from the readings the evaluator already
    /// judged rather than kept in a second copy inside <see cref="EvaluationState"/>.
    /// </summary>
    /// <remarks>
    /// Both facts belong to the alert contract — <c>TriggeringValue</c> is "value at first violation" and
    /// <c>PeakValue</c> is "extremum during the episode" (§07-appendices/02 §3.8) — and both are needed at a moment
    /// (the alert opening) that is later than the readings they describe, by exactly the dwell window. Those
    /// readings are still stored, so the window is read back from them: a handful of rows, once per alert, instead
    /// of a column that would have to be rewritten on every sample to stay in step.
    /// </remarks>
    private async Task<(decimal Triggering, decimal Peak)> EpisodeValuesAsync(
        EvaluationContext context,
        MetricCode metric,
        DateTimeOffset triggeredAt,
        PersistedSample sample,
        decimal value,
        EvaluationState state,
        CancellationToken cancellationToken)
    {
        var values = await store
            .ReadEpisodeValuesAsync(context.TerrariumId, metric, triggeredAt, sample.RecordedAt, cancellationToken)
            .ConfigureAwait(false);

        if (values.Count == 0)
        {
            // Retention removed the window, or the clock disagreed with the stored timestamps: the alert is still
            // correct, it just cannot report anything older than the reading that opened it.
            return (value, value);
        }

        return (values[0], state.Violation == ViolationKind.Cold ? values.Min() : values.Max());
    }
}
