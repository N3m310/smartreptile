using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// Which device topic a message arrived on. Telemetry carries samples; the other three carry device state, health
/// and events, which ingest stores but does not judge (§07-appendices/03 §3.1).
/// </summary>
public enum DeviceChannel
{
    /// <summary><c>sr/v1/d/{id}/telemetry</c> — a sample batch.</summary>
    Telemetry = 0,

    /// <summary><c>sr/v1/d/{id}/health</c> — device health, every five minutes.</summary>
    Health = 1,

    /// <summary><c>sr/v1/d/{id}/status</c> — <c>online</c>/<c>offline</c>/<c>maintenance</c>, retained, LWT included.</summary>
    Status = 2,

    /// <summary><c>sr/v1/d/{id}/events</c> — boot, sensor fault, buffer overflow, clock, calibration.</summary>
    Events = 3,
}

/// <summary>An event as it arrived, before it is stored.</summary>
/// <param name="Type">Event the device reported.</param>
/// <param name="Metric">Metric involved, for a sensor fault or its recovery.</param>
/// <param name="ConsecutiveFailures">
/// Failures the payload reported, for <c>sensor_fault</c> only — the count BR-07.1's device rule waits for. Read
/// here rather than out of <paramref name="DetailJson"/> later because this is the adapter that knows the payload's
/// shape; the payload is still kept whole, so a changed rule can be replayed by re-parsing the stored rows.
/// </param>
/// <param name="DetailJson">The event's own fields, kept verbatim as JSON.</param>
public sealed record DeviceEventDraft(
    DeviceEventType Type,
    MetricCode? Metric,
    int? ConsecutiveFailures,
    string? DetailJson);

/// <summary>
/// One parsed non-telemetry message. The channel says which members matter, which is why they are all optional on
/// one record: three near-identical result types would be three places to add a field the next time the firmware
/// reports something new.
/// </summary>
/// <param name="Channel">Channel the message came off.</param>
/// <param name="DeviceId">Device the payload names; must match the authenticated one, exactly as rule V-04 does.</param>
/// <param name="RecordedAt">Device timestamp, when the payload carried one.</param>
/// <param name="FirmwareVersion">Firmware reported alongside.</param>
/// <param name="Status">Lifecycle state a status message asks for, or null on the other channels.</param>
/// <param name="Health">Health values a health message carried, or null on the other channels.</param>
/// <param name="Event">Event an events message carried, or null on the other channels.</param>
public sealed record DeviceMessage(
    DeviceChannel Channel,
    string? DeviceId,
    DateTimeOffset? RecordedAt,
    string? FirmwareVersion,
    DeviceStatus? Status,
    DeviceHealthDocument? Health,
    DeviceEventDraft? Event);

/// <summary>Result of parsing one non-telemetry payload.</summary>
/// <param name="Message">The parsed message, when the bytes were usable.</param>
/// <param name="Problem">Why they were not, when they were not.</param>
public sealed record DeviceMessageParseResult(DeviceMessage? Message, IngestProblem? Problem)
{
    /// <summary>A parsed message.</summary>
    public static DeviceMessageParseResult Parsed(DeviceMessage message) => new(message, null);

    /// <summary>Bytes that are not a usable message, with the code to count them under.</summary>
    public static DeviceMessageParseResult Rejected(string code, string message) =>
        new(null, new IngestProblem(code, message));
}

/// <summary>
/// Bytes → <see cref="DeviceMessage"/>, for the health, status and events channels. An adapter port for the same
/// reason as <see cref="ITelemetryPayloadParser"/>: JSON is infrastructure, the rules that judge the message are
/// application, and splitting them is what keeps the rules testable without a serialiser.
/// </summary>
public interface IDeviceChannelParser
{
    /// <summary>Parses one payload. Never throws for bad input: a malformed body is an <see cref="IngestProblem"/>.</summary>
    DeviceMessageParseResult Parse(ReadOnlyMemory<byte> payload, DeviceChannel channel);
}

/// <summary>A device status transition, ready to be pushed to the clients that are watching the terrarium.</summary>
/// <param name="TerrariumId">Terrarium whose device changed state.</param>
/// <param name="DevicePublicId">Device that changed.</param>
/// <param name="Status">New state, as the clients spell it (lower case: <c>online</c>, <c>offline</c>, <c>maintenance</c>).</param>
/// <param name="LastSeenAt">When the server last heard from the device, which for a status message is its arrival.</param>
public sealed record DeviceStatusChanged(
    Guid TerrariumId,
    string DevicePublicId,
    string Status,
    DateTimeOffset LastSeenAt);

/// <summary>Outcome of running one health, status or events message through the channel pipeline.</summary>
public sealed record DeviceChannelOutcome
{
    /// <summary>Set when the message was stored.</summary>
    public DeviceStatusChanged? StatusChanged { get; init; }

    /// <summary>
    /// Alerts the message's own evidence opened or closed — the sensor fault of 3.3 — for the fan-out to push after
    /// the commit that gave them their identities.
    /// </summary>
    public IReadOnlyList<AlertChange> AlertChanges { get; init; } = [];

    /// <summary>Set when the message was refused.</summary>
    public IngestProblem? Problem { get; init; }

    /// <summary>True when nothing was refused.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A stored message, with the transition to broadcast when there was one.</summary>
    public static DeviceChannelOutcome Stored(
        DeviceStatusChanged? statusChanged = null,
        IReadOnlyList<AlertChange>? alertChanges = null) =>
        new() { StatusChanged = statusChanged, AlertChanges = alertChanges ?? [] };

    /// <summary>A refusal.</summary>
    public static DeviceChannelOutcome Rejected(string code, string message) =>
        new() { Problem = new IngestProblem(code, message) };
}
