using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Evaluation;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Evaluation;
using SmartReptile.Infrastructure.Summaries;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// <c>TC-I-06</c>: the `daily` fixture played back through the real evaluator and the real summariser, with one fake
/// clock, so the alert and the numbers it produced are asserted together.
/// </summary>
/// <remarks>
/// The fixture is the reference dataset the exposure cases already use — target 26–32 °C, 40 minutes at 34.0 °C then
/// 20 minutes at 33.0 °C — placed inside one local day of the seeded leopard-gecko profile in
/// `Asia/Ho_Chi_Minh`. Everything the test asserts is hand-computed in the comments rather than read back from the
/// code under test, because a value the implementation produces is not evidence that it produced the right value.
/// <para>
/// This is the end-to-end half `SummaryRecomputationTests` deliberately does not cover: that case proves a back-fill
/// updates existing rows, this one proves the evaluator and the summariser agree about one day of evidence.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class EvaluationThroughPipelineTests(DatabaseFixture fixture)
{
    /// <summary>2026-10-19 00:00 local (UTC+7) expressed as a UTC instant.</summary>
    private static readonly DateTimeOffset LocalMidnight = new(2026, 10, 18, 17, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("TestCase", "TC-I-06")]
    public async Task The_daily_fixture_produces_one_alert_and_summaries_that_match_the_hand_computed_values()
    {
        await using var db = fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        // The seeded leopard-gecko profile is the band the fixture is written against: 26–32 °C target,
        // 22–34.5 °C critical, dwell 5/2 minutes, and a photoperiod of 12 h from 07:00 local.
        var profileId = await db.SpeciesProfiles
            .Where(profile => profile.Name.StartsWith("Leopard gecko"))
            .Select(profile => profile.Id)
            .SingleAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Terrarium] SET [SpeciesProfileId] = {profileId} WHERE [Id] = {terrariumId}");

        // The reference dataset as it is written: 40 minutes at 34.0 °C, then 20 at 33.0 °C, and in range
        // otherwise, with no gaps. It is laid out over the local day up to 10:59 so the day has data on every
        // minute it covers — a boundary-only fixture would be averaged together with the grid's three-minute hold-over
        // and would stop being hand-computable.
        var localMidnightOfDay = LocalMidnight;
        var excursionStart = LocalMidnight.AddHours(10);
        var samples = new List<TelemetrySample>();

        for (var minute = 0; minute < 660; minute++)
        {
            var at = localMidnightOfDay.AddMinutes(minute);
            var value = minute switch
            {
                // 10:00–10:39 local: 34.0 °C, above the 32 °C target and below the 34.5 °C critical bound.
                >= 600 and < 640 => 34.0m,

                // 10:40–10:59 local: 33.0 °C, still above target, so the episode never closes.
                >= 640 => 33.0m,

                // 00:00–06:59 local is the night phase, whose band is 22–27 °C: a daytime value there would be a
                // second excursion, and the fixture is one episode, not two.
                < 420 => 25.0m,

                // 07:00–09:59 local is daytime inside the 26–32 °C band.
                _ => 30.0m,
            };

            samples.Add(Sample(terrariumId, deviceId, minute + 1, at, value));
        }

        db.TelemetrySamples.AddRange(samples);
        await db.SaveChangesAsync();

        // Judged at 11:00 local: the whole fixture is in the past, so the dwell window the alert needs has elapsed.
        var clock = new FixedClock(LocalMidnight.AddHours(11));
        var evaluator = new SampleEvaluator(new EfEvaluationStore(db), clock);

        var outcome = await evaluator.EvaluateAsync(
            [.. samples.Select(sample => Persisted(sample, terrariumId, deviceId))],
            CancellationToken.None);

        // ---- the alert ------------------------------------------------------------------------------------
        outcome.AlertsOpened.Should().Be(1);

        var alerts = await db.Alerts.AsNoTracking().Where(alert => alert.TerrariumId == terrariumId).ToListAsync();
        var alert = alerts.Should().ContainSingle("one sustained excursion is one episode").Subject;

        alert.Source.Should().Be(AlertSource.Threshold);
        alert.Metric.Should().Be(MetricCode.TempC);
        alert.Severity.Should().Be(AlertSeverity.Warning, "34.0 °C never reaches the 34.5 °C critical bound");
        alert.TriggeredAt.Should().Be(
            excursionStart,
            "the trigger is back-dated to the first out-of-band reading, not to the moment the dwell expired");
        alert.TriggeringValue.Should().Be(34.0m);
        alert.PeakValue.Should().Be(34.0m, "the hottest reading of the episode");

        // ---- the hourly rollup -----------------------------------------------------------------------------
        var recomputer = new SummaryRecomputer(db, clock);
        await recomputer.RecomputeTerrariumAsync(terrariumId, CancellationToken.None);

        // The samples run 03:00–03:59 UTC (10:00–10:59 local), so this is the hour bucket they belong to.
        var hourStart = excursionStart;
        var rollup = await db.TelemetryHourlyRollups
            .AsNoTracking()
            .SingleAsync(row => row.TerrariumId == terrariumId
                             && row.Metric == MetricCode.TempC
                             && row.HourStartUtc == hourStart);
        rollup.SampleCount.Should().Be(60);
        rollup.MinValue.Should().Be(33.0m);
        rollup.MaxValue.Should().Be(34.0m);
        rollup.OutOfRangeMinutes.Should().Be(60, "every minute of the hour was above the target maximum");

        // (40 × 34 + 20 × 33) ÷ 60 = 33.666…, stored at the column's scale (`decimal(9,3)`).
        rollup.AvgValue.Should().Be(33.667m);

        // (40 × 2 + 20 × 1) ÷ 60 = 1.666… °C·h, stored at `decimal(18,6)`.
        rollup.ExposureValue.Should().Be(1.666667m);

        // ---- the daily summary -----------------------------------------------------------------------------
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(excursionStart, TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")).DateTime);
        var summary = await db.DailyEnvironmentalSummaries
            .AsNoTracking()
            .SingleAsync(row => row.TerrariumId == terrariumId && row.LocalDate == localDate);

        summary.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        summary.TempMin.Should().Be(25.0m, "the night phase is the coolest part of the day");
        summary.TempMax.Should().Be(34.0m);

        // (420 × 25 + 180 × 30 + 40 × 34 + 20 × 33) ÷ 660 = 17920 ÷ 660 = 27.1515…, stored at `decimal(9,3)`.
        summary.TempAvg.Should().Be(27.152m, "the average is the same minutes, not a second calculation");

        // The exposure is the excursion only: (40 × 2 + 20 × 1) ÷ 60 = 1.666… °C·h, stored at `decimal(12,4)`.
        summary.TempExposureDegCHours.Should().Be(1.6667m);
        summary.AlertCount.Should().Be(1, "the summary counts the episode the evaluator opened");
        summary.CriticalAlertCount.Should().Be(0);
        summary.LightHours.Should().Be(0m, "the fixture carried no light readings");
        summary.LightDeficitHours.Should().Be(8m, "the profile requires eight hours and none were measured");

        // 660 measured minutes against 660 expected on an eleven-hour local day: full coverage, so the row is not
        // flagged low-confidence — the fixture is the one day in this suite that covers a whole day.
        summary.CoveragePct.Should().Be(100m);
        summary.IsLowConfidence.Should().BeFalse();

        await transaction.RollbackAsync();
    }

    /// <summary>One stored sample, built the way the ingest pipeline builds it.</summary>
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
            ReceivedAt = recordedAt,
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

    /// <summary>The identity the insert assigned, in the shape the evaluator consumes.</summary>
    private static PersistedSample Persisted(TelemetrySample sample, Guid terrariumId, Guid deviceId) =>
        new(
            sample.Id,
            terrariumId,
            deviceId,
            sample.RecordedAt,
            sample.Sequence,
            sample.QualityFlags,
            [.. sample.Readings.Select(reading => new TelemetryReading(reading.Metric, reading.Value, null))]);

    /// <summary>A clock pinned to one instant, so the dwell window and the local day are both deterministic.</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;

        public DateTimeOffset InZone(DateTimeOffset instantUtc, string timeZoneId) =>
            TimeZoneInfo.ConvertTime(instantUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

        public DateTimeOffset NowIn(string timeZoneId) => InZone(UtcNow, timeZoneId);
    }
}
