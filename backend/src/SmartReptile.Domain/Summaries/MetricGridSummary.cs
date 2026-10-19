using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Domain.Summaries;

/// <summary>Statistics for one metric computed from its minute grid.</summary>
public sealed record MetricGridSummary(
    decimal? MinValue,
    decimal? MaxValue,
    decimal? TimeWeightedAverage,
    int SampleCount,
    int OutOfRangeMinutes,
    decimal ExposureValue,
    decimal HumidityDryExposure,
    decimal HumidityWetExposure,
    decimal LightHours);

/// <summary>Builds metric statistics without treating missing minutes as healthy readings.</summary>
public static class MetricGridSummarizer
{
    public static MetricGridSummary Summarize(
        MetricCode metric,
        IEnumerable<(DateTimeOffset AtUtc, decimal Value)> samples,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        Func<DateTimeOffset, ThresholdBand?> thresholdAt,
        decimal lightThresholdLux)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(thresholdAt);

        var orderedSamples = samples.OrderBy(sample => sample.AtUtc).ToList();
        var points = orderedSamples
            .Where(sample => sample.AtUtc >= fromUtc && sample.AtUtc < toUtc)
            .ToList();
        var grid = MinuteGrid.Build(
            orderedSamples.Select(sample => (sample.AtUtc, (decimal?)sample.Value)),
            fromUtc,
            toUtc);

        var outOfRangeMinutes = 0;
        decimal exposureValue = 0m;
        decimal dryExposure = 0m;
        decimal wetExposure = 0m;
        decimal lightHours = 0m;

        foreach (var cell in grid)
        {
            if (cell.Value is not { } value)
            {
                continue;
            }

            var band = thresholdAt(cell.AtUtc);
            if (band?.IsOutOfBand(value) == true)
            {
                outOfRangeMinutes++;
            }

            switch (metric)
            {
                case MetricCode.TempC when band is not null:
                    exposureValue += (
                        Math.Max(0m, value - band.TargetMax) +
                        Math.Max(0m, band.TargetMin - value)) / 60m;
                    break;
                case MetricCode.HumidityPct when band is not null:
                    dryExposure += Math.Max(0m, band.TargetMin - value) / 60m;
                    wetExposure += Math.Max(0m, value - band.TargetMax) / 60m;
                    break;
                case MetricCode.LightLux when value >= lightThresholdLux:
                    lightHours += 1m / 60m;
                    break;
            }
        }

        if (metric == MetricCode.HumidityPct)
        {
            exposureValue = dryExposure + wetExposure;
        }
        else if (metric == MetricCode.LightLux)
        {
            exposureValue = lightHours;
        }

        return new MetricGridSummary(
            points.Count == 0 ? null : points.Min(sample => sample.Value),
            points.Count == 0 ? null : points.Max(sample => sample.Value),
            ExposureCalculator.TimeWeightedAverage(grid),
            points.Count,
            outOfRangeMinutes,
            exposureValue,
            dryExposure,
            wetExposure,
            lightHours);
    }
}
