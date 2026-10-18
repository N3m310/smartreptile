using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
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

    public DateTimeOffset InZone(DateTimeOffset instantUtc, string timeZoneId) => instantUtc;

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
    /// <summary>The permission sets the service was asked to mint, so a test can assert what the token carried.</summary>
    public List<IReadOnlyCollection<string>> Issued { get; } = [];

    public AccessToken Issue(User user, IReadOnlyCollection<string> permissions)
    {
        Issued.Add(permissions);

        return new AccessToken($"access-token-for-{user.Id:N}", clock.UtcNow.AddMinutes(15));
    }
}

internal sealed class FakeSecretGenerator : ISecretGenerator
{
    private int _issued;

    public string NewOpaqueToken() => $"refresh-token-{Interlocked.Increment(ref _issued)}";

    public byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}

/// <summary>Deterministic recovery codes, so a test can name the code it expects to present.</summary>
internal sealed class FakeRecoveryCodeGenerator : IRecoveryCodeGenerator
{
    private int _issued;

    public string Generate()
    {
        var sequence = Interlocked.Increment(ref _issued);
        var code = new char[RecoveryCode.DefaultLength];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = RecoveryCode.DefaultAlphabet[(sequence + index) % RecoveryCode.DefaultAlphabet.Length];
        }

        return new string(code);
    }
}

/// <summary>
/// Captures what the delivery channel was handed. <see cref="FailNextSend"/> makes the notifier throw, which is how
/// the tests prove a delivery failure cannot change the answer the caller gets.
/// </summary>
internal sealed class FakePasswordResetNotifier : IPasswordResetNotifier
{
    private readonly List<PasswordResetDelivery> _deliveries = [];

    public IReadOnlyList<PasswordResetDelivery> Deliveries => _deliveries;

    public PasswordResetDelivery? Last => _deliveries.Count == 0 ? null : _deliveries[^1];

    public bool FailNextSend { get; set; }

    public Task NotifyAsync(PasswordResetDelivery delivery, CancellationToken cancellationToken)
    {
        if (FailNextSend)
        {
            throw new InvalidOperationException("delivery channel unavailable");
        }

        _deliveries.Add(delivery);
        return Task.CompletedTask;
    }
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
    private readonly List<PasswordResetCode> _resetCodes = [];
    private readonly List<AuditLog> _auditEntries = [];

    /// <summary>
    /// The four system roles, built from the same seed the seeder writes, so a test that assigns
    /// <c>SystemRoles.OwnerId</c> gets the permissions the real deployment would grant.
    /// </summary>
    private readonly List<Role> _roles =
    [
        .. SystemRoles.Seed.Select(seed => new Role
        {
            Id = seed.Id,
            Code = seed.Code,
            Name = seed.Name,
            Description = seed.Description,
            IsSystem = true,
            Rank = seed.Rank,
            Permissions = [.. seed.Permissions.Select(code => new RolePermission { RoleId = seed.Id, PermissionCode = code })],
        }),
    ];

    private readonly List<Permission> _catalog =
    [
        .. Permissions.All.Select((definition, index) => new Permission
        {
            Code = definition.Code,
            Group = definition.Group,
            Name = definition.Name,
            Description = definition.Description,
            CrossesAccounts = definition.CrossesAccounts,
            SortOrder = index,
        }),
    ];

    public IReadOnlyList<User> Users => _users;

    public IReadOnlyList<RefreshToken> Tokens => _tokens;

    public IReadOnlyList<PasswordResetCode> ResetCodes => _resetCodes;

    /// <summary>The roles the fake holds, so a test can add a custom one.</summary>
    public IReadOnlyList<Role> Roles => _roles;

    /// <summary>Accounts a hard delete removed, so the test can assert the row went.</summary>
    public IReadOnlyList<User> RemovedUsers => _removedUsers;

    /// <summary>
    /// What <see cref="FindFootprintAsync"/> answers. Injected rather than computed, because a hard delete's guard
    /// is about rows this fake has no reason to model.
    /// </summary>
    public AccountFootprint Footprint { get; set; } = new(0, 0, 0, 0, 0);

    public int SaveCount { get; private set; }

    private readonly List<User> _removedUsers = [];

    public Task<User?> FindUserAsync(string usernameOrEmail, CancellationToken cancellationToken) =>
        Task.FromResult(Attached(_users.FirstOrDefault(
            user => user.Username == usernameOrEmail || user.Email == usernameOrEmail)));

    public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Attached(_users.FirstOrDefault(user => user.Id == userId)));

    /// <summary>Mimics the adapter's <c>Include(user =&gt; user.Role)</c>, which every real read performs.</summary>
    private User? Attached(User? user)
    {
        if (user is not null && user.Role is null)
        {
            user.Role = _roles.FirstOrDefault(role => role.Id == user.RoleId);
        }

        return user;
    }

    public Task<TakenIdentifiers> FindTakenIdentifiersAsync(
        string username,
        string email,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TakenIdentifiers(
            _users.Any(user => user.Username == username),
            _users.Any(user => user.Email == email)));

    public void AddUser(User user) => _users.Add(user);

    public void RemoveUser(User user)
    {
        _users.Remove(user);
        _removedUsers.Add(user);
    }

    /// <summary>
    /// The administrator's list, filtered the way the adapter filters: a case-insensitive match on either
    /// identifier, ordered by username, then paged in memory.
    /// </summary>
    public Task<IReadOnlyList<User>> ListAsync(
        string? search,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(
            [.. Filtered(search).OrderBy(user => user.Username).Skip(skip).Take(take).Select(user => Attached(user)!)]);

    public Task<int> CountAsync(string? search, CancellationToken cancellationToken) =>
        Task.FromResult(Filtered(search).Count());

    public Task<int> CountUsersWithPermissionAsync(
        string permissionCode,
        Guid? excludingUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(_users.Count(user =>
            user.DisabledAt is null
            && (excludingUserId is null || user.Id != excludingUserId)
            && Grants(user, permissionCode)));

    /// <summary>True when the role the account holds grants the code.</summary>
    private bool Grants(User user, string permissionCode) =>
        (_roles.FirstOrDefault(role => role.Id == user.RoleId)
            ?? user.Role)?.Permissions.Any(grant => grant.PermissionCode == permissionCode) ?? false;

    public Task<IReadOnlyList<string>> ListPermissionsAsync(Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. (_roles.FirstOrDefault(role => role.Id == roleId)?.Permissions ?? [])
                .Select(grant => grant.PermissionCode)
                .OrderBy(Permissions.SortOrderOf)]);

    public Task<Role?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.FirstOrDefault(role => role.Id == roleId));

    public Task<Role?> FindRoleByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.FirstOrDefault(
            role => string.Equals(role.Code, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. _roles.OrderBy(role => role.Rank).ThenBy(role => role.Name)]);

    public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Count(user => user.RoleId == roleId));

    public Task<IReadOnlyDictionary<Guid, int>> CountUsersByRoleAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(
            _users.GroupBy(user => user.RoleId).ToDictionary(group => group.Key, group => group.Count()));

    public Task<IReadOnlyList<Permission>> ListPermissionCatalogAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Permission>>([.. _catalog.OrderBy(permission => permission.SortOrder)]);

    public void AddRole(Role role) => _roles.Add(role);

    public void RemoveRole(Role role) => _roles.Remove(role);

    public Task<AccountFootprint> FindFootprintAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Footprint);

    /// <summary>Audit rows staged for the next save, in order (BR-18.4).</summary>
    public IReadOnlyList<AuditLog> AuditEntries => _auditEntries;

    public void AddAuditEntry(AuditLog entry) => _auditEntries.Add(entry);

    private IEnumerable<User> Filtered(string? search) =>
        string.IsNullOrWhiteSpace(search)
            ? _users
            : _users.Where(user =>
                user.Username.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || user.Email.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

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

    public Task RevokeUserTokensExceptFamilyAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var token in _tokens.Where(
                     token => token.UserId == userId && token.RevokedAt is null && token.FamilyId != familyId))
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

    public void AddPasswordResetCode(PasswordResetCode code) => _resetCodes.Add(code);

    public Task<PasswordResetCode?> FindPasswordResetCodeAsync(
        byte[] codeHash,
        CancellationToken cancellationToken) =>
        Task.FromResult(_resetCodes.FirstOrDefault(code => code.CodeHash.SequenceEqual(codeHash)));

    public Task InvalidateOutstandingResetCodesAsync(
        Guid userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        foreach (var code in _resetCodes.Where(code => code.UserId == userId && code.ConsumedAt is null))
        {
            code.ConsumedAt = nowUtc;
        }

        return Task.CompletedTask;
    }
}
