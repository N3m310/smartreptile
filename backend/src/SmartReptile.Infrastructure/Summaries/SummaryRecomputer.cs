using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Summaries;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Summaries;

/// <summary>Rebuilds recent hourly rollups and the current and previous local-day summaries.</summary>
public sealed class SummaryRecomputer(SmartReptileDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SummaryRecomputeOutcome> RecomputeAsync(CancellationToken cancellationToken) =>
        RecomputeAsync(null, cancellationToken);

    public Task<SummaryRecomputeOutcome> RecomputeTerrariumAsync(
        Guid terrariumId,
        CancellationToken cancellationToken) =>
        RecomputeAsync(terrariumId, cancellationToken);

    private async Task<SummaryRecomputeOutcome> RecomputeAsync(
        Guid? terrariumId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var completedMinute = Floor(now, TimeSpan.FromMinutes(1));
        var rollupFrom = Floor(now.AddHours(-48), TimeSpan.FromHours(1));
        var terrariums = await db.Terrariums
            .AsNoTracking()
            .AsSplitQuery()
            .Include(terrarium => terrarium.SpeciesProfile)
                .ThenInclude(profile => profile!.Thresholds)
            .Include(terrarium => terrarium.ThresholdOverrides)
            .Include(terrarium => terrarium.Device)
            .Where(terrarium => terrarium.Status == TerrariumStatus.Active &&
                                (terrariumId == null || terrarium.Id == terrariumId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var updatedRollups = 0;
        var updatedSummaries = 0;

        foreach (var terrarium in terrariums)
        {
            if (terrarium.SpeciesProfile is null)
            {
                throw new InvalidOperationException(
                    $"Terrarium {terrarium.Id} has no species profile for summary computation.");
            }

            var zone = TimeZoneInfo.FindSystemTimeZoneById(terrarium.TimeZoneId);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var dayRanges = new[]
            {
                GetDayRange(localToday.AddDays(-1), zone, now),
                GetDayRange(localToday, zone, now),
            };
            var firstRead = dayRanges.Min(range => range.FromUtc).AddMinutes(-3);
            if (rollupFrom < firstRead)
            {
                firstRead = rollupFrom.AddMinutes(-3);
            }

            var readings = await ReadingsAsync(terrarium.Id, firstRead, now, cancellationToken)
                .ConfigureAwait(false);
            var snapshots = await db.ThresholdSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.TerrariumId == terrarium.Id && snapshot.CapturedAtUtc <= now)
                .OrderBy(snapshot => snapshot.CapturedAtUtc)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            updatedRollups += await RecomputeRollupsAsync(
                    terrarium,
                    readings,
                    snapshots,
                    rollupFrom,
                    completedMinute,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var range in dayRanges.Where(range => range.ToUtc > range.FromUtc))
            {
                updatedSummaries += await RecomputeDailySummaryAsync(
                        terrarium,
                        readings,
                        range,
                        zone,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (updatedRollups > 0 || updatedSummaries > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new SummaryRecomputeOutcome(terrariums.Count, updatedRollups, updatedSummaries);
    }

    private async Task<int> RecomputeRollupsAsync(
        Terrarium terrarium,
        IReadOnlyList<ReadingPoint> readings,
        IReadOnlyList<ThresholdSnapshot> snapshots,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (toUtc <= fromUtc)
        {
            return 0;
        }

        var validReadings = readings.Where(reading => QualityRules.IsEvaluable(reading.QualityFlags)).ToList();
        var metricGroups = validReadings.GroupBy(reading => reading.Metric).ToList();
        var existing = await db.TelemetryHourlyRollups
            .Where(rollup => rollup.TerrariumId == terrarium.Id &&
                             rollup.HourStartUtc >= fromUtc &&
                             rollup.HourStartUtc < toUtc)
            .ToDictionaryAsync(
                rollup => (rollup.Metric, rollup.HourStartUtc),
                cancellationToken)
            .ConfigureAwait(false);

        var computedAt = clock.UtcNow;
        var updated = 0;

        foreach (var group in metricGroups)
        {
            var thresholdAt = ThresholdAt(terrarium, group.Key, TimeZoneInfo.FindSystemTimeZoneById(terrarium.TimeZoneId));

            foreach (var hourGroup in group.GroupBy(reading => Floor(reading.AtUtc, TimeSpan.FromHours(1))))
            {
                var hourStart = hourGroup.Key;
                if (hourStart < fromUtc || hourStart >= toUtc)
                {
                    continue;
                }

                var hourEnd = Min(hourStart.AddHours(1), toUtc);
                var points = group
                    .Where(point => point.AtUtc >= hourStart.AddMinutes(-3) &&
                                    point.AtUtc <= hourEnd.AddMinutes(3))
                    .Select(point => (point.AtUtc, point.Value))
                    .ToList();
                var result = MetricGridSummarizer.Summarize(
                    group.Key,
                    points,
                    hourStart,
                    hourEnd,
                    thresholdAt,
                    terrarium.SpeciesProfile!.LightThresholdLux);
                var snapshotId = snapshots
                    .Where(snapshot => snapshot.CapturedAtUtc <= hourStart)
                    .Select(snapshot => (Guid?)snapshot.Id)
                    .LastOrDefault();
                var key = (group.Key, hourStart);

                if (!existing.TryGetValue(key, out var rollup))
                {
                    rollup = new TelemetryHourlyRollup
                    {
                        TerrariumId = terrarium.Id,
                        Metric = group.Key,
                        HourStartUtc = hourStart,
                    };
                    db.TelemetryHourlyRollups.Add(rollup);
                    existing.Add(key, rollup);
                }

                rollup.MinValue = result.MinValue;
                rollup.MaxValue = result.MaxValue;
                rollup.AvgValue = result.TimeWeightedAverage;
                rollup.SampleCount = result.SampleCount;
                rollup.OutOfRangeMinutes = result.OutOfRangeMinutes;
                rollup.ExposureValue = result.ExposureValue;
                rollup.ThresholdSnapshotId = snapshotId;
                rollup.ComputedAt = computedAt;
                updated++;
            }
        }

        return updated;
    }

    private async Task<int> RecomputeDailySummaryAsync(
        Terrarium terrarium,
        IReadOnlyList<ReadingPoint> readings,
        DayRange range,
        TimeZoneInfo zone,
        CancellationToken cancellationToken)
    {
        var sampleCount = await db.TelemetrySamples
            .AsNoTracking()
            .CountAsync(
                sample => sample.TerrariumId == terrarium.Id &&
                          sample.RecordedAt >= range.FromUtc &&
                          sample.RecordedAt < range.ToUtc,
                cancellationToken)
            .ConfigureAwait(false);
        if (terrarium.Device is { SamplingIntervalSec: <= 0 })
        {
            throw new InvalidOperationException(
                $"Terrarium {terrarium.Id} has a non-positive device sampling interval.");
        }

        var expectedSamples = terrarium.Device is null
            ? 0
            : (int)Math.Ceiling((range.ToUtc - range.FromUtc).TotalSeconds / terrarium.Device.SamplingIntervalSec);
        var coverage = CoverageMath.CoveragePct(sampleCount, expectedSamples);

        var metrics = new Dictionary<MetricCode, MetricGridSummary>();
        foreach (var definition in MetricDictionary.All)
        {
            var points = readings
                .Where(reading => reading.Metric == definition.Code &&
                                  QualityRules.IsEvaluable(reading.QualityFlags))
                .Select(reading => (reading.AtUtc, reading.Value));
            metrics[definition.Code] = MetricGridSummarizer.Summarize(
                definition.Code,
                points,
                range.FromUtc,
                range.ToUtc,
                ThresholdAt(terrarium, definition.Code, zone),
                terrarium.SpeciesProfile!.LightThresholdLux);
        }

        var alertCounts = await db.Alerts
            .AsNoTracking()
            .Where(alert => alert.TerrariumId == terrarium.Id &&
                            alert.TriggeredAt >= range.FromUtc &&
                            alert.TriggeredAt < range.ToUtc)
            .GroupBy(alert => alert.Severity)
            .Select(group => new { Severity = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var temp = metrics[MetricCode.TempC];
        var humidity = metrics[MetricCode.HumidityPct];
        var light = metrics[MetricCode.LightLux];
        var outOfRangeMinutes = MetricDictionary.All
            .OrderBy(definition => definition.ApiKey, StringComparer.Ordinal)
            .ToDictionary(
                definition => definition.ApiKey,
                definition => metrics[definition.Code].OutOfRangeMinutes,
                StringComparer.Ordinal);

        var summary = await db.DailyEnvironmentalSummaries
            .SingleOrDefaultAsync(
                row => row.TerrariumId == terrarium.Id && row.LocalDate == range.LocalDate,
                cancellationToken)
            .ConfigureAwait(false);
        if (summary is null)
        {
            summary = new DailyEnvironmentalSummary
            {
                TerrariumId = terrarium.Id,
                LocalDate = range.LocalDate,
            };
            db.DailyEnvironmentalSummaries.Add(summary);
        }

        summary.TimeZoneId = terrarium.TimeZoneId;
        summary.CoveragePct = coverage;
        summary.TempMin = temp.MinValue;
        summary.TempMax = temp.MaxValue;
        summary.TempAvg = temp.TimeWeightedAverage;
        summary.HumidityMin = humidity.MinValue;
        summary.HumidityMax = humidity.MaxValue;
        summary.HumidityAvg = humidity.TimeWeightedAverage;
        summary.LuxAvg = metrics[MetricCode.LightLux].TimeWeightedAverage;
        summary.LightHours = light.LightHours;
        summary.UvMax = metrics[MetricCode.UvIndex].MaxValue;
        summary.OutOfRangeMinutesJson = JsonSerializer.Serialize(outOfRangeMinutes, JsonOptions);
        summary.TempExposureDegCHours = temp.ExposureValue;
        summary.HumidityDryExposurePctHours = humidity.HumidityDryExposure;
        summary.HumidityWetExposurePctHours = humidity.HumidityWetExposure;
        summary.LightDeficitHours = ExposureCalculator.LightDeficitHours(
            terrarium.SpeciesProfile!.MinLightHoursPerDay,
            light.LightHours);
        summary.AlertCount = alertCounts.Sum(group => group.Count);
        summary.CriticalAlertCount = alertCounts
            .Where(group => group.Severity == AlertSeverity.Critical)
            .Sum(group => group.Count);
        summary.ComputedAt = clock.UtcNow;
        summary.IsLowConfidence = CoverageMath.IsLowConfidence(coverage);

        return 1;
    }

    private async Task<List<ReadingPoint>> ReadingsAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        await db.MetricReadings
            .AsNoTracking()
            .Where(reading => reading.Sample != null &&
                              reading.Sample.TerrariumId == terrariumId &&
                              reading.Sample.RecordedAt >= fromUtc &&
                              reading.Sample.RecordedAt <= toUtc)
            .Select(reading => new ReadingPoint(
                reading.Metric,
                reading.Value,
                reading.Sample!.RecordedAt,
                reading.Sample.QualityFlags))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static Func<DateTimeOffset, ThresholdBand?> ThresholdAt(
        Terrarium terrarium,
        MetricCode metric,
        TimeZoneInfo zone) =>
        atUtc =>
        {
            var local = TimeZoneInfo.ConvertTime(atUtc, zone);
            return ThresholdResolver.Resolve(
                    metric,
                    terrarium.ThresholdOverrides,
                    terrarium.SpeciesProfile!.Thresholds,
                    TimeOnly.FromDateTime(local.DateTime),
                    terrarium.SpeciesProfile.LightsOnLocalTime,
                    terrarium.SpeciesProfile.PhotoperiodHours)
                ?.Band;
        };

    private static DayRange GetDayRange(DateOnly localDate, TimeZoneInfo zone, DateTimeOffset now)
    {
        var fromUtc = LocalBoundaryToUtc(localDate, zone, chooseEarlierAmbiguousInstant: true);
        var dayEndUtc = LocalBoundaryToUtc(localDate.AddDays(1), zone, chooseEarlierAmbiguousInstant: false);
        return new DayRange(localDate, fromUtc, Min(dayEndUtc, Floor(now, TimeSpan.FromMinutes(1))));
    }

    private static DateTimeOffset LocalBoundaryToUtc(
        DateOnly date,
        TimeZoneInfo zone,
        bool chooseEarlierAmbiguousInstant)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? (chooseEarlierAmbiguousInstant
                ? zone.GetAmbiguousTimeOffsets(local).Max()
                : zone.GetAmbiguousTimeOffsets(local).Min())
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static DateTimeOffset Floor(DateTimeOffset value, TimeSpan interval) =>
        new(value.UtcTicks - (value.UtcTicks % interval.Ticks), TimeSpan.Zero);

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private sealed record DayRange(DateOnly LocalDate, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

    private sealed record ReadingPoint(
        MetricCode Metric,
        decimal Value,
        DateTimeOffset AtUtc,
        QualityFlags QualityFlags);
}

public sealed record SummaryRecomputeOutcome(int Terrariums, int HourlyRollups, int DailySummaries);
