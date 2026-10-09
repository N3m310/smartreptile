using FluentAssertions;
using SmartReptile.Application.Evaluation;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Application.Evaluation;

/// <summary>
/// The evaluator (FR-11, roadmap task 3.2): the guard, the <c>(metric, phase)</c> key, the band it resolves, and
/// the alert lifecycle the decision produces.
/// </summary>
/// <remarks>
/// The decision matrix itself is <c>ThresholdDecisionTests</c>' job. What is asserted here is the wiring around it:
/// that the right band reaches the engine, that the alert row is written with the fields the contracts name, that a
/// second reading updates that row rather than opening another, and that the state's pointer and the alert rows
/// cannot drift apart.
/// </remarks>
public class SampleEvaluatorTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LateEvening = new(2026, 10, 9, 23, 30, 0, TimeSpan.Zero);

    private readonly FakeEvaluationStore _store = new();
    private readonly TestClock _clock = new() { UtcNow = Noon };
    private readonly SampleEvaluator _evaluator;
    private readonly Guid _terrariumId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public SampleEvaluatorTests() => _evaluator = new SampleEvaluator(_store, _clock);

    private PersistedSample Sample(
        long sampleId,
        (MetricCode Code, decimal Value)[] readings,
        DateTimeOffset? recordedAt = null,
        QualityFlags flags = QualityFlags.None) =>
        new(
            sampleId,
            _terrariumId,
            _deviceId,
            recordedAt ?? Noon,
            sampleId,
            flags,
            [.. readings.Select(reading => new TelemetryReading(reading.Code, reading.Value, null))]);

    // ---- the documented guard ------------------------------------------------------------------------

    [Fact]
    public async Task A_faulted_sample_is_skipped_entirely()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 40m)], flags: QualityFlags.SensorFault)],
            CancellationToken.None);

        // §02-design/03 §4.2's first line. This is the rule that makes TC-I-03's "zero alerts created" hold for a
        // stated reason instead of trivially, and TC-U-19 is the same rule seen from the alert side.
        outcome.ReadingsSkipped.Should().Be(1);
        outcome.ReadingsAdvanced.Should().Be(0);
        outcome.AlertsOpened.Should().Be(0);
        _store.Added.Should().BeEmpty();
        _store.AddedAlerts.Should().BeEmpty();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task An_implausible_reading_is_skipped()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        // Stored as evidence, never judged — the same rule the read surface colours as Unavailable (BR-11.1).
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 85m)])],
            CancellationToken.None);

        outcome.ReadingsAdvanced.Should().Be(0);
        outcome.AlertsOpened.Should().Be(0);
        _store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tolerated_flag_does_not_stop_evaluation()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        // Back-filled and clock-unsynced are advisory bits, not exclusions.
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)], flags: QualityFlags.Backfilled)],
            CancellationToken.None);

        outcome.ReadingsAdvanced.Should().Be(1);
        _store.Added.Should().ContainSingle();
    }

    // ---- the key and the band ------------------------------------------------------------------------

    [Fact]
    public async Task A_new_key_gets_a_state_row_carrying_the_sample_it_saw()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        await _evaluator.EvaluateAsync([Sample(42, [(MetricCode.TempC, 28m)])], CancellationToken.None);

        var created = _store.Added.Should().ContainSingle().Subject;
        created.TerrariumId.Should().Be(_terrariumId);
        created.Metric.Should().Be(MetricCode.TempC);
        created.Phase.Should().Be(ThresholdPhase.Any);
        created.LastEvaluatedSampleId.Should().Be(42);

        // A reading inside the band decides nothing, and an episode that never happened leaves no trace.
        created.Violation.Should().Be(ViolationKind.None);
        created.FirstOutOfBandAt.Should().BeNull();
        created.CriticalSinceAt.Should().BeNull();
        created.OpenAlertId.Should().BeNull();

        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task The_watermark_only_moves_forward()
    {
        var context = Context(Band(TestBands.FastDwell()));
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            LastEvaluatedSampleId = 10,
        };

        _store.Add(context);

        // A replayed sample carries an id the evaluator has already seen; judging it again would count one dwell
        // window twice.
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(10, [(MetricCode.TempC, 40m)]), Sample(11, [(MetricCode.TempC, 29m)])],
            CancellationToken.None);

        outcome.ReadingsAdvanced.Should().Be(1);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].LastEvaluatedSampleId.Should().Be(11);
        _store.Added.Should().BeEmpty();
        _store.AddedAlerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Two_readings_of_one_key_in_a_batch_stage_one_row()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)]), Sample(2, [(MetricCode.TempC, 29m)])],
            CancellationToken.None);

        // The primary key is the key itself, so a second row would collide — the row staged by the first sample has
        // to be visible to the second. Two readings moved the one key, hence two advances and one staged row.
        outcome.ReadingsAdvanced.Should().Be(2);
        _store.Added.Should().ContainSingle().Which.LastEvaluatedSampleId.Should().Be(2);
    }

    [Fact]
    public async Task A_metric_with_no_configured_band_gets_no_row()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.HumidityPct, 45m)])],
            CancellationToken.None);

        // Nothing judges humidity on this terrarium, so there is no excursion to track.
        outcome.ReadingsAdvanced.Should().Be(0);
        _store.Added.Should().BeEmpty();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task A_day_band_gets_no_row_for_a_sample_outside_the_photoperiod()
    {
        _store.Add(Context(Band(TestBands.FastDwell(), ThresholdPhase.Day)));

        // Half past eleven at night: inside no Day band, and this terrarium has no Night band for the metric.
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)], LateEvening)],
            CancellationToken.None);

        outcome.ReadingsAdvanced.Should().Be(0);
        _store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task A_day_band_is_keyed_by_day_inside_the_photoperiod()
    {
        _store.Add(Context(Band(TestBands.FastDwell(), ThresholdPhase.Day)));

        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)], Noon)],
            CancellationToken.None);

        outcome.ReadingsAdvanced.Should().Be(1);
        _store.Added.Should().ContainSingle().Which.Phase.Should().Be(ThresholdPhase.Day);
    }

    [Fact]
    public async Task An_any_band_wins_over_the_photoperiod()
    {
        // The profile configures both an Any band and a Day band: the metric is not split, so the key is Any even
        // at night (BR-10.3, BR-11.2). TC-U-20's service-level half.
        _store.Add(Context(
            Band(TestBands.FastDwell()),
            Band(TestBands.FastDwell(), ThresholdPhase.Day)));

        await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)], LateEvening)],
            CancellationToken.None);

        _store.Added.Should().ContainSingle().Which.Phase.Should().Be(ThresholdPhase.Any);
    }

    [Fact]
    public async Task The_band_that_reaches_the_engine_is_the_one_the_read_surface_shows()
    {
        // An override with its own bounds: the evaluator must judge against it, not against the profile band, which
        // is what keeps a card's colour and an alert's BandMin/BandMax the same claim.
        var context = Context(
            [Band(TestBands.FastDwell())],
            [
                new ThresholdOverride
                {
                    TerrariumId = _terrariumId,
                    Metric = MetricCode.TempC,
                    Phase = ThresholdPhase.Any,
                    TargetMin = 20m,
                    TargetMax = 30m,
                    DwellWarnMinutes = 1,
                    DwellCritMinutes = 1,
                    RecoveryMargin = 0.5m,
                    Enabled = true,
                },
            ]);

        _store.Add(context);
        _store.EpisodeValues[MetricCode.TempC] = [31.0m];

        // 31 °C is out of band for the override (20–30) and in band for the profile (26–32).
        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 31.0m)])], CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(2, [(MetricCode.TempC, 31.0m)], Noon.AddMinutes(2))],
            CancellationToken.None);

        outcome.AlertsOpened.Should().Be(1);
        _store.AddedAlerts.Single().BandMin.Should().Be(20m);
        _store.AddedAlerts.Single().BandMax.Should().Be(30m);
    }

    [Fact]
    public async Task A_terrarium_that_no_longer_exists_is_skipped()
    {
        // Nothing registered: the terrarium was deleted between the commit and this pass.
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(1, [(MetricCode.TempC, 28m)])],
            CancellationToken.None);

        outcome.ReadingsSkipped.Should().Be(1);
        _store.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_batch_does_nothing()
    {
        var outcome = await _evaluator.EvaluateAsync([], CancellationToken.None);

        outcome.Should().Be(EvaluationOutcome.None);
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Contexts_are_read_once_per_terrarium_not_once_per_sample()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        await _evaluator.EvaluateAsync(
            [
                Sample(1, [(MetricCode.TempC, 28m)]),
                Sample(2, [(MetricCode.TempC, 29m)]),
                Sample(3, [(MetricCode.TempC, 30m)]),
            ],
            CancellationToken.None);

        // A batch is up to 120 samples from one node; a query per sample would turn one ingest into 120 round trips.
        _store.ContextReads.Should().Be(1);
    }

    // ---- opening an episode --------------------------------------------------------------------------

    [Fact]
    public async Task A_sub_dwell_excursion_opens_nothing()
    {
        var context = Context(Band(TestBands.LeopardGecko()));
        _store.Add(context);

        // Two out-of-band readings a minute apart against a five-minute dwell: the lid was opened, nothing
        // sustained (TC-U-10 at the service level).
        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await _evaluator.EvaluateAsync([Sample(2, [(MetricCode.TempC, 32.5m)])], CancellationToken.None);

        outcome.AlertsOpened.Should().Be(0);
        _store.AddedAlerts.Should().BeEmpty();

        var state = context.States[(MetricCode.TempC, ThresholdPhase.Any)];
        state.Violation.Should().Be(ViolationKind.Hot);
        state.OpenAlertId.Should().BeNull();
    }

    [Fact]
    public async Task A_sustained_excursion_opens_one_alert_with_the_fields_the_contract_names()
    {
        var context = Context(Band(TestBands.FastDwell()));
        _store.Add(context);

        // The dwell window's readings, which the alert reports: the first one back-dates it and the worst one is
        // its peak. Both are read back from the stored readings rather than remembered in the state.
        _store.EpisodeValues[MetricCode.TempC] = [33.0m, 34.0m, 32.6m];

        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 34.0m)])], CancellationToken.None);
        var savesBefore = _store.SaveCount;
        _clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await _evaluator.EvaluateAsync(
            [Sample(2, [(MetricCode.TempC, 32.6m)], Noon.AddMinutes(1))],
            CancellationToken.None);

        outcome.AlertsOpened.Should().Be(1);
        outcome.WarningAlertsOpened.Should().Be(1);
        outcome.CriticalAlertsOpened.Should().Be(0);

        var alert = _store.AddedAlerts.Should().ContainSingle().Subject;
        alert.TerrariumId.Should().Be(_terrariumId);
        alert.DeviceId.Should().Be(_deviceId);
        alert.Metric.Should().Be(MetricCode.TempC);
        alert.Phase.Should().Be(ThresholdPhase.Any);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.State.Should().Be(AlertState.Open);
        alert.Source.Should().Be(AlertSource.Threshold);
        alert.TriggeredAt.Should().Be(Noon, "the alert reports when the excursion started, not when the dwell expired");
        alert.LastObservedAt.Should().Be(Noon.AddMinutes(1));
        alert.TriggeringValue.Should().Be(33.0m);
        alert.PeakValue.Should().Be(34.0m, "the peak is the worst reading of the episode, not the latest one");
        alert.BandMin.Should().Be(26m);
        alert.BandMax.Should().Be(32m);
        alert.DedupeKey.Should().Be(Alert.DedupeKeyFor(_terrariumId, MetricCode.TempC, AlertSeverity.Warning, ThresholdPhase.Any));
        alert.Message.Should().BeNull("the notification text is localised from these fields at render time");

        // The pointer is set from the identity the insert assigned, which is why the alert is committed first:
        // two commits for this pass — the alert, then the state that names it.
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].OpenAlertId.Should().Be(alert.Id);
        (_store.SaveCount - savesBefore).Should().Be(2);
    }

    [Fact]
    public async Task An_excursion_that_is_already_critical_opens_as_critical()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));
        _store.EpisodeValues[MetricCode.TempC] = [36.0m];

        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 36.0m)])], CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await _evaluator.EvaluateAsync([Sample(2, [(MetricCode.TempC, 36.0m)])], CancellationToken.None);

        outcome.AlertsOpened.Should().Be(1);
        outcome.CriticalAlertsOpened.Should().Be(1);
        _store.AddedAlerts.Single().Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task A_cold_excursion_tracks_the_minimum_as_its_peak()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));
        _store.EpisodeValues[MetricCode.TempC] = [25.0m, 24.0m, 25.5m];

        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 24.0m)])], CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _evaluator.EvaluateAsync([Sample(2, [(MetricCode.TempC, 25.5m)])], CancellationToken.None);

        var alert = _store.AddedAlerts.Single();
        alert.TriggeringValue.Should().Be(25.0m);
        alert.PeakValue.Should().Be(24.0m, "for a cold excursion the worst value is the lowest");
    }

    // ---- continuing, escalating, resolving -----------------------------------------------------------

    [Fact]
    public async Task A_continuing_excursion_touches_the_open_alert_and_never_opens_a_second()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Warning);
        context.OpenAlerts.Add(existing);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Noon.AddMinutes(-3),
            OpenAlertId = existing.Id,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        var outcome = await _evaluator.EvaluateAsync([Sample(5, [(MetricCode.TempC, 33.5m)])], CancellationToken.None);

        outcome.AlertsOpened.Should().Be(0);
        outcome.AlertsEscalated.Should().Be(0);
        _store.AddedAlerts.Should().BeEmpty();
        existing.LastObservedAt.Should().Be(Noon);
        existing.PeakValue.Should().Be(33.5m);
        existing.Severity.Should().Be(AlertSeverity.Warning);

        // The peak only grows: a later, milder reading leaves it alone.
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _evaluator.EvaluateAsync(
            [Sample(6, [(MetricCode.TempC, 32.4m)], Noon.AddMinutes(1))],
            CancellationToken.None);

        existing.PeakValue.Should().Be(33.5m);
        existing.LastObservedAt.Should().Be(Noon.AddMinutes(1));
    }

    [Fact]
    public async Task A_critical_reading_past_its_dwell_escalates_the_open_alert_once()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Warning);
        context.OpenAlerts.Add(existing);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Noon.AddMinutes(-10),
            OpenAlertId = existing.Id,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        var first = await _evaluator.EvaluateAsync([Sample(5, [(MetricCode.TempC, 35.0m)])], CancellationToken.None);
        first.AlertsEscalated.Should().Be(0, "the critical dwell has not elapsed yet");

        _clock.Advance(TimeSpan.FromMinutes(1));
        var second = await _evaluator.EvaluateAsync([Sample(6, [(MetricCode.TempC, 35.2m)])], CancellationToken.None);

        second.AlertsEscalated.Should().Be(1);
        existing.Severity.Should().Be(AlertSeverity.Critical);
        existing.State.Should().Be(AlertState.Open, "escalation is a severity change on the same episode");
        existing.Id.Should().Be(5);
        existing.TriggeredAt.Should().Be(Noon.AddMinutes(-10));
        existing.PeakValue.Should().Be(35.2m);

        // The dedupe key follows the severity, which is what lets a later episode be a second row.
        existing.DedupeKey.Should().Be(
            Alert.DedupeKeyFor(_terrariumId, MetricCode.TempC, AlertSeverity.Critical, ThresholdPhase.Any));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var third = await _evaluator.EvaluateAsync([Sample(7, [(MetricCode.TempC, 35.5m)])], CancellationToken.None);

        third.AlertsEscalated.Should().Be(0, "one escalation per episode");
        existing.PeakValue.Should().Be(35.5m);
    }

    [Fact]
    public async Task Three_consecutive_recovery_ticks_resolve_the_open_alert()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Critical);
        context.OpenAlerts.Add(existing);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            OpenAlertId = existing.Id,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        // Three readings inside the band by the margin — 31.5 °C or below for this band — in one batch.
        var outcome = await _evaluator.EvaluateAsync(
            [
                Sample(5, [(MetricCode.TempC, 31.5m)]),
                Sample(6, [(MetricCode.TempC, 31.4m)]),
                Sample(7, [(MetricCode.TempC, 31.0m)]),
            ],
            CancellationToken.None);

        outcome.AlertsResolved.Should().Be(1);
        existing.State.Should().Be(AlertState.Resolved);
        existing.ResolvedReason.Should().Be(ResolvedReason.Recovered);
        existing.ResolvedAt.Should().Be(Noon);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].OpenAlertId.Should().BeNull();
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].Violation.Should().Be(ViolationKind.None);
    }

    [Fact]
    public async Task A_reading_inside_the_band_but_inside_the_margin_does_not_resolve()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Warning);
        context.OpenAlerts.Add(existing);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            OpenAlertId = existing.Id,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        // 31.8 °C is inside the target band but has not cleared the 0.5 °C margin, so the episode stays open
        // (TC-U-15 at the service level).
        var outcome = await _evaluator.EvaluateAsync(
            [
                Sample(5, [(MetricCode.TempC, 31.8m)]),
                Sample(6, [(MetricCode.TempC, 31.8m)]),
                Sample(7, [(MetricCode.TempC, 31.8m)]),
            ],
            CancellationToken.None);

        outcome.AlertsResolved.Should().Be(0);
        existing.State.Should().Be(AlertState.Open);
        existing.ResolvedAt.Should().BeNull();
    }

    // ---- the pointer and the rows cannot drift apart --------------------------------------------------

    [Fact]
    public async Task A_lost_pointer_is_adopted_instead_of_opening_a_second_alert()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Warning);
        context.OpenAlerts.Add(existing);

        // The state row exists but names no alert — the crash window between the alert's insert and the pointer's
        // commit — while the alert itself is there and still open.
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Noon.AddMinutes(-3),
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        var outcome = await _evaluator.EvaluateAsync([Sample(5, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);

        outcome.AlertsOpened.Should().Be(0);
        _store.AddedAlerts.Should().BeEmpty();
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].OpenAlertId.Should().Be(existing.Id);
        existing.LastObservedAt.Should().Be(Noon);
    }

    [Fact]
    public async Task An_alert_resolved_behind_the_evaluator_lets_the_next_excursion_open_a_new_one()
    {
        var context = Context(Band(TestBands.FastDwell()));

        // A human resolved the episode (3.4's route) while the condition persisted. The resolved row is not in the
        // loaded set, so the pointer is stale and the next episode is genuinely new.
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Noon.AddMinutes(-3),
            OpenAlertId = 99,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);
        _store.EpisodeValues[MetricCode.TempC] = [33.0m];

        var outcome = await _evaluator.EvaluateAsync([Sample(5, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);

        outcome.AlertsOpened.Should().Be(1);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)].OpenAlertId.Should().Be(_store.AddedAlerts.Single().Id);
    }

    [Fact]
    public async Task An_episode_whose_readings_are_gone_still_opens_with_the_reading_that_opened_it()
    {
        _store.Add(Context(Band(TestBands.FastDwell())));

        // No readings in the window: retention removed them, or the clock disagreed. The alert is still correct.
        await _evaluator.EvaluateAsync([Sample(1, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _evaluator.EvaluateAsync([Sample(2, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);

        var alert = _store.AddedAlerts.Single();
        alert.TriggeringValue.Should().Be(33.0m);
        alert.PeakValue.Should().Be(33.0m);
    }

    [Fact]
    public async Task An_acknowledged_alert_is_still_an_open_episode()
    {
        var context = Context(Band(TestBands.FastDwell()));
        var existing = OpenAlert(AlertSeverity.Warning);
        existing.State = AlertState.Acknowledged;
        context.OpenAlerts.Add(existing);
        context.States[(MetricCode.TempC, ThresholdPhase.Any)] = new EvaluationState
        {
            TerrariumId = _terrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            OpenAlertId = existing.Id,
            LastEvaluatedSampleId = 4,
        };

        _store.Add(context);

        var outcome = await _evaluator.EvaluateAsync([Sample(5, [(MetricCode.TempC, 33.0m)])], CancellationToken.None);

        // Acknowledgement is ownership, not closure: the episode continues and so does its record.
        outcome.AlertsOpened.Should().Be(0);
        existing.State.Should().Be(AlertState.Acknowledged);
        existing.LastObservedAt.Should().Be(Noon);
    }

    private Alert OpenAlert(AlertSeverity severity) => new()
    {
        Id = 5,
        TerrariumId = _terrariumId,
        DeviceId = _deviceId,
        Severity = severity,
        State = AlertState.Open,
        Source = AlertSource.Threshold,
        Metric = MetricCode.TempC,
        Phase = ThresholdPhase.Any,
        TriggeredAt = Noon.AddMinutes(-10),
        DedupeKey = "existing",
    };

    /// <summary>A terrarium configuration with the given rows on its species profile.</summary>
    private EvaluationContext Context(params Threshold[] bands) =>
        new(_terrariumId, "UTC", new TimeOnly(7, 0), 12m, bands, [], [], []);

    /// <summary>The same, with per-terrarium overrides, which win over the profile rows (BR-10.3).</summary>
    private EvaluationContext Context(
        IReadOnlyList<Threshold> bands,
        IReadOnlyList<ThresholdOverride> overrides) =>
        new(_terrariumId, "UTC", new TimeOnly(7, 0), 12m, bands, overrides, [], []);

    /// <summary>A profile band, built from the test helper's band so bounds and dwells are stated once.</summary>
    private static Threshold Band(ThresholdBand band, ThresholdPhase phase = ThresholdPhase.Any) => new()
    {
        SpeciesProfileId = Guid.NewGuid(),
        Metric = band.Metric,
        Phase = phase,
        TargetMin = band.TargetMin,
        TargetMax = band.TargetMax,
        CriticalMin = band.CriticalMin,
        CriticalMax = band.CriticalMax,
        DwellWarnMinutes = band.DwellWarnMinutes,
        DwellCritMinutes = band.DwellCritMinutes,
        RecoveryMargin = band.RecoveryMargin,
        Enabled = true,
    };

    /// <summary>In-memory <see cref="IEvaluationStore"/>, recording what the evaluator asked for and staged.</summary>
    /// <remarks>
    /// Two behaviours deliberately mirror the real adapter rather than being simpler than it, because the evaluator
    /// depends on them: <see cref="AddAlert"/> plus <see cref="SaveChangesAsync"/> assign an identity the way the
    /// database does, and the flags/details rows it stages are the same objects it was handed, so a test asserts on
    /// the row the evaluator wrote and not on a copy.
    /// </remarks>
    private sealed class FakeEvaluationStore : IEvaluationStore
    {
        private readonly Dictionary<Guid, EvaluationContext> _contexts = [];
        private long _nextAlertId = 100;

        public List<EvaluationState> Added { get; } = [];

        public List<Alert> AddedAlerts { get; } = [];

        /// <summary>The values the episode-window read returns, per metric.</summary>
        public Dictionary<MetricCode, List<decimal>> EpisodeValues { get; } = [];

        public int SaveCount { get; private set; }

        public int ContextReads { get; private set; }

        public void Add(EvaluationContext context) => _contexts[context.TerrariumId] = context;

        public Task<EvaluationContext?> FindContextAsync(Guid terrariumId, CancellationToken cancellationToken)
        {
            ContextReads++;
            return Task.FromResult(_contexts.TryGetValue(terrariumId, out var context) ? context : null);
        }

        public void AddState(EvaluationState state) => Added.Add(state);

        public void AddAlert(Alert alert) => AddedAlerts.Add(alert);

        public Task<IReadOnlyList<decimal>> ReadEpisodeValuesAsync(
            Guid terrariumId,
            MetricCode metric,
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<decimal>>(
                EpisodeValues.TryGetValue(metric, out var values) ? values : []);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;

            // What a real insert does: an identity is assigned at commit and nothing else moves.
            foreach (var alert in AddedAlerts.Where(alert => alert.Id == 0))
            {
                alert.Id = _nextAlertId++;
            }

            return Task.CompletedTask;
        }
    }
}
