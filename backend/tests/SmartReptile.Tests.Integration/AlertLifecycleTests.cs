using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Infrastructure.Alerts;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The alert lifecycle and the silence windows against a real SQL Server (FR-12, FR-13; roadmap 3.4's <c>TC-I-07</c>
/// as far as this project can reach it — the integration project has no HTTP host, so the service and its EF adapter
/// are driven directly and the routes themselves are verified live).
/// </summary>
/// <remarks>
/// What is worth a database here is what a fake cannot answer: the joins that scope a read to the caller's
/// terrariums, the soft-delete filter reaching an alert through its terrarium, the cursor predicate the provider has
/// to translate, and one transaction that has to leave an alert, a re-armed dwell key and an audit row all or none.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AlertLifecycleTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_alert_of_another_account_is_invisible_to_the_list_and_the_lookup()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await OwnedAsync(db, "mine");
        var theirs = await OwnedAsync(db, "theirs");
        var store = new EfAlertStore(db);

        var visible = await store.ListAsync(mine.UserId, new AlertQuery(), 10, CancellationToken.None);

        visible.Should().ContainSingle().Which.Alert.Id.Should().Be(mine.AlertId);
        (await store.FindOwnedAsync(theirs.AlertId, mine.UserId, CancellationToken.None)).Should().BeNull();
        (await store.FindOwnedAsync(theirs.AlertId, theirs.UserId, CancellationToken.None)).Should().NotBeNull();

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task An_alert_of_a_soft_deleted_terrarium_is_invisible()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await OwnedAsync(db, "deleted");
        var store = new EfAlertStore(db);

        var terrarium = await db.Terrariums.SingleAsync(row => row.Id == mine.TerrariumId);
        terrarium.DeletedAt = Now;

        await db.SaveChangesAsync();

        (await store.ListAsync(mine.UserId, new AlertQuery(), 10, CancellationToken.None)).Should().BeEmpty(
            "the soft-delete filter on Terrarium has to reach an alert through its foreign key, not only its own table");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_cursor_pages_newest_first_without_repeating_or_skipping()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await OwnedAsync(db, "paging");
        var store = new EfAlertStore(db);

        for (var index = 1; index <= 3; index++)
        {
            await AddAlertAsync(db, mine, TriggeredAt: Now.AddMinutes(index), dedupe: $"paging-{index}");
        }

        var first = await store.ListAsync(mine.UserId, new AlertQuery(PageSize: 2), 3, CancellationToken.None);
        first.Should().HaveCount(3, "the store answers with one row more than the page");

        var pageOne = first.Take(2).ToArray();
        var cursor = AlertCursor.Encode(pageOne[^1].Alert.TriggeredAt, pageOne[^1].Alert.Id);

        var second = await store
            .ListAsync(mine.UserId, new AlertQuery(PageSize: 2, Cursor: cursor), 3, CancellationToken.None);

        second.Should().HaveCount(1);
        second[0].Alert.TriggeredAt.Should().Be(Now.AddMinutes(1), "newest first, and the newest two are behind us");
        pageOne.Select(row => row.Alert.Id).Should().NotContain(second[0].Alert.Id);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Resolving_commits_the_row_its_re_arm_and_its_audit_entry_together()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await OwnedAsync(db, "resolve");
        var alert = await db.Alerts.SingleAsync(row => row.Id == mine.AlertId);

        db.EvaluationStates.Add(new EvaluationState
        {
            TerrariumId = mine.TerrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Now.AddMinutes(-12),
            OpenAlertId = alert.Id,
            LastEvaluatedSampleId = 4711,
        });

        await db.SaveChangesAsync();

        var broadcaster = new RecordingBroadcaster();
        var service = new AlertService(new EfAlertStore(db), broadcaster, new SystemClock());

        var outcome = await service.ResolveAsync(
            alert.Id,
            mine.UserId,
            new ResolveAlertRequest("Accepted", "lamp on purpose"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();

        var stored = await db.Alerts.SingleAsync(row => row.Id == alert.Id);
        stored.State.Should().Be(AlertState.Resolved);
        stored.ResolvedReason.Should().Be(ResolvedReason.Accepted);
        stored.ResolvedByUserId.Should().Be(mine.UserId);

        var state = await db.EvaluationStates.SingleAsync(row =>
            row.TerrariumId == mine.TerrariumId && row.Metric == MetricCode.TempC && row.Phase == ThresholdPhase.Any);

        state.Violation.Should().Be(ViolationKind.None);
        state.FirstOutOfBandAt.Should().BeNull();
        state.OpenAlertId.Should().BeNull();
        state.LastEvaluatedSampleId.Should().Be(4711, "the watermark is not a lifecycle field");

        var audit = await db.AuditLogs
            .Where(row => row.EntityName == "Alert" && row.EntityId == alert.Id.ToString())
            .SingleAsync();

        audit.Action.Should().Be(AuditAction.AlertResolved);
        audit.AfterJson.Should().Contain("lamp on purpose");
        broadcaster.Alerts.Should().ContainSingle().Which.Event.Should().Be(AlertEvent.Resolved);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_silence_window_round_trips_through_create_list_and_cancel()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await OwnedAsync(db, "silence");
        var store = new EfMetricSilenceStore(db);
        var service = new MetricSilenceService(store, new SystemClock());

        var created = await service.CreateAsync(
            mine.TerrariumId,
            mine.UserId,
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", DateTimeOffset.UtcNow.AddHours(6), "lamp replacement"),
            AuditActor.Unknown,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();

        var listed = await store.ListActiveAsync(mine.TerrariumId, DateTimeOffset.UtcNow, CancellationToken.None);

        listed.Should().ContainSingle().Which.Id.Should().Be(created.Silence!.Id);

        var cancelled = await service.CancelAsync(
            mine.TerrariumId,
            created.Silence.Id,
            mine.UserId,
            AuditActor.Unknown,
            CancellationToken.None);

        cancelled.Succeeded.Should().BeTrue();

        (await store.ListActiveAsync(mine.TerrariumId, DateTimeOffset.UtcNow, CancellationToken.None))
            .Should().BeEmpty("a cancelled window suppresses nothing");

        var audit = await db.AuditLogs
            .Where(row => row.EntityName == "MetricSilence")
            .ToListAsync();

        audit.Select(row => row.Action)
            .Should().Equal(AuditAction.MetricSilenced, AuditAction.SilenceCancelled);

        await tx.RollbackAsync();
    }

    /// <summary>Seeds an account, a terrarium, a device and one open alert, and returns their ids.</summary>
    private static async Task<(Guid UserId, Guid TerrariumId, Guid DeviceId, long AlertId)> OwnedAsync(
        SmartReptileDbContext db,
        string tag)
    {
        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        var alertId = await AddAlertAsync(
            db,
            (userId, terrariumId, deviceId, 0),
            TriggeredAt: Now,
            dedupe: $"{tag}-{Guid.NewGuid():N}");

        return (userId, terrariumId, deviceId, alertId);
    }

    /// <summary>Inserts one open alert through the model, so the insert is the one the API's own writes use.</summary>
    private static async Task<long> AddAlertAsync(
        SmartReptileDbContext db,
        (Guid UserId, Guid TerrariumId, Guid DeviceId, long AlertId) owner,
        DateTimeOffset TriggeredAt,
        string dedupe)
    {
        var alert = new Alert
        {
            TerrariumId = owner.TerrariumId,
            DeviceId = owner.DeviceId,
            Metric = MetricCode.TempC,
            Severity = AlertSeverity.Warning,
            Phase = ThresholdPhase.Any,
            DedupeKey = dedupe,
            State = AlertState.Open,
            Source = AlertSource.Threshold,
            TriggeredAt = TriggeredAt,
            BandMin = 24m,
            BandMax = 32m,
        };

        db.Alerts.Add(alert);

        await db.SaveChangesAsync();

        return alert.Id;
    }

    /// <summary>Records the pushes, so "committed before announced" can be asserted without a hub.</summary>
    private sealed class RecordingBroadcaster : ITelemetryBroadcaster
    {
        public List<AlertChangedPayload> Alerts { get; } = [];

        public Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BroadcastStatusAsync(DeviceStatusChanged statusChanged, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BroadcastAlertsAsync(
            IReadOnlyList<AlertChangedPayload> changes,
            CancellationToken cancellationToken)
        {
            Alerts.AddRange(changes);

            return Task.CompletedTask;
        }
    }
}
