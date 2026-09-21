using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The start-up paths that M1 could only verify by hand, now enforced: the schema is produced by EF migrations
/// rather than by hand-written SQL, and the reference data seeds idempotently against a real SQL Server.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SchemaAndSeedingTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task The_schema_comes_from_migrations_and_none_are_pending()
    {
        await using var db = fixture.CreateContext();

        var applied = await db.Database.GetAppliedMigrationsAsync();
        var pending = await db.Database.GetPendingMigrationsAsync();

        applied.Should().Contain(name => name.EndsWith("_InitialSchema", StringComparison.Ordinal),
            "a migration is the only sanctioned way to change the schema (03-implementation/03 §2)");
        pending.Should().BeEmpty("the fixture ran the application's own start-up initialiser");
    }

    [Fact]
    public async Task Seeding_creates_the_three_built_in_species_profiles_with_their_bands()
    {
        await using var db = fixture.CreateContext();

        var names = await db.SpeciesProfiles.Select(p => p.Name).OrderBy(n => n).ToListAsync();
        names.Should().BeEquivalentTo(new[]
        {
            "Arid (desert)", "Leopard gecko (semi-desert)", "Tropical (humid forest)",
        });

        // The seeded band count is quoted in the README and docs as measured evidence, so it is pinned here.
        // Adding a band is expected to be deliberate — update the docs and this number together.
        var bands = await db.Thresholds.ToListAsync();
        bands.Should().HaveCount(17, "the built-in profiles ship 17 bands in total");

        bands.Select(b => b.SpeciesProfileId).Distinct().Should().HaveCount(names.Count,
            "every built-in profile must arrive with its own bands");
    }

    [Fact]
    public async Task Every_seeded_band_carries_a_provenance_reference()
    {
        await using var db = fixture.CreateContext();

        var uncited = await db.Thresholds
            .Where(t => t.SourceRef == null || t.SourceRef == string.Empty)
            .Select(t => t.Id)
            .ToListAsync();

        // Roadmap gate 1.8: a band without a citation does not ship, so the seeded set must satisfy it even while
        // the references still read "PENDING VERIFICATION".
        uncited.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_anything()
    {
        await using var db = fixture.CreateContext();
        var before = await db.SpeciesProfiles.CountAsync();

        var seeder = new ReferenceDataSeeder(db, NullLogger<ReferenceDataSeeder>.Instance);
        var created = await seeder.SeedAsync();

        created.Should().Be(0, "the seeder is idempotent by name, so a restart must not duplicate rows");
        (await db.SpeciesProfiles.CountAsync()).Should().Be(before);
    }
}
