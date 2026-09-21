using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The invariants that exist <b>only in SQL Server</b> — filtered unique indexes and CHECK constraints — and that
/// a C#-only implementation would silently lose. Asserting the name of the object that rejected the row is what
/// makes these tests meaningful: it proves the specific rule fired, not merely that "something threw".
/// </summary>
/// <remarks>
/// Every test runs inside a transaction that is rolled back, so the database under test is left exactly as it was
/// found. The rollback also means these tests can run against a developer's working database without polluting it.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DatabaseInvariantTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task At_most_one_non_revoked_device_can_be_bound_to_a_terrarium()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        await Seed.DeviceAsync(db, terrariumId, status: 1);

        var failure = await Assert.ThrowsAsync<SqlException>(() => Seed.DeviceAsync(db, terrariumId, status: 1));
        failure.Message.Should().Contain("IX_Device_TerrariumId");

        // The index is filtered on Status <> 3, so a revoked device may sit alongside the active one.
        await Seed.DeviceAsync(db, terrariumId, status: 3);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task An_open_alert_is_unique_per_dedupe_key_but_resolving_it_frees_the_key()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId);
        var dedupeKey = "it-" + Guid.NewGuid().ToString("N")[..8];

        await Seed.AlertAsync(db, terrariumId, deviceId, dedupeKey, state: 0);

        var failure = await Assert.ThrowsAsync<SqlException>(
            () => Seed.AlertAsync(db, terrariumId, deviceId, dedupeKey, state: 0));
        failure.Message.Should().Contain("IX_Alert_DedupeKey");

        // Resolving the alert must free the key, otherwise a terrarium that was fixed could never raise the same
        // alert again — the failure mode this filtered index exists to prevent.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [Alert] SET [State] = 2 WHERE [DedupeKey] = {dedupeKey}");
        await Seed.AlertAsync(db, terrariumId, deviceId, dedupeKey, state: 0);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Threshold_bands_with_an_inverted_or_zero_dwell_order_are_rejected()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var profileId = await Seed.AnySpeciesProfileIdAsync(db);

        // CK_Threshold_TargetOrder is ([TargetMin] < [TargetMax]).
        var inverted = await Assert.ThrowsAsync<SqlException>(
            () => Seed.ThresholdAsync(db, profileId, targetMin: 30m, targetMax: 20m));
        inverted.Message.Should().Contain("CK_Threshold_TargetOrder");

        // CK_Threshold_DwellOrder requires DwellWarnMinutes > 0 and DwellCritMinutes <= DwellWarnMinutes.
        var zeroDwell = await Assert.ThrowsAsync<SqlException>(
            () => Seed.ThresholdAsync(db, profileId, targetMin: 20m, targetMax: 30m, dwellWarnMinutes: 0));
        zeroDwell.Message.Should().Contain("CK_Threshold_DwellOrder");

        // Only the rejections are asserted here. A *valid* band is not inserted because Threshold also carries a
        // unique index on (SpeciesProfileId, Metric, Phase), and the seeded profiles already occupy the pairs the
        // seeder uses — so a positive case would depend on guessing an unused pair rather than on the rule under
        // test. That the constraints do not reject everything is covered by the device and sample tests.

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Sample_sequence_numbers_are_unique_per_device()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var terrariumId = await Seed.TerrariumAsync(db, await Seed.UserAsync(db));
        var deviceId = await Seed.DeviceAsync(db, terrariumId);

        await Seed.TelemetrySampleAsync(db, terrariumId, deviceId, sequence: 42);

        var failure = await Assert.ThrowsAsync<SqlException>(
            () => Seed.TelemetrySampleAsync(db, terrariumId, deviceId, sequence: 42));
        failure.Message.Should().Contain("IX_TelemetrySample_DeviceId_Sequence");

        // A different sequence, and the same sequence on another device, are both fine.
        await Seed.TelemetrySampleAsync(db, terrariumId, deviceId, sequence: 43);
        var otherDevice = await Seed.DeviceAsync(db, await Seed.TerrariumAsync(db, await Seed.UserAsync(db)));
        await Seed.TelemetrySampleAsync(db, terrariumId, otherDevice, sequence: 42);

        await tx.RollbackAsync();
    }
}
