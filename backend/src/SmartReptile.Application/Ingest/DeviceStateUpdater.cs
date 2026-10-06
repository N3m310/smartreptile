using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// Stage 6 of the ingest pipeline: the device's own state — last seen, lifecycle status, firmware, and the health
/// values the fleet screen reads (FR-07, FR-16; TC-I-04's health half).
/// </summary>
/// <remarks>
/// <see cref="Device.LastSeenAt"/> is updated on every accepted batch, including one whose samples were all
/// duplicates. That is not an oversight: a re-delivery still proves the node is alive, and the silence watchdog
/// keys off exactly this column, so skipping it would let a healthy device be declared silent.
/// <para>
/// The status transition follows the state machine of §02-design/02 §4.1 and only the two edges that ingest owns:
/// <c>Provisioning → Online</c> (claim + first publish) and <c>Offline → Online</c> (sample received).
/// <c>Maintenance</c> is left alone because leaving it is the owner's decision, and <c>Revoked</c> never reaches
/// this stage at all.
/// </para>
/// </remarks>
public sealed class DeviceStateUpdater(ITelemetryStore store)
{
    /// <summary>Stages the device mutation and, when the batch carried health, a health row.</summary>
    public void Apply(Device device, TelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(batch);

        device.LastSeenAt = batch.ReceivedAt;

        if (device.Status is DeviceStatus.Provisioning or DeviceStatus.Offline)
        {
            device.Status = DeviceStatus.Online;
        }

        if (!string.IsNullOrWhiteSpace(batch.FirmwareVersion))
        {
            device.FirmwareVersion = batch.FirmwareVersion;
        }

        if (batch.Health is not { } health)
        {
            return;
        }

        // Each health field is optional in the payload, and a device that reports only RSSI is not reporting "no
        // battery". The previous value is therefore kept rather than being overwritten with null.
        device.SignalStrengthDbm = health.RssiDbm ?? device.SignalStrengthDbm;
        device.BatteryPct = health.BatteryPct ?? device.BatteryPct;
        device.UptimeSeconds = health.UptimeSeconds ?? device.UptimeSeconds;
        device.FreeHeapKb = health.FreeHeapKb ?? device.FreeHeapKb;

        store.AddHealthSample(new DeviceHealthSample
        {
            DeviceId = device.Id,
            TerrariumId = device.TerrariumId!.Value,
            RecordedAt = health.RecordedAt,
            RssiDbm = health.RssiDbm,
            UptimeSeconds = health.UptimeSeconds,
            FreeHeapKb = health.FreeHeapKb,
            BatteryPct = health.BatteryPct,
            FirmwareVersion = health.FirmwareVersion ?? batch.FirmwareVersion,
            QualityFlags = Domain.Readings.QualityFlags.None,
        });
    }
}
