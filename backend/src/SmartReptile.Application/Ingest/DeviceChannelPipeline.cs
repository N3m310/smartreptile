using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// The pipeline for the three device channels that are not telemetry — health, status and events
/// (§02-design/03 §1, `07-appendices/03` §3.1).
/// </summary>
/// <remarks>
/// One class rather than three, because the three channels share every stage and differ only in what they write:
/// parse → the payload must name the device the transport authenticated (rule V-04) → the device must exist, be
/// unrevoked and be bound → apply → commit. That is deliberately the same gate the sample path uses, so a device
/// cannot reach the database on one topic that it could not reach on another.
/// <para>
/// <b>Built 2026-10-09 (closes the "only telemetry is forwarded" gap of task 2.4).</b> Before this, the broker
/// accepted these three topics and forwarded nothing, which is why they were left unforwarded rather than
/// half-handled — an accepted status payload that went nowhere would have looked like it worked. What each channel
/// does now: <c>status</c> sets the lifecycle state and is the source of the <c>statusChanged</c> push,
/// <c>health</c> denormalises the fleet figures and stages a health row, <c>events</c> is stored as a
/// <see cref="DeviceEvent"/> for the derived signals of task 3.3 to read.
/// </para>
/// </remarks>
public sealed class DeviceChannelPipeline(
    IDeviceChannelParser parser,
    ITelemetryStore store,
    DeviceStateUpdater stateUpdater)
{
    /// <summary>Runs one health, status or events message through every stage.</summary>
    /// <param name="envelope">The message as it arrived, with its channel set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceChannelOutcome> ProcessAsync(
        TelemetryEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var parsed = parser.Parse(envelope.Payload, envelope.Channel);

        if (parsed.Message is not { } message)
        {
            return DeviceChannelOutcome.Rejected(parsed.Problem!.Code, parsed.Problem.Message);
        }

        if (!string.Equals(message.DeviceId, envelope.DevicePublicId, StringComparison.OrdinalIgnoreCase))
        {
            return DeviceChannelOutcome.Rejected(
                "schema_invalid",
                $"the payload names device '{message.DeviceId}' but the transport authenticated '{envelope.DevicePublicId}'");
        }

        var device = await store.FindDeviceAsync(envelope.DevicePublicId, cancellationToken).ConfigureAwait(false);

        // One refusal for every reason, as on the sample path: a device that learns "revoked" and one that learns
        // "no such device" between them get a working probe for which public ids exist (BR-02.2).
        if (device is null || device.Status == DeviceStatus.Revoked)
        {
            return DeviceChannelOutcome.Rejected(
                "auth_failed",
                $"no usable device matches '{envelope.DevicePublicId}'");
        }

        if (device.TerrariumId is not { } terrariumId)
        {
            return DeviceChannelOutcome.Rejected(
                "auth_failed",
                $"device '{envelope.DevicePublicId}' is not bound to a terrarium");
        }

        // The device's own timestamp is preferred but never trusted for ordering; the server's arrival time is what
        // the health row and the event's ReceivedAt carry (NFR-10).
        var recordedAt = message.RecordedAt ?? envelope.ReceivedAt;

        var statusChanged = Apply(device, terrariumId, message, recordedAt, envelope.ReceivedAt);

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DeviceChannelOutcome.Stored(statusChanged);
    }

    private DeviceStatusChanged? Apply(
        Device device,
        Guid terrariumId,
        DeviceMessage message,
        DateTimeOffset recordedAt,
        DateTimeOffset receivedAt)
    {
        switch (message.Channel)
        {
            case DeviceChannel.Status:
                return ApplyStatus(device, terrariumId, message, receivedAt);

            case DeviceChannel.Health:
                stateUpdater.MarkHeard(device, receivedAt, message.FirmwareVersion);

                if (message.Health is { } health)
                {
                    // The report needs one instant, and the device's own clock is only trusted when it supplied
                    // one: everything else is stamped with arrival (NFR-10).
                    stateUpdater.ApplyHealth(
                        device,
                        new DeviceHealthReport(
                            recordedAt,
                            health.RssiDbm,
                            health.UptimeSeconds,
                            health.FreeHeapKb,
                            health.BatteryPct,
                            health.PowerSource,
                            message.FirmwareVersion),
                        message.FirmwareVersion);
                }

                return null;

            case DeviceChannel.Events:
                stateUpdater.MarkHeard(device, receivedAt, message.FirmwareVersion);

                if (message.Event is { } @event)
                {
                    store.AddDeviceEvent(new DeviceEvent
                    {
                        DeviceId = device.Id,
                        TerrariumId = terrariumId,
                        Type = @event.Type,
                        Metric = @event.Metric,
                        DetailJson = @event.DetailJson,
                        RecordedAt = recordedAt,
                        ReceivedAt = receivedAt,
                    });
                }

                return null;

            default:
                // Telemetry never reaches this pipeline — the worker routes it before calling here. A refusal would
                // be a bug in the router, not a device fault, so it is left to the worker to never do it.
                return null;
        }
    }

    /// <summary>
    /// Applies a status message. The state it may set is the device's to declare; the rules are §02-design/02 §4.1:
    /// <c>online</c> always wins for a device that is running, <c>offline</c> (the LWT) only demotes a device that
    /// was online — it must not overwrite the owner's <c>maintenance</c> — and <c>maintenance</c> is accepted as-is.
    /// </summary>
    /// <param name="device">Tracked, bound device.</param>
    /// <param name="terrariumId">Terrarium the device is bound to.</param>
    /// <param name="message">Parsed status message.</param>
    /// <param name="receivedAt">Server timestamp at arrival; also the <c>lastSeenAt</c> the payload carries.</param>
    private DeviceStatusChanged? ApplyStatus(
        Device device,
        Guid terrariumId,
        DeviceMessage message,
        DateTimeOffset receivedAt)
    {
        if (message.Status is not { } requested)
        {
            return null;
        }

        var previous = device.Status;

        // State only; the lifecycle transition is the message's job here, so LastSeenAt is recorded on its own.
        stateUpdater.MarkSeen(device, receivedAt, message.FirmwareVersion);

        device.Status = requested switch
        {
            DeviceStatus.Online => DeviceStatus.Online,
            DeviceStatus.Maintenance => DeviceStatus.Maintenance,
            DeviceStatus.Offline => previous == DeviceStatus.Online ? DeviceStatus.Offline : previous,
            _ => previous,
        };

        return device.Status == previous
            ? null
            : new DeviceStatusChanged(
                terrariumId,
                device.PublicId,
                DeviceStatusNames.Of(device.Status),
                receivedAt);
    }
}
