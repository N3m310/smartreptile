using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// Stage 4 — calibration, BR-07.4: TC-U-09 in §04-quality/02.
/// </summary>
public class CalibrationApplierTests
{
    private readonly CalibrationApplier _applier = new();

    [Fact]
    [Trait("TestCase", "TC-U-09")]
    public void GivenATemperatureOffset_ThenTheValueIsCorrectedAndTheRawValueIsPreserved()
    {
        var batch = IngestTestData.Batch(
            IngestTestData.Device(),
            IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.TempC, 28.9m, raw: 28.9m)]));

        var applied = _applier.Apply(batch, DeviceCalibration.Parse("""{"tempOffsetC":-0.4}"""));

        var reading = applied.Samples[0].Readings[0];
        reading.Value.Should().Be(28.5m);
        reading.RawValue.Should().Be(28.9m);
        applied.Samples[0].Flags.Should().HaveFlag(QualityFlags.CalibrationApplied);
    }

    [Fact]
    [Trait("TestCase", "TC-U-09")]
    public void GivenAHumidityOffset_ThenItIsAdded()
    {
        var applied = _applier.Apply(
            IngestTestData.Batch(
                IngestTestData.Device(),
                IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.HumidityPct, 41.2m, raw: 39.1m)])),
            DeviceCalibration.Parse("""{"rhOffsetPct":2.1}"""));

        var reading = applied.Samples[0].Readings[0];
        reading.Value.Should().Be(43.3m);
        reading.RawValue.Should().Be(39.1m);
    }

    [Fact]
    public void GivenALuxGain_ThenItIsMultiplied()
    {
        var applied = _applier.Apply(
            IngestTestData.Batch(
                IngestTestData.Device(),
                IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.LightLux, 1000m)])),
            DeviceCalibration.Parse("""{"luxGain":1.03}"""));

        applied.Samples[0].Readings[0].Value.Should().Be(1030m);
    }

    [Fact]
    public void GivenNoCalibration_ThenNothingChangesAndNoQualityBitIsSet()
    {
        var batch = IngestTestData.Batch(IngestTestData.Device());

        var applied = _applier.Apply(batch, DeviceCalibration.None);

        applied.Should().BeSameAs(batch, "an uncalibrated device must not pay for a copy");
        applied.Samples[0].Flags.Should().NotHaveFlag(QualityFlags.CalibrationApplied);
    }

    [Fact]
    public void GivenAnOffsetForAMetricTheSampleDoesNotCarry_ThenNoQualityBitIsSet()
    {
        var applied = _applier.Apply(
            IngestTestData.Batch(
                IngestTestData.Device(),
                IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.LightLux, 1000m)])),
            DeviceCalibration.Parse("""{"tempOffsetC":-0.4}"""));

        applied.Samples[0].Readings[0].Value.Should().Be(1000m);
        applied.Samples[0].Flags.Should().NotHaveFlag(
            QualityFlags.CalibrationApplied,
            "claiming a correction was applied to a sample it did not touch would be a lie in the data");
    }

    [Fact]
    public void GivenARawValue_ThenItIsNeverCalibrated()
    {
        var applied = _applier.Apply(
            IngestTestData.Batch(
                IngestTestData.Device(),
                IngestTestData.Sample(readings: [IngestTestData.Reading(MetricCode.TempC, 28.9m, raw: 28.7m)])),
            DeviceCalibration.Parse("""{"tempOffsetC":-0.4}"""));

        applied.Samples[0].Readings[0].RawValue.Should().Be(28.7m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"unknownKey":5}""")]
    public void GivenUnreadableOrEmptyCalibration_ThenTheDeviceIsTreatedAsUncalibrated(string? json) =>
        DeviceCalibration.Parse(json).Should().Be(DeviceCalibration.None);

    [Fact]
    public void GivenAMalformedCalibration_ThenIngestIsNotFailed()
    {
        // Refusing to store a real measurement because a diagnostic offset is unreadable would be the wrong trade,
        // so the batch survives and the values are stored as measured.
        var applied = _applier.Apply(
            IngestTestData.Batch(IngestTestData.Device()),
            DeviceCalibration.Parse("{ this is not json"));

        applied.Samples[0].Readings[0].Value.Should().Be(28.75m);
    }

    [Fact]
    public void GivenAMissingLuxGain_ThenTheDefaultIsOneAndNotZero()
    {
        // A default of zero would silently turn every light reading into darkness — the failure mode worth a test.
        DeviceCalibration.Parse("""{"tempOffsetC":-0.4}""").LuxGain.Should().Be(1m);
        DeviceCalibration.Parse("""{"luxGain":0}""").LuxGain.Should().Be(0m, "an explicit zero is the operator's choice");
    }

    [Fact]
    public void GivenAllThreeOffsets_ThenEachAppliesToItsOwnMetric()
    {
        var calibration = DeviceCalibration.Parse("""{"tempOffsetC":-0.4,"rhOffsetPct":2.1,"luxGain":1.03}""");

        calibration.Apply(MetricCode.TempC, 28.9m).Should().Be(28.5m);
        calibration.Apply(MetricCode.HumidityPct, 41.2m).Should().Be(43.3m);
        calibration.Apply(MetricCode.LightLux, 1000m).Should().Be(1030m);

        // Surface temperature has no documented offset; inventing one would shift a burn-risk reading.
        calibration.Apply(MetricCode.SurfaceTempC, 31.2m).Should().Be(31.2m);
        calibration.IsNoOp.Should().BeFalse();
    }
}
