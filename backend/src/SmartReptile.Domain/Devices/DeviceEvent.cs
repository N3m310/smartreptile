using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Devices;

/// <summary>
/// The event vocabulary a node publishes on <c>sr/v1/d/{id}/events</c> (`07-appendices/03` §3.4).
/// </summary>
/// <remarks>
/// A closed set on purpose: the column is grouped and filtered on, and an unrecognised type has to be a stored
/// refusal rather than free text that quietly becomes a new category.
/// </remarks>
public enum DeviceEventType
{
    /// <summary>The board restarted; carries its reset reason.</summary>
    Boot = 0,

    /// <summary>A probe stopped answering; carries the metric and the consecutive-failure count.</summary>
    SensorFault = 1,

    /// <summary>A probe that had faulted is answering again.</summary>
    SensorRecovered = 2,

    /// <summary>The ring buffer dropped samples; carries the count and the oldest dropped instant.</summary>
    BufferOverflow = 3,

    /// <summary>NTP never succeeded; samples carry the clock-unsynced quality bit meanwhile.</summary>
    ClockUnsynced = 4,

    /// <summary>Calibration offsets were applied on the board.</summary>
    Calibrated = 5,
}

/// <summary>
/// One device-reported event (§02-design/02 §3.7; `07-appendices/03` §3.4), stored as evidence rather than acted on.
/// </summary>
/// <remarks>
/// This is the storage road-map task 3.3 reads: the derived signals <c>SensorFault</c>, <c>DeviceSilent</c> and
/// <c>DeviceClockSkew</c> are decisions, and they belong to that task, not to the transport that happened to carry
/// the fact. Keeping the raw event means a signal rule can be changed and re-evaluated against history instead of
/// against whatever the rule was when the sample arrived.
/// <para>
/// <see cref="TerrariumId"/> is nullable — unlike <see cref="DeviceHealthSample"/>, which is attributed at write
/// time — because the event is a property of the board, and a board that has been unbound still has a history
/// worth keeping. Today the consumer refuses events from an unbound device, so the column is written non-null;
/// it is nullable so unbinding does not have to rewrite or delete rows.
/// </para>
/// </remarks>
public class DeviceEvent
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Device that raised it.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Terrarium at the time of the event, copied for history integrity.</summary>
    public Guid? TerrariumId { get; set; }

    /// <summary>What happened.</summary>
    public DeviceEventType Type { get; set; }

    /// <summary>Metric the event concerned, for <see cref="DeviceEventType.SensorFault"/> and its recovery.</summary>
    public MetricCode? Metric { get; set; }

    /// <summary>
    /// The event's own fields, verbatim, as a JSON object. Kept whole rather than as a column per type because the
    /// payloads differ per event and a sparse wide table would turn every new firmware event into a migration.
    /// </summary>
    public string? DetailJson { get; set; }

    /// <summary>Device timestamp of the event.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Server timestamp at arrival, authoritative when the device clock is not (NFR-10).</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
