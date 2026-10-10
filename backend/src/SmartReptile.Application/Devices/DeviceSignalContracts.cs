using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Devices;

/// <summary>
/// Persistence for the device-health signals beyond silence (roadmap task 3.3): the sensor fault and the
/// clock-skew notice. The reads are the evidence the rules judge — one open alert, and when the last signal was
/// raised — and the only write stages the alert those rules produce.
/// </summary>
/// <remarks>
/// No <c>SaveChangesAsync</c>: both callers already own a unit of work (the events channel and the sample
/// pipeline each commit once, through <c>ITelemetryStore</c>), and giving this port a commit of its own would let
/// a fault be stored without the alert that explains it — or the reverse.
/// </remarks>
public interface IDeviceSignalStore
{
    /// <summary>The open sensor-fault alert for one metric of one device, when there is one.</summary>
    /// <param name="deviceId">Device to look up.</param>
    /// <param name="metric">Metric the fault is about.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Alert?> FindOpenFaultAlertAsync(Guid deviceId, MetricCode metric, CancellationToken cancellationToken);

    /// <summary>The device's open clock-skew signal, when there is one.</summary>
    /// <param name="deviceId">Device to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Alert?> FindOpenClockSkewAlertAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>When a clock-skew signal was last raised for a device — open or closed — or null if never.</summary>
    /// <param name="deviceId">Device to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DateTimeOffset?> FindLastClockSkewSignalAtAsync(Guid deviceId, CancellationToken cancellationToken);

    /// <summary>Stages a new alert.</summary>
    /// <param name="alert">Alert to stage.</param>
    void AddAlert(Alert alert);
}
