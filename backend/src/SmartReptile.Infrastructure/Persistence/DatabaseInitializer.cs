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
    IOptions<StartupOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Runs the configured start-up steps and reports what happened.</summary>
    public async Task<InitializationOutcome> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var migrationsApplied = false;
        var profilesSeeded = 0;

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
        }

        return new InitializationOutcome(migrationsApplied, profilesSeeded);
    }
}

/// <summary>What the start-up sequence actually did — logged and useful in the report.</summary>
/// <param name="MigrationsApplied">True when at least one migration was applied.</param>
/// <param name="ProfilesSeeded">Number of built-in species profiles created.</param>
public readonly record struct InitializationOutcome(bool MigrationsApplied, int ProfilesSeeded);
