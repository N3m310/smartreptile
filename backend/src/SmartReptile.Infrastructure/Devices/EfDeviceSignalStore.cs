using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Devices;

/// <summary>
/// The <see cref="IDeviceSignalStore"/> adapter: three indexed lookups and one staged write, all inside the unit of
/// work its caller already owns.
/// </summary>
public sealed class EfDeviceSignalStore(SmartReptileDbContext db) : IDeviceSignalStore
{
    /// <inheritdoc />
    public Task<Alert?> FindOpenFaultAlertAsync(
        Guid deviceId,
        MetricCode metric,
        CancellationToken cancellationToken) =>
        db.Alerts.FirstOrDefaultAsync(
            alert => alert.DeviceId == deviceId
                && alert.Source == AlertSource.SensorFault
                && alert.Metric == metric
                && alert.State != AlertState.Resolved,
            cancellationToken);

    /// <inheritdoc />
    public Task<Alert?> FindOpenClockSkewAlertAsync(Guid deviceId, CancellationToken cancellationToken) =>
        db.Alerts.FirstOrDefaultAsync(
            alert => alert.DeviceId == deviceId
                && alert.Source == AlertSource.DeviceClockSkew
                && alert.State != AlertState.Resolved,
            cancellationToken);

    /// <inheritdoc />
    public async Task<DateTimeOffset?> FindLastClockSkewSignalAtAsync(
        Guid deviceId,
        CancellationToken cancellationToken) =>
        await db.Alerts
            .Where(alert => alert.DeviceId == deviceId && alert.Source == AlertSource.DeviceClockSkew)
            .OrderByDescending(alert => alert.TriggeredAt)
            .Select(alert => (DateTimeOffset?)alert.TriggeredAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddAlert(Alert alert) => db.Alerts.Add(alert);
}
