using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// Stage 3 of the ingest pipeline: the plausibility check of rule V-06 (TC-U-06…08).
/// </summary>
/// <remarks>
/// This stage <b>never rejects</b>. An impossible value is evidence — a disconnected probe reading 85 °C, a
/// shorted light sensor reading negative lux — and throwing it away would destroy exactly the diagnostic a
/// calibration review needs (DI-06). What it does instead is set <see cref="QualityFlags.Implausible"/>, which
/// <see cref="QualityRules.IsEvaluable"/> reads as "store it, never alert on it" (BR-11.1).
/// <para>
/// The bit is set on the <i>sample</i> as well as the reading, because that is the column the stored row carries:
/// the firmware's <c>q</c> bitmask is per sample, and the reading table has nowhere to put it.
/// </para>
/// </remarks>
public sealed class PlausibilityGuard
{
    /// <summary>Flags every implausible reading and the samples that contain one.</summary>
    public TelemetryBatch Apply(TelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var samples = new List<TelemetrySampleDraft>(batch.Samples.Count);

        foreach (var sample in batch.Samples)
        {
            var flags = sample.Flags;

            foreach (var reading in sample.Readings)
            {
                flags = QualityRules.ForReading(reading.Code, reading.Value, flags);
            }

            samples.Add(flags == sample.Flags ? sample : sample with { Flags = flags });
        }

        return batch with { Samples = samples };
    }

    /// <summary>
    /// True when the sample may influence thresholds. The evaluator asks this rather than re-deriving the rule,
    /// so "flagged but stored" and "excluded from alerts" can never drift apart.
    /// </summary>
    public static bool IsEvaluable(TelemetrySampleDraft sample) => QualityRules.IsEvaluable(sample.Flags);

    /// <summary>True when this sample must not be evaluated — currently any implausible reading.</summary>
    public static bool HasImplausibleReading(TelemetrySampleDraft sample) =>
        sample.Flags.HasFlag(QualityFlags.Implausible)
        || sample.Readings.Any(reading => !MetricDictionary.IsPlausible(reading.Code, reading.Value));
}
