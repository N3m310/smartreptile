using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
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
        db.Users.FirstOrDefaultAsync(
            user => user.Username == usernameOrEmail || user.Email == usernameOrEmail,
            cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> UserExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(
            user => user.Username == username || user.Email == email,
            cancellationToken);

    /// <inheritdoc />
    public void AddUser(User user) => db.Users.Add(user);

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
