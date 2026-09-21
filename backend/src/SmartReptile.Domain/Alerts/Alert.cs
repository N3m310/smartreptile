namespace SmartReptile.Domain.Alerts;

/// <summary>Alert severity. Only two operable tiers exist on purpose — a third would invent distinctions the biology does not support (§02-design/05 §1).</summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

/// <summary>Alert lifecycle state (§02-design/02 §4.2). <c>Resolved</c> is terminal; recurrence creates a new row.</summary>
public enum AlertState
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2,
}

/// <summary>What produced the alert.</summary>
public enum AlertSource
{
    Threshold = 0,
    DeviceSilent = 1,
    SensorFault = 2,
    DeviceClockSkew = 3,
    Gradient = 4,
    LightDeficit = 5,
}

/// <summary>
/// Why an alert was closed. <c>FalsePositive</c> and <c>SensorFault</c> are retained as labels for the v2
/// dataset (BR-12.5) — this is where supervised learning will get its negative examples.
/// </summary>
public enum ResolvedReason
{
    Recovered = 0,
    FalsePositive = 1,
    SensorFault = 2,
    Accepted = 3,
}

/// <summary>
/// A persisted alert episode. The band in force is denormalised (<see cref="BandMin"/>/<see cref="BandMax"/>)
/// so history stays explainable even after the thresholds are edited (UC-03 A4).
/// </summary>
public class Alert
{
    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Terrarium that raised the alert.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Device involved.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Metric that misbehaved; null for device-level alerts (silence, sensor fault).</summary>
    public Metrics.MetricCode? Metric { get; set; }

    /// <summary>Current severity.</summary>
    public AlertSeverity Severity { get; set; }

    /// <summary>Phase the value was evaluated in.</summary>
    public Thresholds.ThresholdPhase Phase { get; set; }

    /// <summary>
    /// <c>{terrariumId}:{metric}:{severity}:{phase}</c>. A filtered unique index on this column while
    /// <c>State &lt;&gt; Resolved</c> is what guarantees one open alert per key (DI-01).
    /// </summary>
    public string DedupeKey { get; set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public AlertState State { get; set; } = AlertState.Open;

    /// <summary>What produced the alert.</summary>
    public AlertSource Source { get; set; } = AlertSource.Threshold;

    /// <summary>Value at the first out-of-band reading (the trigger is back-dated to that instant).</summary>
    public decimal? TriggeringValue { get; set; }

    /// <summary>Extremum during the episode: maximum for hot excursions, minimum for cold ones.</summary>
    public decimal? PeakValue { get; set; }

    /// <summary>Lower bound of the band in force when the alert fired.</summary>
    public decimal? BandMin { get; set; }

    /// <summary>Upper bound of the band in force when the alert fired.</summary>
    public decimal? BandMax { get; set; }

    /// <summary>Start of the excursion (§02-design/03 §4.2 note 1 — back-dated for honest durations).</summary>
    public DateTimeOffset TriggeredAt { get; set; }

    /// <summary>Last sample seen while the alert was open.</summary>
    public DateTimeOffset? LastObservedAt { get; set; }

    /// <summary>When a human took ownership.</summary>
    public DateTimeOffset? AcknowledgedAt { get; set; }

    /// <summary>When the alert stopped being actionable.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Who acknowledged it.</summary>
    public Guid? AcknowledgedByUserId { get; set; }

    /// <summary>Who resolved it.</summary>
    public Guid? ResolvedByUserId { get; set; }

    /// <summary>Why it was resolved.</summary>
    public ResolvedReason? ResolvedReason { get; set; }

    /// <summary>Structured fallback text; the clients render their own localised message from the fields.</summary>
    public string? Message { get; set; }

    /// <summary>Duration of the episode; null while still open.</summary>
    public TimeSpan? Duration => ResolvedAt is { } resolved ? resolved - TriggeredAt : null;
}
