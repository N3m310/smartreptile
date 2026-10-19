using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Summaries;
using SmartReptile.Infrastructure.Summaries;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

[Collection(DatabaseCollection.Name)]
public sealed class SummaryRecomputationTests(DatabaseFixture fixture)
{
    [Fact]
    [Trait("TestCase", "TC-I-06")]
    public async Task Recomputing_after_a_backfill_updates_existing_rollup_and_daily_summary()
    {
        await using var db = fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var now = DateTimeOffset.UtcNow;
        var hourStart = new DateTimeOffset(
            now.AddMinutes(-10).UtcTicks - (now.AddMinutes(-10).UtcTicks % TimeSpan.TicksPerHour),
            TimeSpan.Zero);
        var firstBackfillAt = hourStart.AddMinutes(1);

        db.TelemetrySamples.AddRange(
            Sample(terrariumId, deviceId, now.UtcTicks, firstBackfillAt.AddMinutes(1), 30m),
            Sample(terrariumId, deviceId, now.UtcTicks + 1, firstBackfillAt.AddMinutes(2), 32m));
        await db.SaveChangesAsync();

        var recomputer = new SummaryRecomputer(db, new SystemClock());
        await recomputer.RecomputeTerrariumAsync(terrariumId, CancellationToken.None);

        var initialRollup = await db.TelemetryHourlyRollups.SingleAsync(
            row => row.TerrariumId == terrariumId &&
                   row.Metric == MetricCode.TempC &&
                   row.HourStartUtc == hourStart);
        initialRollup.SampleCount.Should().Be(2);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstBackfillAt, timeZone).DateTime);
        var initialSummary = await db.DailyEnvironmentalSummaries.SingleAsync(
            row => row.TerrariumId == terrariumId && row.LocalDate == localDate);
        initialSummary.TempMax.Should().Be(32m);

        db.TelemetrySamples.Add(
            Sample(terrariumId, deviceId, now.UtcTicks + 2, firstBackfillAt, 40m));
        await db.SaveChangesAsync();

        await recomputer.RecomputeTerrariumAsync(terrariumId, CancellationToken.None);

        var recomputedRollup = await db.TelemetryHourlyRollups.SingleAsync(
            row => row.TerrariumId == terrariumId &&
                   row.Metric == MetricCode.TempC &&
                   row.HourStartUtc == hourStart);
        recomputedRollup.SampleCount.Should().Be(3);
        recomputedRollup.MaxValue.Should().Be(40m);

        var recomputedSummary = await db.DailyEnvironmentalSummaries.SingleAsync(
            row => row.TerrariumId == terrariumId && row.LocalDate == localDate);
        recomputedSummary.TempMax.Should().Be(40m);
        (await db.DailyEnvironmentalSummaries.CountAsync(row => row.TerrariumId == terrariumId))
            .Should().Be(2, "the worker maintains both the current and previous local day without duplicating either");

        await transaction.RollbackAsync();
    }

    private static TelemetrySample Sample(
        Guid terrariumId,
        Guid deviceId,
        long sequence,
        DateTimeOffset recordedAt,
        decimal temperature) =>
        new()
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,
            Sequence = sequence,
            RecordedAt = recordedAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            FirmwareVersion = "integration",
            Source = IngestSource.Seed,
            Readings =
            {
                new MetricReading
                {
                    Metric = MetricCode.TempC,
                    Value = temperature,
                },
            },
        };
}
