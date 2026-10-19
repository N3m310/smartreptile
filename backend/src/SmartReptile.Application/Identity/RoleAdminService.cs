using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>
/// The permission matrix as data (FR-18): list the roles, read the catalogue, create a custom role, edit a role's
/// permission set, delete a custom role.
/// </summary>
/// <remarks>
/// <b>One guard, and it is the same one the account service keeps.</b> A deployment must never reach a state where
/// no enabled account holds <c>user.manage</c>, because the route that would restore it is behind that permission.
/// So an edit that would remove the last grant of <c>user.manage</c> is refused, and so is deleting or emptying the
/// only role that holds it — checked by counting <em>holders</em>, not by looking for a role named <c>Admin</c>, so
/// a deployment that renamed or replaced its administrator role stays equally protected.
/// <para>
/// <b>System roles may be edited but not deleted or re-coded.</b> Their ids and codes are named by the seed, the
/// documentation and the clients; their permission sets are exactly what an administrator is expected to tune.
/// </para>
/// </remarks>
public sealed class RoleAdminService(IUserStore store, IClock clock)
{
    /// <summary>Where a custom role sorts when the caller does not say. Below the four shipped roles.</summary>
    public const int DefaultRank = 100;

    /// <summary>Longest role code the column holds.</summary>
    public const int MaxCodeLength = Role.CodeMaxLength;

    /// <summary>Every role with its grants and how many accounts hold it.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> ListAsync(CancellationToken cancellationToken = default)
    {
        var roles = await store.ListRolesAsync(cancellationToken);

        // The holders are read once for every role rather than once per role: the matrix is one screen, and a
        // console whose row count came from a query per row would be slower than the table it draws.
        var holders = await store.CountUsersByRoleAsync(cancellationToken);

        return RoleOutcome.Listed(
            [.. roles.Select(role => Describe(role, holders.GetValueOrDefault(role.Id)))]);
    }

    /// <summary>The permission catalogue, which is what the console offers for granting.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> CatalogAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await store.ListPermissionCatalogAsync(cancellationToken);

        return RoleOutcome.Catalogue([.. catalog.Select(permission => new PermissionSummary(
            permission.Code,
            permission.Group,
            permission.Name,
            permission.Description,
            permission.CrossesAccounts))]);
    }

    /// <summary>One role with its grants, its holders and its concurrency token.</summary>
    /// <param name="roleId">Role to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> GetAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await store.FindRoleAsync(roleId, cancellationToken);

        if (role is null)
        {
            return RoleOutcome.Failure("role_not_found", "No such role.");
        }

        var holders = await store.CountUsersInRoleAsync(roleId, cancellationToken);

        return RoleOutcome.Found(Describe(role, holders));
    }

    /// <summary>Creates a custom role.</summary>
    /// <param name="request">Code, name, description and the permissions to grant.</param>
    /// <param name="actorUserId">The administrator creating it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> CreateAsync(
        CreateRoleRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var violations = new List<IdentityViolation>();

        if (code.Length == 0 || code.Length > MaxCodeLength || !IsIdentifier(code))
        {
            violations.Add(new IdentityViolation(
                "code",
                "role_code_invalid",
                $"The code must be 1–{MaxCodeLength} identifier characters (letters, digits, underscore)."));
        }
        else if (await store.FindRoleByCodeAsync(code, cancellationToken) is not null)
        {
            violations.Add(new IdentityViolation("code", "role_code_taken", "A role already uses that code."));
        }

        if (name.Length == 0 || name.Length > Role.NameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "name",
                "role_name_invalid",
                $"The name must be 1–{Role.NameMaxLength} characters."));
        }

        if (!TryResolvePermissions(request.Permissions, out var grants, out var unknown))
        {
            violations.Add(new IdentityViolation(
                "permissions",
                "permission_unknown",
                $"These codes are not in the catalogue: {string.Join(", ", unknown)}."));
        }

        if (violations.Count > 0)
        {
            return RoleOutcome.Invalid("role_invalid", "The role was refused.", violations);
        }

        var now = clock.UtcNow;

        // The id is known before the insert, so the audit row can name the role in the same unit of work — the same
        // reason `MetricSilence.Id` is a Guid rather than an identity column.
        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = (request.Description ?? string.Empty).Trim(),
            IsSystem = false,
            Rank = request.Rank ?? DefaultRank,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var code2 in grants)
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = code2 });
        }

        store.AddRole(role);

        store.AddAuditEntry(AuditLog.ForRole(
            AuditAction.RoleCreated,
            role.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: JsonSerializer.Serialize(new { role.Code, role.Name, permissions = grants })));

        await store.SaveChangesAsync(cancellationToken);

        return RoleOutcome.Found(Describe(role, 0));
    }

    /// <summary>
    /// Edits a role's name, description, display order or permission set.
    /// </summary>
    /// <param name="roleId">Role to edit.</param>
    /// <param name="request">The members to change; an omitted one keeps its value, and <c>permissions</c> replaces the set.</param>
    /// <param name="actorUserId">The administrator editing it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> UpdateAsync(
        Guid roleId,
        UpdateRoleRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await store.FindRoleAsync(roleId, cancellationToken);

        if (role is null)
        {
            return RoleOutcome.Failure("role_not_found", "No such role.");
        }

        var name = request.Name?.Trim();
        var description = request.Description?.Trim();
        var violations = new List<IdentityViolation>();

        if (name is { Length: 0 })
        {
            violations.Add(new IdentityViolation("name", "role_name_invalid", "The name may not be empty."));
        }
        else if (name is { } candidate && candidate.Length > Role.NameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "name",
                "role_name_invalid",
                $"The name must be at most {Role.NameMaxLength} characters."));
        }

        if (description is { } text && text.Length > Role.DescriptionMaxLength)
        {
            violations.Add(new IdentityViolation(
                "description",
                "role_description_invalid",
                $"The description must be at most {Role.DescriptionMaxLength} characters."));
        }

        IReadOnlyList<string>? grants = null;

        if (request.Permissions is { } requested)
        {
            if (!TryResolvePermissions(requested, out var resolved, out var unknown))
            {
                violations.Add(new IdentityViolation(
                    "permissions",
                    "permission_unknown",
                    $"These codes are not in the catalogue: {string.Join(", ", unknown)}."));
            }
            else
            {
                grants = resolved;
            }
        }

        if (violations.Count > 0)
        {
            return RoleOutcome.Invalid("role_invalid", "The change was refused.", violations);
        }

        var before = CurrentGrants(role);

        // Guard: the last grant of `user.manage` cannot be removed, in any role, by anybody.
        if (grants is not null && !grants.Contains(Permissions.UserManage, StringComparer.Ordinal)
            && before.Contains(Permissions.UserManage, StringComparer.Ordinal))
        {
            var others = await store.CountUsersWithPermissionAsync(Permissions.UserManage, excludingUserId: null, cancellationToken);

            if (others == 0)
            {
                return RoleOutcome.Failure(
                    "last_administrator_refused",
                    "This is the only role granting the account permissions. Grant them to another role first.");
            }
        }

        var changed = false;

        if (name is { } newName && newName != role.Name)
        {
            role.Name = newName;
            changed = true;
        }

        if (description is not null && description != role.Description)
        {
            role.Description = description;
            changed = true;
        }

        if (request.Rank is { } rank && rank != role.Rank)
        {
            role.Rank = rank;
            changed = true;
        }

        if (grants is not null && !SameSet(before, grants))
        {
            // Replace, never merge: "the matrix is now this". Removing a grant is deleting its row, so a merge would
            // make a permission impossible to take away through this route.
            foreach (var existing in role.Permissions.ToList())
            {
                if (!grants.Contains(existing.PermissionCode, StringComparer.Ordinal))
                {
                    role.Permissions.Remove(existing);
                }
            }

            foreach (var code in grants)
            {
                if (!before.Contains(code, StringComparer.Ordinal))
                {
                    role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionCode = code });
                }
            }

            changed = true;
        }

        if (!changed)
        {
            // Nothing changes, so nothing is written and no audit row appears — the same rule the account service
            // follows for a repeated role assignment.
            return RoleOutcome.Found(Describe(role, userCount: null));
        }

        var now = clock.UtcNow;
        role.UpdatedAt = now;

        var after = grants ?? before;

        store.AddAuditEntry(AuditLog.ForRole(
            AuditAction.RoleUpdated,
            role.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            beforeJson: JsonSerializer.Serialize(new { role.Code, role.Name, permissions = before }),
            afterJson: JsonSerializer.Serialize(new { role.Code, role.Name, permissions = after })));

        await store.SaveChangesAsync(cancellationToken);

        return RoleOutcome.Found(Describe(role, userCount: null));
    }

    /// <summary>Deletes a custom role no account holds.</summary>
    /// <param name="roleId">Role to delete.</param>
    /// <param name="actorUserId">The administrator deleting it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RoleOutcome> DeleteAsync(
        Guid roleId,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var role = await store.FindRoleAsync(roleId, cancellationToken);

        if (role is null)
        {
            return RoleOutcome.Failure("role_not_found", "No such role.");
        }

        if (role.IsSystem)
        {
            // The id and the code are named by the seed, the docs and the clients; deleting one would make the next
            // start-up recreate it with the shipped defaults, silently undoing whatever an administrator had tuned.
            return RoleOutcome.Failure(
                "system_role_immutable",
                "A role the product ships cannot be deleted. Edit its permissions instead, or create a custom role.");
        }

        var holders = await store.CountUsersInRoleAsync(roleId, cancellationToken);

        if (holders > 0)
        {
            return RoleOutcome.Failure(
                "role_in_use",
                $"{holders} account(s) still hold this role. Move them to another role first.");
        }

        var now = clock.UtcNow;
        var before = CurrentGrants(role);

        store.RemoveRole(role);

        store.AddAuditEntry(AuditLog.ForRole(
            AuditAction.RoleDeleted,
            role.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            beforeJson: JsonSerializer.Serialize(new { role.Code, role.Name, permissions = before })));

        await store.SaveChangesAsync(cancellationToken);

        return RoleOutcome.Found(Describe(role, 0));
    }

    /// <summary>The codes a role currently holds, in catalogue order.</summary>
    private static IReadOnlyList<string> CurrentGrants(Role role) =>
        [.. role.Permissions.Select(grant => grant.PermissionCode).OrderBy(Permissions.SortOrderOf)];

    /// <summary>
    /// Resolves requested codes against the catalogue. Returns false and names the unknown ones rather than
    /// dropping them: a typo that silently grants nothing is the failure mode this check exists to prevent.
    /// </summary>
    private static bool TryResolvePermissions(
        IReadOnlyList<string>? requested,
        out IReadOnlyList<string> resolved,
        out IReadOnlyList<string> unknown)
    {
        if (requested is null)
        {
            resolved = [];
            unknown = [];

            return true;
        }

        var codes = requested
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        unknown = [.. codes.Where(code => !Permissions.Exists(code))];
        resolved = [.. codes.Where(Permissions.Exists).OrderBy(Permissions.SortOrderOf)];

        return unknown.Count == 0;
    }

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == right.Count && left.All(code => right.Contains(code, StringComparer.Ordinal));

    private static bool IsIdentifier(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static RoleSummary Describe(Role role, int? userCount) =>
        new(
            role.Id,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.Rank,
            CurrentGrants(role),
            userCount ?? 0,
            role.RowVersion is { } version ? Convert.ToBase64String(version) : null);
}
