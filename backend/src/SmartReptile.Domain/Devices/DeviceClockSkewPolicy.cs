using SmartReptile.Domain.Readings;

namespace SmartReptile.Domain.Devices;

/// <summary>
/// The clock-skew signal's rule (rule V-09, NFR-10): a skewed sample is recorded and flagged at ingest, and the
/// keeper is told about the *device* rather than about every sample it sends.
/// </summary>
/// <remarks>
/// The threshold is deliberately not here. It is <see cref="TelemetryIngestRules.MaxClockSkewSeconds"/>, applied
/// where the sample's timing is decided, and the outcome travels as <see cref="QualityFlags.ClockUnsynced"/> —
/// which is also what keeps a long back-fill from being reported as a clock fault, since V-08's branch never sets
/// that flag ("every honest back-fill would be labelled a device fault"). This type owns the one thing that is
/// left: how often the signal may repeat, and what counts as a sample that has the skew.
/// </remarks>
public static class DeviceClockSkewPolicy
{
    /// <summary>
    /// At most one signal per device per hour, however many skewed samples arrive in between (V-09). The floor
    /// applies to a *new* signal: while one is open it is refreshed rather than repeated, so a device that has been
    /// unsynced all day produces one timeline entry per episode, not twenty-four.
    /// </summary>
    public const int SignalIntervalMinutes = 60;

    /// <summary>True when a sample carries the skew this signal is about.</summary>
    /// <param name="flags">The sample's quality flags.</param>
    public static bool IsSkewed(QualityFlags flags) => (flags & QualityFlags.ClockUnsynced) != 0;

    /// <summary>True when a new signal is due, given when the last one was raised.</summary>
    /// <param name="nowUtc">Now.</param>
    /// <param name="lastSignalledAtUtc">When a signal was last raised for this device, or null if never.</param>
    public static bool IsSignalDue(DateTimeOffset nowUtc, DateTimeOffset? lastSignalledAtUtc) =>
        lastSignalledAtUtc is not { } last || nowUtc - last >= TimeSpan.FromMinutes(SignalIntervalMinutes);
}
