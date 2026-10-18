namespace SmartReptile.Domain.Devices;

/// <summary>
/// The bounds a device's settings are kept inside (FR-04's configuration surface). Constants rather than literals
/// in a validator, so the documentation, the service and the tests name the same numbers.
/// </summary>
/// <remarks>
/// The interval bounds are engineering limits, not biology: below ten seconds a battery-powered node spends more
/// time awake than asleep, and above five minutes the dashboard's coverage ratio (FR-07) stops being meaningful.
/// A publish interval is validated on its own rather than against the sampling interval — the firmware is free to
/// publish a buffered batch, and inventing a cross-rule here would refuse a configuration the board can serve.
/// </remarks>
public static class DeviceLimits
{
    /// <summary>Fastest interval a node may be asked to report at, in seconds.</summary>
    public const int MinIntervalSec = 10;

    /// <summary>Slowest interval a node may be asked to report at, in seconds.</summary>
    public const int MaxIntervalSec = 300;

    /// <summary>Longest display name, matching the column width.</summary>
    public const int NameMaxLength = 60;
}
