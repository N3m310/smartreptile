using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// A message as it arrived from the transport: the topic that carried it, the device the broker authenticated on
/// that topic, and the bytes. The topic's device id is kept next to the payload's <c>deviceId</c> on purpose —
/// rule V-04 is that the two agree, and a payload cannot be allowed to name a device it did not authenticate as.
/// </summary>
/// <param name="DevicePublicId">Device the transport authenticated (from the topic).</param>
/// <param name="Payload">Raw batch body.</param>
/// <param name="ReceivedAt">Server timestamp at arrival.</param>
/// <param name="Source">Which transport it came in on.</param>
/// <param name="PresentedSecret">
/// Secret the request carried, when the transport has one. MQTT authenticates at CONNECT and leaves this null;
/// the HTTPS fallback puts its <c>Authorization: Device {id}.{secret}</c> credential here (rule V-05).
/// </param>
public sealed record TelemetryEnvelope(
    string DevicePublicId,
    byte[] Payload,
    DateTimeOffset ReceivedAt,
    IngestSource Source,
    string? PresentedSecret = null,
    DeviceChannel Channel = DeviceChannel.Telemetry);

/// <summary>Result of turning bytes into a <see cref="TelemetryPayloadDocument"/>.</summary>
/// <param name="Document">The parsed batch, when the bytes were a JSON object.</param>
/// <param name="Problem">Why they were not, when they were not.</param>
public sealed record PayloadParseResult(TelemetryPayloadDocument? Document, IngestProblem? Problem)
{
    /// <summary>A parsed payload.</summary>
    public static PayloadParseResult Parsed(TelemetryPayloadDocument document) => new(document, null);

    /// <summary>Bytes that are not a batch at all, with the code to count them under.</summary>
    public static PayloadParseResult Rejected(string code, string message) => new(null, new IngestProblem(code, message));
}

/// <summary>
/// Bytes → document. An adapter port because JSON is an infrastructure concern while the rules that judge the
/// document (§02-design/03 §3) belong to the application; splitting them is what makes TC-U-01…04 testable
/// without a broker or a serialiser.
/// </summary>
public interface ITelemetryPayloadParser
{
    /// <summary>Parses one payload. Never throws for bad input: a malformed body is a <see cref="IngestProblem"/>.</summary>
    PayloadParseResult Parse(ReadOnlyMemory<byte> payload, TelemetryValidationLimits limits);
}

/// <summary>
/// The persistence the pipeline needs, in one port. The stages stage changes on the tracked <see cref="Device"/>
/// and add sample rows; a single <see cref="SaveChangesAsync"/> commits the batch, so the sample rows and the
/// device's <c>LastSeenAt</c> either both land or neither does (§02-design/03 §7).
/// </summary>
public interface ITelemetryStore
{
    /// <summary>
    /// The device, tracked, or null when no such public id exists. Callers must not tell the device which of the
    /// two it was (BR-02.2): an unknown device and a revoked one are the same answer.
    /// </summary>
    Task<Device?> FindDeviceAsync(string publicId, CancellationToken cancellationToken);

    /// <summary>
    /// Sequences in <c>[first, last]</c> that already exist for the device — the duplicate fast path. The unique
    /// index is still the guarantee; this only avoids writing rows that are certain to be refused.
    /// </summary>
    Task<IReadOnlySet<long>> FindExistingSequencesAsync(
        Guid deviceId,
        long first,
        long last,
        CancellationToken cancellationToken);

    /// <summary>Stages a sample (with its readings).</summary>
    void AddSample(TelemetrySample sample);

    /// <summary>Stages a health row.</summary>
    void AddHealthSample(DeviceHealthSample health);

    /// <summary>Stages a device event (boot, sensor fault, buffer overflow, clock, calibration).</summary>
    void AddDeviceEvent(DeviceEvent deviceEvent);

    /// <summary>Commits everything staged by this scope in one transaction.</summary>
    /// <returns>
    /// True when the batch was committed. False when the unique <c>(DeviceId, Sequence)</c> index refused the
    /// insert — meaning another writer stored the same sequence between the duplicate check and this commit. The
    /// staged changes are discarded in that case; the caller re-runs the dedupe check and commits again
    /// (TC-I-02: a duplicate delivery is never a 5xx).
    /// </returns>
    Task<bool> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One sample that a commit actually wrote. Carries the generated <see cref="SampleId"/> because both consumers —
/// the SignalR push and the evaluation queue — need a stable reference to what was stored, not what was proposed.
/// </summary>
/// <param name="SampleId">Identity assigned by the insert.</param>
/// <param name="TerrariumId">Terrarium the sample belongs to.</param>
/// <param name="DeviceId">Device that produced it.</param>
/// <param name="RecordedAt">Device timestamp as stored.</param>
/// <param name="Sequence">Monotonic counter.</param>
/// <param name="Flags">Quality bitmask as stored.</param>
/// <param name="Readings">The metric values of the sample.</param>
public sealed record PersistedSample(
    long SampleId,
    Guid TerrariumId,
    Guid DeviceId,
    DateTimeOffset RecordedAt,
    long Sequence,
    QualityFlags Flags,
    IReadOnlyList<TelemetryReading> Readings);

/// <summary>
/// Push stored samples to connected clients (FR-09). Fan-out happens <b>after</b> the commit, so a failed push
/// can never roll back a stored measurement (§02-design/03 §1).
/// </summary>
public interface ITelemetryBroadcaster
{
    /// <summary>Broadcasts samples that are already committed.</summary>
    Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken);

    /// <summary>
    /// Broadcasts a device status transition that is already committed (the <c>statusChanged</c> event of
    /// `07-appendices/03` §6). Separate from <see cref="BroadcastAsync"/> because a status message commits no
    /// samples: there is nothing to carry, only the new state.
    /// </summary>
    Task BroadcastStatusAsync(DeviceStatusChanged statusChanged, CancellationToken cancellationToken);
}

/// <summary>
/// Hands stored samples to the threshold engine. A queue rather than a call so evaluation latency cannot become
/// ingest latency, and so a failing evaluator is retried by the next sample instead of blocking the pipeline.
/// </summary>
public interface IEvaluationQueue
{
    /// <summary>Queues samples that are already committed.</summary>
    Task EnqueueAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken);
}
