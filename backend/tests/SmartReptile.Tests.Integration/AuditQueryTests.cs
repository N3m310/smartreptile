using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Auditing;
using SmartReptile.Domain.Auditing;
using SmartReptile.Infrastructure.Auditing;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The audit query against a real SQL Server (FR-18, NFR-12; roadmap 3.7's <c>GET /audit</c>).
/// </summary>
/// <remarks>
/// What is worth a database here is the scoping, because it is a predicate the provider has to translate rather than
/// a filter over loaded rows: a row is the caller's if their account caused it <i>or</i> if it concerns a device in
/// one of their terrariums — which is two joins, one of them through a soft-delete filter the trail has to ignore.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class AuditQueryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_row_the_caller_caused_is_visible_and_another_accounts_is_not()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await Seed.UserAsync(db);
        var theirs = await Seed.UserAsync(db);

        // A silence row carries no device, which is exactly the shape only the actor can make visible.
        await AddAsync(db, mine, null, AuditEntity.MetricSilence, "silence-mine", AuditAction.MetricSilenced);
        await AddAsync(db, theirs, null, AuditEntity.MetricSilence, "silence-theirs", AuditAction.MetricSilenced);

        var page = await new EfAuditStore(db).ListAsync(mine, new AuditQuery(), 50, CancellationToken.None);

        page.Should().ContainSingle();
        page[0].Entry.EntityId.Should().Be("silence-mine");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_row_about_the_callers_device_is_visible_whoever_caused_it()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var technician = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        // The technician acted, so the row carries their account rather than the owner's.
        await AddAsync(db, technician, deviceId, AuditEntity.Alert, "812", AuditAction.AlertAcknowledged);

        var page = await new EfAuditStore(db).ListAsync(owner, new AuditQuery(), 50, CancellationToken.None);

        page.Should().ContainSingle("a technician acknowledging an alert on the owner's enclosure is the owner's business");
        page[0].Entry.Action.Should().Be(AuditAction.AlertAcknowledged);
        page[0].DevicePublicId.Should().Be(await PublicIdAsync(db, deviceId));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_row_about_a_device_in_a_soft_deleted_terrarium_is_still_visible()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var owner = await Seed.UserAsync(db);
        var technician = await Seed.UserAsync(db);
        var terrariumId = await Seed.TerrariumAsync(db, owner);
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await AddAsync(db, technician, deviceId, AuditEntity.Alert, "812", AuditAction.AlertAcknowledged);

        await Seed.SoftDeleteAsync(db, terrariumId);

        var page = await new EfAuditStore(db).ListAsync(owner, new AuditQuery(), 50, CancellationToken.None);

        page.Should().ContainSingle("an audit trail is the one place a removed enclosure must not erase its own history");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task An_unowned_row_with_no_actor_stays_invisible()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var caller = await Seed.UserAsync(db);

        // Nobody's: no acting account, and the device is bound to nothing.
        var unattached = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Device] (Id, PublicId, DeviceName, ChipId, MacAddress, FirmwareVersion, Status, Protocol,
                                  SamplingIntervalSec, PublishIntervalSec, CreatedAt)
            VALUES ({unattached}, {"sr-orphan"}, {"orphan node"}, {"chip-orphan"}, 'AA:BB:CC:00:00:09', '0.1.0', 0, 0, 60, 60, SYSDATETIMEOFFSET())
            """);

        await AddAsync(db, null, unattached, AuditEntity.Device, "sr-orphan", AuditAction.DeviceRevoked);

        var page = await new EfAuditStore(db).ListAsync(caller, new AuditQuery(), 50, CancellationToken.None);

        page.Should().BeEmpty("the trail is scoped to the caller, not to every row in the table");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_entity_filter_reaches_the_query_and_the_cursor_pages_the_trail()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var mine = await Seed.UserAsync(db);
        var at = new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);
        var store = new EfAuditStore(db);

        await AddAsync(db, mine, null, AuditEntity.Device, "sr-a", AuditAction.DeviceClaimed, at);
        await AddAsync(db, mine, null, AuditEntity.Alert, "7", AuditAction.AlertAcknowledged, at.AddMinutes(1));
        await AddAsync(db, mine, null, AuditEntity.Alert, "8", AuditAction.AlertResolved, at.AddMinutes(2));

        var alerts = await store.ListAsync(mine, new AuditQuery(Entity: AuditEntity.Alert), 50, CancellationToken.None);
        alerts.Select(row => row.Entry.EntityId).Should().Equal("8", "7");

        var byEntityId = await store.ListAsync(mine, new AuditQuery(EntityId: "7"), 50, CancellationToken.None);
        byEntityId.Should().ContainSingle().Which.Entry.Action.Should().Be(AuditAction.AlertAcknowledged);

        // The store obeys `take`; the service is what asks for one row more than the page.
        var first = await store.ListAsync(mine, new AuditQuery(PageSize: 2), 2, CancellationToken.None);
        first.Select(row => row.Entry.EntityId).Should().Equal("8", "7");

        var cursor = AuditCursor.Encode(first[^1].Entry.OccurredAt, first[^1].Entry.Id);
        var second = await store.ListAsync(mine, new AuditQuery(Cursor: cursor), 50, CancellationToken.None);

        second.Should().ContainSingle();
        second[0].Entry.EntityId.Should().Be("sr-a");
        first.Select(row => row.Entry.Id).Should().NotContain(second[0].Entry.Id);

        await tx.RollbackAsync();
    }

    /// <summary>An audit row in the shape the writers produce, staged inside the caller's transaction.</summary>
    private static async Task AddAsync(
        SmartReptileDbContext db,
        Guid? actorUserId,
        Guid? deviceId,
        string entity,
        string entityId,
        string action,
        DateTimeOffset? occurredAt = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            DeviceId = deviceId,
            EntityName = entity,
            EntityId = entityId,
            Action = action,
            IpAddress = "127.0.0.1",
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            AfterJson = """{"state":"Resolved"}""",
        });

        await db.SaveChangesAsync();
    }

    private static Task<string> PublicIdAsync(SmartReptileDbContext db, Guid deviceId) =>
        db.Devices.Where(device => device.Id == deviceId).Select(device => device.PublicId).SingleAsync();
}
