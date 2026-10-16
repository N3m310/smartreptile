using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Infrastructure.Evaluation;
using SmartReptile.Infrastructure.Mqtt;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Security;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The threshold write path against a real SQL Server (FR-10; roadmap 3.1's write half, <c>TC-U-26</c>).
/// </summary>
/// <remarks>
/// What is worth a database here is what a fake cannot answer. The override change and its snapshot have to commit
/// as one unit of work against the real schema — the snapshot's <c>char(64)</c> hash and <c>nvarchar(max)</c> JSON,
/// the override's one row per (terrarium, metric, phase) — and the evaluator's own band source has to see the new
/// band while an alert that was already open keeps the band it was judged against.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ThresholdWriteTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_change_snapshots_the_new_set_and_the_next_evaluation_uses_the_new_band()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var alertId = await AddOpenAlertAsync(db, terrariumId, deviceId, bandMin: 24m, bandMax: 32m);

        var service = NewService(db);

        var outcome = await service.UpsertThresholdsAsync(
            terrariumId,
            new UpdateThresholdsRequest([Input("tempC", 28m, 30m, note: "summer")]),
            userId,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();

        // The snapshot committed with the change, and carries the set now in force.
        var snapshot = await db.ThresholdSnapshots.AsNoTracking()
            .SingleAsync(row => row.TerrariumId == terrariumId);

        snapshot.EffectiveThresholdsJson.Should().Contain("\"targetMin\":28").And.Contain("\"source\":\"override\"");
        snapshot.ThresholdVersionHash.Should().MatchRegex("^[0-9a-f]{64}$");

        var storedOverride = await db.ThresholdOverrides.AsNoTracking()
            .SingleAsync(row => row.TerrariumId == terrariumId);

        storedOverride.TargetMin.Should().Be(28m);
        storedOverride.TargetMax.Should().Be(30m);
        storedOverride.Note.Should().Be("summer");

        // TC-U-26: an alert already open keeps the band it was judged against. The card columns are denormalised on
        // the row precisely so a later threshold edit cannot rewrite history.
        var alert = await db.Alerts.AsNoTracking().SingleAsync(row => row.Id == alertId);

        alert.BandMin.Should().Be(24m);
        alert.BandMax.Should().Be(32m);

        // …and the band the evaluator resolves for the next sample is the new one, because it reads the configured
        // rows this write just persisted rather than a cached answer.
        var context = await new EfEvaluationStore(db).FindContextAsync(terrariumId, CancellationToken.None);

        var effective = ThresholdResolver.Resolve(
            MetricCode.TempC,
            context!.Overrides,
            context.ProfileBands,
            new TimeOnly(12, 0),
            context.LightsOnLocalTime,
            context.PhotoperiodHours);

        effective.Should().NotBeNull();
        effective!.Source.Should().Be(ThresholdSource.Override);
        effective.Band.TargetMin.Should().Be(28m);
        effective.Band.TargetMax.Should().Be(30m);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Reverting_a_metric_removes_the_override_row_and_snapshots_the_profile_band()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var service = NewService(db);

        await service.UpsertThresholdsAsync(
            terrariumId,
            new UpdateThresholdsRequest([Input("tempC", 28m, 30m)]),
            userId,
            CancellationToken.None);

        var reverted = await service.DeleteThresholdOverrideAsync(terrariumId, "tempC", userId, CancellationToken.None);

        reverted.Succeeded.Should().BeTrue();

        (await db.ThresholdOverrides.AsNoTracking().AnyAsync(row => row.TerrariumId == terrariumId))
            .Should().BeFalse("a revert deletes the override rather than disabling it");

        // One snapshot per change: the authored override, then the reverted set the profile supplies again.
        var snapshots = await db.ThresholdSnapshots.AsNoTracking()
            .Where(row => row.TerrariumId == terrariumId)
            .ToListAsync();

        snapshots.Should().HaveCount(2);
        snapshots.Should().Contain(row => row.EffectiveThresholdsJson.Contains("\"source\":\"override\""));
        snapshots.Should().Contain(row => !row.EffectiveThresholdsJson.Contains("\"source\":\"override\""));

        await tx.RollbackAsync();
    }

    private static TerrariumService NewService(SmartReptileDbContext db) =>
        new(
            new EfTerrariumStore(db),
            new EfTimeZoneStore(db),
            NewProvisioningService(db),
            new SystemClock(),
            new TerrariumSettings("Asia/Ho_Chi_Minh", 3));

    /// <summary>
    /// The claim use case the create-with-device path stages through (TASKS 2026-10-14, item 2). Built over the same
    /// context, so a create that binds a board commits both writes in the one unit of work the real services share.
    /// </summary>
    private static DeviceProvisioningService NewProvisioningService(SmartReptileDbContext db) =>
        new(
            new EfProvisioningStore(db),
            new ClaimCodeGenerator(Options.Create(new ProvisioningOptions())),
            new DeviceCredentials(),
            new InMemoryOnboardingThrottleStore(),
            new DeviceSessionRegistry(),
            new SystemClock(),
            new ProvisioningSettings(ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength, 15, OnboardingLimits.Documented),
            new DeviceSilenceSettings(3));

    private static ThresholdOverrideInput Input(
        string metric,
        decimal targetMin,
        decimal targetMax,
        string? note = null) =>
        new(metric, "any", targetMin, targetMax, null, null, null, null, null, note, null);

    /// <summary>One open alert carrying the denormalised band a threshold edit must not disturb.</summary>
    private static async Task<long> AddOpenAlertAsync(
        SmartReptileDbContext db,
        Guid terrariumId,
        Guid deviceId,
        decimal bandMin,
        decimal bandMax)
    {
        var alert = new Alert
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,
            Metric = MetricCode.TempC,
            Severity = AlertSeverity.Warning,
            Phase = ThresholdPhase.Any,
            DedupeKey = $"threshold-write-{Guid.NewGuid():N}",
            State = AlertState.Open,
            Source = AlertSource.Threshold,
            TriggeredAt = DateTimeOffset.UtcNow,
            BandMin = bandMin,
            BandMax = bandMax,
        };

        db.Alerts.Add(alert);

        await db.SaveChangesAsync();

        return alert.Id;
    }
}
