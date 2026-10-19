using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Summaries;

/// <summary>Recomputable statistics for one metric in one UTC hour.</summary>
public class TelemetryHourlyRollup
{
    public long Id { get; set; }

    public Guid TerrariumId { get; set; }

    public MetricCode Metric { get; set; }

    public DateTimeOffset HourStartUtc { get; set; }

    public decimal? MinValue { get; set; }

    public decimal? MaxValue { get; set; }

    public decimal? AvgValue { get; set; }

    public int SampleCount { get; set; }

    public int OutOfRangeMinutes { get; set; }

    public decimal ExposureValue { get; set; }

    public Guid? ThresholdSnapshotId { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
