using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// One telemetry batch exactly as it came off the wire, before any rule has been applied
/// (§07-appendices/03 §3.2). Every field is nullable because the point of the document is to survive a payload
/// the firmware got wrong: a missing <c>seq</c> has to become a field-level violation, not a deserialisation
/// exception that loses the diagnostic.
/// </summary>
/// <param name="DeviceId">The public id the device claims; must match the authenticated one.</param>
/// <param name="Sequence">Monotonic per-device counter of the batch's first sample; the dedupe key (DI-02).</param>
/// <param name="FirmwareVersion">Firmware that produced the batch.</param>
/// <param name="Timestamp">ISO-8601 UTC text of <c>t = 0</c>, unparsed on purpose (rule V-03).</param>
/// <param name="Samples">The samples, or null when the array was missing.</param>
/// <param name="Health">Optional device health block, carried in every telemetry batch (§3.2).</param>
public sealed record TelemetryPayloadDocument(
    string? DeviceId,
    long? Sequence,
    string? FirmwareVersion,
    string? Timestamp,
    IReadOnlyList<TelemetrySampleDocument>? Samples,
    DeviceHealthDocument? Health);

/// <summary>
/// One sample inside a batch. Metric values stay keyed by their wire key (<c>tf</c>, <c>rh</c>, …) because the
/// dictionary lookup — and therefore "this firmware sent a metric I do not know" — is a rule, not a parse step
/// (rule V-10).
/// </summary>
/// <param name="OffsetSeconds">Seconds from the batch timestamp; must be strictly increasing across the batch.</param>
/// <param name="Metrics">Post-filter values, keyed by payload key.</param>
/// <param name="RawMetrics">Sensor values for diagnostics, keyed by payload key; never used for evaluation.</param>
/// <param name="Quality">Quality bitmask reported by the firmware.</param>
/// <param name="InvalidKeys">
/// Keys that were present but did not carry a number. A known metric among these is a schema violation for the
/// whole batch; an unknown one is simply dropped, like any other unrecognised key.
/// </param>
public sealed record TelemetrySampleDocument(
    int? OffsetSeconds,
    IReadOnlyDictionary<string, decimal> Metrics,
    IReadOnlyDictionary<string, decimal> RawMetrics,
    int? Quality,
    IReadOnlyList<string> InvalidKeys);

/// <summary>The optional health block of a telemetry batch.</summary>
/// <param name="RssiDbm">Wi-Fi signal strength.</param>
/// <param name="UptimeSeconds">Device uptime.</param>
/// <param name="FreeHeapKb">Free heap, the early warning for a leak.</param>
/// <param name="BatteryPct">Battery percentage, when the node has one.</param>
/// <param name="PowerSource"><c>mains</c> or <c>battery</c>.</param>
public sealed record DeviceHealthDocument(
    int? RssiDbm,
    long? UptimeSeconds,
    int? FreeHeapKb,
    decimal? BatteryPct,
    string? PowerSource);

/// <summary>One metric value destined for a <c>MetricReading</c> row.</summary>
/// <param name="Code">Dictionary code, resolved from the payload key.</param>
/// <param name="Value">Post-calibration value used for evaluation.</param>
/// <param name="RawValue">Sensor output kept for diagnostics.</param>
public sealed record TelemetryReading(MetricCode Code, decimal Value, decimal? RawValue);

/// <summary>One sample destined for a <c>TelemetrySample</c> row, after plausibility and calibration.</summary>
/// <param name="Sequence">Dedupe key: the batch's <c>seq</c> plus the sample's index within the batch.</param>
/// <param name="RecordedAt">Device clock timestamp, already clamped by the timing rules.</param>
/// <param name="ClockSkewSeconds">Observed <c>ReceivedAt − RecordedAt</c>.</param>
/// <param name="Flags">Merged quality bitmask.</param>
/// <param name="Readings">The metric values of this sample.</param>
public sealed record TelemetrySampleDraft(
    long Sequence,
    DateTimeOffset RecordedAt,
    int ClockSkewSeconds,
    QualityFlags Flags,
    IReadOnlyList<TelemetryReading> Readings);

/// <summary>A validated, calibrated batch ready for persistence.</summary>
/// <param name="DevicePublicId">Public id, already cross-checked against the transport.</param>
/// <param name="FirmwareVersion">Firmware reported by the device.</param>
/// <param name="RecordedAt">Batch timestamp (<c>t = 0</c>).</param>
/// <param name="ReceivedAt">Server timestamp, authoritative for ordering (NFR-10).</param>
/// <param name="Samples">One entry per sample in the batch, in the device's order.</param>
/// <param name="Health">Health values to denormalise onto the device, when the batch carried any.</param>
/// <param name="Source">Transport the batch arrived on.</param>
public sealed record TelemetryBatch(
    string DevicePublicId,
    string FirmwareVersion,
    DateTimeOffset RecordedAt,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<TelemetrySampleDraft> Samples,
    DeviceHealthReport? Health,
    IngestSource Source);

/// <summary>Health values of one batch, ready to be written to the device row and the health table.</summary>
/// <param name="RecordedAt">Batch timestamp the health block was reported with.</param>
/// <param name="RssiDbm">Wi-Fi signal strength.</param>
/// <param name="UptimeSeconds">Device uptime.</param>
/// <param name="FreeHeapKb">Free heap.</param>
/// <param name="BatteryPct">Battery percentage.</param>
/// <param name="PowerSource"><c>mains</c> or <c>battery</c>.</param>
/// <param name="FirmwareVersion">Firmware at report time.</param>
public sealed record DeviceHealthReport(
    DateTimeOffset RecordedAt,
    int? RssiDbm,
    long? UptimeSeconds,
    int? FreeHeapKb,
    decimal? BatteryPct,
    string? PowerSource,
    string? FirmwareVersion);

/// <summary>An expected ingest failure, with the stable code the counters and the logs both use.</summary>
/// <param name="Code">Rule code, e.g. <c>schema_invalid</c> or <c>payload_too_large</c>.</param>
/// <param name="Message">Short explanation, safe to log.</param>
public sealed record IngestProblem(string Code, string Message);

/// <summary>
/// Outcome of running one batch through the pipeline. <see cref="Problem"/> is null on success, so a device that
/// re-sends a batch it already delivered gets the duplicate counts rather than an error (BR-06.3).
/// </summary>
public sealed record IngestOutcome
{
    /// <summary>Samples this call committed, with the identities the insert assigned. The fan-out step reads these.</summary>
    public IReadOnlyList<PersistedSample> Stored { get; init; } = [];

    /// <summary>Samples written by this call.</summary>
    public int Persisted => Stored.Count;

    /// <summary>Samples the unique <c>(DeviceId, Sequence)</c> index already held.</summary>
    public int Duplicates { get; init; }

    /// <summary>Metric keys dropped because this build has no such metric (rule V-10).</summary>
    public IReadOnlyList<string> DroppedMetricKeys { get; init; } = [];

    /// <summary>The expected failure, when the batch was refused.</summary>
    public IngestProblem? Problem { get; init; }

    /// <summary>True when the batch was accepted (entirely or partly).</summary>
    public bool Succeeded => Problem is null;

    /// <summary>True when the batch carried nothing new, which the HTTP fallback reports as 0 accepted.</summary>
    public bool FullyDuplicate => Succeeded && Persisted == 0;

    /// <summary>An accepted batch.</summary>
    public static IngestOutcome Accepted(
        IReadOnlyList<PersistedSample> stored,
        int duplicates,
        IReadOnlyList<string>? droppedKeys = null) =>
        new() { Stored = stored, Duplicates = duplicates, DroppedMetricKeys = droppedKeys ?? [] };

    /// <summary>A refused batch.</summary>
    public static IngestOutcome Rejected(string code, string message) =>
        new() { Problem = new IngestProblem(code, message) };
}

/// <summary>Limits the validator enforces, bound from configuration so a demo can tighten them without a rebuild.</summary>
/// <param name="MaxSamplesPerBatch">Rule V-01.</param>
/// <param name="MaxOffsetSeconds">Largest accepted <c>t</c> offset.</param>
/// <param name="MaxPayloadKb">Rule V-01's size half; enforced where the bytes are, by the transport adapter.</param>
public sealed record TelemetryValidationLimits(int MaxSamplesPerBatch, int MaxOffsetSeconds, int MaxPayloadKb)
{
    /// <summary>The documented defaults of §02-design/03 §3.</summary>
    public static TelemetryValidationLimits Default { get; } = new(
        TelemetryIngestRules.MaxSamplesPerBatch,
        TelemetryIngestRules.MaxSampleOffsetSeconds,
        TelemetryIngestRules.MaxPayloadKb);
}

/// <summary>Result of the schema stage: either a batch, or the reason there is none.</summary>
public sealed record TelemetryValidation
{
    /// <summary>The validated batch, when the payload passed every rule.</summary>
    public TelemetryBatch? Batch { get; init; }

    /// <summary>The refusal, when it did not.</summary>
    public IngestProblem? Problem { get; init; }

    /// <summary>Metric keys dropped along the way (rule V-10), for the counter and the log line.</summary>
    public IReadOnlyList<string> DroppedMetricKeys { get; init; } = [];

    /// <summary>True when a batch is available.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A payload that passed validation.</summary>
    public static TelemetryValidation Valid(TelemetryBatch batch, IReadOnlyList<string> droppedKeys) =>
        new() { Batch = batch, DroppedMetricKeys = droppedKeys };

    /// <summary>A payload that was refused.</summary>
    public static TelemetryValidation Invalid(string code, string message) =>
        new() { Problem = new IngestProblem(code, message) };
}
