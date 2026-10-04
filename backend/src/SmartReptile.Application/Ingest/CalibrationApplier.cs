using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// Stage 4 of the ingest pipeline: applies the device's calibration offsets (BR-07.4, TC-U-09).
/// </summary>
/// <remarks>
/// The rule that matters is which column moves: <c>MetricReading.Value</c> is the corrected value the evaluator
/// and the charts use, and <c>RawValue</c> keeps the sensor's own output untouched. Collapsing the two would
/// make a later calibration review — "is this probe drifting, or did someone set an offset?" — unanswerable,
/// which is the whole reason the raw column exists (DI-06).
/// </remarks>
public sealed class CalibrationApplier
{
    /// <summary>Applies <paramref name="calibration"/> to every reading, leaving raw values alone.</summary>
    public TelemetryBatch Apply(TelemetryBatch batch, DeviceCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(calibration);

        // An uncalibrated device (the common case) must not pay for a per-reading copy, and must not collect a
        // CalibrationApplied bit that would tell the UI something changed when nothing did.
        if (calibration.IsNoOp)
        {
            return batch;
        }

        var samples = new List<TelemetrySampleDraft>(batch.Samples.Count);

        foreach (var sample in batch.Samples)
        {
            var readings = new List<TelemetryReading>(sample.Readings.Count);
            var applied = false;

            foreach (var reading in sample.Readings)
            {
                var corrected = calibration.Apply(reading.Code, reading.Value);
                applied |= corrected != reading.Value;

                readings.Add(reading with { Value = corrected });
            }

            samples.Add(applied
                ? sample with { Readings = readings, Flags = sample.Flags | QualityFlags.CalibrationApplied }
                : sample);
        }

        return batch with { Samples = samples };
    }
}
