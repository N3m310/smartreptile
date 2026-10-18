using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Notifications;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Notifications;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The notification store against a real SQL Server (FR-13, roadmap 3.5).
/// </summary>
/// <remarks>
/// What is worth a database here is everything the unit tests fake: the candidate query's joins (owner, technicians,
/// device state, silences), the hourly window the cap counts in, the digest's grouping, and the two
/// <c>ExecuteUpdate</c> writes that mark rows read and switch a dead push token off. Those are predicates the
/// provider has to translate rather than filters over loaded rows — the same reason the audit query has its own
/// integration test.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class NotificationStoreTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_sweep_sees_the_owner_a_silence_and_a_device_in_maintenance()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);

        // DeviceStatus 4 is Maintenance: it records everything and tells nobody.
        var deviceId = await Seed.DeviceAsync(db, terrariumId, status: 4);

        await Seed.AlertAsync(db, terrariumId, deviceId, $"it-{Guid.NewGuid():N}", state: 0, metric: (int)MetricCode.TempC);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [MetricSilence] (Id, TerrariumId, Metric, UntilUtc, Reason, CreatedByUserId, CreatedAt)
            VALUES ({Guid.NewGuid()}, {terrariumId}, {MetricCode.TempC}, {Now.AddHours(6)}, {"testing"},
                    {owner}, {Now})
            """);

        await db.SaveChangesAsync();

        var candidates = await new EfNotificationStore(db)
            .ListCandidatesAsync(Now.AddHours(-24), Now, CancellationToken.None);

        var candidate = candidates.Should().ContainSingle(row => row.TerrariumId == terrariumId).Subject;

        candidate.Maintenance.Should().BeTrue("the bound device is in Maintenance");
        candidate.Silenced.Should().BeTrue("a live silence covers tempC");
        candidate.Recipients.Should().Contain(recipient => recipient.UserId == owner && recipient.IsOwner);
        candidate.Attempts.Should().BeEmpty("nothing has been delivered for this alert yet");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task An_expired_or_cancelled_silence_does_not_suppress_anything()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await Seed.AlertAsync(db, terrariumId, deviceId, $"it-{Guid.NewGuid():N}", state: 0, metric: (int)MetricCode.TempC);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [MetricSilence] (Id, TerrariumId, Metric, UntilUtc, Reason, CreatedByUserId, CreatedAt,
                                         CancelledAt)
            VALUES ({Guid.NewGuid()}, {terrariumId}, {MetricCode.TempC}, {Now.AddHours(6)}, {"cancelled early"},
                    {owner}, {Now}, {Now})
            """);

        await db.SaveChangesAsync();

        var candidates = await new EfNotificationStore(db)
            .ListCandidatesAsync(Now.AddHours(-24), Now, CancellationToken.None);

        var candidate = candidates.Should().ContainSingle(row => row.TerrariumId == terrariumId).Subject;

        candidate.Silenced.Should().BeFalse("a cancelled window suppresses nothing, however far in the future it ends");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Rows_round_trip_and_the_inbox_only_shows_what_was_delivered()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await Seed.AlertAsync(db, terrariumId, deviceId, $"it-{Guid.NewGuid():N}", state: 0);
        await db.SaveChangesAsync();

        var alertId = await db.Alerts.Where(alert => alert.TerrariumId == terrariumId)
            .Select(alert => alert.Id).SingleAsync();

        var store = new EfNotificationStore(db);

        await store.AddAsync(
            [
                Row(alertId, terrariumId, owner, NotificationChannel.InApp, NotificationStatus.Sent, Now),
                Row(alertId, terrariumId, owner, NotificationChannel.Fcm, NotificationStatus.Sent, Now),
                Row(alertId, terrariumId, owner, NotificationChannel.Smtp, NotificationStatus.Suppressed, Now,
                    NotificationReason.QuietHours),
            ],
            CancellationToken.None);

        await store.SaveChangesAsync(CancellationToken.None);

        var inbox = await store.ListInboxAsync(owner, unreadOnly: false, 50, CancellationToken.None);

        inbox.Should().ContainSingle("the push and the withheld email are not inbox entries");
        inbox[0].Title.Should().NotBeNullOrWhiteSpace();

        (await store.CountUnreadAsync(owner, CancellationToken.None)).Should().Be(1);

        // The timeline reads the same rows back for the alert (§02-design/05 §6).
        var deliveries = await store.ListForAlertAsync(alertId, CancellationToken.None);

        deliveries.Should().HaveCount(3);
        deliveries.Should().Contain(row => row.SuppressedReason == NotificationReason.QuietHours);

        // Only the delivered push counts against §5's hourly cap.
        (await store.CountDeliveredAsync(terrariumId, Now.AddHours(-1), Now.AddHours(1), CancellationToken.None))
            .Should().Be(1);

        (await store.FindInboxRowAsync(owner, inbox[0].Id, CancellationToken.None)).Should().NotBeNull();
        (await store.FindInboxRowAsync(Guid.NewGuid(), inbox[0].Id, CancellationToken.None))
            .Should().BeNull("another account's id answers exactly like a nonexistent one");

        // Marking one read is idempotent, and marking everything read reports what changed.
        (await store.MarkReadAsync(owner, inbox[0].Id, Now, CancellationToken.None)).Should().Be(1);
        (await store.MarkReadAsync(owner, inbox[0].Id, Now, CancellationToken.None)).Should().Be(0);
        (await store.CountUnreadAsync(owner, CancellationToken.None)).Should().Be(0);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_refused_push_token_is_switched_off_and_cleared()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);

        await new EfNotificationStore(db).DisablePushAsync(owner, CancellationToken.None);

        var user = await db.Users.AsNoTracking().SingleAsync(row => row.Id == owner);

        user.ChannelFcmEnabled.Should().BeFalse();
        user.FcmToken.Should().BeNull("a token FCM says is gone would only fail again");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_digest_input_groups_the_hours_coalesced_alerts_by_terrarium()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await Seed.AlertAsync(db, terrariumId, deviceId, $"it-{Guid.NewGuid():N}", state: 0);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"it-{Guid.NewGuid():N}", state: 0);
        await db.SaveChangesAsync();

        var alertIds = await db.Alerts.Where(alert => alert.TerrariumId == terrariumId)
            .Select(alert => alert.Id).ToListAsync();

        var hourStart = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
        var store = new EfNotificationStore(db);

        await store.AddAsync(
            [
                .. alertIds.Select(id => Row(
                    id, terrariumId, owner, NotificationChannel.Fcm, NotificationStatus.Suppressed,
                    hourStart.AddMinutes(5), NotificationReason.DigestCoalesced)),
            ],
            CancellationToken.None);

        await store.SaveChangesAsync(CancellationToken.None);

        var inputs = await store.ListDigestInputsAsync(hourStart, hourStart.AddHours(1), CancellationToken.None);

        var input = inputs.Should().ContainSingle(row => row.TerrariumId == terrariumId).Subject;

        input.Sources.Should().HaveCount(2);
        input.Recipients.Should().ContainSingle(recipient => recipient.IsOwner);

        // Nothing has been digested for that hour yet, and after a digest row lands the answer flips — which is what
        // keeps the sweep from sending it twice.
        (await store.DigestExistsAsync(terrariumId, hourStart.AddHours(1), CancellationToken.None)).Should().BeFalse();

        await store.AddAsync(
            [Row(null, terrariumId, owner, NotificationChannel.InApp, NotificationStatus.Sent, hourStart.AddHours(1))],
            CancellationToken.None);

        await store.SaveChangesAsync(CancellationToken.None);

        (await store.DigestExistsAsync(terrariumId, hourStart.AddHours(1), CancellationToken.None)).Should().BeTrue();

        await tx.RollbackAsync();
    }

    private static NotificationLog Row(
        long? alertId,
        Guid terrariumId,
        Guid userId,
        NotificationChannel channel,
        NotificationStatus status,
        DateTimeOffset at,
        string? reason = null) =>
        new()
        {
            AlertId = alertId,
            TerrariumId = terrariumId,
            UserId = userId,
            Channel = channel,
            Title = "Terrarium · Temperature high",
            Body = "33.4 °C (band 28–32 °C), out of range 12 min",
            DeepLink = alertId is { } id ? $"smartreptile://alerts/{id}" : null,
            Status = status,
            SuppressedReason = reason,
            Attempts = status == NotificationStatus.Sent ? (byte)1 : (byte)0,
            CreatedAt = at,
            SentAt = status == NotificationStatus.Sent ? at : null,
        };
}
