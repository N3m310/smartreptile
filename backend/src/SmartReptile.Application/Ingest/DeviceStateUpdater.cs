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

        MarkHeard(device, batch.ReceivedAt, batch.FirmwareVersion);

        if (batch.Health is { } health)
        {
            ApplyHealth(device, health, batch.FirmwareVersion);
        }
    }

    /// <summary>
    /// Records that the device was heard from without touching its lifecycle state. Used by the status channel,
    /// which sets the state itself, so that "last seen" and "what it says it is" cannot fight over one write.
    /// </summary>
    /// <param name="device">Tracked device.</param>
    /// <param name="receivedAt">Server timestamp at arrival.</param>
    /// <param name="firmwareVersion">Firmware the message reported, when it reported one.</param>
    public void MarkSeen(Device device, DateTimeOffset receivedAt, string? firmwareVersion)
    {
        ArgumentNullException.ThrowIfNull(device);

        device.LastSeenAt = receivedAt;

        if (!string.IsNullOrWhiteSpace(firmwareVersion))
        {
            device.FirmwareVersion = firmwareVersion;
        }
    }

    /// <summary>
    /// Marks the device alive and moves it out of the two states ingest owns: <c>Provisioning → Online</c> (claim
    /// plus a first message) and <c>Offline → Online</c> (a message arrived). <c>Maintenance</c> is left alone
    /// because leaving it is the owner's decision, and <c>Revoked</c> never reaches this stage.
    /// </summary>
    /// <param name="device">Tracked device.</param>
    /// <param name="receivedAt">Server timestamp at arrival.</param>
    /// <param name="firmwareVersion">Firmware the message reported, when it reported one.</param>
    /// <returns>True when the lifecycle state moved.</returns>
    public bool MarkHeard(Device device, DateTimeOffset receivedAt, string? firmwareVersion)
    {
        MarkSeen(device, receivedAt, firmwareVersion);

        if (device.Status is not (DeviceStatus.Provisioning or DeviceStatus.Offline))
        {
            return false;
        }

        device.Status = DeviceStatus.Online;
        return true;
    }

    /// <summary>
    /// Denormalises a health report onto the device and stages a health row. The device must be bound: the row is
    /// attributed to a terrarium at write time so that rebinding never rewrites history.
    /// </summary>
    /// <param name="device">Tracked, bound device.</param>
    /// <param name="health">Health values to record.</param>
    /// <param name="fallbackFirmware">Firmware of the enclosing message, when the report itself names none.</param>
    public void ApplyHealth(Device device, DeviceHealthReport health, string? fallbackFirmware = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(health);

        if (device.TerrariumId is not { } terrariumId)
        {
            throw new InvalidOperationException(
                "A health row is attributed to a terrarium at write time, so the device must be bound first.");
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
            TerrariumId = terrariumId,
            RecordedAt = health.RecordedAt,
            RssiDbm = health.RssiDbm,
            UptimeSeconds = health.UptimeSeconds,
            FreeHeapKb = health.FreeHeapKb,
            BatteryPct = health.BatteryPct,
            FirmwareVersion = health.FirmwareVersion ?? fallbackFirmware ?? device.FirmwareVersion,
            QualityFlags = Domain.Readings.QualityFlags.None,
        });
    }
}
