using FluentAssertions;
using SmartReptile.Domain.Summaries;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// Exposure index, coverage and compliance maths — TC-U-46…TC-U-50 in §04-quality/02.
/// These tests exist because the numbers they check end up in the report and on the keeper's screen: an
/// exposure figure that quietly counts missing data as "in range" would make a bad day look healthy.
/// </summary>
public class ExposureCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 21, 0, 0, 0, TimeSpan.Zero);

    private static List<MinuteSample> Flat(decimal value, int minutes) =>
        Enumerable.Range(0, minutes).Select(i => new MinuteSample(Start.AddMinutes(i), value)).ToList();

    [Fact]
    [Trait("TestCase", "TC-U-46")]
    public void GivenWorkedExample_ThenTemperatureExposureMatchesTheDocumentedValue()
    {
        // §03-implementation/06 §5.1: 40 min at 34.0 °C then 20 min at 33.0 °C, target max 32 °C → 1.67 °C·h
        var grid = Flat(34m, 40).Concat(Flat(33m, 20)).ToList();

        var hot = ExposureCalculator.Hot(grid, targetMax: 32m);

        hot.Should().BeApproximately(1.67m, 0.02m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-46")]
    public void GivenValueExactlyAtTheBound_ThenItIsNotAnExcursion()
    {
        var grid = Flat(32m, 60);

        ExposureCalculator.Hot(grid, targetMax: 32m).Should().Be(0m);
        ExposureCalculator.Cold(grid, targetMin: 26m).Should().Be(0m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-46")]
    public void GivenColdExcursion_ThenColdExposureIsMeasuredNotHot()
    {
        var grid = Flat(24m, 30);

        ExposureCalculator.Cold(grid, targetMin: 26m).Should().BeApproximately(1.0m, 0.01m);
        ExposureCalculator.Hot(grid, targetMax: 32m).Should().Be(0m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-46")]
    public void GivenMissingCells_ThenTheyAreSkippedRatherThanTreatedAsInRange()
    {
        var grid = new List<MinuteSample>
        {
            new(Start, 34m),
            new(Start.AddMinutes(1), 34m),
            new(Start.AddMinutes(2), null),   // no data
            new(Start.AddMinutes(3), null),   // no data
            new(Start.AddMinutes(4), 34m),
            new(Start.AddMinutes(5), 34m),
        };

        // 4 cells with data (the two nulls contribute nothing, and are not silently counted as healthy).
        ExposureCalculator.Hot(grid, targetMax: 32m).Should().BeApproximately(4m * 2m / 60m, 0.0001m);
        CoverageMath.MinutesWithData(grid).Should().Be(4);
    }

    [Fact]
    [Trait("TestCase", "TC-U-47")]
    public void GivenHumidityExcursions_ThenDryAndWetHoursAreReportedSeparately()
    {
        // §03-implementation/06 §5.2: dryness and wetness must not be netted off against each other —
        // for a tropical species the dry direction is the dangerous one.
        // 60 min at 25 %RH with target min 30 → (30 − 25) × 1.0 h = 5.0 %RH·h
        // 30 min at 45 %RH with target max 40 → (45 − 40) × 0.5 h = 2.5 %RH·h
        var grid = Flat(25m, 60).Concat(Flat(45m, 30)).ToList();

        ExposureCalculator.HumidityDry(grid, targetMin: 30m).Should().BeApproximately(5.0m, 0.01m);
        ExposureCalculator.HumidityWet(grid, targetMax: 40m).Should().BeApproximately(2.5m, 0.01m);
        CoverageMath.MinutesWithData(grid).Should().Be(90);
    }

    [Fact]
    [Trait("TestCase", "TC-U-48")]
    public void GivenLightBelowThreshold_ThenLightHoursAndDeficitAreComputed()
    {
        var grid = Flat(1500m, 360).Concat(Flat(200m, 120)).ToList();

        var lightHours = ExposureCalculator.LightHours(grid, luxThreshold: 1000m);
        var deficit = ExposureCalculator.LightDeficitHours(requiredHours: 8m, actualHours: lightHours);

        lightHours.Should().BeApproximately(6.0m, 0.01m);
        deficit.Should().BeApproximately(2.0m, 0.01m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-48")]
    public void GivenNoData_ThenLightHoursIsZeroAndDeficitIsTheFullRequirement()
    {
        var grid = new List<MinuteSample> { new(Start, null), new(Start.AddMinutes(1), null) };

        ExposureCalculator.LightHours(grid, luxThreshold: 1000m).Should().Be(0m);
        ExposureCalculator.LightDeficitHours(8m, 0m).Should().Be(8m);
    }

    [Theory]
    [Trait("TestCase", "TC-U-49")]
    [InlineData(600, 864, 69.44)]
    [InlineData(864, 864, 100.0)]
    [InlineData(0, 864, 0.0)]
    public void GivenReceivedAndExpectedSamples_ThenCoverageIsComputed(int received, int expected, double expectedPct)
    {
        CoverageMath.CoveragePct(received, expected).Should().BeApproximately((decimal)expectedPct, 0.01m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-49")]
    public void GivenLowCoverage_ThenTheSummaryIsFlaggedLowConfidence()
    {
        var coverage = CoverageMath.CoveragePct(600, 864);

        coverage.Should().BeLessThan(80m);
        CoverageMath.IsLowConfidence(coverage).Should().BeTrue();
        CoverageMath.IsLowConfidence(95m).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-U-49")]
    public void GivenNoExpectedSamples_ThenCoverageIsZeroRatherThanDividedByZero()
    {
        CoverageMath.CoveragePct(0, 0).Should().Be(0m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-50")]
    public void GivenOutOfRangeMinutes_ThenComplianceUsesMinutesWithData()
    {
        // 60 out-of-range minutes out of 1440 minutes that actually have data.
        CoverageMath.CompliancePct(outOfRangeMinutes: 60, minutesWithData: 1440)
            .Should().BeApproximately(95.83m, 0.01m);
    }

    [Fact]
    [Trait("TestCase", "TC-U-50")]
    public void GivenGapsInTheGrid_ThenTheTimeWeightedAverageIgnoresMissingCells()
    {
        var grid = new List<MinuteSample>
        {
            new(Start, 30m),
            new(Start.AddMinutes(1), null),
            new(Start.AddMinutes(2), 34m),
        };

        ExposureCalculator.TimeWeightedAverage(grid).Should().Be(32m);
        ExposureCalculator.Min(grid).Should().Be(30m);
        ExposureCalculator.Max(grid).Should().Be(34m);
    }

    [Fact]
    public void GivenGridWithOnlyMissingCells_ThenStatisticsAreNullNotZero()
    {
        var grid = new List<MinuteSample> { new(Start, null), new(Start.AddMinutes(1), null) };

        ExposureCalculator.TimeWeightedAverage(grid).Should().BeNull();
        ExposureCalculator.Min(grid).Should().BeNull();
        ExposureCalculator.Max(grid).Should().BeNull();
    }
}
