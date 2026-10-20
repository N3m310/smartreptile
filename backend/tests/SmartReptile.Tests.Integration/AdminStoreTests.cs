using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Admin;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Admin;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The administrator's get-all reads against a real SQL Server (TASKS 2026-10-14, item 13). What is worth a database
/// here is exactly what a fake cannot answer: the ownership joins, the left joins that keep an unclaimed board and a
/// metric-less alert in the list, and the family filter reaching the <c>Alert.Family</c> column.
/// </summary>
/// <remarks>
/// The database is shared by the whole collection, so nothing here asserts an absolute total: each case measures its
/// own tag's rows and compares against the same query with a filter that excludes them only when the filter is what
/// is under test.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AdminStoreTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task An_enclosure_is_listed_with_its_owner_its_board_and_its_open_alerts()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var owner = await db.Users.SingleAsync(user => user.Id == userId);
        var name = await db.Terrariums.Where(t => t.Id == terrariumId).Select(t => t.Name).SingleAsync();

        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 1);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 2);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 2, metric: 1);

        var page = await new EfAdminStore(db).ListTerrariumsAsync(name, 0, 10, CancellationToken.None);

        var row = page.Should().ContainSingle().Subject;
        row.Id.Should().Be(terrariumId);
        row.OwnerId.Should().Be(userId);
        row.OwnerUsername.Should().Be(owner.Username);
        row.DevicePublicId.Should().NotBeNull();
        row.OpenAlerts.Should().Be(2, "the resolved episode is history rather than something to act on");
        row.Status.Should().Be("Active");

        // The same filter the page used, so the pager and the page agree.
        (await new EfAdminStore(db).CountTerrariumsAsync(name, CancellationToken.None)).Should().Be(1);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_search_matches_the_owner_as_well_as_the_enclosure()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        await Seed.TerrariumAsync(db, userId);
        var username = await db.Users.Where(user => user.Id == userId).Select(user => user.Username).SingleAsync();

        var page = await new EfAdminStore(db).ListTerrariumsAsync(username, 0, 10, CancellationToken.None);

        page.Should().OnlyContain(row => row.OwnerUsername == username);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_board_no_account_has_claimed_is_still_listed()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var publicId = $"sr-{Guid.NewGuid():N}"[..9];

        // The row the API writes when a factory-provisioned board is registered but never claimed: no owner, no
        // enclosure. It is exactly the row an administrator goes looking for, so the join must not drop it.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Device] (Id, PublicId, DeviceName, ChipId, MacAddress, FirmwareVersion, Status, Protocol,
                                  SamplingIntervalSec, PublishIntervalSec, CreatedAt)
            VALUES (NEWID(), {publicId}, {"it-unclaimed"}, {"chip-" + publicId}, {"AA:BB:CC:DD:EE:FF"},
                    '0.1.0-test', 0, 0, 60, 60, SYSDATETIMEOFFSET())
            """);

        var page = await new EfAdminStore(db).ListDevicesAsync(publicId, 0, 10, CancellationToken.None);

        var row = page.Should().ContainSingle().Subject;
        row.PublicId.Should().Be(publicId);
        row.OwnerUsername.Should().BeNull();
        row.TerrariumId.Should().BeNull();
        row.TerrariumName.Should().BeNull();
        row.Status.Should().Be(DeviceStatus.Provisioning.ToString());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Alerts_are_filtered_by_family_and_carry_the_metric_key()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        // 1 TempC is the species family, 2 HumidityPct is the enclosure's, and the third row has no metric at all.
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 1);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 2);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0);

        var store = new EfAdminStore(db);
        var name = await db.Terrariums.Where(t => t.Id == terrariumId).Select(t => t.Name).SingleAsync();

        var species = await store.ListAlertsAsync(AlertFamily.Species, state: null, 0, 50, CancellationToken.None);
        var speciesHere = species.Where(row => row.TerrariumId == terrariumId).ToList();

        speciesHere.Should().ContainSingle();
        speciesHere[0].Family.Should().Be("species");
        speciesHere[0].Metric.Should().Be("tempC");
        speciesHere[0].OwnerUsername.Should().Be(
            await db.Users.Where(user => user.Id == userId).Select(user => user.Username).SingleAsync());
        speciesHere[0].TerrariumName.Should().Be(name);

        var enclosure = await store.ListAlertsAsync(AlertFamily.Enclosure, state: null, 0, 50, CancellationToken.None);
        var enclosureHere = enclosure.Where(row => row.TerrariumId == terrariumId).ToList();

        enclosureHere.Should().HaveCount(2, "humidity and the metric-less device row are both the enclosure's");

        var open = await store.ListAlertsAsync(AlertFamily.Enclosure, AlertState.Open, 0, 50, CancellationToken.None);
        open.Where(row => row.TerrariumId == terrariumId).Should().HaveCount(2);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_overview_counts_what_the_new_rows_added()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var store = new EfAdminStore(db);
        var before = await store.CountAsync(CancellationToken.None);

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 1);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 2);

        var after = await store.CountAsync(CancellationToken.None);

        (after.Accounts - before.Accounts).Should().Be(1);
        (after.AccountsActive - before.AccountsActive).Should().Be(1, "a seeded account is enabled");
        (after.Terrariums - before.Terrariums).Should().Be(1);
        (after.TerrariumsWithDevice - before.TerrariumsWithDevice).Should().Be(1);
        (after.Devices - before.Devices).Should().Be(1);
        (after.DevicesOnline - before.DevicesOnline).Should().Be(1);
        (after.AlertsOpen - before.AlertsOpen).Should().Be(2);
        (after.AlertsOpenSpecies - before.AlertsOpenSpecies).Should().Be(1);
        (after.AlertsOpenEnclosure - before.AlertsOpenEnclosure).Should().Be(1);
        after.SpeciesProfiles.Should().BeGreaterThan(0, "the reference-data seed created the built-in profiles");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_account_rollup_reports_each_accounts_holdings()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, userId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        await Seed.AlertAsync(db, terrariumId, deviceId, $"{Guid.NewGuid():N}", state: 0, metric: 1);

        var rollup = (await new EfAdminStore(db).ListAccountRollupsAsync(CancellationToken.None))
            .Should().ContainSingle(row => row.UserId == userId).Subject;

        rollup.Role.Should().Be(SystemRoles.Owner, "a freshly registered account is the keeper");
        rollup.IsActive.Should().BeTrue();
        rollup.Terrariums.Should().Be(1);
        rollup.Devices.Should().Be(1);
        rollup.AlertsOpen.Should().Be(1);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Deleting_an_enclosure_removes_it_unbinds_its_board_and_audits_both()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var ownerId = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, ownerId);
        var deviceId = await Seed.DeviceAsync(db, terrariumId, status: 1);
        var actingUserId = await Seed.UserAsync(db);
        var publicId = await db.Devices
            .Where(device => device.Id == deviceId)
            .Select(device => device.PublicId)
            .SingleAsync();
        var now = DateTimeOffset.UtcNow;

        var deleted = await new EfAdminStore(db)
            .DeleteTerrariumAsync(terrariumId, actingUserId, AuditActor.Unknown, now, CancellationToken.None);

        deleted.Should().NotBeNull();
        deleted!.OwnerUserId.Should().Be(ownerId, "the caller learns whose enclosure it was");
        deleted.UnboundDevicePublicIds.Should().Equal(publicId);

        // The board outlives the enclosure: still a row, no longer bound, free to be claimed again.
        var device = await db.Devices.SingleAsync(row => row.Id == deviceId);
        device.TerrariumId.Should().BeNull();
        device.Status.Should().Be(DeviceStatus.Provisioning, "a board with no enclosure is unassigned, not revoked");

        // A hard delete, not the keeper's soft one: nothing is left behind for a query filter to hide.
        (await db.Terrariums.IgnoreQueryFilters().AnyAsync(row => row.Id == terrariumId))
            .Should().BeFalse("the administrator's delete removes the row");

        var terrariumAudit = await db.AuditLogs
            .Where(row => row.Action == AuditAction.TerrariumDeleted && row.EntityId == terrariumId.ToString())
            .ToListAsync();

        var row = terrariumAudit.Should().ContainSingle().Subject;
        row.UserId.Should().Be(actingUserId, "the trail says who acted");
        row.BeforeJson.Should().Contain(ownerId.ToString(), "and the payload says whose data it was");

        var deviceAudit = await db.AuditLogs
            .Where(row => row.Action == AuditAction.DeviceUnbound && row.EntityId == publicId)
            .ToListAsync();

        deviceAudit.Should().ContainSingle().Which.UserId.Should().Be(actingUserId);

        // An id nobody holds is not a delete, which the route turns into 404.
        (await new EfAdminStore(db).DeleteTerrariumAsync(
            Guid.NewGuid(), actingUserId, AuditActor.Unknown, now, CancellationToken.None))
            .Should().BeNull();

        await tx.RollbackAsync();
    }
}
