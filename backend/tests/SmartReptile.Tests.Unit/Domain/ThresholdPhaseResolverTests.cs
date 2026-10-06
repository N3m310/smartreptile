using FluentAssertions;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// BR-11.2 — phase selection: Day inside the photoperiod window, Night outside it. Pure, so these assert the rule
/// rather than the time-zone database; the caller is responsible for the local-time conversion (ADR-015).
/// </summary>
public class ThresholdPhaseResolverTests
{
    private static readonly TimeOnly LightsOn = new(7, 0);

    [Theory]
    [InlineData(7, 0, ThresholdPhase.Day)]
    [InlineData(12, 30, ThresholdPhase.Day)]
    [InlineData(18, 59, ThresholdPhase.Day)]
    [InlineData(19, 0, ThresholdPhase.Night)]
    [InlineData(23, 30, ThresholdPhase.Night)]
    [InlineData(6, 59, ThresholdPhase.Night)]
    public void Resolve_uses_the_lighting_window(int hour, int minute, ThresholdPhase expected) =>
        ThresholdPhaseResolver
            .Resolve(new TimeOnly(hour, minute), LightsOn, 12m)
            .Should().Be(expected);

    [Fact]
    public void Resolve_handles_a_window_that_crosses_midnight()
    {
        // Lights on at 20:00 for 8 hours: the window is 20:00–04:00 the next day.
        var lightsOn = new TimeOnly(20, 0);

        ThresholdPhaseResolver.Resolve(new TimeOnly(23, 0), lightsOn, 8m).Should().Be(ThresholdPhase.Day);
        ThresholdPhaseResolver.Resolve(new TimeOnly(2, 0), lightsOn, 8m).Should().Be(ThresholdPhase.Day);
        ThresholdPhaseResolver.Resolve(new TimeOnly(12, 0), lightsOn, 8m).Should().Be(ThresholdPhase.Night);
        ThresholdPhaseResolver.Resolve(new TimeOnly(19, 0), lightsOn, 8m).Should().Be(ThresholdPhase.Night);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(23, 59)]
    public void Resolve_is_always_day_for_a_full_photoperiod(int hour, int minute) =>
        ThresholdPhaseResolver
            .Resolve(new TimeOnly(hour, minute), LightsOn, 24m)
            .Should().Be(ThresholdPhase.Day);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Resolve_is_night_when_there_is_no_photoperiod(int hours) =>
        ThresholdPhaseResolver
            .Resolve(new TimeOnly(12, 0), LightsOn, hours)
            .Should().Be(
                ThresholdPhase.Night,
                "a profile with no lighting schedule describes a night-only regime, not a day-only one");
}
