using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Ingest;
using SmartReptile.Application.Signals;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Signals;
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
    /// <param name="samples">Committed samples released in per-device event-time order.</param>
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
        var changes = new List<AlertChange>();
        var derivedMoved = false;

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
                else if (state.LastEvaluatedAtUtc is { } lastEvaluatedAt &&
                         CompareSampleOrder(sample.RecordedAt, sample.Sequence, lastEvaluatedAt, state.LastEvaluatedSequence) <= 0)
                {
                    // The persisted event-time watermark makes retries idempotent without confusing database identity
                    // order with device event order.
                    skipped++;
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
                            changes.Add(new AlertChange(openedAlert, AlertEvent.Opened));
                            opened++;

                            if (openedAlert.Severity == AlertSeverity.Critical)
                            {
                                openedCritical++;
                            }

                            break;
                        }

                    case DecisionKind.Escalate:
                        ThresholdAlertWriter.Escalate(alert!, state.Violation, reading.Value, sample.RecordedAt);
                        changes.Add(new AlertChange(alert!, AlertEvent.Escalated));
                        escalated++;
                        break;

                    case DecisionKind.Touch:
                        ThresholdAlertWriter.Touch(alert!, state.Violation, reading.Value, sample.RecordedAt);
                        break;

                    case DecisionKind.Resolve:
                        ThresholdAlertWriter.Resolve(alert!, sample.RecordedAt);
                        state.OpenAlertId = null;
                        changes.Add(new AlertChange(alert!, AlertEvent.Resolved));
                        resolved++;
                        break;

                    default:
                        break;
                }

                state.LastEvaluatedSampleId = Math.Max(state.LastEvaluatedSampleId, sample.SampleId);
                state.LastEvaluatedAtUtc = sample.RecordedAt;
                state.LastEvaluatedSequence = sample.Sequence;
                advanced++;
            }

            // The one derived signal that needs a single sample's two readings: no band compares surface against air,
            // so a surface that is 15 °C hotter than the air it sits in would pass both bands on their own.
            var gradient = ApplyGradient(context, sample);

            if (gradient.Moved)
            {
                derivedMoved = true;
            }

            opened += gradient.Opened;
            resolved += gradient.Resolved;

            if (gradient.Change is { } gradientChange)
            {
                changes.Add(gradientChange);
            }
        }

        if (advanced > 0 || derivedMoved)
        {
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new EvaluationOutcome(advanced, skipped, opened, openedCritical, escalated, resolved)
        {
            AlertChanges = changes,
        };
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
    /// <para>
    /// The <see cref="AlertSource.Threshold"/> filter is what keeps a derived signal out of the band engine's
    /// episode: a <c>GradientWarning</c> names <c>SurfaceTempC</c> and the any-phase, so without it a profile with an
    /// any-phase surface band would have adopted the gradient row and then touched or resolved a signal it does not
    /// own.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Applies the <c>GradientWarning</c> rule to one sample: surface minus air, when the sample carries both.
    /// </summary>
    /// <remarks>
    /// Decided here rather than in a sweep because the evidence is a pair of readings from the same instant — a sweep
    /// would have to guess which surface reading belonged with which air reading. A sample missing either number
    /// moves nothing: the rule has no opinion without both, and closing an open episode on absent evidence would
    /// report a recovery nobody observed.
    /// </remarks>
    /// <param name="context">Terrarium context, including the alerts currently open.</param>
    /// <param name="sample">The sample being judged.</param>
    private SignalOutcome ApplyGradient(EvaluationContext context, PersistedSample sample)
    {
        var surface = PlausibleValue(sample, MetricCode.SurfaceTempC);
        var air = PlausibleValue(sample, MetricCode.TempC);

        if (surface is null || air is null)
        {
            return default;
        }

        var open = context.OpenAlerts.FirstOrDefault(alert =>
            alert.Source == AlertSource.Gradient && alert.State != AlertState.Resolved);

        if (!GradientWarningPolicy.IsExceeded(surface, air))
        {
            if (open is null)
            {
                return default;
            }

            HabitatSignalAlertWriter.ResolveGradient(open, sample.RecordedAt);

            return new SignalOutcome(true, 0, 1, new AlertChange(open, AlertEvent.Resolved));
        }

        var difference = GradientWarningPolicy.Difference(surface, air)!.Value;

        if (open is not null)
        {
            // Still too hot: the episode continues, so the row moves and no second one is written.
            HabitatSignalAlertWriter.Touch(open, sample.RecordedAt, difference);

            return new SignalOutcome(true, 0, 0, null);
        }

        var raised = HabitatSignalAlertWriter.OpenGradient(
            sample.TerrariumId,
            sample.DeviceId,
            sample.RecordedAt,
            difference);

        store.AddAlert(raised);

        // Visible to the rest of this batch, so the next sample of the same package updates this row instead of
        // staging a second one and colliding on the filtered unique index.
        context.OpenAlerts.Add(raised);

        return new SignalOutcome(true, 1, 0, new AlertChange(raised, AlertEvent.Opened));
    }

    /// <summary>The reading's value when it is present and physically possible, otherwise null.</summary>
    /// <param name="sample">The sample to read.</param>
    /// <param name="code">Metric wanted.</param>
    private static decimal? PlausibleValue(PersistedSample sample, MetricCode code)
    {
        foreach (var reading in sample.Readings)
        {
            if (reading.Code == code && MetricDictionary.IsPlausible(code, reading.Value))
            {
                return reading.Value;
            }
        }

        return null;
    }

    /// <summary>What a derived-signal rule did with one sample.</summary>
    /// <param name="Moved">True when the rule touched, opened or closed a row — the reason to save even with no band advance.</param>
    /// <param name="Opened">Rows the rule opened.</param>
    /// <param name="Resolved">Rows the rule closed.</param>
    /// <param name="Change">The lifecycle move to announce, when there was one.</param>
    private readonly record struct SignalOutcome(bool Moved, int Opened, int Resolved, AlertChange? Change);

    private static Alert? Adopt(
        EvaluationContext context,
        (MetricCode Metric, ThresholdPhase Phase) key,
        EvaluationState state)
    {
        var open = context.OpenAlerts.FirstOrDefault(alert =>
            alert.Source == AlertSource.Threshold
            && alert.State != AlertState.Resolved
            && alert.Metric == key.Metric
            && alert.Phase == key.Phase);

        // A newly opened alert has an identity only after its insert, and that is exactly where the evaluator sets
        // this itself, so the reconciliation never has to invent one.
        state.OpenAlertId = open is { Id: > 0 } ? open.Id : null;

        return open;
    }

    private static int CompareSampleOrder(
        DateTimeOffset leftAt,
        long leftSequence,
        DateTimeOffset rightAt,
        long rightSequence)
    {
        var timeOrder = leftAt.CompareTo(rightAt);
        return timeOrder != 0 ? timeOrder : leftSequence.CompareTo(rightSequence);
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
