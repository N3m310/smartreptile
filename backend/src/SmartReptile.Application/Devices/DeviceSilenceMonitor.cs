using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Devices;

/// <summary>
/// The silence watchdog's sweep (FR-07, roadmap task 3.3): judges every watched device once, moves the one that has
/// gone quiet to <c>Offline</c> with an alert that says so, and closes that alert when it speaks again.
/// </summary>
/// <remarks>
/// A sweep rather than a timer per device, because silence is a property of the *absence* of messages: nothing
/// arrives to trigger anything, so somebody has to look. The interval it runs at is the worker's business, not the
/// rule's — the rule is <see cref="DeviceSilencePolicy"/> and it is pure, which is what lets the boundaries be
/// tested without a clock, a store or a wait.
/// <para>
/// Three things this deliberately does not do. It does not raise a metric alert for missing data: a silent node
/// produces no readings, so the band engine has nothing to judge, and "no data" must never be reported as "in
/// range" or as an excursion (BR-11.1). It does not touch a device in <c>Maintenance</c>, because that state exists
/// so routine servicing does not create false alerts (§02-design/02 §4.1) — nor does it close an alert that was
/// already open when the device went in. And it does not send anything: delivery is the dispatcher's (task 3.5),
/// which is the same boundary the band engine keeps.
/// </para>
/// </remarks>
public sealed class DeviceSilenceMonitor(
    IDeviceSilenceStore store,
    IClock clock,
    DeviceSilenceSettings settings)
{
    /// <summary>Judges every watched device once.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DeviceSilenceOutcome> SweepAsync(CancellationToken cancellationToken = default)
    {
        var watched = await store.FindWatchedDevicesAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;

        var judged = 0;
        var offline = 0;
        var opened = 0;
        var critical = 0;
        var escalated = 0;
        var resolved = 0;
        var changes = new List<AlertChange>();

        foreach (var (device, openAlert) in watched)
        {
            if (device.TerrariumId is not { } terrariumId
                || device.Status == DeviceStatus.Maintenance
                || device.LastSeenAt is not { } lastSeenAt)
            {
                continue;
            }

            judged++;

            var severity = DeviceSilencePolicy.SeverityFor(
                now - lastSeenAt,
                device.SamplingIntervalSec,
                settings.SilentAfterIntervals);

            if (severity is null)
            {
                if (openAlert is not null)
                {
                    DeviceSilenceAlertWriter.Resolve(openAlert, lastSeenAt);
                    changes.Add(new AlertChange(openAlert, AlertEvent.Resolved));
                    resolved++;
                }

                continue;
            }

            // The state machine's Online → Offline edge is silence (§02-design/02 §4.1), and it is the same
            // threshold as the warning, so the badge a client renders from the stored status and the alert agree by
            // construction. A device already Offline was moved by an earlier sweep or by the device's own LWT.
            if (device.Status == DeviceStatus.Online)
            {
                device.Status = DeviceStatus.Offline;
                offline++;
            }

            if (openAlert is null)
            {
                var raised = DeviceSilenceAlertWriter.Open(
                    device,
                    terrariumId,
                    severity.Value,
                    DeviceSilencePolicy.SilenceStartedAt(
                        lastSeenAt,
                        device.SamplingIntervalSec,
                        settings.SilentAfterIntervals));

                store.AddAlert(raised);
                changes.Add(new AlertChange(raised, AlertEvent.Opened));
                opened++;

                if (severity.Value == AlertSeverity.Critical)
                {
                    critical++;
                }
            }
            else if (severity.Value == AlertSeverity.Critical && openAlert.Severity != AlertSeverity.Critical)
            {
                DeviceSilenceAlertWriter.Escalate(openAlert);
                changes.Add(new AlertChange(openAlert, AlertEvent.Escalated));
                escalated++;
            }
        }

        // Saved even when nothing changed: the sweep is one unit of work, and a batch-sized conditional around the
        // commit is how a staged change gets left behind on the path nobody exercised.
        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new DeviceSilenceOutcome(judged, offline, opened, critical, escalated, resolved)
        {
            AlertChanges = changes,
        };
    }
}
