using SmartReptile.Application.Devices;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// The ingest pipeline of §02-design/03 §1: bytes in, a stored sample batch or a refusal out. Each stage is a
/// separate type so each rule has one implementation and one test, and the order below is the design's stage
/// table — parse, authenticate, plausibility, calibrate, dedupe-and-persist, update device state.
/// </summary>
/// <remarks>
/// <b>Refusal codes.</b> <c>schema_invalid</c> and <c>payload_too_large</c> mean the device must fix its payload;
/// <c>auth_failed</c> means it must not be talking at all. All three increment <c>ingest_rejected_total</c> — the
/// counter that makes a firmware/backend mismatch visible before anyone notices the graphs are flat.
/// <para>
/// <b>Fan-out is not here.</b> The stored samples are returned on <see cref="IngestOutcome.Stored"/> and pushed by
/// the caller. That is deliberate: the push is best-effort and the commit is not, so mixing them would let a
/// SignalR hiccup be reported as a failed ingest of a sample that is already in the database (FR-09, 2.9).
/// </para>
/// </remarks>
public sealed class IngestPipeline(
    ITelemetryPayloadParser parser,
    TelemetryPayloadValidator validator,
    TelemetryValidationLimits limits,
    DeviceAuthenticator authenticator,
    PlausibilityGuard plausibility,
    CalibrationApplier calibration,
    TelemetryWriter writer,
    DeviceStateUpdater stateUpdater,
    DeviceSignalRecorder signals,
    ITelemetryStore store)
{
    /// <summary>Runs one envelope through every stage.</summary>
    public async Task<IngestOutcome> ProcessAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var parsed = parser.Parse(envelope.Payload, limits);

        if (parsed.Document is null)
        {
            return Reject(parsed.Problem!);
        }

        var validation = validator.Validate(parsed.Document, limits, envelope.ReceivedAt, envelope.Source);

        if (validation.Batch is null)
        {
            return Reject(validation.Problem!);
        }

        var authentication = await authenticator
            .AuthenticateAsync(validation.Batch, envelope.DevicePublicId, envelope.PresentedSecret, cancellationToken)
            .ConfigureAwait(false);

        if (authentication.Device is null)
        {
            return Reject(authentication.Problem!);
        }

        var device = authentication.Device;
        var batch = validation.Batch;

        batch = plausibility.Apply(batch);
        batch = calibration.Apply(batch, DeviceCalibration.Parse(device.CalibrationJson));

        // The clock-skew rule is applied here, before the commit, so the notice it may raise lands in the same unit
        // of work as the samples that evidenced it — a batch that fails to save fails to signal too, which is the
        // right order. Staged once rather than per attempt: the retry below re-stages the samples, not the signals.
        if (device.TerrariumId is { } terrariumId)
        {
            var skewed = batch.Samples.Any(sample => DeviceClockSkewPolicy.IsSkewed(sample.Flags));

            await signals
                .RecordBatchClockAsync(device, terrariumId, skewed, envelope.ReceivedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        var (write, committed) = await PersistAsync(batch, device, cancellationToken).ConfigureAwait(false);

        var stored = committed
            ? write.Staged.Where(sample => sample.Id != 0).Select(ToPersistedSample).ToList()
            : [];

        // A commit that lost the dedupe race twice means every sample is in the table already, which is a success.
        // A commit that failed for any other reason must not be reported as "handled": the caller sees zero stored
        // samples and the exception from the store decides what happens next (NFR-03: the device keeps its buffer).
        var duplicates = committed ? write.Duplicates : batch.Samples.Count;

        return IngestOutcome.Accepted(stored, duplicates, validation.DroppedMetricKeys);
    }

    /// <summary>
    /// Stages and commits the batch, retrying once when the dedupe fast path loses a race. The retry is bounded on
    /// purpose: a second loss means the rows are genuinely there, and looping would turn a duplicate into an
    /// outage.
    /// </summary>
    private async Task<(WriteOutcome Write, bool Committed)> PersistAsync(
        TelemetryBatch batch,
        Device device,
        CancellationToken cancellationToken)
    {
        var write = await writer.WriteAsync(batch, device, cancellationToken).ConfigureAwait(false);

        // The device state is updated even when every sample was a duplicate: a re-delivery still proves the node
        // is alive, and LastSeenAt is what the silence watchdog reads.
        stateUpdater.Apply(device, batch);

        if (await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            return (write, true);
        }

        write = await writer.WriteAsync(batch, device, cancellationToken).ConfigureAwait(false);
        stateUpdater.Apply(device, batch);

        return (write, await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false));
    }

    private static IngestOutcome Reject(IngestProblem problem) => IngestOutcome.Rejected(problem.Code, problem.Message);

    private static PersistedSample ToPersistedSample(TelemetrySample sample) => new(
        sample.Id,
        sample.TerrariumId,
        sample.DeviceId,
        sample.RecordedAt,
        sample.Sequence,
        sample.QualityFlags,
        [.. sample.Readings.Select(reading => new TelemetryReading(reading.Metric, reading.Value, reading.RawValue))]);
}
