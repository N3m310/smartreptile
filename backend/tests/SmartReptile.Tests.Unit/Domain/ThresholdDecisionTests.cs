using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// TC-U-10…20 — the dwell/hysteresis/escalation matrix of §02-design/03 §4.2. The highest-value tests in the
/// project: this is the algorithm the product exists to get right, and the only one that can be exhausted in
/// milliseconds because <see cref="ThresholdDecision.Decide"/> has no dependencies at all.
/// </summary>
/// <remarks>
/// Each test drives the engine with an explicit instant for every reading, so an assertion about "five minutes"
/// is arithmetic rather than a sleep. The scenarios are the ones `04-quality/02` §B2 names, in its order.
/// </remarks>
public class ThresholdDecisionTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 14, 1, 0, TimeSpan.Zero);

    private static ThresholdDecision Decide(
        ThresholdBand band,
        decimal value,
        int minute,
        EvaluationState state,
        AlertSeverity? openAlertSeverity = null,
        int evaluatedAtMinute = -1) =>
        ThresholdDecision.Decide(
            band,
            value,
            Start.AddMinutes(minute),
            state,
            Start.AddMinutes(evaluatedAtMinute < 0 ? minute : evaluatedAtMinute),
            openAlertSeverity);

    // ---- TC-U-10 / TC-U-11: dwell is a filter, not a delay -------------------------------------------

    [Fact]
    public void GivenValueAboveTargetForFourMinutes_ThenNoAlertIsRaised()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        // 32.4 at minute 1 … 30.8 back inside at minute 4: the lid was opened, nothing sustained.
        Decide(band, 32.4m, 1, state);
        Decide(band, 32.9m, 2, state);
        Decide(band, 32.1m, 3, state);
        Decide(band, 31.0m, 4, state);
        var decision = Decide(band, 30.8m, 5, state);

        decision.Kind.Should().Be(DecisionKind.None);
        state.OpenAlertId.Should().BeNull();
        state.Violation.Should().Be(ViolationKind.None);

        // "State reset": the excursion is over, so the next one starts its own dwell window rather than inheriting
        // this one's start and opening instantly.
        state.FirstOutOfBandAt.Should().BeNull();
        state.CriticalSinceAt.Should().BeNull();
    }

    [Fact]
    public void GivenValueAboveTargetForFiveMinutes_ThenOneAlertIsOpenedAndBackDated()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        for (var minute = 1; minute <= 4; minute++)
        {
            Decide(band, 32.5m + (minute * 0.1m), minute, state);
        }

        var decision = Decide(band, 33.4m, 6, state, evaluatedAtMinute: 6);

        decision.Kind.Should().Be(DecisionKind.Open);
        decision.Severity.Should().Be(AlertSeverity.Warning);

        // Design note 1: the alert reports the start of the excursion, not the instant the dwell expired.
        decision.TriggeredAt.Should().Be(Start.AddMinutes(1));
        state.FirstOutOfBandAt.Should().Be(Start.AddMinutes(1));
        state.Violation.Should().Be(ViolationKind.Hot);
    }

    [Fact]
    public void An_excursion_that_re_arms_after_recovery_needs_its_own_dwell_window()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        // Three minutes out, back inside for one reading, then out again for one: the second excursion is one
        // minute old, so five minutes of dwell have not elapsed for it.
        Decide(band, 33.0m, 1, state);
        Decide(band, 33.0m, 2, state);
        Decide(band, 33.0m, 3, state);
        Decide(band, 31.0m, 4, state);
        var decision = Decide(band, 33.0m, 5, state);

        decision.Kind.Should().Be(DecisionKind.None);
        state.FirstOutOfBandAt.Should().Be(Start.AddMinutes(5));
    }

    // ---- TC-U-12: one episode, one alert --------------------------------------------------------------

    [Fact]
    public void A_sustained_excursion_touches_the_open_alert_without_opening_a_second_one()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;

        var peak = 33.0m;

        for (var minute = 1; minute <= 10; minute++)
        {
            peak = 33.0m + (minute * 0.1m);
            var decision = Decide(band, peak, minute, state, AlertSeverity.Warning);

            decision.Kind.Should().Be(DecisionKind.Touch);
            decision.ObservedValue.Should().Be(peak);
        }

        state.OpenAlertId.Should().Be(77);
        state.Violation.Should().Be(ViolationKind.Hot);
    }

    // ---- TC-U-13 / TC-U-14: escalation needs its own dwell --------------------------------------------

    [Fact]
    public void A_critical_excursion_sustained_for_its_dwell_escalates_the_same_alert()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;

        var first = Decide(band, 34.9m, 21, state, AlertSeverity.Warning);
        first.Kind.Should().Be(DecisionKind.Touch, "one minute is less than the two-minute critical dwell");
        state.CriticalSinceAt.Should().Be(Start.AddMinutes(21));

        var escalation = Decide(band, 35.0m, 23, state, AlertSeverity.Warning);

        escalation.Kind.Should().Be(DecisionKind.Escalate);
        escalation.Severity.Should().Be(AlertSeverity.Critical);
        state.OpenAlertId.Should().Be(77, "escalation is a severity change on the same episode");
    }

    [Fact]
    public void A_critical_reading_that_does_not_hold_for_its_dwell_never_escalates()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;

        Decide(band, 34.9m, 21, state, AlertSeverity.Warning);
        var back_inside_critical = Decide(band, 34.0m, 22, state, AlertSeverity.Warning);

        back_inside_critical.Kind.Should().Be(DecisionKind.Touch);
        state.CriticalSinceAt.Should().BeNull("the critical window is consecutive, so one reading clears it");

        var later = Decide(band, 34.9m, 23, state, AlertSeverity.Warning);

        // The window restarted at minute 23, so escalation is not due yet.
        later.Kind.Should().Be(DecisionKind.Touch);
        state.CriticalSinceAt.Should().Be(Start.AddMinutes(23));
    }

    [Fact]
    public void An_escalated_episode_does_not_escalate_again()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;
        state.CriticalSinceAt = Start.AddMinutes(21);

        var decision = Decide(band, 35.0m, 24, state, AlertSeverity.Critical);

        // Already critical: the row's last-seen instant and peak keep moving, nothing else happens.
        decision.Kind.Should().Be(DecisionKind.Touch);
    }

    // ---- TC-U-15…17: recovery hysteresis --------------------------------------------------------------

    [Fact]
    public void A_value_that_has_not_cleared_the_recovery_margin_does_not_recover()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;
        state.Violation = ViolationKind.Hot;

        ThresholdDecision decision = ThresholdDecision.None;

        for (var minute = 1; minute <= 3; minute++)
        {
            // 31.8 °C is inside the target band but has not cleared the 0.5 °C margin, which asks for 31.5 or below:
            // the episode stays open. (The margin is what stops a noisy sensor flapping at the edge.)
            decision = Decide(band, 31.8m, minute, state, AlertSeverity.Warning);
        }

        decision.Kind.Should().Be(DecisionKind.None);
        state.ConsecutiveRecoveryTicks.Should().Be(0);
        state.OpenAlertId.Should().Be(77, "only a resolve clears the pointer");
    }

    [Fact]
    public void A_value_still_just_outside_the_band_does_not_recover()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;

        // TC-U-15's own example value, read literally: 32.2 °C is past TargetMax, so this is not recovery at all —
        // the episode continues and the alert keeps being touched, which is also "no Resolve".
        var decision = Decide(band, 32.2m, 1, state, AlertSeverity.Warning);

        decision.Kind.Should().Be(DecisionKind.Touch);
        state.ConsecutiveRecoveryTicks.Should().Be(0);
    }

    [Fact]
    public void Recovery_after_three_ticks_resolves_the_alert()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;
        state.Violation = ViolationKind.Hot;
        state.FirstOutOfBandAt = Start.AddMinutes(-20);
        state.CriticalSinceAt = Start.AddMinutes(-10);

        Decide(band, 31.5m, 1, state, AlertSeverity.Warning).Kind.Should().Be(DecisionKind.None);
        Decide(band, 31.4m, 2, state, AlertSeverity.Warning).Kind.Should().Be(DecisionKind.None);

        var decision = Decide(band, 31.0m, 3, state, AlertSeverity.Warning);

        decision.Kind.Should().Be(DecisionKind.Resolve);
        state.ConsecutiveRecoveryTicks.Should().Be(ThresholdDecision.RecoveryTicks);
        state.Violation.Should().Be(ViolationKind.None);
        state.CriticalSinceAt.Should().BeNull();
        state.FirstOutOfBandAt.Should().BeNull();
    }

    [Fact]
    public void A_single_excursion_resets_the_recovery_counter()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.OpenAlertId = 77;

        Decide(band, 31.5m, 1, state, AlertSeverity.Warning);
        var excursion = Decide(band, 33.0m, 2, state, AlertSeverity.Warning);

        excursion.Kind.Should().Be(DecisionKind.Touch);
        state.ConsecutiveRecoveryTicks.Should().Be(0, "three ticks must be consecutive");

        Decide(band, 31.5m, 3, state, AlertSeverity.Warning).Kind.Should().Be(DecisionKind.None);
        Decide(band, 31.5m, 4, state, AlertSeverity.Warning).Kind.Should().Be(DecisionKind.None);
        var decision = Decide(band, 31.5m, 5, state, AlertSeverity.Warning);

        decision.Kind.Should().Be(DecisionKind.Resolve);
    }

    [Fact]
    public void A_recovering_key_with_no_open_alert_never_resolves()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        for (var minute = 1; minute <= 4; minute++)
        {
            Decide(band, 31.5m, minute, state).Kind.Should().Be(DecisionKind.None);
        }

        state.ConsecutiveRecoveryTicks.Should().Be(4, "the counter still tracks, but there is nothing to close");
    }

    // ---- TC-U-18: cold is the mirror of hot ----------------------------------------------------------

    [Fact]
    public void A_cold_excursion_opens_a_warning_and_tracks_its_direction()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        Decide(band, 25.0m, 1, state);
        var decision = Decide(band, 24.0m, 6, state, evaluatedAtMinute: 6);

        decision.Kind.Should().Be(DecisionKind.Open);
        decision.Severity.Should().Be(AlertSeverity.Warning);
        state.Violation.Should().Be(ViolationKind.Cold);
        decision.TriggeredAt.Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public void A_cold_reading_below_the_critical_bound_is_opened_as_critical()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        Decide(band, 21.0m, 1, state);
        var decision = Decide(band, 21.0m, 6, state, evaluatedAtMinute: 6);

        // Already past the critical bound when the dwell expired: the episode opens at the severity the reading
        // deserves rather than escalating one sample later.
        decision.Kind.Should().Be(DecisionKind.Open);
        decision.Severity.Should().Be(AlertSeverity.Critical);
        state.Violation.Should().Be(ViolationKind.Cold);
    }

    // ---- TC-U-19 / TC-U-20: the boundaries the engine is not asked about ------------------------------

    [Fact]
    public void Any_phase_band_ignores_the_day_night_split()
    {
        // TC-U-20 is about the phase the *key* resolves to, which is ThresholdPhaseResolver's job; what this
        // asserts is that a decision never looks at the clock at all — the band arrives already resolved.
        var band = TestBands.LeopardGecko(ThresholdPhase.Any);
        var state = EvaluationState.Empty();
        var night = new DateTimeOffset(2026, 9, 21, 23, 0, 0, TimeSpan.Zero);

        var first = ThresholdDecision.Decide(band, 33.0m, night, state, night);
        var second = ThresholdDecision.Decide(band, 33.0m, night.AddMinutes(6), state, night.AddMinutes(6));

        first.Kind.Should().Be(DecisionKind.None);
        second.Kind.Should().Be(DecisionKind.Open);
    }

    [Fact]
    public void Decide_does_not_touch_the_watermark_or_the_pointer()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();
        state.LastEvaluatedSampleId = 41;

        Decide(band, 33.0m, 1, state);
        Decide(band, 33.0m, 6, state, evaluatedAtMinute: 6);

        // The watermark is the evaluator's bookkeeping and the pointer is set from the alert row's identity, so
        // neither is this method's to write.
        state.LastEvaluatedSampleId.Should().Be(41);
        state.OpenAlertId.Should().BeNull();
    }

    [Fact]
    public void A_band_whose_dwell_is_longer_than_supplied_time_never_opens()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        // Four minutes of wall-clock evaluation and one reading: the excursion's *start* is what dwell counts from,
        // so a single late sample cannot satisfy a five-minute window.
        var decision = Decide(band, 40m, 1, state, evaluatedAtMinute: 5);

        decision.Kind.Should().Be(DecisionKind.None);
        state.FirstOutOfBandAt.Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public void A_back_filled_excursion_opens_because_its_wall_clock_age_satisfies_the_dwell()
    {
        var band = TestBands.LeopardGecko();
        var state = EvaluationState.Empty();

        // The device was offline for an hour and has just delivered one out-of-band reading taken then. The
        // excursion is real and old: it opens (for the record), back-dated to when it happened. What stops this
        // being an alarm burst is the notification rule of BR-06.6/11.8, not the dwell filter.
        var decision = Decide(band, 33.0m, 1, state, evaluatedAtMinute: 60);

        decision.Kind.Should().Be(DecisionKind.Open);
        decision.TriggeredAt.Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public void An_empty_state_is_the_documented_starting_point()
    {
        var empty = EvaluationState.Empty();

        empty.Violation.Should().Be(ViolationKind.None);
        empty.FirstOutOfBandAt.Should().BeNull();
        empty.CriticalSinceAt.Should().BeNull();
        empty.ConsecutiveRecoveryTicks.Should().Be(0);
        empty.OpenAlertId.Should().BeNull();
        empty.LastEvaluatedSampleId.Should().Be(0);
        empty.LastNotificationAt.Should().BeNull();
    }
}
