using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds reference data on start-up, under the switches in
/// <see cref="StartupOptions"/> (§03-implementation/03 §2). In release the migration step is part of the
/// runbook instead, so a failed migration cannot silently take the demo host down.
/// </summary>
public sealed class DatabaseInitializer(
    SmartReptileDbContext db,
    ReferenceDataSeeder seeder,
    IdentitySeeder identitySeeder,
    SystemAdminAccountSeeder adminSeeder,
    DemoDataSeeder demoSeeder,
    IOptions<StartupOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Runs the configured start-up steps and reports what happened.</summary>
    public async Task<InitializationOutcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var migrationsApplied = false;
        var profilesSeeded = 0;
        var timeZonesSeeded = 0;
        var identitySeed = new IdentitySeedOutcome(0, 0, 0);

        if (settings.ApplyMigrationsOnStartup)
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

            if (pending.Count > 0)
            {
                logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                migrationsApplied = true;
            }
            else
            {
                logger.LogInformation("Database schema is up to date");
            }
        }
        else
        {
            logger.LogInformation("Start-up migrations are disabled; the schema is expected to be current");
        }

        if (settings.SeedReferenceDataOnStartup)
        {
            profilesSeeded = await seeder.SeedAsync(cancellationToken).ConfigureAwait(false);

            // The time-zone catalogue is seeded by the same switch, because it is read by the same clients: a
            // deployment that skips reference data has no picker to fill, and the terrarium write paths fall back
            // to refusing an id the table does not hold.
            timeZonesSeeded = await seeder.SeedTimeZonesAsync(cancellationToken).ConfigureAwait(false);
        }

        // Not behind the reference-data switch, and deliberately: roles and the permission catalogue are not
        // reference data a deployment may skip — they are what authorisation reads. A database with no `Role` row
        // cannot create an account at all, because `User.RoleId` points at one. The seed is a reconciliation, so a
        // database that already has them is untouched.
        identitySeed = await identitySeeder.SeedAsync(cancellationToken).ConfigureAwait(false);

        // Not behind a start-up switch either: the Admin section is the switch, so a deployment that names no
        // administrator disables this by saying nothing. It runs after the reference data because the catalogue is
        // what the account exists to author, and after the identity seed because the account needs a role to hold.
        var adminSeeded = await adminSeeder.SeedAsync(cancellationToken).ConfigureAwait(false);

        // Last, because it needs everything above: accounts on the seeded roles, enclosures on the seeded profiles,
        // and a catalogue to choose them from. The seeder refuses outside Development and when the flag is off.
        var demo = await demoSeeder.SeedAsync(cancellationToken).ConfigureAwait(false);

        return new InitializationOutcome(migrationsApplied, profilesSeeded, timeZonesSeeded, adminSeeded, identitySeed)
        {
            Demo = demo,
        };
    }
}

/// <summary>What the start-up sequence actually did — logged and useful in the report.</summary>
/// <param name="MigrationsApplied">True when at least one migration was applied.</param>
/// <param name="ProfilesSeeded">Number of built-in species profiles created.</param>
/// <param name="TimeZonesSeeded">Number of time-zone catalogue rows created.</param>
/// <param name="AdminSeeded">True when the configured system administrator account was created.</param>
/// <param name="IdentitySeed">Roles and permission-catalogue rows this run had to create, if any.</param>
public readonly record struct InitializationOutcome(
    bool MigrationsApplied,
    int ProfilesSeeded,
    int TimeZonesSeeded,
    bool AdminSeeded,
    IdentitySeedOutcome IdentitySeed)
{
    /// <summary>What the demo-data step did; <see cref="DemoSeedOutcome.NotRequested"/> unless it was asked for.</summary>
    public DemoSeedOutcome Demo { get; init; }
}
