using FluentAssertions;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// Band validation and evaluation helpers.
/// Covers TC-U-21 (ordering), TC-U-22 (critical enclosure), TC-U-23 (dwell + recovery margin) and the
/// evaluation helpers used by the alert engine.
/// </summary>
public class ThresholdBandTests
{
    private static ThresholdBand LeopardGeckoDay => new()
    {
        Metric = MetricCode.TempC,
        Phase = ThresholdPhase.Day,
        TargetMin = 26m,
        TargetMax = 32m,
        CriticalMin = 22m,
        CriticalMax = 34.5m,
        DwellWarnMinutes = 5,
        DwellCritMinutes = 2,
        RecoveryMargin = 0.5m,
    };

    [Fact]
    [Trait("TestCase", "TC-U-21")]
    public void GivenTargetMinimumAboveMaximum_ThenOrderingViolationIsReported()
    {
        var band = LeopardGeckoDay with { TargetMin = 40m, TargetMax = 30m };

        var violations = band.Validate();

        violations.Should().ContainSingle(v => v.Code == "threshold_ordering_invalid")
            .Which.Field.Should().Be("targetMin");
    }

    [Fact]
    [Trait("TestCase", "TC-U-22")]
    public void GivenCriticalBandInsideTargetBand_ThenCriticalViolationIsReported()
    {
        // Critical must enclose the target band, otherwise "critical" would be less severe than "warning".
        var band = LeopardGeckoDay with { CriticalMin = 28m, CriticalMax = 31m };

        var violations = band.Validate();

        violations.Should().Contain(v => v.Code == "threshold_critical_invalid");
        band.IsValid().Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-23")]
    public void GivenCriticalDwellLongerThanWarningDwell_ThenDwellViolationIsReported()
    {
        var band = LeopardGeckoDay with { DwellWarnMinutes = 5, DwellCritMinutes = 6 };

        band.Validate().Should().Contain(v => v.Code == "threshold_dwell_invalid");
    }

    [Fact]
    [Trait("TestCase", "TC-U-23")]
    public void GivenRecoveryMarginWiderThanHalfTheBand_ThenMarginViolationIsReported()
    {
        // A 6 °C band with a 4 °C margin could never be "recovered" — recovery would be unreachable.
        var band = LeopardGeckoDay with { RecoveryMargin = 4m };

        band.Validate().Should().Contain(v => v.Code == "threshold_recovery_margin_invalid");
    }

    [Fact]
    [Trait("TestCase", "TC-U-21")]
    public void GivenSeededLeopardGeckoBand_ThenItIsValid()
    {
        ThresholdBand band = LeopardGeckoDay;

        band.Validate().Should().BeEmpty();
    }

    [Theory]
    [InlineData(31.9, ViolationKind.None)]
    [InlineData(32.0, ViolationKind.None)]
    [InlineData(32.1, ViolationKind.Hot)]
    [InlineData(25.9, ViolationKind.Cold)]
    [InlineData(26.0, ViolationKind.None)]
    public void GivenValue_ThenDirectionMatchesTheTargetBand(double value, ViolationKind expected)
    {
        LeopardGeckoDay.Direction((decimal)value).Should().Be(expected);
    }

    [Theory]
    [InlineData(34.5, false)]
    [InlineData(34.6, true)]
    [InlineData(22.0, false)]
    [InlineData(21.9, true)]
    public void GivenValue_ThenCriticalIsDecidedByTheCriticalBand(double value, bool expectedCritical)
    {
        LeopardGeckoDay.IsCritical((decimal)value).Should().Be(expectedCritical);
    }

    [Theory]
    [InlineData(32.4, false)]   // inside the band but within the recovery margin → not recovered yet
    [InlineData(31.5, true)]    // inside by more than the margin → recovered (TC-U-15/16)
    [InlineData(26.4, false)]
    [InlineData(26.5, true)]
    public void GivenValue_ThenRecoveryRequiresTheHysteresisMargin(double value, bool expectedRecovered)
    {
        LeopardGeckoDay.IsRecovered((decimal)value).Should().Be(expectedRecovered);
    }

    [Fact]
    public void GivenBandWithoutCriticalBounds_ThenCriticalMarginsAreDerived()
    {
        var band = LeopardGeckoDay with { CriticalMin = null, CriticalMax = null };

        // 10% of the 6 °C band = 0.6, floored at 2 → derived critical band 24…34.
        band.EffectiveCriticalMin.Should().Be(24m);
        band.EffectiveCriticalMax.Should().Be(34m);
    }
}
