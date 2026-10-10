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
