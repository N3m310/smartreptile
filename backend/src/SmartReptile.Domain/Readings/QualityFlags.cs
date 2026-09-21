using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Readings;

/// <summary>
/// Quality bitmask carried on every sample (§01-product/02 glossary). A flagged sample is stored — it is
/// evidence for calibration review — but implausible or faulted values are excluded from threshold
/// evaluation (BR-11.1).
/// </summary>
[Flags]
public enum QualityFlags
{
    None = 0,

    /// <summary>Sensor read failed (I²C/1-Wire fault reported by the device).</summary>
    SensorFault = 1 << 0,

    /// <summary>Value outside the metric's plausible range (rule V-06).</summary>
    Implausible = 1 << 1,

    /// <summary>First reading after boot; settling time has not elapsed.</summary>
    FirstAfterBoot = 1 << 2,

    /// <summary>Back-filled from the device ring buffer after an outage (rule V-08).</summary>
    Backfilled = 1 << 3,

    /// <summary>Device clock was not NTP-synced when the sample was produced (rule V-03).</summary>
    ClockUnsynced = 1 << 4,

    /// <summary>A calibration offset was applied at ingest (BR-06.7).</summary>
    CalibrationApplied = 1 << 5,
}

/// <summary>Rules about which flagged samples may influence alerts.</summary>
public static class QualityRules
{
    /// <summary>Bitmask of flags that make a reading unusable for threshold evaluation.</summary>
    public const QualityFlags NotEvaluable = QualityFlags.SensorFault | QualityFlags.Implausible;

    /// <summary>True when the sample may be evaluated against thresholds.</summary>
    public static bool IsEvaluable(QualityFlags flags) => (flags & NotEvaluable) == 0;

    /// <summary>Builds the flag set for one reading from plausibility (§02-design/03 §3).</summary>
    public static QualityFlags ForReading(MetricCode code, decimal value, QualityFlags baseFlags)
    {
        var flags = baseFlags;

        if (!MetricDictionary.IsPlausible(code, value))
        {
            flags |= QualityFlags.Implausible;
        }

        return flags;
    }
}
