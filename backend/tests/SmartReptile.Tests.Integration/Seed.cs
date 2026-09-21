using Microsoft.EntityFrameworkCore;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// Minimal row builders for the invariant tests.
/// </summary>
/// <remarks>
/// These write SQL directly rather than going through the domain types on purpose: the tests assert what the
/// <b>database</b> refuses, so they must be free to attempt rows that the domain model would never let a caller
/// construct. Going through the entities would test the wrong layer and would pass even if the SQL constraints
/// were missing entirely.
/// <para>
/// Every helper takes the caller's context, so the work happens inside the caller's transaction and disappears
/// when the test rolls back.
/// </para>
/// </remarks>
public static class Seed
{
    /// <summary>Id of any seeded species profile, for rows that carry a profile foreign key.</summary>
    public static Task<Guid> AnySpeciesProfileIdAsync(SmartReptileDbContext db) =>
        db.SpeciesProfiles.OrderBy(p => p.Name).Select(p => p.Id).FirstAsync();

    /// <summary>An owner. Required because <c>Terrarium.UserId</c> is a non-null foreign key.</summary>
    public static async Task<Guid> UserAsync(SmartReptileDbContext db)
    {
        var id = Guid.NewGuid();
        var tag = id.ToString("N")[..8];

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [User] (Id, Username, Email, PasswordHash, PasswordSalt, PasswordIterations, Role,
                                PreferredLanguage, TimeZoneId, MinNotifySeverity, ChannelFcmEnabled,
                                ChannelTelegramEnabled, ChannelEmailEnabled, CreatedAt)
            VALUES ({id}, {"it-" + tag}, {tag + "@example.test"}, 0x00, 0x00, 100000, 1,
                    'en', 'Asia/Ho_Chi_Minh', 2, 1, 0, 0, SYSDATETIMEOFFSET())
            """);

        return id;
    }

    /// <summary>A terrarium owned by <paramref name="userId"/>, attached to any seeded species profile.</summary>
    public static async Task<Guid> TerrariumAsync(SmartReptileDbContext db, Guid userId)
    {
        var id = Guid.NewGuid();
        var profileId = await AnySpeciesProfileIdAsync(db).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Terrarium] (Id, UserId, Name, SpeciesProfileId, TimeZoneId, CreatedAt, UpdatedAt)
            VALUES ({id}, {userId}, {"it-terrarium-" + id.ToString("N")[..8]}, {profileId},
                    'Asia/Ho_Chi_Minh', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """);

        return id;
    }

    /// <summary>
    /// A sensor node bound to <paramref name="terrariumId"/>. <paramref name="status"/> is the raw
    /// <c>DeviceStatus</c> value: 1 Online, 3 Revoked — the value that matters for the DI-04 filtered index,
    /// which excludes only revoked devices.
    /// </summary>
    public static async Task<Guid> DeviceAsync(SmartReptileDbContext db, Guid terrariumId, int status = 1)
    {
        var id = Guid.NewGuid();
        var tag = id.ToString("N")[..10];

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Device] (Id, PublicId, DeviceName, ChipId, MacAddress, FirmwareVersion, Status, Protocol,
                                  TerrariumId, SamplingIntervalSec, PublishIntervalSec, CreatedAt)
            VALUES ({id}, {"sr-" + tag[..6]}, {"it-node-" + tag[..6]}, {"chip-" + tag}, {"AA:BB:CC:" + tag[..5]},
                    '0.1.0-test', {status}, 0, {terrariumId}, 60, 60, SYSDATETIMEOFFSET())
            """);

        return id;
    }

    /// <summary>An alert row. <paramref name="state"/> is the raw <c>AlertState</c>: 0 open, 2 resolved.</summary>
    public static Task AlertAsync(
        SmartReptileDbContext db, Guid terrariumId, Guid deviceId, string dedupeKey, int state) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Alert] (TerrariumId, DeviceId, Severity, Phase, DedupeKey, State, Source, TriggeredAt)
            VALUES ({terrariumId}, {deviceId}, 1, 0, {dedupeKey}, {state}, 0, SYSDATETIMEOFFSET())
            """);

    /// <summary>A threshold band. The ordering CHECK constraints are exactly what some tests violate.</summary>
    public static Task ThresholdAsync(
        SmartReptileDbContext db, Guid speciesProfileId, decimal targetMin, decimal targetMax,
        int dwellWarnMinutes = 60, int dwellCritMinutes = 10) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Threshold] (Id, SpeciesProfileId, Metric, Phase, TargetMin, TargetMax,
                                     DwellWarnMinutes, DwellCritMinutes, RecoveryMargin, Enabled)
            VALUES (NEWID(), {speciesProfileId}, 0, 0, {targetMin}, {targetMax},
                    {dwellWarnMinutes}, {dwellCritMinutes}, 0.5, 1)
            """);

    /// <summary>A telemetry sample; <paramref name="sequence"/> is what the per-device unique index guards.</summary>
    public static Task TelemetrySampleAsync(
        SmartReptileDbContext db, Guid terrariumId, Guid deviceId, long sequence) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [TelemetrySample] (TerrariumId, DeviceId, RecordedAt, ReceivedAt, Sequence,
                                           QualityFlags, FirmwareVersion, Source)
            VALUES ({terrariumId}, {deviceId}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), {sequence},
                    0, '0.1.0-test', 0)
            """);
}
