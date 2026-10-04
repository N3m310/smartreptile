using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// Stage 3 — plausibility, §02-design/03 §3 rule V-06: TC-U-06…08 in §04-quality/02.
/// </summary>
/// <remarks>
/// The rule these tests exist to pin down is that the stage <b>flags and stores</b> rather than refusing. The
/// domain-level boundary assertions live in <c>MetricAndQualityRulesTests</c>; what is asserted here is what the
/// stage does with a whole batch.
/// </remarks>
public class PlausibilityGuardTests
{
    private readonly PlausibilityGuard _guard = new();

    [Fact]
    [Trait("TestCase", "TC-U-06")]
    public void GivenAnImpossibleTemperature_ThenTheSampleIsStoredFlaggedAndNotEvaluable()
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.TempC, 85m)]));

        var result = _guard.Apply(batch);

        result.Samples.Should().HaveCount(1, "an implausible value is evidence, not a reason to drop the batch");

        var sample = result.Samples[0];
        sample.Flags.Should().HaveFlag(QualityFlags.Implausible);
        sample.Readings.Should().ContainSingle().Which.Value.Should().Be(85m, "the value itself is preserved for review");
        PlausibilityGuard.IsEvaluable(sample).Should().BeFalse();
        PlausibilityGuard.HasImplausibleReading(sample).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(100, false)]
    [InlineData(100.1, true)]
    [InlineData(-0.1, true)]
    [Trait("TestCase", "TC-U-07")]
    public void GivenHumidityAtTheBoundary_ThenOnlyOutsideItIsFlagged(double value, bool expectedFlagged)
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.HumidityPct, (decimal)value)]));

        var sample = _guard.Apply(batch).Samples[0];

        sample.Flags.HasFlag(QualityFlags.Implausible).Should().Be(expectedFlagged);
        PlausibilityGuard.IsEvaluable(sample).Should().Be(!expectedFlagged);
    }

    [Fact]
    [Trait("TestCase", "TC-U-08")]
    public void GivenNegativeIlluminance_ThenItIsFlagged()
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.LightLux, -5m)]));

        _guard.Apply(batch).Samples[0].Flags.Should().HaveFlag(QualityFlags.Implausible);
    }

    [Fact]
    [Trait("TestCase", "TC-U-06")]
    public void GivenPlausibleValues_ThenNothingIsFlagged()
    {
        var result = _guard.Apply(IngestTestData.Batch(IngestTestData.Device()));

        result.Samples[0].Flags.Should().Be(QualityFlags.None);
        PlausibilityGuard.IsEvaluable(result.Samples[0]).Should().BeTrue();
    }

    [Fact]
    public void GivenOneBadReadingAmongGoodOnes_ThenTheSampleIsFlaggedAndTheGoodValuesSurvive()
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(readings:
            [
                IngestTestData.Reading(MetricCode.TempC, 28.75m),
                IngestTestData.Reading(MetricCode.HumidityPct, 41.2m),
                IngestTestData.Reading(MetricCode.SurfaceTempC, 81m),
            ]));

        var sample = _guard.Apply(batch).Samples[0];

        sample.Flags.Should().HaveFlag(QualityFlags.Implausible);
        sample.Readings.Should().HaveCount(3);
        PlausibilityGuard.IsEvaluable(sample).Should().BeFalse();
    }

    [Fact]
    public void GivenASensorFaultReportedByTheFirmware_ThenTheSampleIsNotEvaluableEvenIfValuesLookFine()
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(flags: QualityFlags.SensorFault));

        var sample = _guard.Apply(batch).Samples[0];

        sample.Flags.Should().HaveFlag(QualityFlags.SensorFault);
        PlausibilityGuard.IsEvaluable(sample).Should().BeFalse();
    }
}
