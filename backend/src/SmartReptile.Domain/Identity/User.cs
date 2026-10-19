namespace SmartReptile.Domain.Identity;

/// <summary>A user account with its notification preferences (FR-01, FR-13).</summary>
public class User
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Login name; unique, case-insensitive.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Email address; unique.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>PBKDF2-HMAC-SHA256 output (never the plaintext password).</summary>
    public byte[] PasswordHash { get; set; } = Array.Empty<byte>();

    /// <summary>Per-user salt, 16 bytes.</summary>
    public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Iteration count used for the stored hash. Stored per user so the cost can be raised later and the
    /// hash upgraded on the next successful login without a mass password reset (TC-U-32).
    /// </summary>
    public int PasswordIterations { get; set; } = 210_000;

    /// <summary>
    /// Salted hash of the account's current backup recovery code (BR-01.5). Null when the account has none —
    /// one created before recovery codes existed, or one whose code was cleared.
    /// </summary>
    /// <remarks>
    /// A null code is not a special case in the API: it fails verification exactly like a wrong code does, so the
    /// caller cannot tell an account without a code from an account that does not exist.
    /// </remarks>
    public byte[]? RecoveryCodeHash { get; set; }

    /// <summary>Salt the recovery-code digest was produced with.</summary>
    public byte[]? RecoveryCodeSalt { get; set; }

    /// <summary>When the current recovery code was issued; set on creation and again on every rotation.</summary>
    public DateTimeOffset? RecoveryCodeIssuedAt { get; set; }

    /// <summary>
    /// Authorisation role (FR-18). A foreign key into <c>Role</c> rather than an enum column: what an account may do
    /// is the permission set its role holds, and that set is a deployment's decision the administrator edits.
    /// </summary>
    /// <remarks>
    /// Registration and the seeder write a constant from <see cref="SystemRoles"/>, so the key is never the result
    /// of a lookup that an empty database could fail to answer.
    /// </remarks>
    public Guid RoleId { get; set; } = SystemRoles.OwnerId;

    /// <summary>Role navigation. Loaded where the permission set is needed.</summary>
    public Role? Role { get; set; }

    /// <summary>UI language, <c>vi</c> or <c>en</c> (ADR-013).</summary>
    public string PreferredLanguage { get; set; } = "vi";

    /// <summary>Timezone used for rendering and for quiet hours.</summary>
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Start of the quiet-hours window, if the user configured one.</summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>End of the quiet-hours window.</summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>Lowest severity that produces a notification (BR-13.2).</summary>
    public Alerts.AlertSeverity MinNotifySeverity { get; set; } = Alerts.AlertSeverity.Warning;

    /// <summary>Push notifications enabled.</summary>
    public bool ChannelFcmEnabled { get; set; }

    /// <summary>Email notifications enabled.</summary>
    public bool ChannelEmailEnabled { get; set; }

    /// <summary>FCM registration token for the mobile app.</summary>
    public string? FcmToken { get; set; }

    /// <summary>Account creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last successful login.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Set when an account is disabled; disabled accounts cannot log in.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}

/// <summary>
/// A rotating refresh token. Single use: consuming it issues a replacement, and replaying a consumed token
/// revokes the whole family (token-theft detection, FR-01 BR-01.3).
/// </summary>
public class RefreshToken
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning user.</summary>
    public Guid UserId { get; set; }

    /// <summary><c>SHA-256</c> of the opaque token; the token itself is never stored.</summary>
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    /// <summary>Rotation family id — all descendants of one login share it, so reuse can revoke them together.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>Issue time.</summary>
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Hard expiry (default 30 days).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the token is exchanged; reuse after this instant means theft.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }

    /// <summary>Set when the token (or its family) is revoked.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Client description for the session list (device model, platform).</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>
    /// True when the token may still be exchanged for a new session: unexpired, not yet consumed and not
    /// revoked. A consumed token is not "unusable" by accident — presenting it again is evidence of theft
    /// and revokes the family (TC-U-34), so this deliberately reports <c>false</c> for it too.
    /// </summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) =>
        RevokedAt is null && ConsumedAt is null && ExpiresAt > nowUtc;
}
