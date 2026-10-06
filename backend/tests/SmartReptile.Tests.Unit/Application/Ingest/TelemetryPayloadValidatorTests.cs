using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// Stage 1 — schema and shape, §02-design/03 §3 rules V-01…V-03 and V-10: TC-U-01…04 in §04-quality/02.
/// </summary>
public class TelemetryPayloadValidatorTests
{
    private readonly TelemetryPayloadValidator _validator = new();

    private TelemetryValidation Validate(TelemetryPayloadDocument document) =>
        _validator.Validate(document, TelemetryValidationLimits.Default, IngestTestData.Now, IngestSource.Mqtt);

    [Fact]
    [Trait("TestCase", "TC-U-01")]
    public void GivenABatchWithoutASequence_ThenItIsRefusedAsSchemaInvalid()
    {
        var result = Validate(IngestTestData.Document(sequence: null));

        result.Succeeded.Should().BeFalse();
        result.Batch.Should().BeNull();
        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("TestCase", "TC-U-01")]
    public void GivenANonPositiveSequence_ThenItIsRefused(int sequence)
    {
        var result = Validate(IngestTestData.Document(sequence: sequence));

        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Theory]
    [InlineData("yesterday")]
    [InlineData("2026-10-04T08:15:00")]
    [InlineData("2026-10-04T08:15:00+07:00")]
    [InlineData("")]
    [Trait("TestCase", "TC-U-02")]
    public void GivenAnUnparseableOrZonelessTimestamp_ThenItIsRefused(string timestamp)
    {
        var result = Validate(IngestTestData.Document(timestamp: timestamp));

        result.Succeeded.Should().BeFalse();
        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    [Trait("TestCase", "TC-U-03")]
    public void GivenOneHundredAndTwentyOneSamples_ThenTheBatchIsTooLarge()
    {
        var samples = Enumerable.Range(0, 121)
            .Select(index => IngestTestData.SampleDocument(offsetSeconds: index * 60))
            .ToList();

        var result = Validate(IngestTestData.Document(samples: samples));

        result.Succeeded.Should().BeFalse();
        result.Batch.Should().BeNull();
        result.Problem!.Code.Should().Be("payload_too_large");
    }

    [Fact]
    [Trait("TestCase", "TC-U-03")]
    public void GivenExactlyOneHundredAndTwentySamples_ThenTheBatchIsAccepted()
    {
        var samples = Enumerable.Range(0, 120)
            .Select(index => IngestTestData.SampleDocument(offsetSeconds: index * 60))
            .ToList();

        var result = Validate(IngestTestData.Document(samples: samples));

        result.Succeeded.Should().BeTrue();
        result.Batch!.Samples.Should().HaveCount(120);
    }

    [Fact]
    [Trait("TestCase", "TC-U-04")]
    public void GivenAnUnknownMetricKey_ThenOnlyThatReadingIsDroppedAndTheBatchSurvives()
    {
        var metrics = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["tf"] = 28.75m,
            ["rh"] = 41.2m,
            ["zz"] = 1.0m,
        };

        var result = Validate(IngestTestData.Document(
            samples: [IngestTestData.SampleDocument(metrics: metrics)]));

        result.Succeeded.Should().BeTrue();
        result.DroppedMetricKeys.Should().ContainSingle().Which.Should().Be("zz");

        var readings = result.Batch!.Samples[0].Readings;
        readings.Select(reading => reading.Code)
            .Should().BeEquivalentTo([MetricCode.TempC, MetricCode.HumidityPct]);
    }

    [Fact]
    [Trait("TestCase", "TC-U-04")]
    public void GivenAKnownMetricWithANonNumericValue_ThenTheBatchIsRefused()
    {
        var result = Validate(IngestTestData.Document(
            samples: [IngestTestData.SampleDocument(invalidKeys: ["tf"])]));

        result.Succeeded.Should().BeFalse();
        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    [Trait("TestCase", "TC-U-04")]
    public void GivenASampleWhoseOnlyMetricIsUnknown_ThenTheBatchIsRefused()
    {
        // A stored sample with no readings would be a row that says nothing was measured, which is worse than a
        // refusal: the device has to notice that its payload carries no usable data.
        var metrics = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["zz"] = 1.0m };

        var result = Validate(IngestTestData.Document(
            samples: [IngestTestData.SampleDocument(metrics: metrics)]));

        result.Succeeded.Should().BeFalse();
        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public void GivenSamplesThatAreNotStrictlyIncreasing_ThenTheBatchIsRefused()
    {
        var result = Validate(IngestTestData.Document(samples:
        [
            IngestTestData.SampleDocument(offsetSeconds: 60),
            IngestTestData.SampleDocument(offsetSeconds: 60),
        ]));

        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public void GivenAnOffsetBeyondADay_ThenTheBatchIsRefused()
    {
        var result = Validate(IngestTestData.Document(samples:
        [
            IngestTestData.SampleDocument(offsetSeconds: TelemetryIngestRules.MaxSampleOffsetSeconds + 1),
        ]));

        result.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public void GivenABatchOfNSamples_ThenSequencesRunFromSeqToSeqPlusNMinusOne()
    {
        // DI-02 depends on this: the wire carries only the batch's first seq, so the mapping from sample index to
        // sequence is what makes a re-delivered batch dedupe per row rather than only at its head.
        var result = Validate(IngestTestData.Document(sequence: 1_000, samples:
        [
            IngestTestData.SampleDocument(offsetSeconds: 0),
            IngestTestData.SampleDocument(offsetSeconds: 60),
            IngestTestData.SampleDocument(offsetSeconds: 120),
        ]));

        result.Batch!.Samples.Select(sample => sample.Sequence).Should().Equal(1_000, 1_001, 1_002);
    }

    [Fact]
    public void GivenABatchTimestampAndOffsets_ThenEachSampleIsStampedRelativeToIt()
    {
        var result = Validate(IngestTestData.Document(timestamp: "2026-10-04T08:15:00Z", samples:
        [
            IngestTestData.SampleDocument(offsetSeconds: 0),
            IngestTestData.SampleDocument(offsetSeconds: 90),
        ]));

        result.Batch!.Samples[0].RecordedAt.Should().Be(new DateTimeOffset(2026, 10, 4, 8, 15, 0, TimeSpan.Zero));
        result.Batch.Samples[1].RecordedAt.Should().Be(new DateTimeOffset(2026, 10, 4, 8, 16, 30, TimeSpan.Zero));
    }

    [Fact]
    public void GivenAnUnknownRawKey_ThenItIsDroppedWithTheSameRuleAsAMetricKey()
    {
        var raw = new Dictionary<string, decimal>(StringComparer.Ordinal) { ["zz"] = 9.0m };

        var result = Validate(IngestTestData.Document(
            samples: [IngestTestData.SampleDocument(rawMetrics: raw)]));

        result.Succeeded.Should().BeTrue();
        result.DroppedMetricKeys.Should().Contain("zz");
    }

    [Fact]
    public void GivenAHealthBlock_ThenItIsCarriedOntoTheBatch()
    {
        var result = Validate(IngestTestData.Document(
            health: new DeviceHealthDocument(-63, 86_400, 142, null, "mains")));

        var health = result.Batch!.Health;
        health.Should().NotBeNull();
        health!.RssiDbm.Should().Be(-63);
        health.UptimeSeconds.Should().Be(86_400);
        health.FreeHeapKb.Should().Be(142);
        health.BatteryPct.Should().BeNull();
        health.FirmwareVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void GivenNoHealthBlock_ThenTheBatchIsStillAccepted()
    {
        var result = Validate(IngestTestData.Document());

        result.Succeeded.Should().BeTrue();
        result.Batch!.Health.Should().BeNull();
    }

    [Fact]
    public void GivenAnUnusableDeviceOrFirmware_ThenTheBatchIsRefused()
    {
        Validate(IngestTestData.Document(deviceId: "  ")).Problem!.Code.Should().Be("schema_invalid");
        Validate(IngestTestData.Document(firmware: null)).Problem!.Code.Should().Be("schema_invalid");
        Validate(IngestTestData.Document(firmware: new string('1', TelemetryIngestRules.MaxFirmwareLength + 1)))
            .Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public void GivenNoSamplesAtAll_ThenTheBatchIsRefused()
    {
        Validate(IngestTestData.Document(samples: [])).Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    [Trait("TestCase", "TC-U-09")]
    public void GivenAFirmwareQualityBitmask_ThenItIsCarriedOn() =>
        Validate(IngestTestData.Document(samples: [IngestTestData.SampleDocument(quality: (int)QualityFlags.Backfilled)]))
            .Batch!.Samples[0].Flags.Should().HaveFlag(QualityFlags.Backfilled);

    [Fact]
    public void GivenQualityBitsThisBuildDoesNotDefine_ThenTheyAreDiscarded()
    {
        // The column is a smallint and the bits are a closed vocabulary; an undefined bit is a firmware bug and
        // must not reach the database where nothing can interpret it.
        var flags = Validate(IngestTestData.Document(samples: [IngestTestData.SampleDocument(quality: 0xFF)]))
            .Batch!.Samples[0].Flags;

        ((int)flags & ~0b111111).Should().Be(0);
    }
}
