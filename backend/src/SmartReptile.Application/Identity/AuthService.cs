using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>
/// The FR-01 use cases: register, log in, refresh, log out, read the profile, change the password, and recover a
/// forgotten one with the account's backup code.
/// </summary>
/// <remarks>
/// Two properties are deliberate and load-bearing:
/// <list type="bullet">
/// <item><b>Failures disclose as little as they can.</b> Login answers the same way for an unknown identifier and a
/// wrong password — and pays the hashing cost even when there is no account to compare against — and recovery answers
/// the same way for an unknown identifier and a wrong code. Registration and <c>forgot-password</c> are the two
/// deliberate exceptions (ADR-020): both are about an identifier the caller typed themselves, so both say so when it
/// is unknown (§02-design/06 §2).</item>
/// <item><b>Refresh is single-use.</b> A rotated token is consumed, and presenting a consumed one is treated as
/// theft: the whole rotation family is revoked (TC-U-34).</item>
/// </list>
/// </remarks>
public sealed class AuthService(
    IUserStore store,
    IPasswordHasher passwordHasher,
    IAccessTokenService accessTokens,
    ISecretGenerator secrets,
    ISecretHasher secretHasher,
    IRecoveryCodeGenerator recoveryCodes,
    IPasswordResetNotifier notifier,
    ILoginThrottleStore throttle,
    IClock clock,
    AuthSettings settings)
{
    /// <summary>
    /// Creates an account and issues its backup recovery code. Issues no session: login is the only path that
    /// does (§02-design/06 §2), which keeps "who is signed in" with a single implementation.
    /// </summary>
    /// <returns>
    /// A session-less success plus the new account's recovery code, or <c>registration_conflict</c> with the
    /// identifier that is already in use. Registration is the one credential path that discloses (ADR-020): the
    /// caller typed these values themselves, and answering "accepted" for an address that was never registered is
    /// a lie whose cost is a keeper hunting for an account that does not exist.
    /// </returns>
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
        var taken = await store.FindTakenIdentifiersAsync(username, email, cancellationToken);

        if (taken.Any)
        {
            var conflicts = new List<IdentityViolation>();

            if (taken.Username)
            {
                conflicts.Add(new IdentityViolation(
                    "username", "username_taken", "That username already has an account."));
            }

            if (taken.Email)
            {
                conflicts.Add(new IdentityViolation(
                    "email", "email_taken", "That email address already has an account."));
            }

            return AuthOutcome.Invalid(
                "registration_conflict", "Those account details are already in use.", conflicts);
        }

        var recoveryCode = recoveryCodes.Generate();
        var hashed = passwordHasher.Hash(request.Password!);
        var recoveryHash = secretHasher.Hash(recoveryCode);

        store.AddUser(new User
        {
            Username = username,
            Email = email,
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            PasswordIterations = hashed.Iterations,
            RecoveryCodeHash = recoveryHash.Hash,
            RecoveryCodeSalt = recoveryHash.Salt,
            RecoveryCodeIssuedAt = clock.UtcNow,
            Role = UserRole.Owner,
            PreferredLanguage = NormaliseLanguage(request.PreferredLanguage),
        });

        await store.SaveChangesAsync(cancellationToken);
        return AuthOutcome.WithRecoveryCode(recoveryCode);
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
    /// Changes the password of the signed-in account, after re-proving the current one. Ends every session of the
    /// account except the caller's own, which the request names.
    /// </summary>
    /// <remarks>
    /// Ending *every* session is the simpler rule and it is the wrong one here: the caller has just typed the very
    /// credential the change is about, so evicting them buys no security and costs a sign-out — while the sessions a
    /// keeper actually wants gone are the others (the browser left open on a shared machine, the phone that was
    /// lost). A caller who does not name a session, or names one this account does not hold, keeps none: an
    /// unnamed session is not the caller's, and revoking everything is the reading that fails safely.
    /// </remarks>
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

        var survives = await LiveFamilyAsync(user.Id, request.RefreshToken, cancellationToken);

        if (survives is { } familyId)
        {
            await store.RevokeUserTokensExceptFamilyAsync(user.Id, familyId, clock.UtcNow, cancellationToken);
        }
        else
        {
            await store.RevokeAllUserTokensAsync(user.Id, clock.UtcNow, cancellationToken);
        }

        await store.SaveChangesAsync(cancellationToken);

        return AuthOutcome.Success();
    }

    /// <summary>
    /// The rotation family the presented refresh token belongs to, when that token is this account's and still
    /// live. A consumed-but-not-revoked token still counts: rotation keeps the family id, so a client that rotated
    /// its token a moment before changing the password is still the caller of the change.
    /// </summary>
    private async Task<Guid?> LiveFamilyAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var token = await store.FindRefreshTokenAsync(secrets.Sha256(refreshToken), cancellationToken);

        return token is not null && token.UserId == userId && token.RevokedAt is null ? token.FamilyId : null;
    }

    /// <summary>
    /// Replaces a forgotten password using the account's stored backup code (§02-design/06 §2).
    /// </summary>
    /// <remarks>
    /// Consumes the code and issues a replacement, because a single-use code the user could not renew would leave
    /// them exactly one attempt at recovery and then a second, permanent lockout.
    /// <para>
    /// Reached without a session, so it is throttled with the same counters and limits as a failed login
    /// (BR-01.4): guessing a recovery code is guessing a credential, and the lockout is what makes a leaked
    /// identifier useless on its own.
    /// </para>
    /// </remarks>
    public async Task<AuthOutcome> RecoverAsync(
        RecoverRequest request,
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

        // The password policy is checked before the code, so a weak new password is reported as what it is and
        // nobody has to guess whether their code or their password was refused. A policy refusal is not a
        // credential guess and is deliberately not counted against the throttle.
        var violations = PasswordPolicy.Validate(request.NewPassword);

        if (violations.Count > 0)
        {
            return AuthOutcome.Invalid("password_policy_violation", "The new password was refused.", violations);
        }

        var user = await store.FindUserAsync(identifier, cancellationToken);
        var presented = RecoveryCode.Normalise(request.RecoveryCode);

        var codeMatches =
            user is { RecoveryCodeHash: { } storedHash, RecoveryCodeSalt: { } storedSalt }
            && RecoveryCode.IsWellFormed(presented)
            && secretHasher.Verify(presented, storedHash, storedSalt);

        if (user is null || user.DisabledAt is not null || !codeMatches)
        {
            throttle.RecordFailure(identifier, ipAddress, now);

            // An unknown identifier, an account with no code on file and a wrong code answer identically. The
            // shape check above happens first only because a malformed value cannot be a code at all — it reveals
            // nothing about the account, which is the property that matters (BR-02.2).
            return AuthOutcome.Failure("invalid_recovery_code", "That account and recovery code do not match.");
        }

        var replacement = RotateCredentials(user, request.NewPassword!, now);

        // Whoever holds the forgotten password may still hold a live session. Recovering the account ends every
        // one of them, exactly as an authenticated password change does — otherwise "I lost my password" would
        // leave the person who took it signed in.
        await store.RevokeAllUserTokensAsync(user.Id, now, cancellationToken);
        throttle.ResetUsername(identifier);
        await store.SaveChangesAsync(cancellationToken);

        return AuthOutcome.WithRecoveryCode(replacement);
    }

    /// <summary>
    /// Issues a password-reset code and hands it to the delivery channel (BR-01.5).
    /// </summary>
    /// <remarks>
    /// Unlike every other credential path, this one <b>discloses</b> whether the identifier has an account
    /// (ADR-020): it answers <c>identifier_unknown</c> when none does, because the alternative is a keeper waiting
    /// for a code that was never generated. A disabled account answers the same way — it cannot be recovered
    /// either, and a third answer would tell a stranger more, not less.
    /// <para>
    /// The cost is recorded rather than hidden: this endpoint can now be used to ask whether a given address has
    /// an account. Login, recovery, reset, registration's password rules and every terrarium route still do not.
    /// </para>
    /// </remarks>
    public async Task<AuthOutcome> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var identifier = AccountIdentifierPolicy.NormaliseUsername(request.UsernameOrEmail);
        var user = await store.FindUserAsync(identifier, cancellationToken);

        if (user is null || user.DisabledAt is not null)
        {
            return AuthOutcome.Failure("identifier_unknown", "No account uses that identifier.");
        }

        var now = clock.UtcNow;
        var code = recoveryCodes.Generate();
        var expiresAt = now.AddMinutes(settings.PasswordResetCodeMinutes);

        // One live code per account: asking again replaces the previous one rather than leaving two ways in.
        await store.InvalidateOutstandingResetCodesAsync(user.Id, now, cancellationToken);

        store.AddPasswordResetCode(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = secrets.Sha256(code),
            CreatedAt = now,
            ExpiresAt = expiresAt,
            RequestedFromAddress = ipAddress,
        });

        await store.SaveChangesAsync(cancellationToken);

        // After the commit, so a delivery failure can never roll back — or hide — a code that now exists.
        //
        // Guarded even though the port requires implementations not to throw: a 500 here would be indistinguishable
        // from a broken deployment, and the keeper would be told nothing about the code that does exist. The channel
        // is required to log its own failures; the response is not allowed to depend on it.
        try
        {
            await notifier.NotifyAsync(
                new PasswordResetDelivery(user.Username, user.Email, user.PreferredLanguage, code, expiresAt),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Deliberately swallowed. See above: the alternative is an oracle.
        }

        return AuthOutcome.Success();
    }

    /// <summary>
    /// Replaces a forgotten password with a code the server issued (BR-01.5). Same rules as
    /// <see cref="RecoverAsync"/>: throttled like a login, single use, ends every session, and rotates the backup
    /// code so the keeper leaves with a fresh one.
    /// </summary>
    public async Task<AuthOutcome> ResetPasswordAsync(
        ResetPasswordRequest request,
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

        var violations = PasswordPolicy.Validate(request.NewPassword);

        if (violations.Count > 0)
        {
            return AuthOutcome.Invalid("password_policy_violation", "The new password was refused.", violations);
        }

        var user = await store.FindUserAsync(identifier, cancellationToken);
        var presented = RecoveryCode.Normalise(request.ResetCode);

        // Shape first: a value that cannot be a code has no digest worth looking up.
        var code = RecoveryCode.IsWellFormed(presented)
            ? await store.FindPasswordResetCodeAsync(secrets.Sha256(presented), cancellationToken)
            : null;

        if (user is null
            || user.DisabledAt is not null
            || code is null
            || code.UserId != user.Id
            || !code.IsUsableAt(now))
        {
            throttle.RecordFailure(identifier, ipAddress, now);

            // An unknown identifier, a wrong code, a spent code and someone else's code all answer the same way.
            return AuthOutcome.Failure("invalid_reset_code", "That account and reset code do not match.");
        }

        var replacement = RotateCredentials(user, request.NewPassword!, now);

        code.ConsumedAt = now;
        await store.InvalidateOutstandingResetCodesAsync(user.Id, now, cancellationToken);
        await store.RevokeAllUserTokensAsync(user.Id, now, cancellationToken);
        throttle.ResetUsername(identifier);
        await store.SaveChangesAsync(cancellationToken);

        return AuthOutcome.WithRecoveryCode(replacement);
    }

    /// <summary>
    /// Applies a new password and rotates the account's backup recovery code, returning the replacement.
    /// </summary>
    /// <remarks>
    /// Shared by the two unauthenticated flows so "what a password change leaves behind" has one definition: a
    /// fresh password hash at the current cost, and a fresh backup code, because the old one is exactly the sort of
    /// thing that leaks alongside a password — and was available to whoever just held the account.
    /// </remarks>
    private string RotateCredentials(User user, string newPassword, DateTimeOffset now)
    {
        var hashed = passwordHasher.Hash(newPassword);
        user.PasswordHash = hashed.Hash;
        user.PasswordSalt = hashed.Salt;
        user.PasswordIterations = hashed.Iterations;

        var replacement = recoveryCodes.Generate();
        var replacementHash = secretHasher.Hash(replacement);
        user.RecoveryCodeHash = replacementHash.Hash;
        user.RecoveryCodeSalt = replacementHash.Salt;
        user.RecoveryCodeIssuedAt = now;

        return replacement;
    }

    private const string SessionEndedMessage = "The session is no longer valid. Sign in again.";

    /// <summary>The signed-in account, or <c>null</c> when it no longer exists.</summary>
    public async Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);
        return user is null ? null : ToProfile(user);
    }

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
