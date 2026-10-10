using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Devices;

/// <summary>
/// Writes the three field-level changes a silence episode can take: the row it opens, the severity it can reach and
/// the end it gets when the device speaks again.
/// </summary>
/// <remarks>
/// The same split <see cref="Evaluation.ThresholdAlertWriter"/> uses for the band engine — "what does this quiet
/// mean?" is the monitor's question, "which fields does that change?" is this one's — and for the same second
/// reason: kept static and dependency-free, a test can assert one alert row without a store, a clock or a sweep.
/// </remarks>
public static class DeviceSilenceAlertWriter
{
    /// <summary>
    /// The alert a silence opens. A device-level row: no metric, no band, no value, and dated from the instant the
    /// device should have been heard from rather than from the sweep that noticed.
    /// </summary>
    /// <param name="device">Device that has gone quiet.</param>
    /// <param name="terrariumId">Terrarium it is bound to.</param>
    /// <param name="severity">Severity the silence deserves.</param>
    /// <param name="silenceStartedAt">When the device should have been heard from.</param>
    public static Alert Open(
        Device device,
        Guid terrariumId,
        AlertSeverity severity,
        DateTimeOffset silenceStartedAt)
    {
        ArgumentNullException.ThrowIfNull(device);

        return new Alert
        {
            TerrariumId = terrariumId,
            DeviceId = device.Id,

            // Silence is not a metric misbehaving, which is why this row carries no band and no reading at all
            // (§02-design/02 §3.14: MetricId is null for DeviceSilent and SensorFault).
            Metric = null,
            Phase = ThresholdPhase.Any,
            Severity = severity,
            DedupeKey = Alert.DedupeKeyFor(terrariumId, null, severity, ThresholdPhase.Any),
            State = AlertState.Open,
            Source = AlertSource.DeviceSilent,
            TriggeredAt = silenceStartedAt,

            // TriggeringValue, PeakValue, BandMin, BandMax and LastObservedAt all stay null on purpose: silence is
            // the absence of readings, and putting a number, a band or an "observed at" on this row would be
            // inventing one. The clients render their own sentence from Source, TriggeredAt and the device's own
            // figures (ADR-013).
        };
    }

    /// <summary>
    /// Silence that has become an emergency: the same row, the severity it now deserves, and its original start —
    /// the outage began when it began, and a re-dated row would report the moment the watchdog noticed instead.
    /// </summary>
    /// <param name="alert">The open silence alert.</param>
    public static void Escalate(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.Severity = AlertSeverity.Critical;
        alert.DedupeKey = Alert.DedupeKeyFor(alert.TerrariumId, alert.Metric, AlertSeverity.Critical, alert.Phase);
    }

    /// <summary>
    /// The device is back. Resolved at the instant it spoke again rather than at the instant the sweep noticed,
    /// because that sample is what ended the outage and it is also the last reading this alert ever saw.
    /// <c>Recovered</c> is the only reason the watchdog writes: a human's verdict belongs to the lifecycle API
    /// (task 3.4).
    /// </summary>
    /// <param name="alert">The open silence alert.</param>
    /// <param name="heardAgainAt">When the device was heard from again.</param>
    public static void Resolve(Alert alert, DateTimeOffset heardAgainAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = heardAgainAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
        alert.LastObservedAt = heardAgainAt;
    }
}
