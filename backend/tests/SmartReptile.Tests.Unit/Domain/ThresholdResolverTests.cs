using FluentAssertions;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// TC-U-24 — the resolution order of `03-implementation/06` §1: terrarium override, then the assigned profile, with
/// the phase decided inside one layer. These were previously only observable through the reading cards, which is
/// why the two precedences the document leaves ambiguous are pinned here instead.
/// </summary>
public class ThresholdResolverTests
{
    private static readonly TimeOnly LightsOn = new(7, 0);
    private const decimal Photoperiod = 12m;

    // Midday and 23:00 in the same zone, so the same rows resolve to opposite phases.
    private static readonly TimeOnly Noon = new(12, 0);
    private static readonly TimeOnly Night = new(23, 0);

    [Fact]
    public void Resolve_reports_the_profile_as_the_source_when_no_override_exists()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [],
            [ProfileBand(MetricCode.TempC, 26m, 32m)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved.Should().NotBeNull();
        resolved!.Source.Should().Be(ThresholdSource.Profile);
        resolved.Phase.Should().Be(ThresholdPhase.Any);
        resolved.Band.TargetMin.Should().Be(26m);
        resolved.Band.TargetMax.Should().Be(32m);
    }

    [Fact]
    public void Resolve_prefers_an_override_over_the_profile()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [OverrideBand(MetricCode.TempC, 28m, 30m)],
            [ProfileBand(MetricCode.TempC, 26m, 32m)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved!.Source.Should().Be(ThresholdSource.Override);
        resolved.Band.TargetMax.Should().Be(30m);
    }

    [Fact]
    public void Resolve_lets_an_override_beat_a_phase_specific_profile_row_that_also_applies()
    {
        // The ambiguous sentence in §1 read one way: precedence is per layer, so an override that applies at this
        // instant wins even though the profile is the only layer that describes day/night at all.
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [OverrideBand(MetricCode.TempC, 28m, 30m)],
            [ProfileBand(MetricCode.TempC, 26m, 32m, ThresholdPhase.Day)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved!.Source.Should().Be(ThresholdSource.Override);
        resolved.Band.TargetMin.Should().Be(28m);
    }

    [Fact]
    public void Resolve_falls_through_to_the_profile_when_the_override_covers_only_the_other_phase()
    {
        // The other half of the same sentence, and the behaviour `readings/latest` already had: an override that
        // says nothing about this instant is silence, not a decision, so the profile's night band still applies.
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [OverrideBand(MetricCode.TempC, 28m, 30m, ThresholdPhase.Day)],
            [ProfileBand(MetricCode.TempC, 20m, 24m, ThresholdPhase.Night)],
            Night,
            LightsOn,
            Photoperiod);

        resolved!.Source.Should().Be(ThresholdSource.Profile);
        resolved.Phase.Should().Be(ThresholdPhase.Night);
        resolved.Band.TargetMax.Should().Be(24m);
    }

    [Fact]
    public void Resolve_answers_an_any_row_within_a_layer_in_preference_to_a_phase_specific_one()
    {
        // "If any applicable row has Phase = Any → Any", applied inside one layer rather than across both.
        var resolved = ThresholdResolver.Resolve(
            MetricCode.HumidityPct,
            [],
            [
                ProfileBand(MetricCode.HumidityPct, 30m, 40m),
                ProfileBand(MetricCode.HumidityPct, 45m, 55m, ThresholdPhase.Day),
            ],
            Noon,
            LightsOn,
            Photoperiod);

        resolved!.Phase.Should().Be(ThresholdPhase.Any);
        resolved.Band.TargetMin.Should().Be(30m);
    }

    [Fact]
    public void Resolve_uses_the_night_row_outside_the_window()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [],
            [
                ProfileBand(MetricCode.TempC, 26m, 32m, ThresholdPhase.Day),
                ProfileBand(MetricCode.TempC, 20m, 24m, ThresholdPhase.Night),
            ],
            Night,
            LightsOn,
            Photoperiod);

        resolved!.Phase.Should().Be(ThresholdPhase.Night);
        resolved.Band.TargetMax.Should().Be(24m);
    }

    [Fact]
    public void Resolve_has_nothing_to_say_when_the_only_row_is_the_other_phase()
    {
        // A day-only metric has no band at night, which is a real answer: the evaluator must not judge it against
        // a day band it was never configured for, and the editor must show it as unconfigured rather than wrong.
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [],
            [ProfileBand(MetricCode.TempC, 26m, 32m, ThresholdPhase.Day)],
            Night,
            LightsOn,
            Photoperiod);

        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_ignores_disabled_rows_in_both_layers()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [OverrideBand(MetricCode.TempC, 28m, 30m, enabled: false)],
            [ProfileBand(MetricCode.TempC, 26m, 32m, enabled: false)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_only_looks_at_the_metric_it_was_asked_about()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.UvIndex,
            [OverrideBand(MetricCode.TempC, 28m, 30m)],
            [ProfileBand(MetricCode.HumidityPct, 30m, 40m)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved.Should().BeNull();
    }

    [Fact]
    public void Resolve_carries_the_dwell_and_critical_bounds_the_editor_previews()
    {
        var resolved = ThresholdResolver.Resolve(
            MetricCode.TempC,
            [],
            [ProfileBand(MetricCode.TempC, 26m, 32m, criticalMax: 38m)],
            Noon,
            LightsOn,
            Photoperiod);

        resolved!.Band.CriticalMax.Should().Be(38m);
    }

    private static Threshold ProfileBand(
        MetricCode metric,
        decimal targetMin,
        decimal targetMax,
        ThresholdPhase phase = ThresholdPhase.Any,
        decimal? criticalMin = null,
        decimal? criticalMax = null,
        bool enabled = true) => new()
        {
            SpeciesProfileId = Guid.NewGuid(),
            Metric = metric,
            Phase = phase,
            TargetMin = targetMin,
            TargetMax = targetMax,
            CriticalMin = criticalMin,
            CriticalMax = criticalMax,
            Enabled = enabled,
        };

    private static ThresholdOverride OverrideBand(
        MetricCode metric,
        decimal targetMin,
        decimal targetMax,
        ThresholdPhase phase = ThresholdPhase.Any,
        bool enabled = true) => new()
        {
            TerrariumId = Guid.NewGuid(),
            Metric = metric,
            Phase = phase,
            TargetMin = targetMin,
            TargetMax = targetMax,
            Enabled = enabled,
        };
}
