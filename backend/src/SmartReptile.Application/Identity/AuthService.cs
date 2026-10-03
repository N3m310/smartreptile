using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>
/// The FR-01 use cases: register, log in, refresh, log out, read the profile, change the password.
/// </summary>
/// <remarks>
/// Two properties are deliberate and load-bearing:
/// <list type="bullet">
/// <item><b>Failures do not disclose whether an account exists.</b> Login answers the same way for an unknown
/// identifier and a wrong password, registration answers the same way for a free and a taken identifier, and
/// login pays the hashing cost even when there is no account to compare against (§02-design/06 §2).</item>
/// <item><b>Refresh is single-use.</b> A rotated token is consumed, and presenting a consumed one is treated as
/// theft: the whole rotation family is revoked (TC-U-34).</item>
/// </list>
/// </remarks>
public sealed class AuthService(
    IUserStore store,
    IPasswordHasher passwordHasher,
    IAccessTokenService accessTokens,
    ISecretGenerator secrets,
    ILoginThrottleStore throttle,
    IClock clock,
    AuthSettings settings)
{
    /// <summary>
    /// Creates an account. Issues no session: login is the only path that does (§02-design/06 §2), which keeps
    /// "who is signed in" with a single implementation.
    /// </summary>
    /// <returns>Success without a session, whether or not the account was created.</returns>
    public async Task<AuthOutcome> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var violations = new List<IdentityViolation>();
        violations.AddRange(AccountIdentifierPolicy.ValidateUsername(request.Username));
        violations.AddRange(AccountIdentifierPolicy.ValidateEmail(request.Email));
        violations.AddRange(PasswordPolicy.Validate(request.Password));

        if (violations.Count > 0)
        {
            return AuthOutcome.Invalid("registration_invalid", "The account details were refused.", violations);
        }

        var username = AccountIdentifierPolicy.NormaliseUsername(request.Username);
        var email = AccountIdentifierPolicy.NormaliseEmail(request.Email);

        // A taken identifier is not disclosed: the caller gets the same answer as for a free one and simply
        // cannot log in with it (§02-design/06 §2).
        if (await store.UserExistsAsync(username, email, cancellationToken))
        {
            return AuthOutcome.Success();
        }

        var hashed = passwordHasher.Hash(request.Password!);

        store.AddUser(new User
        {
            Username = username,
            Email = email,
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            PasswordIterations = hashed.Iterations,
            Role = UserRole.Owner,
            PreferredLanguage = NormaliseLanguage(request.PreferredLanguage),
        });

        await store.SaveChangesAsync(cancellationToken);
        return AuthOutcome.Success();
    }

    /// <summary>
    /// Verifies credentials, throttles repeated failures and issues a session.
    /// </summary>
    /// <param name="request">Credentials.</param>
    /// <param name="ipAddress">Caller address, for the per-address failure window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AuthOutcome> LoginAsync(
        LoginRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var identifier = AccountIdentifierPolicy.NormaliseUsername(request.UsernameOrEmail);
        var now = clock.UtcNow;

        var window = throttle.GetWindow(
            identifier,
            ipAddress,
            now.AddMinutes(-LoginThrottlePolicy.UsernameWindowMinutes));

        var decision = LoginThrottlePolicy.Evaluate(window.UsernameFailures, window.IpFailures);

        if (decision.IsLocked)
        {
            return AuthOutcome.Failure(decision.Code!, "Too many failed attempts. Try again in a few minutes.");
        }

        var user = await store.FindUserAsync(identifier, cancellationToken);

        var stored = user is null
            ? DummyHash()
            : new PasswordHash(user.PasswordHash, user.PasswordSalt, user.PasswordIterations);

        var passwordMatches = passwordHasher.Verify(request.Password ?? string.Empty, stored);

        if (user is null || user.DisabledAt is not null || !passwordMatches)
        {
            throttle.RecordFailure(identifier, ipAddress, now);
            return AuthOutcome.Failure("invalid_credentials", "Incorrect username or password.");
        }

        // Raising the PBKDF2 cost later must not require a mass reset, so a stale hash is upgraded in place
        // on the login that proves the password (TC-U-32).
        if (passwordHasher.NeedsRehash(user.PasswordIterations))
        {
            var upgraded = passwordHasher.Hash(request.Password!);
            user.PasswordHash = upgraded.Hash;
            user.PasswordSalt = upgraded.Salt;
            user.PasswordIterations = upgraded.Iterations;
        }

        user.LastLoginAt = now;
        throttle.ResetUsername(identifier);

        return await IssueSessionAsync(user, request.DeviceInfo, familyId: null, cancellationToken);
    }

    /// <summary>
    /// Exchanges a refresh token for a new session, consuming it. Presenting a token that was already consumed
    /// revokes the whole family, because that can only happen if the token was copied.
    /// </summary>
    public async Task<AuthOutcome> RefreshAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthOutcome.Failure("refresh_token_invalid", SessionEndedMessage);
        }

        var now = clock.UtcNow;
        var token = await store.FindRefreshTokenAsync(secrets.Sha256(refreshToken), cancellationToken);

        if (token is null || token.RevokedAt is not null)
        {
            return AuthOutcome.Failure("refresh_token_invalid", SessionEndedMessage);
        }

        if (token.ConsumedAt is not null)
        {
            await store.RevokeTokenFamilyAsync(token.FamilyId, now, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);

            return AuthOutcome.Failure(
                "token_reused",
                "This session was ended because a refresh token was presented twice. Sign in again.");
        }

        if (!token.IsUsableAt(now))
        {
            return AuthOutcome.Failure("refresh_token_invalid", SessionEndedMessage);
        }

        var user = await store.FindUserByIdAsync(token.UserId, cancellationToken);

        if (user is null || user.DisabledAt is not null)
        {
            return AuthOutcome.Failure("refresh_token_invalid", SessionEndedMessage);
        }

        token.ConsumedAt = now;
        return await IssueSessionAsync(user, token.DeviceInfo, token.FamilyId, cancellationToken);
    }

    /// <summary>
    /// Ends the session that owns the supplied refresh token. Idempotent: logging out twice, or with a token
    /// that never existed, is not an error.
    /// </summary>
    public async Task<AuthOutcome> LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var token = await store.FindRefreshTokenAsync(secrets.Sha256(refreshToken), cancellationToken);

            if (token is not null && token.RevokedAt is null)
            {
                token.RevokedAt = clock.UtcNow;
                await store.SaveChangesAsync(cancellationToken);
            }
        }

        return AuthOutcome.Success();
    }

    /// <summary>
    /// Changes the password of the signed-in account, after re-proving the current one. Ends every session.
    /// </summary>
    public async Task<AuthOutcome> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return AuthOutcome.Failure("invalid_credentials", "Incorrect current password.");
        }

        var current = new PasswordHash(user.PasswordHash, user.PasswordSalt, user.PasswordIterations);

        if (!passwordHasher.Verify(request.CurrentPassword ?? string.Empty, current))
        {
            return AuthOutcome.Failure("invalid_credentials", "Incorrect current password.");
        }

        var violations = PasswordPolicy.Validate(request.NewPassword);

        if (violations.Count > 0)
        {
            return AuthOutcome.Invalid("password_policy_violation", "The new password was refused.", violations);
        }

        var hashed = passwordHasher.Hash(request.NewPassword!);
        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;
        user.PasswordIterations = hashed.Iterations;

        await store.RevokeAllUserTokensAsync(user.Id, clock.UtcNow, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success();
    }

    /// <summary>The signed-in account, or <c>null</c> when it no longer exists.</summary>
    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);
        return user is null ? null : ToProfile(user);
    }

    private const string SessionEndedMessage = "The session is no longer valid. Sign in again.";

    /// <summary>Lazily built hash of a fixed non-password, used to equalise the login failure path's cost.</summary>
    private PasswordHash? _dummyHash;

    private PasswordHash DummyHash() => _dummyHash ??= passwordHasher.Hash("smartreptile-timing-equaliser");

    private async Task<AuthOutcome> IssueSessionAsync(
        User user,
        string? deviceInfo,
        Guid? familyId,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var access = accessTokens.Issue(user);
        var plaintext = secrets.NewOpaqueToken();
        var expiresAt = now.AddDays(settings.RefreshTokenDays);

        store.AddRefreshToken(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = secrets.Sha256(plaintext),
            FamilyId = familyId ?? Guid.NewGuid(),
            IssuedAt = now,
            ExpiresAt = expiresAt,
            DeviceInfo = deviceInfo,
        });

        await store.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success(new AuthSession(ToProfile(user), access.Value, access.ExpiresAtUtc, plaintext, expiresAt));
    }

    private static UserProfile ToProfile(User user) =>
        new(user.Id, user.Username, user.Email, user.Role.ToString(), user.PreferredLanguage, user.TimeZoneId);

    private static string NormaliseLanguage(string? language) =>
        string.Equals(language?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";
}
