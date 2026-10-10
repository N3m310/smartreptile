using SmartReptile.Application.Alerts;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Devices;

/// <summary>
/// Applies the two device-health rules that are not silence (roadmap task 3.3), at the moment the evidence
/// arrives: the sensor fault from the event that reports it, and the clock-skew notice from the batch that shows
/// it.
/// </summary>
/// <remarks>
/// Both rules decide the same way — is there an open entry, and does it still describe the world? — and both write
/// through the caller's unit of work, which is why this lives beside the alerts rather than inside either
/// pipeline. Nothing here sends anything: an Info entry and a Warning are rows the dispatcher will notice (3.5),
/// which is the same boundary the band engine keeps.
/// </remarks>
public sealed class DeviceSignalRecorder(IDeviceSignalStore store)
{
    /// <summary>Applies the sensor-fault rule to one event the device published.</summary>
    /// <param name="device">Device that published it.</param>
    /// <param name="terrariumId">Terrarium it is bound to.</param>
    /// <param name="draft">The event as parsed, with the failure count the payload carried.</param>
    /// <param name="recordedAt">Instant to date the decision from — the device's own timestamp.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The alert that opened or closed, empty when the event moved nothing. The caller pushes it after its commit,
    /// which is the only moment the row has an identity to name in a payload.
    /// </returns>
    public async Task<IReadOnlyList<AlertChange>> RecordEventAsync(
        Device device,
        Guid terrariumId,
        DeviceEventDraft draft,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(draft);

        // A fault with no metric names no probe, so there is nothing to report unavailable; the event itself is
        // still stored by the caller, which is the evidence rule a later replay would read.
        if (draft.Metric is not { } metric)
        {
            return [];
        }

        switch (draft.Type)
        {
            case DeviceEventType.SensorFault when SensorFaultPolicy.IsConfirmed(draft.ConsecutiveFailures):
                if (await store.FindOpenFaultAlertAsync(device.Id, metric, cancellationToken).ConfigureAwait(false)
                    is null)
                {
                    var opened = DeviceSignalAlertWriter.OpenSensorFault(device, terrariumId, metric, recordedAt);
                    store.AddAlert(opened);

                    return [new AlertChange(opened, AlertEvent.Opened)];
                }

                break;

            case DeviceEventType.SensorRecovered:
                if (await store.FindOpenFaultAlertAsync(device.Id, metric, cancellationToken).ConfigureAwait(false)
                    is { } recovered)
                {
                    DeviceSignalAlertWriter.ResolveSensorFault(recovered, recordedAt);

                    return [new AlertChange(recovered, AlertEvent.Resolved)];
                }

                break;

            default:
                // Boot, buffer overflow, clock-unsynced-from-the-device and calibration are stored as evidence and
                // are nobody's alert today. Naming them here is what keeps this switch from reading as an omission.
                break;
        }

        return [];
    }

    /// <summary>Applies the clock-skew rule to one accepted batch.</summary>
    /// <param name="device">Device the batch came from.</param>
    /// <param name="terrariumId">Terrarium it is bound to.</param>
    /// <param name="skewed">
    /// True when any sample in the batch carries <c>QualityFlags.ClockUnsynced</c>. The batch is the unit here
    /// because the *device's* clock is what is questioned, and a batch of one minute's samples says it once.
    /// </param>
    /// <param name="observedAt">Instant the batch arrived.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The notice that opened or closed, empty when the batch moved nothing.</returns>
    public async Task<IReadOnlyList<AlertChange>> RecordBatchClockAsync(
        Device device,
        Guid terrariumId,
        bool skewed,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var open = await store.FindOpenClockSkewAlertAsync(device.Id, cancellationToken).ConfigureAwait(false);

        if (!skewed)
        {
            if (open is not null)
            {
                DeviceSignalAlertWriter.ResolveClockSkew(open, observedAt);

                return [new AlertChange(open, AlertEvent.Resolved)];
            }

            return [];
        }

        if (open is not null)
        {
            // A touch is not a lifecycle move, so it is not announced: the documented event reports open, escalate,
            // acknowledge and resolve, and a client that re-rendered the same entry every batch would be noise.
            DeviceSignalAlertWriter.Touch(open, observedAt);

            return [];
        }

        var lastSignalAt = await store.FindLastClockSkewSignalAtAsync(device.Id, cancellationToken).ConfigureAwait(false);

        if (!DeviceClockSkewPolicy.IsSignalDue(observedAt, lastSignalAt))
        {
            return [];
        }

        var notice = DeviceSignalAlertWriter.OpenClockSkew(device, terrariumId, observedAt);
        store.AddAlert(notice);

        return [new AlertChange(notice, AlertEvent.Opened)];
    }
}
