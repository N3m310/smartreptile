using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Application.Devices;

/// <summary>
/// The silence watchdog's settings (FR-07). Only the missed-interval count is configuration: the read surface
/// derives the online badge from the same number, so a deployment that changes it changes both answers at once.
/// The 30-minute critical threshold is a rule, not a setting (§02-design/03 §4.3).
/// </summary>
/// <param name="SilentAfterIntervals">Missed intervals before a device reads as silent.</param>
public sealed record DeviceSilenceSettings(int SilentAfterIntervals);

/// <summary>
/// One device the watchdog is responsible for: the tracked row it may move, and the silence alert that is already
/// open for it.
/// </summary>
/// <param name="Device">Tracked device row. A sweep may move its lifecycle state to <c>Offline</c>.</param>
/// <param name="OpenAlert">
/// The device's open <c>DeviceSilent</c> alert, when one exists. Carried here so that going critical and coming
/// back are changes to one row — an escalation that opened a second row would lose the episode's start, and a
/// resolution that opened another would leave two alerts for one outage.
/// </param>
public sealed record WatchedDevice(Device Device, Alert? OpenAlert);

/// <summary>What one sweep did, so the worker can log and count it without re-reading the database.</summary>
/// <param name="Watched">Devices judged in this sweep (bound, unrevoked, heard from at least once, not in maintenance).</param>
/// <param name="MarkedOffline">Devices whose stored lifecycle state moved <c>Online → Offline</c>.</param>
/// <param name="AlertsOpened">New <c>DeviceSilent</c> alert rows written.</param>
/// <param name="CriticalAlertsOpened">Of those, how many opened straight into silence's critical window.</param>
/// <param name="AlertsEscalated">Open silence alerts whose severity was raised to critical.</param>
/// <param name="AlertsResolved">Open silence alerts closed because the device spoke again.</param>
public sealed record DeviceSilenceOutcome(
    int Watched,
    int MarkedOffline = 0,
    int AlertsOpened = 0,
    int CriticalAlertsOpened = 0,
    int AlertsEscalated = 0,
    int AlertsResolved = 0)
{
    /// <summary>Nothing to do.</summary>
    public static readonly DeviceSilenceOutcome None = new(0);

    /// <summary>Of the alerts opened, the ones that were warnings.</summary>
    public int WarningAlertsOpened => AlertsOpened - CriticalAlertsOpened;
}

/// <summary>
/// Persistence for the silence watchdog (FR-07, roadmap task 3.3). A port of its own rather than a method on
/// <c>ITelemetryStore</c>: the watchdog writes the device's lifecycle state and one alert family, and the ingest
/// pipeline must not be able to reach either by accident.
/// </summary>
public interface IDeviceSilenceStore
{
    /// <summary>
    /// Every device the watchdog watches: bound to a terrarium, unrevoked and heard from at least once. A device
    /// nobody has ever heard from has no silence to measure, and one that is unbound has no terrarium to alert on.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<WatchedDevice>> FindWatchedDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Stages a new alert.</summary>
    /// <param name="alert">Alert to stage.</param>
    void AddAlert(Alert alert);

    /// <summary>Commits everything this sweep staged.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
