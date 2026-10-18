using SmartReptile.Application.Devices;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// In-memory <see cref="IDeviceSignalStore"/>: the alert rows a recorder staged, and the same three lookups the
/// real store answers, so a test can assert both the decision and its idempotency.
/// </summary>
internal sealed class FakeDeviceSignalStore : IDeviceSignalStore
{
    private readonly List<Alert> _alerts = [];

    public IReadOnlyList<Alert> Alerts => _alerts;

    /// <summary>Puts a row in place, as the database would already have it.</summary>
    public void Seed(Alert alert) => _alerts.Add(alert);

    public Task<Alert?> FindOpenFaultAlertAsync(
        Guid deviceId,
        MetricCode metric,
        CancellationToken cancellationToken) =>
        Task.FromResult(_alerts.FirstOrDefault(alert =>
            alert.DeviceId == deviceId
            && alert.Source == AlertSource.SensorFault
            && alert.Metric == metric
            && alert.State != AlertState.Resolved));

    public Task<Alert?> FindOpenClockSkewAlertAsync(Guid deviceId, CancellationToken cancellationToken) =>
        Task.FromResult(_alerts.FirstOrDefault(alert =>
            alert.DeviceId == deviceId
            && alert.Source == AlertSource.DeviceClockSkew
            && alert.State != AlertState.Resolved));

    public Task<DateTimeOffset?> FindLastClockSkewSignalAtAsync(
        Guid deviceId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_alerts
            .Where(alert => alert.DeviceId == deviceId && alert.Source == AlertSource.DeviceClockSkew)
            .Select(alert => (DateTimeOffset?)alert.TriggeredAt)
            .OrderByDescending(at => at)
            .FirstOrDefault());

    public void AddAlert(Alert alert) => _alerts.Add(alert);
}
