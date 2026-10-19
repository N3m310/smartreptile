using Microsoft.EntityFrameworkCore;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>What the identity seed did, so start-up can log it and a test can assert it.</summary>
/// <param name="PermissionsCreated">Catalogue rows inserted because the catalogue had grown.</param>
/// <param name="RolesCreated">System roles inserted because the row was missing.</param>
/// <param name="GrantsCreated">Default grants written with a role that had to be created.</param>
public readonly record struct IdentitySeedOutcome(
    int PermissionsCreated,
    int RolesCreated,
    int GrantsCreated)
{
    /// <summary>True when nothing was missing, which is the normal case after the first start-up.</summary>
    public bool IsNoOp => PermissionsCreated == 0 && RolesCreated == 0 && GrantsCreated == 0;
}

/// <summary>
/// Makes the permission catalogue and the four system roles exist, from the code that defines them
/// (<see cref="Permissions.All"/>, <see cref="SystemRoles"/>).
/// </summary>
/// <remarks>
/// <b>What this deliberately does not do: touch a grant that exists.</b> The matrix is a deployment's decision —
/// an administrator who removes <c>audit.read</c> from <c>Technician</c> has made a choice, and a seeder that
/// re-applied the shipped defaults on every restart would silently undo it. So the grants are written exactly once,
/// when the role row itself is created; afterwards the seed only adds what is missing.
/// <para>
/// <b>And what it does do: make a new permission visible without granting it.</b> When the catalogue grows, the row
/// is inserted so the console can show it and an administrator can grant it; it is granted to nobody, because
/// widening a role's reach is not a decision a start-up may take. That is the one asymmetry in this file, and it is
/// the safe direction in both cases.
/// </para>
/// <para>
/// The identity rows also arrive with the migration that creates the tables, so this runs as a repair on an
/// existing database and as a no-op on a fresh one. It is not redundant: a deployment that inserts a system role by
/// hand, or a future release that adds a permission, is reconciled here rather than at the first request that
/// needs it.
/// </para>
/// </remarks>
public sealed class IdentitySeeder(SmartReptileDbContext db, ILogger<IdentitySeeder> logger)
{
    /// <summary>Runs the reconciliation.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IdentitySeedOutcome> SeedAsync(CancellationToken cancellationToken)
    {
        var permissionsCreated = await SeedPermissionsAsync(cancellationToken).ConfigureAwait(false);
        var (rolesCreated, grantsCreated) = await SeedRolesAsync(cancellationToken).ConfigureAwait(false);

        var outcome = new IdentitySeedOutcome(permissionsCreated, rolesCreated, grantsCreated);

        if (!outcome.IsNoOp)
        {
            logger.LogInformation(
                "Identity seed: {Permissions} permission row(s) and {Roles} system role(s) created, {Grants} default grant(s) written",
                outcome.PermissionsCreated,
                outcome.RolesCreated,
                outcome.GrantsCreated);
        }

        return outcome;
    }

    /// <summary>Inserts catalogue rows the table does not have, and keeps the labels in step with the code.</summary>
    private async Task<int> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.PermissionCatalog
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;
        var order = 0;

        foreach (var definition in Permissions.All)
        {
            order++;

            if (existing.TryGetValue(definition.Code, out var row))
            {
                // The labels are documentation, so they follow the code. The grants are not touched here.
                row.Group = definition.Group;
                row.Name = definition.Name;
                row.Description = definition.Description;
                row.CrossesAccounts = definition.CrossesAccounts;
                row.SortOrder = order;

                continue;
            }

            db.PermissionCatalog.Add(new Permission
            {
                Code = definition.Code,
                Group = definition.Group,
                Name = definition.Name,
                Description = definition.Description,
                CrossesAccounts = definition.CrossesAccounts,
                SortOrder = order,
            });

            created++;
        }

        return created;
    }

    /// <summary>
    /// Inserts a missing system role <b>and</b> its default grants — the only place the shipped matrix is applied —
    /// and refreshes the display fields of one that exists.
    /// </summary>
    private async Task<(int RolesCreated, int GrantsCreated)> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Roles
            .Include(role => role.Permissions)
            .ToDictionaryAsync(role => role.Id, cancellationToken)
            .ConfigureAwait(false);

        var rolesCreated = 0;
        var grantsCreated = 0;

        foreach (var seed in SystemRoles.Seed)
        {
            if (existing.TryGetValue(seed.Id, out var row))
            {
                // Name and description are shown to an administrator, so they track the code. `Code` and `IsSystem`
                // are not touched: a system role's identity is what the seed and the docs name it by.
                row.Name = seed.Name;
                row.Description = seed.Description;

                continue;
            }

            var role = new Role
            {
                Id = seed.Id,
                Code = seed.Code,
                Name = seed.Name,
                Description = seed.Description,
                IsSystem = true,
                Rank = seed.Rank,
            };

            foreach (var code in seed.Permissions)
            {
                role.Permissions.Add(new RolePermission { RoleId = seed.Id, PermissionCode = code });
                grantsCreated++;
            }

            db.Roles.Add(role);
            rolesCreated++;
        }

        return (rolesCreated, grantsCreated);
    }
}
