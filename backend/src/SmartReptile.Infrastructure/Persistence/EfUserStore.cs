using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// The <see cref="IUserStore"/> adapter. Queries are intentionally bare — this is the "simple CRUD goes
/// straight through the context" case from §03-implementation/03 §9, and a repository per entity would add a
/// layer without removing a decision.
/// </summary>
public sealed class EfUserStore(SmartReptileDbContext db) : IUserStore
{
    /// <inheritdoc />
    public Task<User?> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        Accounts(db.Users)
            .FirstOrDefaultAsync(
                user => user.Username == usernameOrEmail || user.Email == usernameOrEmail,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Accounts(db.Users)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public async Task<TakenIdentifiers> FindTakenIdentifiersAsync(
        string username,
        string email,
        CancellationToken cancellationToken)
    {
        // One round trip, then two comparisons in memory: the match set is at most one account per identifier.
        var matches = await db.Users
            .Where(user => user.Username == username || user.Email == email)
            .Select(user => new { user.Username, user.Email })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TakenIdentifiers(
            matches.Exists(match => match.Username == username),
            matches.Exists(match => match.Email == email));
    }

    /// <inheritdoc />
    public void AddUser(User user) => db.Users.Add(user);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = Filtered(search);

        return await query
            .OrderBy(user => user.Username)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(string? search, CancellationToken cancellationToken) =>
        Filtered(search).CountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountUsersWithPermissionAsync(
        string permissionCode,
        Guid? excludingUserId,
        CancellationToken cancellationToken)
    {
        // Counted through the join rather than by role name: what protects the deployment is "somebody can still
        // manage accounts", and a deployment whose administrator role was renamed or split must still be safe.
        var query = db.Users.Where(user =>
            user.DisabledAt == null
            && user.Role!.Permissions.Any(grant => grant.PermissionCode == permissionCode));

        if (excludingUserId is { } excluded)
        {
            query = query.Where(user => user.Id != excluded);
        }

        return query.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListPermissionsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var codes = await db.RolePermissions
            .Where(grant => grant.RoleId == roleId)
            .Select(grant => grant.PermissionCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Ordered here rather than in SQL: the catalogue's order lives in code, and a role holding a code this build
        // no longer defines would otherwise sort unpredictably.
        return [.. codes.OrderBy(Permissions.SortOrderOf)];
    }

    /// <inheritdoc />
    public Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        db.Roles
            .Include(role => role.Permissions)
            .FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    /// <inheritdoc />
    public Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Roles
            .Include(role => role.Permissions)
            .FirstOrDefaultAsync(role => role.Code == code, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken) =>
        await db.Roles
            .Include(role => role.Permissions)
            .OrderBy(role => role.Rank)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        db.Users.CountAsync(user => user.RoleId == roleId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountUsersByRoleAsync(CancellationToken cancellationToken)
    {
        // Grouped in the database, then folded into a dictionary: a role nobody holds produces no group, and the
        // caller's lookup treats a missing key as zero.
        var counts = await db.Users
            .GroupBy(user => user.RoleId)
            .Select(group => new { RoleId = group.Key, Holders = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return counts.ToDictionary(row => row.RoleId, row => row.Holders);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Permission>> ListPermissionCatalogAsync(CancellationToken cancellationToken) =>
        await db.PermissionCatalog
            .OrderBy(permission => permission.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public void AddRole(Role role) => db.Roles.Add(role);

    /// <inheritdoc />
    public void RemoveRole(Role role) => db.Roles.Remove(role);

    /// <inheritdoc />
    public async Task<AccountPurge> PurgeAccountAsync(User user, CancellationToken cancellationToken)
    {
        // Soft-deleted enclosures are included: their rows still hold the RESTRICT foreign key, so leaving them
        // behind would make the account's own delete fail in the database instead of succeeding here.
        var terrariums = await db.Terrariums
            .IgnoreQueryFilters()
            .Where(row => row.UserId == user.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var terrariumIds = terrariums.ConvertAll(row => row.Id);

        var devices = await db.Devices
            .Where(device => device.TerrariumId != null && terrariumIds.Contains(device.TerrariumId.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var device in devices)
        {
            // Unbound, not revoked, and pointed at no enclosure: the same state the keeper's own delete leaves a
            // board in. The board itself survives, because a deployment that lost a piece of hardware every time an
            // account went away would have no way to claim it again.
            device.TerrariumId = null;
            device.Status = DeviceStatus.Provisioning;
            device.ProvisionedAt = null;
            device.ClaimCode = null;
            device.ClaimCodeExpiresAt = null;
        }

        var profiles = await db.SpeciesProfiles
            .Where(profile => profile.CreatedByUserId == user.Id && !profile.IsBuiltIn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var notifications = await db.NotificationLogs
            .Where(row => row.UserId == user.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Staged in dependency order, and committed as one unit: the enclosures before the profiles they name, and
        // the account last. A failure anywhere leaves the account exactly as it was.
        db.Terrariums.RemoveRange(terrariums);
        db.SpeciesProfiles.RemoveRange(profiles);
        db.NotificationLogs.RemoveRange(notifications);
        db.Users.Remove(user);

        return new AccountPurge(terrariums.Count, devices.Count, profiles.Count, notifications.Count);
    }

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <summary>
    /// The list's filter, so paging and its count cannot disagree about which accounts are in the set. The search is
    /// a <c>LIKE</c> on the two identifiers an administrator would search by; SQL Server's default collation makes it
    /// case-insensitive, which is what BR-01.1 requires of the identifiers themselves.
    /// </summary>
    /// <remarks>
    /// The role and its grants are loaded here as well as by the single-account reads, because the account summary
    /// answers "what can this account do" from the navigation: without the second include the list would report an
    /// empty permission set for every row, which reads as "this account can do nothing" rather than as a query that
    /// forgot a join.
    /// </remarks>
    private IQueryable<User> Filtered(string? search) =>
        Accounts(
            string.IsNullOrWhiteSpace(search)
                ? db.Users
                : db.Users.Where(user =>
                    user.Username.Contains(search.Trim()) || user.Email.Contains(search.Trim())));

    /// <summary>An account query with the role and its grants, which every describe path reads.</summary>
    /// <param name="query">Query to load the navigation for.</param>
    private static IQueryable<User> Accounts(IQueryable<User> query) =>
        query.Include(user => user.Role).ThenInclude(role => role!.Permissions);

    /// <inheritdoc />
    public Task<RefreshToken?> FindRefreshTokenAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    /// <inheritdoc />
    public void AddRefreshToken(RefreshToken token) => db.RefreshTokens.Add(token);

    /// <inheritdoc />
    public async Task RevokeTokenFamilyAsync(Guid familyId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var live = await db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.RevokedAt = nowUtc;
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllUserTokensAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var live = await db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.RevokedAt = nowUtc;
        }
    }

    /// <inheritdoc />
    public async Task RevokeUserTokensExceptFamilyAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var live = await db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null && token.FamilyId != familyId)
            .ToListAsync(cancellationToken);

        foreach (var token in live)
        {
            token.RevokedAt = nowUtc;
        }
    }

    /// <inheritdoc />
    public void AddPasswordResetCode(PasswordResetCode code) => db.PasswordResetCodes.Add(code);

    /// <inheritdoc />
    public Task<PasswordResetCode?> FindPasswordResetCodeAsync(
        byte[] codeHash,
        CancellationToken cancellationToken) =>
        db.PasswordResetCodes.FirstOrDefaultAsync(code => code.CodeHash == codeHash, cancellationToken);

    /// <inheritdoc />
    public async Task InvalidateOutstandingResetCodesAsync(
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var outstanding = await db.PasswordResetCodes
            .Where(code => code.UserId == userId && code.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var code in outstanding)
        {
            code.ConsumedAt = nowUtc;
        }
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
