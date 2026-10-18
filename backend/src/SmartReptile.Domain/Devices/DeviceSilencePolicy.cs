using SmartReptile.Domain.Alerts;

namespace SmartReptile.Domain.Devices;

/// <summary>
/// The silence rule (FR-07; §02-design/03 §4.3): a node that stops publishing has to be told apart from a habitat
/// that is fine, and silence must never look like safety.
/// </summary>
/// <remarks>
/// Two thresholds, and only the first is a multiple of the device's own interval. The missed-interval rule is
/// relative because a node configured to report every 5 minutes is not silent after one minute; the critical one is
/// absolute (30 minutes) because past it the question stops being "is the interval right?" and becomes "is the node
/// gone?" — and that answer must not stretch with a configuration change.
/// <para>
/// The comparison is strictly greater, which is deliberate: <c>TerrariumService.DeriveStatus</c> renders the
/// online/offline badge from the same threshold and the same direction, so the badge a keeper sees and the alert
/// this policy opens cannot disagree at the boundary. Both go through <see cref="SilenceThreshold"/> for that
/// reason — one rule, one place, the same discipline the threshold resolver follows.
/// </para>
/// </remarks>
public static class DeviceSilencePolicy
{
    /// <summary>Missed intervals before a device reads as silent (§02-design/02 §4.1's <c>Online → Offline</c> edge).</summary>
    public const int DefaultSilentAfterIntervals = 3;

    /// <summary>Silence that is an emergency rather than a warning (§02-design/05 §2).</summary>
    public const int CriticalAfterMinutes = 30;

    /// <summary>
    /// The age at which a device counts as silent, from its own sampling interval: the same number the read surface
    /// derives the offline badge from, and the instant the alert's <c>TriggeredAt</c> is back-dated to.
    /// </summary>
    /// <param name="samplingIntervalSec">Interval the device was configured with.</param>
    /// <param name="silentAfterIntervals">Missed intervals before silence is called (default <c>3</c>).</param>
    public static TimeSpan SilenceThreshold(int samplingIntervalSec, int silentAfterIntervals) =>
        TimeSpan.FromSeconds(
            (long)Math.Max(1, samplingIntervalSec) * Math.Max(1, silentAfterIntervals));

    /// <summary>True when the device has been quiet long enough to be called offline.</summary>
    /// <param name="silence">How long the server has gone without hearing from it.</param>
    /// <param name="samplingIntervalSec">Interval the device was configured with.</param>
    /// <param name="silentAfterIntervals">Missed intervals before silence is called.</param>
    public static bool IsSilent(TimeSpan silence, int samplingIntervalSec, int silentAfterIntervals) =>
        silence > SilenceThreshold(samplingIntervalSec, silentAfterIntervals);

    /// <summary>
    /// What the silence deserves: <c>null</c> while it is normal, <see cref="AlertSeverity.Warning"/> once the
    /// missed-interval threshold has passed and <see cref="AlertSeverity.Critical"/> at 30 minutes.
    /// </summary>
    /// <param name="silence">How long the server has gone without hearing from it.</param>
    /// <param name="samplingIntervalSec">Interval the device was configured with.</param>
    /// <param name="silentAfterIntervals">Missed intervals before silence is called.</param>
    public static AlertSeverity? SeverityFor(TimeSpan silence, int samplingIntervalSec, int silentAfterIntervals)
    {
        if (silence >= TimeSpan.FromMinutes(CriticalAfterMinutes))
        {
            return AlertSeverity.Critical;
        }

        return IsSilent(silence, samplingIntervalSec, silentAfterIntervals) ? AlertSeverity.Warning : null;
    }

    /// <summary>
    /// The instant the silence began — when the device should have been heard from. The alert is dated from there
    /// rather than from the sweep that noticed it, so its duration measures the outage instead of measuring how
    /// often the watchdog runs.
    /// </summary>
    /// <param name="lastSeenAt">When the server last heard from the device.</param>
    /// <param name="samplingIntervalSec">Interval the device was configured with.</param>
    /// <param name="silentAfterIntervals">Missed intervals before silence is called.</param>
    public static DateTimeOffset SilenceStartedAt(
        DateTimeOffset lastSeenAt,
        int samplingIntervalSec,
        int silentAfterIntervals) =>
        lastSeenAt + SilenceThreshold(samplingIntervalSec, silentAfterIntervals);
}
