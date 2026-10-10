using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Devices;

/// <summary>
/// The two device-health alerts that are not silence: a probe that stopped answering, and a clock the server
/// cannot trust for ordering or phase.
/// </summary>
public static class DeviceSignalAlertWriter
{
    /// <summary>
    /// A confirmed sensor fault. One row per metric, so two dead probes are two entries and the keeper is told
    /// which one to look at.
    /// </summary>
    /// <remarks>
    /// This carries the metric, where `02-design/02` §3.14 sketches `MetricId` as null for `SensorFault` alongside
    /// `DeviceSilent`. The sketch is right for silence — that alert is about the device as a whole, and the
    /// designers' own card has no metric to draw — but a fault has one, the dedupe key has a slot for it, and a
    /// device-level row would collapse "the humidity probe died" and "the light sensor died" into one entry that
    /// names neither. Recorded as a deviation in `02-design/03` §4.3 rather than left to be discovered.
    /// </remarks>
    /// <param name="device">Device whose probe failed.</param>
    /// <param name="terrariumId">Terrarium it is bound to.</param>
    /// <param name="metric">Metric the probe measures.</param>
    /// <param name="faultedAt">Instant the device reported the fault.</param>
    public static Alert OpenSensorFault(
        Device device,
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset faultedAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new Alert
        {
            TerrariumId = terrariumId,
            DeviceId = device.Id,
            Metric = metric,
            Phase = ThresholdPhase.Any,
            Severity = SensorFaultPolicy.Severity,
            DedupeKey = Alert.DedupeKeyFor(terrariumId, metric, SensorFaultPolicy.Severity, ThresholdPhase.Any),
            State = AlertState.Open,
            Source = AlertSource.SensorFault,
            TriggeredAt = faultedAt,
            LastObservedAt = faultedAt,

            // No band, no value: nothing was measured. The metric itself plus the device is the whole content, and
            // the clients build their sentence from it (ADR-013).
        };
    }

    /// <summary>
    /// The probe is answering again: the row closes at the instant the device said so, with the reason that is
    /// true — it recovered. <c>ResolvedReason.SensorFault</c> is the label for the *other* direction (a band alert
    /// closed because the probe, not the habitat, was at fault) and using it here would blur the two in the v2
    /// dataset BR-12.5 keeps.
    /// </summary>
    /// <param name="alert">The open fault alert.</param>
    /// <param name="recoveredAt">Instant the device reported the recovery.</param>
    public static void ResolveSensorFault(Alert alert, DateTimeOffset recoveredAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = recoveredAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
        alert.LastObservedAt = recoveredAt;
    }

    /// <summary>
    /// A clock-skew notice: an Info entry on the timeline, device-level because the fault is the board's. Info
    /// rather than Warning is the design's call (§02-design/05 §1) — the samples are stored and flagged, the
    /// day/night phase falls back to server time, and nothing needs doing beyond syncing the node, which happens
    /// by itself when NTP succeeds.
    /// </summary>
    /// <param name="device">Device whose clock is out.</param>
    /// <param name="terrariumId">Terrarium it is bound to.</param>
    /// <param name="observedAt">Instant the skewed sample arrived.</param>
    public static Alert OpenClockSkew(Device device, Guid terrariumId, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new Alert
        {
            TerrariumId = terrariumId,
            DeviceId = device.Id,
            Metric = null,
            Phase = ThresholdPhase.Any,
            Severity = AlertSeverity.Info,
            DedupeKey = Alert.DedupeKeyFor(terrariumId, null, AlertSeverity.Info, ThresholdPhase.Any),
            State = AlertState.Open,
            Source = AlertSource.DeviceClockSkew,
            TriggeredAt = observedAt,
            LastObservedAt = observedAt,
        };
    }

    /// <summary>The clock is keeping server time again: the entry closes at that instant.</summary>
    /// <param name="alert">The open skew entry.</param>
    /// <param name="syncedAt">Instant of the first trusted sample.</param>
    public static void ResolveClockSkew(Alert alert, DateTimeOffset syncedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = syncedAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
        alert.LastObservedAt = syncedAt;
    }

    /// <summary>
    /// The condition is still true: the open entry moves its last-observed instant instead of being repeated. One
    /// entry per episode is what the keeper reads as "still like this", where an hourly row would read as a new
    /// problem every hour.
    /// </summary>
    /// <param name="alert">The open entry.</param>
    /// <param name="observedAt">Instant being recorded.</param>
    public static void Touch(Alert alert, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.LastObservedAt = observedAt;
    }
}
