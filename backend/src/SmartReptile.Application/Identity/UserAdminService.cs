using System.Globalization;
using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>
/// Account management for the system administrator (FR-18): list, read one, create, edit, change a role, reset a
/// password, disable, re-enable, and delete.
/// </summary>
/// <remarks>
/// <b>The guard is one question, asked before every change that could answer it wrongly:</b> afterwards, is there
/// still an enabled account holding <c>user.manage</c>? It is counted by holders of the <em>permission</em> rather
/// than by accounts whose role is named <c>Admin</c>, so a deployment that renamed its administrator role or split
/// it across two is equally protected — and the refusal says why, because the caller is the one account entitled
/// to know.
/// <para>
/// <b>Three changes end the target's sessions.</b> A role change, a password reset and a delete each make the
/// tokens already issued wrong: the first because permissions travel in the token
/// (<c>JwtAccessTokenService</c>), the second because the credential they were obtained with is gone, the third
/// because there is nobody left to be. A disable does the same, which it already did.
/// </para>
/// <para>
/// <b>Two ways to remove an account, and only one of them is always available.</b> A soft delete disables it and
/// keeps every row that names it; a hard delete erases the row and is allowed only when nothing references it (no
/// enclosure, no board, no authored profile). The refusal names what still holds it rather than cascading, because
/// a board whose owner no longer exists is worse than an account that is merely disabled.
/// </para>
/// <para>
/// Every change writes one audit row, in the same unit of work as the change (BR-18.4). The acting account is the
/// administrator; the account acted on is the one in the path.
/// </para>
/// </remarks>
public sealed class UserAdminService(IUserStore store, IPasswordHasher hasher, IClock clock)
{
    /// <summary>Page sizes the route accepts, so a client cannot ask for the whole table in one call.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>Upper bound on one page.</summary>
    public const int MaxPageSize = 100;

    /// <summary>A page of accounts, with the total for the pager.</summary>
    /// <param name="search">Free text matched against username and email, or null.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">Rows per page; clamped to <see cref="MaxPageSize"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> ListAsync(
        string? search,
        int? skip,
        int? take,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize);
        var offset = Math.Max(0, skip ?? 0);

        var users = await store.ListAsync(search, offset, pageSize, cancellationToken);
        var total = await store.CountAsync(search, cancellationToken);

        return UserOutcome.Listed(new UserPage([.. users.Select(Describe)], total, offset, pageSize));
    }

    /// <summary>One account, or <c>user_not_found</c>.</summary>
    /// <param name="userId">Account to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        return user is null
            ? UserOutcome.Failure("user_not_found", "No such account.")
            : UserOutcome.Found(Describe(user));
    }

    /// <summary>
    /// Creates an account with the password the administrator supplies and the role they choose.
    /// </summary>
    /// <param name="request">Identifiers, first password, role and language.</param>
    /// <param name="actorUserId">The administrator creating it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> CreateAsync(
        CreateUserRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = Normalise(request.Username);
        var email = Normalise(request.Email);
        var violations = new List<IdentityViolation>();

        violations.AddRange(AccountIdentifierPolicy.ValidateUsername(username));
        violations.AddRange(AccountIdentifierPolicy.ValidateEmail(email));
        violations.AddRange(PasswordPolicy.Validate(request.Password));

        if (username.Length > 0 && email.Length > 0)
        {
            var taken = await store.FindTakenIdentifiersAsync(username, email, cancellationToken).ConfigureAwait(false);

            if (taken.Username)
            {
                violations.Add(new IdentityViolation("username", "username_taken", "That username is already used."));
            }

            if (taken.Email)
            {
                violations.Add(new IdentityViolation("email", "email_taken", "That email address is already used."));
            }
        }

        var role = await ResolveRoleAsync(request.Role ?? SystemRoles.Owner, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            violations.Add(new IdentityViolation("role", "role_invalid", "No role uses that code."));
        }

        if (violations.Count > 0)
        {
            return UserOutcome.Invalid("account_invalid", "The account was refused.", violations);
        }

        var hash = hasher.Hash(request.Password!);
        var now = clock.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            Email = email,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordIterations = hash.Iterations,
            RoleId = role!.Id,
            Role = role,
            PreferredLanguage = NormaliseLanguage(request.PreferredLanguage),
            CreatedAt = now,
        };

        store.AddUser(user);

        store.AddAuditEntry(AuditLog.ForUser(
            AuditAction.AccountCreated,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: JsonSerializer.Serialize(new
            {
                user.Username,
                user.Email,
                role = role.Code,
                user.PreferredLanguage,
            })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(Describe(user));
    }

    /// <summary>Edits an account's identifiers and notification preferences.</summary>
    /// <param name="userId">Account to edit.</param>
    /// <param name="request">The members to change; an omitted one keeps its value.</param>
    /// <param name="actorUserId">The administrator editing it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The account's time zone is deliberately not editable here. It describes where the person is, not what the
    /// deployment thinks of them, and the account has a screen of its own for it (<c>PATCH /auth/me</c>) where it is
    /// validated against the seeded catalogue.
    /// </remarks>
    public async Task<UserOutcome> UpdateAsync(
        Guid userId,
        UpdateAccountRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserOutcome.Failure("user_not_found", "No such account.");
        }

        var before = Describe(user);
        var violations = new List<IdentityViolation>();
        var username = request.Username is null ? user.Username : Normalise(request.Username);
        var email = request.Email is null ? user.Email : Normalise(request.Email);

        violations.AddRange(AccountIdentifierPolicy.ValidateUsername(username));
        violations.AddRange(AccountIdentifierPolicy.ValidateEmail(email));

        if (username != user.Username || email != user.Email)
        {
            // Uniqueness is asked about the pair only when it changed, and evaluated against the *other* accounts:
            // keeping your own name is not a collision with yourself.
            var taken = await store.FindTakenIdentifiersAsync(username, email, cancellationToken).ConfigureAwait(false);

            if (taken.Username && username != user.Username)
            {
                violations.Add(new IdentityViolation("username", "username_taken", "That username is already used."));
            }

            if (taken.Email && email != user.Email)
            {
                violations.Add(new IdentityViolation("email", "email_taken", "That email address is already used."));
            }
        }

        var language = request.PreferredLanguage is null
            ? user.PreferredLanguage
            : NormaliseLanguage(request.PreferredLanguage);
        var severity = ResolveSeverity(request.MinNotifySeverity, user.MinNotifySeverity, violations);
        var quietHours = ResolveQuietHours(request, user, violations);

        if (violations.Count > 0)
        {
            return UserOutcome.Invalid("account_invalid", "The change was refused.", violations);
        }

        var changed = false;

        if (username != user.Username)
        {
            user.Username = username;
            changed = true;
        }

        if (email != user.Email)
        {
            user.Email = email;
            changed = true;
        }

        if (language != user.PreferredLanguage)
        {
            user.PreferredLanguage = language;
            changed = true;
        }

        if (severity != user.MinNotifySeverity)
        {
            user.MinNotifySeverity = severity;
            changed = true;
        }

        if (quietHours.Start != user.QuietHoursStart || quietHours.End != user.QuietHoursEnd)
        {
            (user.QuietHoursStart, user.QuietHoursEnd) = (quietHours.Start, quietHours.End);
            changed = true;
        }

        if (!changed)
        {
            // Nothing changes, so nothing is written and no audit row appears.
            return UserOutcome.Found(before);
        }

        var now = clock.UtcNow;
        var after = Describe(user);

        store.AddAuditEntry(AuditLog.ForUser(
            AuditAction.AccountUpdated,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            beforeJson: JsonSerializer.Serialize(new
            {
                before.Username,
                before.Email,
                before.PreferredLanguage,
                before.MinNotifySeverity,
            }),
            afterJson: JsonSerializer.Serialize(new
            {
                after.Username,
                after.Email,
                after.PreferredLanguage,
                after.MinNotifySeverity,
            })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(after);
    }

    /// <summary>
    /// Replaces an account's password on the administrator's instruction, and ends every session it holds.
    /// </summary>
    /// <param name="userId">Account whose password is replaced.</param>
    /// <param name="request">The new password.</param>
    /// <param name="actorUserId">The administrator performing the reset.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> ResetPasswordAsync(
        Guid userId,
        AdminResetPasswordRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var violations = PasswordPolicy.Validate(request.NewPassword);

        if (violations.Count > 0)
        {
            return UserOutcome.Invalid("password_invalid", "The password was refused.", violations);
        }

        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserOutcome.Failure("user_not_found", "No such account.");
        }

        var now = clock.UtcNow;
        var hash = hasher.Hash(request.NewPassword!);

        user.PasswordHash = hash.Hash;
        user.PasswordSalt = hash.Salt;
        user.PasswordIterations = hash.Iterations;

        // The credential those sessions were obtained with no longer exists, so they end. Without this, a stolen
        // session would outlive the reset performed to kill it.
        await store.RevokeAllUserTokensAsync(user.Id, now, cancellationToken);

        store.AddAuditEntry(AuditLog.ForUser(
            AuditAction.AccountPasswordReset,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: JsonSerializer.Serialize(new { sessionsEnded = true })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(Describe(user));
    }

    /// <summary>Changes an account's role, with the guards this service exists for.</summary>
    /// <param name="userId">Account to change.</param>
    /// <param name="request">Code of the role to assign.</param>
    /// <param name="actorUserId">The administrator making the change.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> ChangeRoleAsync(
        Guid userId,
        UpdateUserRoleRequest request,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await ResolveRoleAsync(request.Role, cancellationToken).ConfigureAwait(false);

        if (role is null)
        {
            var roles = await store.ListRolesAsync(cancellationToken).ConfigureAwait(false);

            return UserOutcome.Invalid(
                "role_invalid",
                "The role was refused.",
                [
                    new IdentityViolation(
                        "role",
                        "role_invalid",
                        $"The roles are: {string.Join(", ", roles.Select(candidate => candidate.Code))}."),
                ]);
        }

        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserOutcome.Failure("user_not_found", "No such account.");
        }

        // Guard one: self-change of any kind. An administrator who removes their own account permissions cannot put
        // them back — the route that would is behind the permission just given up — and promoting yourself is the
        // same request with the opposite sign, so the whole self-change is out.
        if (userId == actorUserId)
        {
            return UserOutcome.Failure(
                "self_change_refused",
                "An administrator may not change their own role. Ask another administrator, or re-run the "
                + "deployment that seeds the account.");
        }

        // Guard two: whatever this change does, somebody must still be able to manage accounts.
        if (await WouldStrandTheDeploymentAsync(user, role, cancellationToken).ConfigureAwait(false))
        {
            return UserOutcome.Failure(
                "last_administrator_refused",
                "This is the only account able to manage accounts. Give another account that permission first, "
                + "then change this one.");
        }

        if (user.RoleId == role.Id)
        {
            // Nothing changes, so nothing is written and no audit row appears — the rule device revoke follows.
            return UserOutcome.Found(Describe(user));
        }

        var before = Describe(user);
        var now = clock.UtcNow;

        user.RoleId = role.Id;
        user.Role = role;

        // The issued token carries the permission set it was minted with, so it has to go: a demotion that took
        // effect only when the access token expired would be a demotion that did not take effect.
        await store.RevokeAllUserTokensAsync(user.Id, now, cancellationToken);

        var after = Describe(user);

        store.AddAuditEntry(AuditLog.ForUser(
            AuditAction.AccountRoleChanged,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            beforeJson: JsonSerializer.Serialize(new { role = before.Role }),
            afterJson: JsonSerializer.Serialize(new { role = after.Role })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(after);
    }

    /// <summary>
    /// Disables an account, or re-enables it. <c>POST /users/{id}/disable</c> and <c>POST /users/{id}/enable</c>
    /// differ only in the value they set, so they share one implementation.
    /// </summary>
    /// <param name="userId">Account to change.</param>
    /// <param name="disabled">True to disable, false to re-enable.</param>
    /// <param name="actorUserId">The administrator making the change.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> SetDisabledAsync(
        Guid userId,
        bool disabled,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserOutcome.Failure("user_not_found", "No such account.");
        }

        // Guard one, and the reason it is not "self-service": disabling yourself is how a deployment loses its last
        // administrator by accident. Re-enabling yourself cannot happen, because a disabled account cannot sign in.
        if (disabled && userId == actorUserId)
        {
            return UserOutcome.Failure(
                "self_change_refused",
                "An administrator may not disable their own account. Ask another administrator.");
        }

        if (disabled && user.DisabledAt is null
            && await store.CountUsersWithPermissionAsync(Permissions.UserManage, user.Id, cancellationToken)
                .ConfigureAwait(false) == 0)
        {
            return UserOutcome.Failure(
                "last_administrator_refused",
                "This is the only account able to manage accounts. Give another account that permission first, "
                + "then disable this one.");
        }

        if (disabled ? user.DisabledAt is not null : user.DisabledAt is null)
        {
            // Already in the state the caller asked for: a repeat is not an error and is not recorded.
            return UserOutcome.Found(Describe(user));
        }

        var now = clock.UtcNow;

        if (disabled)
        {
            user.DisabledAt = now;

            // Every session ends with the account, so disabling takes effect now rather than when the access token
            // expires. Disposal on the next sign-in attempt is guaranteed by the login path's own check.
            await store.RevokeAllUserTokensAsync(user.Id, now, cancellationToken);
        }
        else
        {
            user.DisabledAt = null;
        }

        var after = Describe(user);

        store.AddAuditEntry(AuditLog.ForUser(
            disabled ? AuditAction.AccountDisabled : AuditAction.AccountEnabled,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: JsonSerializer.Serialize(new { disabledAt = after.DisabledAt, disabledBy = actorUserId })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(after);
    }

    /// <summary>
    /// Removes an account: by disabling it, or — when <paramref name="purge"/> is set and nothing references it —
    /// by erasing the row.
    /// </summary>
    /// <param name="userId">Account to remove.</param>
    /// <param name="purge">True to erase the row rather than disable the account.</param>
    /// <param name="actorUserId">The administrator removing it.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserOutcome> DeleteAsync(
        Guid userId,
        bool purge,
        Guid actorUserId,
        AuditActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        var user = await store.FindUserByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserOutcome.Failure("user_not_found", "No such account.");
        }

        if (userId == actorUserId)
        {
            return UserOutcome.Failure(
                "self_change_refused",
                "An administrator may not delete their own account. Use `DELETE /auth/me` to close it, or ask "
                + "another administrator.");
        }

        if (!purge)
        {
            // A soft delete is a disable, so it shares the disable's guards and its sessions rule; only the verb the
            // caller used differs, and the route's own description says so.
            return await SetDisabledAsync(userId, disabled: true, actorUserId, actor, cancellationToken)
                .ConfigureAwait(false);
        }

        if (user.DisabledAt is null
            && await store.CountUsersWithPermissionAsync(Permissions.UserManage, user.Id, cancellationToken)
                .ConfigureAwait(false) == 0)
        {
            return UserOutcome.Failure(
                "last_administrator_refused",
                "This is the only account able to manage accounts. Give another account that permission first, "
                + "then delete this one.");
        }

        var footprint = await store.FindFootprintAsync(userId, cancellationToken).ConfigureAwait(false);

        if (!footprint.IsEmpty)
        {
            // Cascading would leave enclosures, boards and alerts whose owner no longer exists — rows the product
            // can no longer reach and no screen can explain. Disabling keeps them, so that is the answer.
            return UserOutcome.Failure(
                "account_in_use",
                $"This account still owns {string.Join(", ", footprint.HeldBy)}. Disable it instead, or reassign "
                + "what it owns first.");
        }

        var now = clock.UtcNow;

        store.RemoveUser(user);

        store.AddAuditEntry(AuditLog.ForUser(
            AuditAction.AccountDeleted,
            user.Id,
            actorUserId,
            actor ?? AuditActor.Unknown,
            now,
            afterJson: JsonSerializer.Serialize(new
            {
                purge = true,
                username = user.Username,
                email = user.Email,
                role = user.Role?.Code,
                sessionsEnded = footprint.Sessions,
            })));

        await store.SaveChangesAsync(cancellationToken);

        return UserOutcome.Found(Describe(user));
    }

    /// <summary>
    /// True when applying <paramref name="role"/> to <paramref name="user"/> would leave no enabled account able to
    /// manage accounts. Only a change that <em>removes</em> the capability can: granting it always leaves at least
    /// the grantor.
    /// </summary>
    /// <param name="user">Account being changed.</param>
    /// <param name="role">Role it would be moved to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<bool> WouldStrandTheDeploymentAsync(
        User user,
        Role role,
        CancellationToken cancellationToken)
    {
        if (user.DisabledAt is not null)
        {
            // A disabled account cannot manage anything, so its role is not what protects the deployment.
            return false;
        }

        if (role.Permissions.Any(grant => grant.PermissionCode == Permissions.UserManage))
        {
            return false;
        }

        var others = await store
            .CountUsersWithPermissionAsync(Permissions.UserManage, user.Id, cancellationToken)
            .ConfigureAwait(false);

        return others == 0;
    }

    /// <summary>The role a request named by code, or null when no role uses it.</summary>
    private Task<Role?> ResolveRoleAsync(string? code, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(code)
            ? Task.FromResult<Role?>(null)
            : store.FindRoleByCodeAsync(code.Trim(), cancellationToken);

    /// <summary>
    /// The quiet-hours pair the request asks for: the stored pair when the request says nothing, the pair the
    /// request carries when it names both ends, and no window at all when either end is an empty string.
    /// </summary>
    /// <remarks>
    /// Half a pair is refused rather than completed, for the reason the profile route refuses it: half a window
    /// says nothing about when notifications are suppressed. An empty string is the documented way to clear one.
    /// </remarks>
    private static (TimeOnly? Start, TimeOnly? End) ResolveQuietHours(
        UpdateAccountRequest request,
        User user,
        List<IdentityViolation> violations)
    {
        if (request.QuietHoursStart is null && request.QuietHoursEnd is null)
        {
            return (user.QuietHoursStart, user.QuietHoursEnd);
        }

        var startText = request.QuietHoursStart?.Trim() ?? string.Empty;
        var endText = request.QuietHoursEnd?.Trim() ?? string.Empty;

        if (startText.Length == 0 || endText.Length == 0)
        {
            if (startText.Length == 0 && endText.Length == 0)
            {
                return (null, null);
            }

            violations.Add(new IdentityViolation(
                "quietHoursStart",
                "quiet_hours_incomplete",
                "Quiet hours are a pair: send both ends, or send both empty to clear them."));

            return (user.QuietHoursStart, user.QuietHoursEnd);
        }

        if (!TimeOnly.TryParseExact(startText, "HH:mm", out var start)
            || !TimeOnly.TryParseExact(endText, "HH:mm", out var end))
        {
            violations.Add(new IdentityViolation(
                "quietHoursStart",
                "quiet_hours_invalid",
                "Quiet hours are `HH:mm` values, for example 22:00."));

            return (user.QuietHoursStart, user.QuietHoursEnd);
        }

        return (start, end);
    }

    /// <summary>The severity the request asks for, or the stored one when it says nothing.</summary>
    private static AlertSeverity ResolveSeverity(
        string? value,
        AlertSeverity current,
        List<IdentityViolation> violations)
    {
        if (value is null)
        {
            return current;
        }

        if (Enum.TryParse<AlertSeverity>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        violations.Add(new IdentityViolation(
            "minNotifySeverity",
            "severity_invalid",
            "The severities are: info, warning, critical."));

        return current;
    }

    /// <summary>Identifiers are stored lower-case, and a client may send either.</summary>
    private static string Normalise(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Anything but <c>en</c> is Vietnamese, which is the product's default (ADR-013).</summary>
    private static string NormaliseLanguage(string? value) =>
        string.Equals(value?.Trim(), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "vi";

    private static UserSummary Describe(User user) =>
        new(
            user.Id,
            user.Username,
            user.Email,
            user.Role?.Code ?? SystemRoles.CodeOf(user.RoleId) ?? string.Empty,
            user.RoleId,
            [.. (user.Role?.Permissions ?? [])
                .Select(grant => grant.PermissionCode)
                .Where(Permissions.Exists)
                .OrderBy(Permissions.SortOrderOf)],
            user.PreferredLanguage,
            user.TimeZoneId,
            user.QuietHoursStart?.ToString("HH:mm", CultureInfo.InvariantCulture),
            user.QuietHoursEnd?.ToString("HH:mm", CultureInfo.InvariantCulture),
            user.MinNotifySeverity.ToString().ToLowerInvariant(),
            user.CreatedAt,
            user.LastLoginAt,
            user.DisabledAt);
}
