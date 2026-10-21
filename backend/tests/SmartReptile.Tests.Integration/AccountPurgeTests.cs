using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The hard delete of an account (TASKS 2026-10-19, BE-03) against a real SQL Server. What a fake cannot answer is
/// the part this test exists for: <c>Terrarium.UserId</c> is <c>RESTRICT</c>, so the ordered delete either works
/// against the real constraints or fails in a way no unit test would show.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AccountPurgeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Deleting_an_account_removes_its_enclosures_and_inbox_and_unbinds_its_board()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var ownerId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, ownerId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 1);

        db.NotificationLogs.Add(new NotificationLog
        {
            UserId = ownerId,
            TerrariumId = terrariumId,
            Channel = NotificationChannel.InApp,
            Status = NotificationStatus.Sent,
            Title = "it-notification",
            Body = "purge check",
            CreatedAt = DateTimeOffset.UtcNow,
            SentAt = DateTimeOffset.UtcNow,
        });

        // A second account's enclosure, to prove the purge is scoped to the account being deleted.
        var otherOwnerId = await Seed.UserAsync(db);
        var otherTerrariumId = await Seed.TerrariumAsync(db, otherOwnerId);

        await db.SaveChangesAsync();

        var user = await db.Users.SingleAsync(row => row.Id == ownerId);

        var purge = await new EfUserStore(db).PurgeAccountAsync(user, CancellationToken.None);
        await db.SaveChangesAsync();

        purge.Terrariums.Should().Be(1, "the account held one enclosure");
        purge.Devices.Should().Be(1, "and one board inside it");
        purge.Notifications.Should().Be(1);

        (await db.Users.AnyAsync(row => row.Id == ownerId)).Should().BeFalse("a delete is a hard delete");
        (await db.Terrariums.IgnoreQueryFilters().AnyAsync(row => row.Id == terrariumId)).Should().BeFalse();
        (await db.Alerts.IgnoreQueryFilters().AnyAsync(row => row.TerrariumId == terrariumId)).Should().BeFalse();
        (await db.NotificationLogs.AnyAsync(row => row.UserId == ownerId)).Should().BeFalse();

        // The board outlives the account: unbound, unassigned, and free for another account to claim.
        var device = await db.Devices.SingleAsync(row => row.Id == deviceId);
        device.TerrariumId.Should().BeNull();
        device.Status.Should().Be(DeviceStatus.Provisioning);

        // Nobody else's enclosure was touched.
        (await db.Terrariums.AnyAsync(row => row.Id == otherTerrariumId)).Should().BeTrue();

        await tx.RollbackAsync();
    }
}
