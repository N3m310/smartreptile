using System.Text.Json;
using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// Stages 1…6 together: the pipeline as the worker runs it, over the real parser, validator, plausibility guard,
/// calibration applier and writer, with only the database replaced (TC-I-01…03 in §04-quality/02, at unit speed).
/// </summary>
public class IngestPipelineTests
{
    private readonly FakeTelemetryStore _store = new();
    private readonly IngestPipeline _pipeline;

    public IngestPipelineTests() =>
        _pipeline = new IngestPipeline(
            new JsonTelemetryPayloadParser(),
            new TelemetryPayloadValidator(),
            TelemetryValidationLimits.Default,
            new DeviceAuthenticator(_store, new DeviceCredentials(), new FakeClock(IngestTestData.Now)),
            new PlausibilityGuard(),
            new CalibrationApplier(),
            new TelemetryWriter(_store),
            new DeviceStateUpdater(_store),
            _store);

    private Device SeedDevice(string publicId = "sr-3f9a2c", DeviceStatus status = DeviceStatus.Offline)
    {
        var device = IngestTestData.Device(publicId, status);
        _store.Devices.Add(device);
        return device;
    }

    private async Task<IngestOutcome> IngestAsync(
        Device device,
        string? payload = null,
        string? transportDeviceId = null) =>
        await _pipeline.ProcessAsync(
            IngestTestData.Envelope(transportDeviceId ?? device.PublicId, payload ?? Payload()),
            CancellationToken.None);

    [Fact]
    [Trait("TestCase", "TC-I-01")]
    public async Task GivenOneValidBatch_ThenTheSampleReadingsHealthAndDeviceStateAreWritten()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device);

        outcome.Succeeded.Should().BeTrue();
        outcome.Persisted.Should().Be(1);
        outcome.Duplicates.Should().Be(0);

        var sample = _store.Samples.Should().ContainSingle().Subject;
        sample.DeviceId.Should().Be(device.Id);
        sample.TerrariumId.Should().Be(device.TerrariumId!.Value);
        sample.Sequence.Should().Be(10_457);
        sample.RecordedAt.Should().Be(new DateTimeOffset(2026, 10, 4, 8, 15, 0, TimeSpan.Zero));
        sample.ReceivedAt.Should().Be(IngestTestData.Now);
        sample.FirmwareVersion.Should().Be("1.0.0");
        sample.Source.Should().Be(IngestSource.Mqtt);
        sample.Readings.Should().HaveCount(4, "the four core metrics of the fixture payload");

        device.LastSeenAt.Should().Be(IngestTestData.Now);
        device.Status.Should().Be(DeviceStatus.Online, "Offline → Online is the edge ingest owns (§02-design/02 §4.1)");
        device.FirmwareVersion.Should().Be("1.0.0");

        device.SignalStrengthDbm.Should().Be(-63);
        device.UptimeSeconds.Should().Be(86_400);
        device.FreeHeapKb.Should().Be(142);

        var health = _store.Health.Should().ContainSingle().Subject;
        health.DeviceId.Should().Be(device.Id);
        health.TerrariumId.Should().Be(device.TerrariumId!.Value);
        health.FirmwareVersion.Should().Be("1.0.0");
    }

    [Fact]
    [Trait("TestCase", "TC-I-01")]
    public async Task GivenABatchOfNSamples_ThenNSequencesAreStoredWithConsecutiveNumbers()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, Payload(sequence: 500, samples:
        [
            Sample(offset: 0),
            Sample(offset: 60),
            Sample(offset: 120),
        ]));

        outcome.Persisted.Should().Be(3);
        _store.Samples.Select(sample => sample.Sequence).Should().Equal(500, 501, 502);
    }

    [Fact]
    [Trait("TestCase", "TC-I-02")]
    public async Task GivenTheSameBatchPublishedFiveTimes_ThenExactlyOneSampleIsStoredAndFourAreDuplicates()
    {
        var device = SeedDevice();

        var outcomes = new List<IngestOutcome>();

        for (var publish = 0; publish < 5; publish++)
        {
            outcomes.Add(await IngestAsync(device));
        }

        outcomes.Should().AllSatisfy(outcome => outcome.Succeeded.Should().BeTrue("a retry is normal, never a 5xx"));
        outcomes[0].Persisted.Should().Be(1);
        outcomes.Skip(1).Should().AllSatisfy(outcome => outcome.Persisted.Should().Be(0));

        _store.Samples.Should().ContainSingle();
        outcomes.Sum(outcome => outcome.Duplicates).Should().Be(4);
    }

    [Fact]
    [Trait("TestCase", "TC-I-02")]
    public async Task GivenARedeliveryAfterARace_ThenTheLosingWriterReReadsAndStillSucceeds()
    {
        // The duplicate check is a fast path; DI-02 is the unique index. When two writers stage the same sequence
        // the loser must re-read and count the row that won rather than fail the batch.
        var device = SeedDevice();
        _store.RaceInsertedSequences.Add(10_457);

        var outcome = await IngestAsync(device);

        outcome.Succeeded.Should().BeTrue();
        outcome.Persisted.Should().Be(0);
        outcome.Duplicates.Should().Be(1);
        _store.SaveCount.Should().Be(2, "one refused commit, then one that succeeded");
        device.LastSeenAt.Should().Be(IngestTestData.Now, "a duplicate delivery still proves the device is alive");
    }

    [Fact]
    [Trait("TestCase", "TC-I-03")]
    public async Task GivenAnImplausibleSample_ThenItIsStoredFlaggedAndCarriesNoEvaluation()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, Payload(samples: [Sample(tf: 85)]));

        outcome.Succeeded.Should().BeTrue("rule V-06 stores it: a wild reading is evidence, not a refusal");

        var sample = _store.Samples.Should().ContainSingle().Subject;
        sample.QualityFlags.Should().HaveFlag(QualityFlags.Implausible);
        sample.Readings.Should().ContainSingle(reading => reading.Metric == MetricCode.TempC)
            .Which.Value.Should().Be(85m);

        // Nothing raised an alert because nothing evaluates yet (3.2); the row carries the flag that tells the
        // evaluator to skip it once it exists.
        QualityRules.IsEvaluable(sample.QualityFlags).Should().BeFalse();
    }

    [Fact]
    [Trait("TestCase", "TC-I-04")]
    public async Task GivenAHealthBlock_ThenTheDeviceDenormsAndTheHealthRowAreBothUpdated()
    {
        var device = SeedDevice();
        device.SignalStrengthDbm = -90;
        device.BatteryPct = 55m;

        await IngestAsync(device, Payload(health: """{"rssi":-70,"up_s":120,"heap_kb":100,"bat":null,"src":"mains"}"""));

        device.SignalStrengthDbm.Should().Be(-70);
        device.BatteryPct.Should().Be(55m, "a battery this node does not have must not be reported as 0 %");
        device.UptimeSeconds.Should().Be(120);
        device.FreeHeapKb.Should().Be(100);

        _store.Health.Should().ContainSingle();
    }

    [Fact]
    public async Task GivenABatchWithoutHealth_ThenNoHealthRowIsWrittenAndTheSampleStillLands()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, Payload(health: null));

        outcome.Persisted.Should().Be(1);
        _store.Health.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenADeviceCalibration_ThenStoredValuesAreCorrectedAndRawsAreNot()
    {
        var device = SeedDevice();
        device.CalibrationJson = """{"tempOffsetC":-0.4,"luxGain":1.03}""";

        await IngestAsync(device, Payload(samples:
        [
            Sample(tf: 28.9, lux: 1000, raw: new Dictionary<string, double>(StringComparer.Ordinal) { ["tf"] = 28.9, ["lux"] = 1000 }),
        ]));

        var sample = _store.Samples.Should().ContainSingle().Subject;

        sample.QualityFlags.Should().HaveFlag(QualityFlags.CalibrationApplied);
        sample.Readings.Single(reading => reading.Metric == MetricCode.TempC).Value.Should().Be(28.5m);
        sample.Readings.Single(reading => reading.Metric == MetricCode.LightLux).Value.Should().Be(1030m);
        sample.Readings.Single(reading => reading.Metric == MetricCode.TempC).RawValue.Should().Be(28.9m);
    }

    [Fact]
    public async Task GivenAnUnknownMetricKey_ThenItIsReportedAndTheRestOfTheBatchIsStored()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, Payload(samples: [Sample(extra: new Dictionary<string, object?>(StringComparer.Ordinal) { ["zz"] = 1.0 })]));

        outcome.Succeeded.Should().BeTrue();
        outcome.DroppedMetricKeys.Should().ContainSingle().Which.Should().Be("zz");
        _store.Samples.Should().ContainSingle();
    }

    [Fact]
    public async Task GivenASampleOlderThanTheBackfillCutoff_ThenItIsStoredAsBackfill()
    {
        var device = SeedDevice();

        // 14 hours of lateness is a sample recovered from the ring buffer after an outage (rule V-08) — not a clock
        // problem, so it must not also be reported as one.
        await IngestAsync(device, Payload(timestamp: IngestTestData.Now.AddHours(-14).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));

        var sample = _store.Samples.Should().ContainSingle().Subject;
        sample.QualityFlags.Should().HaveFlag(QualityFlags.Backfilled);
        sample.QualityFlags.Should().NotHaveFlag(QualityFlags.ClockUnsynced);
    }

    [Fact]
    public async Task GivenASampleMinutesLate_ThenItIsFlaggedAsClockSkewRatherThanBackfill()
    {
        var device = SeedDevice();

        await IngestAsync(device, Payload(timestamp: IngestTestData.Now.AddMinutes(-5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));

        var sample = _store.Samples.Should().ContainSingle().Subject;
        sample.QualityFlags.Should().HaveFlag(QualityFlags.ClockUnsynced);
        sample.QualityFlags.Should().NotHaveFlag(QualityFlags.Backfilled);
        sample.ClockSkewSeconds.Should().Be(300);
    }

    [Fact]
    public async Task GivenADeviceWhoseClockRanAhead_ThenTheTimestampIsClampedToArrival()
    {
        var device = SeedDevice();

        await IngestAsync(device, Payload(timestamp: IngestTestData.Now.AddHours(3).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));

        var sample = _store.Samples.Should().ContainSingle().Subject;

        // The stored timestamp is the server's, so a wrong device clock cannot reorder history or shift the sample
        // into the wrong day/night phase. The skew itself is still recorded, and flagged, rather than hidden.
        sample.RecordedAt.Should().Be(IngestTestData.Now);
        sample.ClockSkewSeconds.Should().Be(-(int)TimeSpan.FromHours(3).TotalSeconds);
        sample.QualityFlags.Should().HaveFlag(QualityFlags.ClockUnsynced);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task GivenAPayloadThatIsNotABatch_ThenItIsRefusedBeforeAnythingIsTouched(string payload)
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, payload);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("schema_invalid");
        _store.Samples.Should().BeEmpty();
        _store.SaveCount.Should().Be(0, "nothing may be staged for a batch that was refused");
    }

    [Fact]
    public async Task GivenABatchNamingAnotherDevice_ThenNothingIsStored()
    {
        var device = SeedDevice();

        var outcome = await IngestAsync(device, Payload(deviceId: "sr-999999"));

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("auth_failed");
        _store.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenARevokedDevice_ThenNothingIsStored()
    {
        var device = SeedDevice(status: DeviceStatus.Revoked);

        var outcome = await IngestAsync(device);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("auth_failed");
        _store.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenOneHundredAndTwentyOneSamples_ThenTheBatchIsRefusedAsTooLarge()
    {
        var device = SeedDevice();
        var samples = Enumerable.Range(0, 121).Select(index => Sample(offset: index * 60)).ToList();

        var outcome = await IngestAsync(device, Payload(samples: samples));

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("payload_too_large");
        _store.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenAProvisioningDevicePublishingItsFirstSample_ThenItBecomesOnline()
    {
        var device = SeedDevice(status: DeviceStatus.Provisioning);

        await IngestAsync(device);

        device.Status.Should().Be(DeviceStatus.Online);
    }

    [Fact]
    public async Task GivenADeviceInMaintenance_ThenIngestDoesNotClearIt()
    {
        // Leaving Maintenance is the owner's decision (§02-design/02 §4.1); ingest must not decide that for them.
        var device = SeedDevice(status: DeviceStatus.Maintenance);

        await IngestAsync(device);

        device.Status.Should().Be(DeviceStatus.Maintenance);
        device.LastSeenAt.Should().Be(IngestTestData.Now);
    }

    /// <summary>The health block the fixture batch carries unless a test says otherwise.</summary>
    private const string DefaultHealth = """{"rssi":-63,"up_s":86400,"heap_kb":142,"bat":null,"src":"mains"}""";

    /// <summary>Builds a batch body the way the firmware does (§07-appendices/03 §3.2).</summary>
    /// <param name="health">Raw health JSON, or null for a batch that carries none.</param>
    private static string Payload(
        string deviceId = "sr-3f9a2c",
        long sequence = 10_457,
        IReadOnlyList<Dictionary<string, object?>>? samples = null,
        string? health = DefaultHealth,
        string timestamp = "2026-10-04T08:15:00Z")
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["deviceId"] = deviceId,
            ["seq"] = sequence,
            ["fw"] = "1.0.0",
            ["ts"] = timestamp,
            ["samples"] = samples ?? [Sample()],
        };

        if (health is not null)
        {
            body["health"] = JsonSerializer.Deserialize<Dictionary<string, object?>>(health);
        }

        return JsonSerializer.Serialize(body);
    }

    /// <summary>One sample object with the four core metrics; <paramref name="extra"/> adds arbitrary keys.</summary>
    private static Dictionary<string, object?> Sample(
        int offset = 0,
        double tf = 28.75,
        double rh = 41.2,
        double? lux = 1820.5,
        double? uvi = 0.3,
        int? quality = null,
        Dictionary<string, double>? raw = null,
        Dictionary<string, object?>? extra = null)
    {
        var sample = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["t"] = offset,
            ["tf"] = tf,
            ["rh"] = rh,
        };

        if (lux is not null)
        {
            sample["lux"] = lux;
        }

        if (uvi is not null)
        {
            sample["uvi"] = uvi;
        }

        if (quality is not null)
        {
            sample["q"] = quality;
        }

        if (raw is not null)
        {
            sample["raw"] = raw;
        }

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                sample[key] = value;
            }
        }

        return sample;
    }
}
