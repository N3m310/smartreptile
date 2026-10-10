using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Devices;

/// <summary>
/// The <see cref="IDeviceSilenceStore"/> adapter. Two queries per sweep — the fleet, then the open silence alerts —
/// because the watchdog judges every device at the same instant and a query per device would be the same N+1 the
/// read surface avoids for the same reason.
/// </summary>
public sealed class EfDeviceSilenceStore(SmartReptileDbContext db) : IDeviceSilenceStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WatchedDevice>> FindWatchedDevicesAsync(CancellationToken cancellationToken)
    {
        // Watched means bound and reachable: an unbound device has no terrarium to alert on, a revoked one is meant
        // to be quiet, a device still in provisioning has not started reporting, and one nobody has ever heard from
        // has no silence to measure.
        var devices = await db.Devices
            .Where(device => device.TerrariumId != null
                && device.Status != DeviceStatus.Revoked
                && device.Status != DeviceStatus.Provisioning
                && device.LastSeenAt != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (devices.Count == 0)
        {
            return [];
        }

        var deviceIds = devices.Select(device => device.Id).ToList();

        var openAlerts = await db.Alerts
            .Where(alert => alert.Source == AlertSource.DeviceSilent
                && alert.State != AlertState.Resolved
                && deviceIds.Contains(alert.DeviceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return devices
            .Select(device => new WatchedDevice(device, openAlerts.Find(alert => alert.DeviceId == device.Id)))
            .ToList();
    }

    /// <inheritdoc />
    public void AddAlert(Alert alert) => db.Alerts.Add(alert);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
