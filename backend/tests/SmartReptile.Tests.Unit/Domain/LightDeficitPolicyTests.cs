using FluentAssertions;
using SmartReptile.Domain.Signals;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// <c>LightDeficit</c> (`TC-U-31`): the accumulated deficit arithmetic and the 21:00 boundary that makes the rule
/// judge a finished day rather than every morning.
/// </summary>
public class LightDeficitPolicyTests
{
    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void Six_hours_of_light_against_an_eight_hour_requirement_is_a_two_hour_deficit()
    {
        LightDeficitPolicy.DeficitHours(requiredHours: 8m, actualHours: 6m).Should().Be(2.0m);
        LightDeficitPolicy.HasDeficit(requiredHours: 8m, actualHours: 6m).Should().BeTrue();
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void A_day_that_met_its_requirement_has_no_deficit()
    {
        LightDeficitPolicy.DeficitHours(requiredHours: 8m, actualHours: 8m).Should().Be(0m);
        LightDeficitPolicy.HasDeficit(requiredHours: 8m, actualHours: 8.25m).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void A_deficit_is_never_negative_even_when_the_day_was_brighter_than_required()
    {
        LightDeficitPolicy.DeficitHours(requiredHours: 8m, actualHours: 12m).Should().Be(0m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void A_day_with_no_light_at_all_owes_the_whole_requirement()
    {
        LightDeficitPolicy.DeficitHours(requiredHours: 8m, actualHours: 0m).Should().Be(8m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void The_day_becomes_judgeable_at_exactly_twenty_one_hundred_local()
    {
        LightDeficitPolicy.IsDue(new TimeOnly(20, 59)).Should().BeFalse("the photoperiod has not closed yet");
        LightDeficitPolicy.IsDue(new TimeOnly(21, 0)).Should().BeTrue("the boundary itself is judgeable");
        LightDeficitPolicy.IsDue(new TimeOnly(21, 1)).Should().BeTrue();
        LightDeficitPolicy.IsDue(new TimeOnly(23, 59)).Should().BeTrue();
    }

    [Fact]
    [Trait("TestCase", "TC-U-31")]
    public void A_morning_is_never_judged_as_a_deficit()
    {
        // The reason the rule has a due time at all: at 09:00 no healthy day has accumulated eight hours yet, and a
        // rule that judged every hour of the day would raise a deficit every morning.
        LightDeficitPolicy.IsDue(new TimeOnly(9, 0)).Should().BeFalse();
        LightDeficitPolicy.IsDue(new TimeOnly(7, 0)).Should().BeFalse();
        LightDeficitPolicy.IsDue(new TimeOnly(0, 0)).Should().BeFalse();
    }
}
