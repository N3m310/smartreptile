using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// Brings a real SQL Server up to schema-current and seeded, once per test collection, by running the
/// application's own start-up path — <see cref="DatabaseInitializer"/> — rather than by scripting SQL. If this
/// fixture cannot initialise, the application could not have started either, which is the point.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// The database under test. Supplied as <c>ConnectionStrings__Default</c> so the same variable the API reads
    /// drives the tests; there is deliberately no hard-coded fallback, because that would mean committing a
    /// password and would silently test some other database.
    /// </summary>
    public string ConnectionString { get; } = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
        ?? throw new InvalidOperationException(
            """
            These integration tests need a real SQL Server, supplied through ConnectionStrings__Default.

              docker compose up -d db
              export ConnectionStrings__Default="Server=127.0.0.1,14330;Database=SmartReptile;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;Encrypt=False"

            Use 127.0.0.1 and not localhost: on Windows localhost resolves to IPv6 ::1 first, and Docker Desktop
            does not proxy the published port there, so the connection hangs until the driver's 15 s timeout
            instead of failing fast.
            """);

    /// <summary>A fresh context over the database under test.</summary>
    public SmartReptileDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<SmartReptileDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await using var db = CreateContext();

        var initializer = new DatabaseInitializer(
            db,
            new ReferenceDataSeeder(db, NullLogger<ReferenceDataSeeder>.Instance),
            Options.Create(new StartupOptions()),
            NullLogger<DatabaseInitializer>.Instance);

        var outcome = await initializer.InitializeAsync();

        // Kept on the fixture so tests can assert what start-up actually did rather than inferring it.
        MigrationsAppliedAtStartup = outcome.MigrationsApplied;
        ProfilesCreatedAtStartup = outcome.ProfilesSeeded;
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>True when the fixture had to apply pending migrations to reach schema-current.</summary>
    public bool MigrationsAppliedAtStartup { get; private set; }

    /// <summary>Profiles created by the fixture's own seeding run — 0 when already seeded.</summary>
    public int ProfilesCreatedAtStartup { get; private set; }
}

/// <summary>Serialises the integration tests, so they share one initialised database.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    /// <summary>Collection name shared by every integration test class.</summary>
    public const string Name = "database";
}
