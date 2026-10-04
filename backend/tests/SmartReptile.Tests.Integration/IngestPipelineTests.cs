using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Security;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The ingest pipeline against a real SQL Server: the stages are exercised end to end over the real
/// <see cref="EfTelemetryStore"/>, so the dedupe index, the <c>decimal(9,3)</c> columns and the device denorms are
/// the ones actually being asserted rather than a fake's idea of them (TC-I-01…03 in §04-quality/02).
/// </summary>
/// <remarks>
/// Every test runs inside a transaction that is rolled back, exactly like <see cref="DatabaseInvariantTests"/>, so
/// the database under test is left as it was found. The pipeline shares the test's context, which is what makes
/// that possible — its commit is a save inside this transaction, not a second connection.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IngestPipelineTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 4, 8, 15, 30, TimeSpan.Zero);
    private const string Timestamp = "2026-10-04T08:15:00Z";

    [Fact]
    [Trait("TestCase", "TC-I-01")]
    public async Task A_valid_batch_stores_the_sample_its_readings_and_the_device_state()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId, status: 2);
        var publicId = await PublicIdAsync(db, deviceId);

        var outcome = await CreatePipeline(db).ProcessAsync(
            Envelope(publicId, Payload(publicId, samples: [Sample(tf: 28.75, rh: 41.2, lux: 1820.5, uvi: 0.3)])),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Persisted.Should().Be(1);
        outcome.Duplicates.Should().Be(0);

        var sample = await db.TelemetrySamples
            .Include(row => row.Readings)
            .SingleAsync(row => row.DeviceId == deviceId);

        sample.TerrariumId.Should().Be(terrariumId);
        sample.Sequence.Should().Be(10_457);
        sample.RecordedAt.Should().Be(new DateTimeOffset(2026, 10, 4, 8, 15, 0, TimeSpan.Zero));
        sample.FirmwareVersion.Should().Be("1.0.0");
        sample.Source.Should().Be(IngestSource.Mqtt);
        sample.Readings.Should().HaveCount(4);

        // The decimal column is the reason to run this against SQL Server: a value that survives a fake can still
        // lose its scale on the way in.
        sample.Readings.Single(reading => reading.Metric == MetricCode.TempC).Value.Should().Be(28.75m);
        sample.Readings.Single(reading => reading.Metric == MetricCode.LightLux).Value.Should().Be(1820.5m);

        var device = await db.Devices.SingleAsync(row => row.Id == deviceId);
        device.LastSeenAt.Should().NotBeNull();
        device.Status.Should().Be(DeviceStatus.Online);
        device.FirmwareVersion.Should().Be("1.0.0");
        device.SignalStrengthDbm.Should().Be(-63);
        device.UptimeSeconds.Should().Be(86_400);
        device.FreeHeapKb.Should().Be(142);

        (await db.DeviceHealthSamples.CountAsync(row => row.DeviceId == deviceId)).Should().Be(1);

        await tx.RollbackAsync();
    }

    [Fact]
    [Trait("TestCase", "TC-I-02")]
    public async Task Re_delivering_the_same_batch_five_times_stores_one_sample_and_counts_four_duplicates()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var publicId = await PublicIdAsync(db, deviceId);
        var pipeline = CreatePipeline(db);
        var envelope = Envelope(publicId, Payload(publicId));

        var outcomes = new List<IngestOutcome>();

        for (var publish = 0; publish < 5; publish++)
        {
            outcomes.Add(await pipeline.ProcessAsync(envelope, CancellationToken.None));
        }

        outcomes.Should().AllSatisfy(outcome => outcome.Succeeded.Should().BeTrue());
        outcomes[0].Persisted.Should().Be(1);
        outcomes.Skip(1).Should().AllSatisfy(outcome => outcome.Persisted.Should().Be(0));
        outcomes.Sum(outcome => outcome.Duplicates).Should().Be(4);

        (await db.TelemetrySamples.CountAsync(row => row.DeviceId == deviceId)).Should().Be(1);

        await tx.RollbackAsync();
    }

    [Fact]
    [Trait("TestCase", "TC-I-03")]
    public async Task An_implausible_sample_is_stored_flagged_and_raises_no_alert()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var publicId = await PublicIdAsync(db, deviceId);

        var outcome = await CreatePipeline(db).ProcessAsync(
            Envelope(publicId, Payload(publicId, samples: [Sample(tf: 85)])),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();

        var sample = await db.TelemetrySamples
            .Include(row => row.Readings)
            .SingleAsync(row => row.DeviceId == deviceId);

        sample.QualityFlags.Should().HaveFlag(QualityFlags.Implausible);
        sample.Readings.Single(reading => reading.Metric == MetricCode.TempC).Value.Should().Be(85m);
        QualityRules.IsEvaluable(sample.QualityFlags).Should().BeFalse();

        // "Zero alerts" is currently trivially true because the evaluator is task 3.2; what the ingest side owns is
        // the flag on the row, which is what will make the evaluator skip it.
        (await db.Alerts.CountAsync(alert => alert.TerrariumId == terrariumId)).Should().Be(0);

        await tx.RollbackAsync();
    }

    [Fact]
    [Trait("TestCase", "TC-I-04")]
    public async Task A_health_block_updates_the_device_denorms_and_writes_a_health_row()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var publicId = await PublicIdAsync(db, deviceId);

        await CreatePipeline(db).ProcessAsync(
            Envelope(publicId, Payload(publicId, health: """{"rssi":-70,"up_s":120,"heap_kb":100,"bat":38.5,"src":"battery"}""")),
            CancellationToken.None);

        var device = await db.Devices.SingleAsync(row => row.Id == deviceId);
        device.SignalStrengthDbm.Should().Be(-70);
        device.UptimeSeconds.Should().Be(120);
        device.FreeHeapKb.Should().Be(100);
        device.BatteryPct.Should().Be(38.5m);

        var health = await db.DeviceHealthSamples.SingleAsync(row => row.DeviceId == deviceId);
        health.TerrariumId.Should().Be(terrariumId);
        health.RssiDbm.Should().Be(-70);
        health.BatteryPct.Should().Be(38.5m);
        health.FirmwareVersion.Should().Be("1.0.0");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_batch_from_a_revoked_device_stores_nothing()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId, status: 3);
        var publicId = await PublicIdAsync(db, deviceId);

        var outcome = await CreatePipeline(db).ProcessAsync(Envelope(publicId, Payload(publicId)), CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("auth_failed");
        (await db.TelemetrySamples.CountAsync(row => row.DeviceId == deviceId)).Should().Be(0);

        await tx.RollbackAsync();
    }

    private static IngestPipeline CreatePipeline(SmartReptileDbContext db)
    {
        var store = new EfTelemetryStore(db, NullLogger<EfTelemetryStore>.Instance);

        return new IngestPipeline(
            new JsonTelemetryPayloadParser(),
            new TelemetryPayloadValidator(),
            TelemetryValidationLimits.Default,
            new DeviceAuthenticator(store, new DeviceCredentials(), new SystemClock()),
            new PlausibilityGuard(),
            new CalibrationApplier(),
            new TelemetryWriter(store),
            new DeviceStateUpdater(store),
            store);
    }

    private static Task<string> PublicIdAsync(SmartReptileDbContext db, Guid deviceId) =>
        db.Devices.Where(device => device.Id == deviceId).Select(device => device.PublicId).FirstAsync();

    private static TelemetryEnvelope Envelope(string publicId, string payload) =>
        new(publicId, System.Text.Encoding.UTF8.GetBytes(payload), ReceivedAt, IngestSource.Mqtt);

    private const string DefaultHealth = """{"rssi":-63,"up_s":86400,"heap_kb":142,"bat":null,"src":"mains"}""";

    private static string Payload(
        string deviceId,
        IReadOnlyList<Dictionary<string, object?>>? samples = null,
        string? health = DefaultHealth)
    {
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["deviceId"] = deviceId,
            ["seq"] = 10_457L,
            ["fw"] = "1.0.0",
            ["ts"] = Timestamp,
            ["samples"] = samples ?? [Sample()],
        };

        if (health is not null)
        {
            body["health"] = JsonSerializer.Deserialize<Dictionary<string, object?>>(health);
        }

        return JsonSerializer.Serialize(body);
    }

    private static Dictionary<string, object?> Sample(
        double tf = 28.75,
        double? rh = 41.2,
        double? lux = null,
        double? uvi = null)
    {
        var sample = new Dictionary<string, object?>(StringComparer.Ordinal) { ["t"] = 0, ["tf"] = tf };

        if (rh is not null)
        {
            sample["rh"] = rh;
        }

        if (lux is not null)
        {
            sample["lux"] = lux;
        }

        if (uvi is not null)
        {
            sample["uvi"] = uvi;
        }

        return sample;
    }
}
