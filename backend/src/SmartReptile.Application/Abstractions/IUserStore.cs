using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Abstractions;

/// <summary>Which registration identifiers already belong to an account.</summary>
/// <param name="Username">True when the username is taken.</param>
/// <param name="Email">True when the email address is taken.</param>
public readonly record struct TakenIdentifiers(bool Username, bool Email)
{
    /// <summary>True when either identifier is taken.</summary>
    public bool Any => Username || Email;
}

/// <summary>
/// What a hard delete removed, so the audit row can say what went with the account rather than only that it went.
/// </summary>
/// <param name="Terrariums">Enclosures deleted, soft-deleted ones included.</param>
/// <param name="Devices">Boards unbound from those enclosures; the boards themselves survive.</param>
/// <param name="SpeciesProfiles">Catalogue profiles the account had authored; built-ins are never among them.</param>
/// <param name="Notifications">Rows removed from the account's inbox.</param>
public readonly record struct AccountPurge(int Terrariums, int Devices, int SpeciesProfiles, int Notifications);

/// <summary>
/// Persistence port for accounts and sessions. A port rather than a <c>DbContext</c> because the Application
/// project deliberately has no EF Core reference (§02-design/01 §4): the use cases must be testable without a
/// database, and the query shapes here are small and fixed.
/// </summary>
public interface IUserStore
{
    /// <summary>Finds an account by username or email, both compared in their stored (lower-case) form.</summary>
    Task<User?> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken);

    /// <summary>Finds an account by id.</summary>
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Which of the two registration identifiers is already in use. Two flags rather than one boolean because
    /// registration answers with the field to fix (ADR-020): "one of these is taken" is a worse answer than
    /// "the email is", and the caller typed both.
    /// </summary>
    Task<TakenIdentifiers> FindTakenIdentifiersAsync(string username, string email, CancellationToken cancellationToken);

    /// <summary>Stages a new account.</summary>
    void AddUser(User user);

    /// <summary>
    /// A page of accounts, newest last, with an optional case-insensitive search over username and email — the
    /// administrator's list (TASKS 2026-10-14, item 12).
    /// </summary>
    /// <param name="search">Free text matched against username and email, or null for every account.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<User>> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>How many accounts match the same search, so a client can page without guessing.</summary>
    /// <param name="search">Free text matched against username and email, or null for every account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountAsync(string? search, CancellationToken cancellationToken);

    /// <summary>
    /// How many enabled accounts hold a permission, through whatever role grants it. Read before a role change, a
    /// disable or a delete, and before a matrix edit that would remove it, because the last account able to manage
    /// accounts is the one change that can lock everybody out for good (TASKS 2026-10-14, item 12).
    /// </summary>
    /// <param name="permissionCode">Permission to count holders of.</param>
    /// <param name="excludingUserId">Account to leave out of the count, when the caller is the one being changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUsersWithPermissionAsync(
        string permissionCode,
        Guid? excludingUserId,
        CancellationToken cancellationToken);

    /// <summary>The permission codes a role grants, in catalogue order. Empty when the role has none.</summary>
    /// <param name="roleId">Role to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<string>> ListPermissionsAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>A role with its grants, or null.</summary>
    /// <param name="roleId">Role to find.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>A role by its code, compared case-insensitively, or null.</summary>
    /// <param name="code">Code to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Every role with its grants, in display order — the console's matrix.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken);

    /// <summary>How many accounts hold a role, so a delete can refuse rather than orphan them.</summary>
    /// <param name="roleId">Role to count.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// How many accounts hold each role, in one read.
    /// </summary>
    /// <remarks>
    /// The list needs the figure for every role at once, and asking once per role would make the console's matrix a
    /// query per row. A role nobody holds is absent from the answer rather than zero, which is what the dictionary's
    /// <c>GetValueOrDefault</c> is for.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<Guid, int>> CountUsersByRoleAsync(CancellationToken cancellationToken);

    /// <summary>The whole permission catalogue, in display order.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Permission>> ListPermissionCatalogAsync(CancellationToken cancellationToken);

    /// <summary>Stages a new role.</summary>
    /// <param name="role">Role to add, with its grants already attached.</param>
    void AddRole(Role role);

    /// <summary>Stages a role for deletion.</summary>
    /// <param name="role">Role to remove.</param>
    void RemoveRole(Role role);

    /// <summary>
    /// Stages the ordered hard delete of an account and of everything that would otherwise outlive it: its boards are
    /// unbound, its enclosures are removed with everything cascading from them, its own catalogue profiles and its
    /// inbox are removed, and finally the account row.
    /// </summary>
    /// <remarks>
    /// One unit of work, because the order is the point. <c>Terrarium.UserId</c> is <c>RESTRICT</c>, so an account
    /// cannot be deleted while it owns an enclosure; unbinding first is also what keeps a board from pointing at a
    /// row nobody can load. Soft-deleted enclosures are included: their rows still hold that foreign key, so leaving
    /// them would make the delete fail in the database rather than here.
    /// </remarks>
    /// <param name="user">Account to erase.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AccountPurge> PurgeAccountAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Stages an audit row. It lives on this port rather than one of its own so the row is committed by the same
    /// <see cref="SaveChangesAsync"/> as the change it describes (BR-18.4): an audited change that commits without
    /// its entry is worse than no trail at all, because the gap reads as "nothing happened".
    /// </summary>
    void AddAuditEntry(Domain.Auditing.AuditLog entry);

    /// <summary>Finds a session by the hash of its refresh token.</summary>
    Task<RefreshToken?> FindRefreshTokenAsync(byte[] tokenHash, CancellationToken cancellationToken);

    /// <summary>Stages a new refresh token.</summary>
    void AddRefreshToken(RefreshToken token);

    /// <summary>
    /// Revokes every token in a rotation family. Called when a consumed token is presented again: the reuse is
    /// treated as theft, and the whole family dies (TC-U-34).
    /// </summary>
    Task RevokeTokenFamilyAsync(Guid familyId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Revokes every session of one account — used when a caller cannot name one to keep (§02-design/06 §2).</summary>
    Task RevokeAllUserTokensAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every session of one account except the rotation family named. A password change evicts the
    /// sessions a keeper no longer trusts while leaving them signed in on the device they just used, because that
    /// caller has re-proved the credential the change is about.
    /// </summary>
    Task RevokeUserTokensExceptFamilyAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Stages a password-reset code.</summary>
    void AddPasswordResetCode(PasswordResetCode code);

    /// <summary>
    /// Finds a reset code by the SHA-256 of the presented value. Unsalted by design, which is what makes this a
    /// lookup rather than a scan of every outstanding code (see <see cref="PasswordResetCode"/>).
    /// </summary>
    Task<PasswordResetCode?> FindPasswordResetCodeAsync(byte[] codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// Consumes every outstanding reset code of an account. Called when a new code is issued and when one is
    /// spent, so at most one code is ever live for an account — a second request replaces the first rather than
    /// leaving two working ways in.
    /// </summary>
    Task InvalidateOutstandingResetCodesAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Commits staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
