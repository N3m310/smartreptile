using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Persistence;
using SmartReptile.Infrastructure.Security;
using SmartReptile.Infrastructure.Summaries;
using SmartReptile.Infrastructure.Time;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The demo-data seed (TASKS 2026-10-19, BE-07) against a real SQL Server. Three things a fake could not answer are
/// the reasons it is an integration test: whether a week of readings actually inserts, whether the summary recompute
/// finds them, and whether a second run writes nothing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DemoDataSeederTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Seeding_writes_the_accounts_the_enclosures_and_a_week_of_readings()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var outcome = await Seeder(db, demoData: true).SeedAsync(CancellationToken.None);

        outcome.Seeded.Should().BeTrue();
        outcome.Accounts.Should().Be(3);
        outcome.Terrariums.Should().Be(3);
        outcome.Alerts.Should().Be(6, "one open and one resolved episode per enclosure");
        outcome.Notifications.Should().Be(6);

        // The supporting rows behind the screens the three lists do not reach.
        outcome.HealthSamples.Should().Be(84, "28 six-hourly reports per board");
        outcome.DeviceEvents.Should().Be(18, "six events per board");
        outcome.ThresholdSnapshots.Should().Be(6, "the set in force a week ago and now, per enclosure");
        outcome.ThresholdOverrides.Should().Be(1);
        outcome.MetricSilences.Should().Be(2, "one in force and one cancelled");
        outcome.AuditRows.Should().Be(6, "a claim and an edit per enclosure");

        // Seven days at one sample a minute, per enclosure, with four metrics on each sample.
        outcome.Samples.Should().BeGreaterThanOrEqualTo(3 * 7 * 24 * 60);
        outcome.Readings.Should().Be(outcome.Samples * 4);

        var owner = await db.Users
            .Include(user => user.Role)
            .SingleAsync(user => user.Username == DemoDataSeeder.OwnerUsername);

        owner.Role!.Code.Should().Be("Owner");

        var terrariums = await db.Terrariums.Where(row => row.UserId == owner.Id).ToListAsync();
        terrariums.Should().HaveCount(3);
        terrariums.Select(row => row.SpeciesProfileId).Distinct().Should().HaveCount(3, "each enclosure names its own profile");

        // Every enclosure has a board, and every board reports into it.
        (await db.Devices.CountAsync(device => device.UserId == owner.Id)).Should().Be(3);

        // The summary recompute ran after the readings, so the newest local day has a row derived from them.
        var terrariumIds = terrariums.ConvertAll(row => row.Id);
        (await db.DailyEnvironmentalSummaries.CountAsync(row => terrariumIds.Contains(row.TerrariumId)))
            .Should().BePositive("rollups and summaries are computed from what was seeded, never inserted by hand");

        (await db.NotificationLogs.CountAsync(row => row.UserId == owner.Id)).Should().BePositive();

        // Every screen that reads a table of its own has something in it: the fleet's health history, the device
        // detail's events, the threshold history, the silence list and the audit trail. Counted across the owner's
        // enclosures rather than for one of them, because the read returns them in no particular order — the seeder
        // gives the override to the first enclosure it created, not to whichever row a query happens to return first.
        (await db.DeviceHealthSamples.CountAsync(sample => terrariumIds.Contains(sample.TerrariumId))).Should().Be(84);
        (await db.DeviceEvents.CountAsync(entry => entry.TerrariumId != null && terrariumIds.Contains(entry.TerrariumId.Value))).Should().Be(18);
        (await db.ThresholdSnapshots.CountAsync(snapshot => terrariumIds.Contains(snapshot.TerrariumId))).Should().Be(6);
        (await db.ThresholdOverrides.CountAsync(row => terrariumIds.Contains(row.TerrariumId))).Should().Be(1);
        (await db.MetricSilences.CountAsync()).Should().Be(2);
        (await db.AuditLogs.CountAsync(row => row.UserId == owner.Id)).Should().Be(6);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        await Seeder(db, demoData: true).SeedAsync(CancellationToken.None);
        var second = await Seeder(db, demoData: true).SeedAsync(CancellationToken.None);

        second.Seeded.Should().BeFalse();
        second.Should().Be(DemoSeedOutcome.AlreadyPresent);
        (await db.Users.CountAsync(user => user.Username == DemoDataSeeder.OwnerUsername)).Should().Be(1);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task The_flag_off_writes_nothing()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var outcome = await Seeder(db, demoData: false).SeedAsync(CancellationToken.None);

        outcome.Seeded.Should().BeFalse();
        (await db.Users.AnyAsync(user => user.Username == DemoDataSeeder.OwnerUsername)).Should().BeFalse();

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Outside_development_nothing_is_written_even_when_the_flag_is_on()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var outcome = await Seeder(db, demoData: true, environment: Environments.Production)
            .SeedAsync(CancellationToken.None);

        // The environment check is the guard a release cannot switch off by accident, so it is asserted on its own.
        outcome.Seeded.Should().BeFalse();
        (await db.Users.AnyAsync(user => user.Username == DemoDataSeeder.OwnerUsername)).Should().BeFalse();

        await tx.RollbackAsync();
    }

    private static DemoDataSeeder Seeder(
        SmartReptileDbContext db,
        bool demoData,
        string environment = "Development")
    {
        var clock = new SystemClock();

        return new DemoDataSeeder(
            db,
            new Pbkdf2PasswordHasher(),
            new SummaryRecomputer(db, clock),
            clock,
            new DemoEnvironment(environment),
            Options.Create(new DemoDataOptions { DemoData = demoData }),
            NullLogger<DemoDataSeeder>.Instance);
    }

    /// <summary>An environment name the seeder can be pointed at, so Development-only can be asserted.</summary>
    private sealed class DemoEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "SmartReptile.Tests.Integration";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
