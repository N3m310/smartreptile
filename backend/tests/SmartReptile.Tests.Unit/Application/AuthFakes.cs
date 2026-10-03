using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Application;

/// <summary>
/// Fakes for every port the authentication use cases use. The rule from §04-quality/01 §4 applies: no socket, no
/// database, no real PBKDF2 — a unit test must not pay 210 000 iterations to find out whether a branch was taken.
/// </summary>
internal sealed class TestClock : IClock
{
    /// <summary>A fixed instant, so nothing in these tests depends on when the suite runs.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset NowIn(string timeZoneId) => UtcNow;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>Cheap stand-in for PBKDF2 that still depends on the salt and the iteration count.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public const int TargetIterations = PasswordPolicy.TargetIterations;

    private static readonly byte[] FixedSalt = new byte[16];

    public PasswordHash Hash(string password) => Derive(password, FixedSalt, TargetIterations);

    public bool Verify(string password, PasswordHash stored) =>
        stored.Hash.Length > 0
        && Derive(password, stored.Salt, stored.Iterations).Hash.SequenceEqual(stored.Hash);

    public bool NeedsRehash(int iterations) => iterations < TargetIterations;

    /// <summary>Produces a hash at a chosen cost, so a stale row can be staged without waiting for a real one.</summary>
    public static PasswordHash Derive(string password, byte[] salt, int iterations) =>
        new(SHA256.HashData([.. Encoding.UTF8.GetBytes(password), .. salt, .. BitConverter.GetBytes(iterations)]), salt, iterations);

    /// <summary>Creates a stored hash as an older release would have, for the rehash-on-login case (TC-U-32).</summary>
    public static PasswordHash DeriveAtCost(string password, int iterations) => Derive(password, FixedSalt, iterations);
}

internal sealed class FakeAccessTokenService(IClock clock) : IAccessTokenService
{
    public AccessToken Issue(User user) =>
        new($"access-token-for-{user.Id:N}", clock.UtcNow.AddMinutes(15));
}

internal sealed class FakeSecretGenerator : ISecretGenerator
{
    private int _issued;

    public string NewOpaqueToken() => $"refresh-token-{Interlocked.Increment(ref _issued)}";

    public byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

/// <summary>In-memory failed-login counters with the same window semantics as the real store.</summary>
internal sealed class FakeLoginThrottleStore : ILoginThrottleStore
{
    private readonly List<(string Key, DateTimeOffset At)> _byIdentifier = [];
    private readonly List<(string Key, DateTimeOffset At)> _byAddress = [];

    public LoginFailureWindow GetWindow(string usernameOrEmail, string ipAddress, DateTimeOffset sinceUtc) =>
        new(
            _byIdentifier.Count(entry => entry.Key == Normalise(usernameOrEmail) && entry.At >= sinceUtc),
            _byAddress.Count(entry => entry.Key == Normalise(ipAddress) && entry.At >= sinceUtc));

    public void RecordFailure(string usernameOrEmail, string ipAddress, DateTimeOffset nowUtc)
    {
        _byIdentifier.Add((Normalise(usernameOrEmail), nowUtc));
        _byAddress.Add((Normalise(ipAddress), nowUtc));
    }

    public void ResetUsername(string usernameOrEmail) =>
        _byIdentifier.RemoveAll(entry => entry.Key == Normalise(usernameOrEmail));

    private static string Normalise(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}

/// <summary>
/// In-memory <see cref="IUserStore"/>. Returns the same object instances the real context would track, so a use
/// case that mutates a loaded entity is observable afterwards — which is what the rehash and rotation tests check.
/// </summary>
internal sealed class FakeUserStore : IUserStore
{
    private readonly List<User> _users = [];
    private readonly List<RefreshToken> _tokens = [];

    public IReadOnlyList<User> Users => _users;

    public IReadOnlyList<RefreshToken> Tokens => _tokens;

    public int SaveCount { get; private set; }

    public Task<User?> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(
            user => user.Username == usernameOrEmail || user.Email == usernameOrEmail));

    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.FirstOrDefault(user => user.Id == userId));

    public Task<bool> UserExistsAsync(string username, string email, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Any(user => user.Username == username || user.Email == email));

    public void AddUser(User user) => _users.Add(user);

    public Task<RefreshToken?> FindRefreshTokenAsync(byte[] tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(_tokens.FirstOrDefault(token => token.TokenHash.SequenceEqual(tokenHash)));

    public void AddRefreshToken(RefreshToken token) => _tokens.Add(token);

    public Task RevokeTokenFamilyAsync(Guid familyId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        foreach (var token in _tokens.Where(token => token.FamilyId == familyId && token.RevokedAt is null))
        {
            token.RevokedAt = nowUtc;
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllUserTokensAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        foreach (var token in _tokens.Where(token => token.UserId == userId && token.RevokedAt is null))
        {
            token.RevokedAt = nowUtc;
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
