using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>What the writer did with one batch.</summary>
/// <param name="Staged">Sample rows staged for insert, in the device's order. Ids are assigned by the commit.</param>
/// <param name="Duplicates">Samples whose <c>(DeviceId, Sequence)</c> the table already held.</param>
public sealed record WriteOutcome(IReadOnlyList<TelemetrySample> Staged, int Duplicates)
{
    /// <summary>Samples this call will insert.</summary>
    public int Persisted => Staged.Count;
}

/// <summary>
/// Stage 5 of the ingest pipeline: dedupe and stage the sample rows (DI-02/DI-03, TC-I-01…03).
/// </summary>
/// <remarks>
/// MQTT is at-least-once, so the same batch arriving twice is <b>normal</b>, not an incident: the device is
/// retrying because it never saw an acknowledgement. A duplicate is therefore answered as success with the
/// duplicate counts, which is what stops a flapping link from also flapping the device's buffer.
/// <para>
/// The existence check is a fast path, not the guarantee. DI-02 is the unique index; a race between two workers is
/// caught by the database, and the losing writer re-runs this stage against the rows that won.
/// </para>
/// <para>
/// Nothing is committed here. Staging and committing are separate so that the device's <c>LastSeenAt</c> and the
/// sample rows land in one transaction (§02-design/03 §7).
/// </para>
/// </remarks>
public sealed class TelemetryWriter(ITelemetryStore store)
{
    /// <summary>Stages every sample of the batch that the device has not already delivered.</summary>
    public async Task<WriteOutcome> WriteAsync(
        TelemetryBatch batch,
        Device device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(device);

        // A validated batch always has at least one sample, and sequences are the batch's seq plus the index, so
        // the first and last cover them all without a second query.
        var first = batch.Samples[0].Sequence;
        var last = batch.Samples[^1].Sequence;

        var existing = await store
            .FindExistingSequencesAsync(device.Id, first, last, cancellationToken)
            .ConfigureAwait(false);

        var staged = new List<TelemetrySample>(batch.Samples.Count);
        var duplicates = 0;

        foreach (var sample in batch.Samples)
        {
            if (existing.Contains(sample.Sequence))
            {
                duplicates++;
                continue;
            }

            var row = Map(sample, batch, device);
            store.AddSample(row);
            staged.Add(row);
        }

        return new WriteOutcome(staged, duplicates);
    }

    private static TelemetrySample Map(TelemetrySampleDraft sample, TelemetryBatch batch, Device device) => new()
    {
        TerrariumId = device.TerrariumId!.Value,
        DeviceId = device.Id,
        RecordedAt = sample.RecordedAt,
        ReceivedAt = batch.ReceivedAt,
        Sequence = sample.Sequence,
        QualityFlags = sample.Flags,
        ClockSkewSeconds = sample.ClockSkewSeconds,
        FirmwareVersion = batch.FirmwareVersion,
        Source = batch.Source,
        Readings = [.. sample.Readings.Select(reading => new MetricReading
        {
            Metric = reading.Code,
            Value = reading.Value,
            RawValue = reading.RawValue,
        })],
    };
}
