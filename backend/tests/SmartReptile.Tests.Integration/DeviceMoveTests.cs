using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The bind/move path against a real SQL Server (<c>POST /devices/{id}/move</c>).
/// </summary>
/// <remarks>
/// A fake cannot answer the question this route turns on. DI-04 is a filtered unique index, so taking an enclosure
/// from the board that holds it and giving it to another means the first binding must be released before the second
/// is written — and EF Core promises no order between two tracked updates. The store therefore releases it with one
/// set-based statement ahead of the save, and this is the test that proves the two agree.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DeviceMoveTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_move_releases_the_board_that_held_the_enclosure_and_binds_the_newcomer()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var target = await Seed.TerrariumAsync(db, userId);
        var source = await Seed.TerrariumAsync(db, userId);

        var occupant = await AddDeviceAsync(db, userId, target, "sr-occupant");
        var mover = await AddDeviceAsync(db, userId, source, "sr-mover");

        var outcome = await NewService(db).MoveAsync("sr-mover", target, userId);

        outcome.Succeeded.Should().BeTrue();
        outcome.Move!.PreviousTerrariumId.Should().Be(source);
        outcome.Move.DisplacedDeviceId.Should().Be("sr-occupant");

        var stored = await db.Devices.AsNoTracking()
            .Where(device => device.Id == occupant || device.Id == mover)
            .ToDictionaryAsync(device => device.Id);

        stored[occupant].TerrariumId.Should().BeNull("one enclosure holds one board (DI-04)");
        stored[occupant].Status.Should().Be(DeviceStatus.Provisioning, "the displaced board is unbound, not revoked");
        stored[occupant].ProvisionedAt.Should().BeNull();
        stored[mover].TerrariumId.Should().Be(target);
    }

    [Fact]
    public async Task The_audit_rows_commit_with_the_binding_and_the_freed_board_can_be_bound_again()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var userId = await Seed.UserAsync(db);
        var target = await Seed.TerrariumAsync(db, userId);
        var elsewhere = await Seed.TerrariumAsync(db, userId);

        await AddDeviceAsync(db, userId, target, "sr-hold");
        await AddDeviceAsync(db, userId, elsewhere, "sr-take");

        var service = NewService(db);

        (await service.MoveAsync("sr-take", target, userId)).Succeeded.Should().BeTrue();

        var actions = await db.AuditLogs.AsNoTracking()
            .Where(row => row.EntityName == "Device" && (row.EntityId == "sr-hold" || row.EntityId == "sr-take"))
            .OrderBy(row => row.Id)
            .Select(row => row.Action)
            .ToListAsync();

        actions.Should().Equal(AuditAction.DeviceUnbound, AuditAction.DeviceMoved);

        // The slot it lost is not a dead end: the released board can be given the other enclosure in the same
        // transaction, which is what a keeper swapping two boards does.
        (await service.MoveAsync("sr-hold", elsewhere, userId)).Succeeded.Should().BeTrue();
    }

    private static DeviceProvisioningService NewService(SmartReptileDbContext db) => new(
        new EfProvisioningStore(db),
        new UnusedClaimCodes(),
        new UnusedCredentials(),
        new NoThrottle(),
        new NoSessions(),
        new SystemClock(),
        new ProvisioningSettings(ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength, 15, OnboardingLimits.Documented),
        new DeviceSilenceSettings(3));

    private static async Task<Guid> AddDeviceAsync(
        SmartReptileDbContext db,
        Guid userId,
        Guid terrariumId,
        string publicId)
    {
        var tag = Guid.NewGuid().ToString("N");

        var device = new Device
        {
            PublicId = publicId,
            DeviceName = publicId,
            ChipId = "chip-" + tag[..10],
            MacAddress = "AA:BB:CC:" + tag[..5].ToUpperInvariant(),
            Status = DeviceStatus.Online,
            TerrariumId = terrariumId,
            UserId = userId,
            ProvisionedAt = DateTimeOffset.UtcNow,
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return device.Id;
    }

    /// <summary>Unused by a move; the service needs the port but never reaches for a code.</summary>
    private sealed class UnusedClaimCodes : IClaimCodeGenerator
    {
        public string Generate() => throw new NotSupportedException("a move issues no claim code");
    }

    /// <summary>Unused by a move; no credential is minted and the board keeps the one it has.</summary>
    private sealed class UnusedCredentials : IDeviceCredentials
    {
        public string NewSecret() => throw new NotSupportedException("a move issues no secret");

        public string NewPublicId() => throw new NotSupportedException("a move allocates no public id");

        public DeviceSecretHash HashSecret(string secret) => throw new NotSupportedException();

        public bool VerifySecret(string secret, byte[] hash, byte[] salt) => throw new NotSupportedException();
    }

    /// <summary>Unused by a move: only self-registration is throttled.</summary>
    private sealed class NoThrottle : IOnboardingThrottleStore
    {
        public OnboardingAttemptWindow GetWindow(
            string ipAddress,
            DateTimeOffset sinceIpUtc,
            DateTimeOffset sinceGlobalUtc) => new(0, 0);

        public void RecordAttempt(string ipAddress, DateTimeOffset nowUtc) => throw new NotSupportedException();
    }

    /// <summary>Unused by a move: nothing is disconnected.</summary>
    private sealed class NoSessions : IDeviceSessionRegistry
    {
        public Task<int> KickAsync(
            string devicePublicId,
            string reason,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public bool IsConnected(string devicePublicId) => false;

        public int ConnectedDevices => 0;
    }
}
