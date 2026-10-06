using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// In-memory <see cref="ITelemetryStore"/>. It behaves like the real one in the two ways the pipeline depends on:
/// ids are only assigned by a successful save (so "stored" cannot be reported for a batch that was not), and a
/// failed save discards what that attempt staged.
/// </summary>
internal sealed class FakeTelemetryStore : ITelemetryStore
{
    private long _nextSampleId = 1;

    public List<Device> Devices { get; } = [];

    public List<TelemetrySample> Samples { get; } = [];

    public List<DeviceHealthSample> Health { get; } = [];

    public int SaveCount { get; private set; }

    /// <summary>
    /// Sequences that "another writer" inserts just before the next save, so the save fails on the unique index the
    /// way <c>IX_TelemetrySample_DeviceId_Sequence</c> would. Both writers keep their row — which is the point:
    /// the second pass has to notice it rather than overwrite it.
    /// </summary>
    public List<long> RaceInsertedSequences { get; } = [];

    public Task<Device?> FindDeviceAsync(string publicId, CancellationToken cancellationToken) =>
        Task.FromResult(Devices.FirstOrDefault(device => device.PublicId == publicId));

    public Task<IReadOnlySet<long>> FindExistingSequencesAsync(
        Guid deviceId,
        long first,
        long last,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<long>>(Samples
            .Where(sample => sample.DeviceId == deviceId && sample.Sequence >= first && sample.Sequence <= last)
            .Select(sample => sample.Sequence)
            .ToHashSet());

    public void AddSample(TelemetrySample sample) => Samples.Add(sample);

    public void AddHealthSample(DeviceHealthSample health) => Health.Add(health);

    public Task<bool> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        if (RaceInsertedSequences.Count > 0)
        {
            var staged = Samples.Where(sample => sample.Id == 0).ToList();

            foreach (var sample in staged)
            {
                Samples.Remove(sample);
            }

            var deviceId = staged.FirstOrDefault()?.DeviceId ?? Guid.Empty;
            var terrariumId = staged.FirstOrDefault()?.TerrariumId ?? Guid.Empty;

            foreach (var sequence in RaceInsertedSequences)
            {
                Samples.Add(new TelemetrySample
                {
                    Id = _nextSampleId++,
                    DeviceId = deviceId,
                    TerrariumId = terrariumId,
                    Sequence = sequence,
                });
            }

            RaceInsertedSequences.Clear();
            return Task.FromResult(false);
        }

        foreach (var sample in Samples.Where(sample => sample.Id == 0))
        {
            sample.Id = _nextSampleId++;

            foreach (var reading in sample.Readings)
            {
                reading.SampleId = sample.Id;
            }
        }

        return Task.FromResult(true);
    }
}

/// <summary>A clock frozen at one instant, so skew arithmetic in a test is arithmetic and not a race.</summary>
internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; } = now;

    /// <inheritdoc />
    public DateTimeOffset InZone(DateTimeOffset instantUtc, string timeZoneId) => instantUtc;

    /// <inheritdoc />
    public DateTimeOffset NowIn(string timeZoneId) => UtcNow;
}

/// <summary>Row and payload builders, so a test states the value under test and not the wiring around it.</summary>
internal static class IngestTestData
{
    /// <summary>The instant every fixture is anchored to.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 15, 0, TimeSpan.Zero);

    /// <summary>A device bound to a terrarium and not revoked.</summary>
    /// <param name="bound">
    /// False leaves <c>TerrariumId</c> null. This has to be an explicit argument rather than "pass a null id",
    /// because "null means give me a fresh one" is exactly how a test for an unbound device ends up testing a
    /// bound one.
    /// </param>
    public static Device Device(
        string publicId = "sr-3f9a2c",
        DeviceStatus status = DeviceStatus.Online,
        bool bound = true,
        string? calibrationJson = null) => new()
        {
            Id = Guid.NewGuid(),
            PublicId = publicId,
            ChipId = "chip-" + publicId,
            MacAddress = "AA:BB:CC:DD:EE:FF",
            Status = status,
            TerrariumId = bound ? Guid.NewGuid() : null,
            CalibrationJson = calibrationJson,
        };

    /// <summary>A reading inside a sample.</summary>
    public static TelemetryReading Reading(
        MetricCode code = MetricCode.TempC,
        decimal value = 28.75m,
        decimal? raw = null) => new(code, value, raw);

    /// <summary>One sample of a batch.</summary>
    public static TelemetrySampleDraft Sample(
        long sequence = 10_457,
        QualityFlags flags = QualityFlags.None,
        DateTimeOffset? recordedAt = null,
        params TelemetryReading[] readings) => new(
            sequence,
            recordedAt ?? Now,
            0,
            flags,
            readings.Length == 0 ? [Reading()] : readings);

    /// <summary>A batch carrying <paramref name="samples"/>.</summary>
    public static TelemetryBatch Batch(
        Device device,
        params TelemetrySampleDraft[] samples) => new(
            device.PublicId,
            "1.0.0",
            Now,
            Now,
            samples.Length == 0 ? [Sample()] : samples,
            null,
            IngestSource.Mqtt);

    /// <summary>A parsed document with one sample, so a test only writes the field it is about.</summary>
    public static TelemetryPayloadDocument Document(
        string? deviceId = "sr-3f9a2c",
        long? sequence = 10_457,
        string? firmware = "1.0.0",
        string? timestamp = "2026-10-04T08:15:00Z",
        IReadOnlyList<TelemetrySampleDocument>? samples = null,
        DeviceHealthDocument? health = null) => new(
            deviceId,
            sequence,
            firmware,
            timestamp,
            samples ?? [SampleDocument()],
            health);

    /// <summary>One parsed sample with the four core metrics.</summary>
    public static TelemetrySampleDocument SampleDocument(
        int? offsetSeconds = 0,
        IReadOnlyDictionary<string, decimal>? metrics = null,
        IReadOnlyDictionary<string, decimal>? rawMetrics = null,
        int? quality = null,
        IReadOnlyList<string>? invalidKeys = null) => new(
            offsetSeconds,
            metrics ?? new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["tf"] = 28.75m,
                ["rh"] = 41.2m,
                ["lux"] = 1820.5m,
                ["uvi"] = 0.3m,
            },
            rawMetrics ?? new Dictionary<string, decimal>(StringComparer.Ordinal),
            quality,
            invalidKeys ?? []);

    /// <summary>Wraps a batch in the envelope the pipeline consumes.</summary>
    public static TelemetryEnvelope Envelope(string devicePublicId, string json, string? presentedSecret = null) =>
        new(devicePublicId, System.Text.Encoding.UTF8.GetBytes(json), Now, IngestSource.Mqtt, presentedSecret);
}
