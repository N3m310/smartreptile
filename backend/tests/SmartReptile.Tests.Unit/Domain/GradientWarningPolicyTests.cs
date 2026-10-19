using FluentAssertions;
using SmartReptile.Domain.Signals;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// <c>GradientWarning</c> (`TC-U-30`): the surface-minus-air boundary, and the cases where the rule must have no
/// opinion at all.
/// </summary>
/// <remarks>
/// The two decimals in the test case are what make the boundary decidable: 44.0 − 31.9 is 12.1 and must raise, while
/// 43.9 − 32.0 is 11.9 and must not. <see cref="decimal"/> is used rather than a floating type precisely so the first
/// of those is not 12.100000000000001 and the comparison stays a statement about temperature.
/// </remarks>
public class GradientWarningPolicyTests
{
    [Fact]
    [Trait("TestCase", "TC-U-30")]
    public void A_gradient_of_twelve_point_one_degrees_raises_a_warning()
    {
        GradientWarningPolicy.Difference(44.0m, 31.9m).Should().Be(12.1m);
        GradientWarningPolicy.IsExceeded(44.0m, 31.9m).Should().BeTrue();
    }

    [Fact]
    [Trait("TestCase", "TC-U-30")]
    public void A_gradient_of_eleven_point_nine_degrees_is_silent()
    {
        GradientWarningPolicy.Difference(43.9m, 32.0m).Should().Be(11.9m);
        GradientWarningPolicy.IsExceeded(43.9m, 32.0m).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-30")]
    public void Exactly_the_threshold_does_not_raise_because_the_comparison_is_strict()
    {
        GradientWarningPolicy.IsExceeded(44.0m, 32.0m).Should().BeFalse(
            "12 °C is the boundary the design names, not the first value past it");
    }

    [Theory]
    [Trait("TestCase", "TC-U-30")]
    [InlineData(null, 30.0)]
    [InlineData(44.0, null)]
    [InlineData(null, null)]
    public void A_missing_reading_is_not_a_gradient(double? surface, double? air)
    {
        var surfaceValue = surface is { } s ? (decimal?)s : null;
        var airValue = air is { } a ? (decimal?)a : null;

        GradientWarningPolicy.Difference(surfaceValue, airValue).Should().BeNull();
        GradientWarningPolicy.IsExceeded(surfaceValue, airValue).Should().BeFalse(
            "the rule needs both readings to have an opinion");
    }

    [Fact]
    [Trait("TestCase", "TC-U-30")]
    public void A_cooler_surface_is_a_negative_gradient_and_never_raises()
    {
        GradientWarningPolicy.Difference(25.0m, 31.0m).Should().Be(-6.0m);
        GradientWarningPolicy.IsExceeded(25.0m, 31.0m).Should().BeFalse();
    }
}
