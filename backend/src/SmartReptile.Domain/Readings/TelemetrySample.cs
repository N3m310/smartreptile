using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Readings;

/// <summary>
/// One device report: all metrics measured at the same instant, plus device health.
/// The pair <c>(DeviceId, Sequence)</c> is the ingest idempotency key (DI-02) — at-least-once MQTT delivery
/// must not create a second row.
/// </summary>
public class TelemetrySample
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Terrarium this sample belongs to. Copied at ingest so rebinding a device never rewrites history.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Device that produced the sample.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Device clock timestamp (UTC), NTP-synced.</summary>
    public DateTimeOffset RecordedAt { get; set; }

    /// <summary>Server timestamp at ingest — authoritative for ordering fallback (NFR-10).</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Monotonic per-device counter; part of the dedupe key.</summary>
    public long Sequence { get; set; }

    /// <summary>Quality bitmask for the sample as a whole.</summary>
    public QualityFlags QualityFlags { get; set; }

    /// <summary><c>ReceivedAt − RecordedAt</c> at ingest; null when the device reported no NTP sync.</summary>
    public int? ClockSkewSeconds { get; set; }

    /// <summary>Firmware version that produced the sample.</summary>
    public string FirmwareVersion { get; set; } = string.Empty;

    /// <summary>Transport the sample arrived on (MQTT primary, HTTPS fallback, seed/demo data).</summary>
    public IngestSource Source { get; set; }

    /// <summary>Metric values belonging to this sample.</summary>
    public ICollection<MetricReading> Readings { get; set; } = new List<MetricReading>();
}

/// <summary>How a sample reached the server.</summary>
public enum IngestSource
{
    Mqtt = 0,
    HttpFallback = 1,
    Seed = 2,
}

/// <summary>One metric value inside a sample. Normalised so a new metric is a dictionary row, not a migration (ADR-004).</summary>
public class MetricReading
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Owning sample.</summary>
    public long SampleId { get; set; }

    /// <summary>Owning sample navigation.</summary>
    public TelemetrySample? Sample { get; set; }

    /// <summary>Which metric this value is.</summary>
    public MetricCode Metric { get; set; }

    /// <summary>Post-calibration value used for evaluation.</summary>
    public decimal Value { get; set; }

    /// <summary>Raw sensor output, kept for diagnostics; never used for evaluation.</summary>
    public decimal? RawValue { get; set; }
}
