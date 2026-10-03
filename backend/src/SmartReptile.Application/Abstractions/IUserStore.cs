using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Abstractions;

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

    /// <summary>True when either identifier is already taken. Used only to decide whether to create the row.</summary>
    Task<bool> UserExistsAsync(string username, string email, CancellationToken cancellationToken);

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

    /// <summary>Revokes every session of one account — used by a password change (§02-design/06 §2).</summary>
    Task RevokeAllUserTokensAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Commits staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
