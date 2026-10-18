using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Tests.Integration;

/// <summary>
/// The identity seed against a real SQL Server (FR-18; TASKS 2026-10-14, item 12). What is worth a database here is
/// the asymmetry the seeder is written around: a missing row is created, and an existing grant is never re-applied.
/// </summary>
/// <remarks>
/// The migration inserts the same rows on a fresh database, so the first test is really "the migration and the
/// seeder agree" — the two write the catalogue from the same constants.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class IdentitySeederTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Start_up_left_the_catalogue_and_the_four_roles_complete()
    {
        await using var db = fixture.CreateContext();

        var codes = await db.PermissionCatalog.Select(permission => permission.Code).ToListAsync();
        codes.Should().BeEquivalentTo(Permissions.All.Select(definition => definition.Code));

        var roles = await db.Roles.Include(role => role.Permissions).ToListAsync();

        foreach (var seed in SystemRoles.Seed)
        {
            var row = roles.Should().ContainSingle(role => role.Id == seed.Id).Subject;
            row.Code.Should().Be(seed.Code);
            row.IsSystem.Should().BeTrue();
            row.Permissions.Select(grant => grant.PermissionCode).Should().BeEquivalentTo(seed.Permissions);
        }
    }

    [Fact]
    public async Task A_second_pass_finds_nothing_to_repair()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var outcome = await new IdentitySeeder(db, NullLogger<IdentitySeeder>.Instance)
            .SeedAsync(CancellationToken.None);

        outcome.IsNoOp.Should().BeTrue(
            "start-up runs after the migration, so the ordinary case is a database that already has everything");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_grant_an_administrator_removed_is_not_restored()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var grant = await db.RolePermissions.SingleAsync(row =>
            row.RoleId == SystemRoles.ViewerId && row.PermissionCode == Permissions.NotificationRead);

        db.RolePermissions.Remove(grant);
        await db.SaveChangesAsync();

        var outcome = await new IdentitySeeder(db, NullLogger<IdentitySeeder>.Instance)
            .SeedAsync(CancellationToken.None);
        await db.SaveChangesAsync();

        outcome.IsNoOp.Should().BeTrue();
        (await db.RolePermissions.AnyAsync(row =>
            row.RoleId == SystemRoles.ViewerId && row.PermissionCode == Permissions.NotificationRead))
            .Should().BeFalse("re-applying the shipped defaults on every restart would undo the deployment's decision");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task A_missing_system_role_comes_back_with_its_default_grants()
    {
        await using var db = fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        // Viewer holds nobody in this database, so the delete is the one the API would also allow.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [RolePermission] WHERE [RoleId] = {SystemRoles.ViewerId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [Role] WHERE [Id] = {SystemRoles.ViewerId}");

        var outcome = await new IdentitySeeder(db, NullLogger<IdentitySeeder>.Instance)
            .SeedAsync(CancellationToken.None);
        await db.SaveChangesAsync();

        outcome.RolesCreated.Should().Be(1);
        outcome.GrantsCreated.Should().Be(SystemRoles.Seed.Single(role => role.Id == SystemRoles.ViewerId).Permissions.Length);

        var restored = await db.Roles
            .Include(role => role.Permissions)
            .SingleAsync(role => role.Id == SystemRoles.ViewerId);

        restored.IsSystem.Should().BeTrue();
        restored.Permissions.Select(grant => grant.PermissionCode).Should().BeEquivalentTo(
            SystemRoles.Seed.Single(role => role.Id == SystemRoles.ViewerId).Permissions);

        await tx.RollbackAsync();
    }
}
