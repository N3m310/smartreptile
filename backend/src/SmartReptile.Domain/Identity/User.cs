namespace SmartReptile.Domain.Identity;

/// <summary>
/// Roles from §02-design/06 §3. The permission matrix lives in one authorisation handler, so an endpoint
/// never re-implements the rule (§02-design/06 §3 implementation note).
/// </summary>
public enum UserRole
{
    Owner = 0,
    Technician = 1,
    Viewer = 2,
}

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

    /// <summary>Authorisation role.</summary>
    public UserRole Role { get; set; } = UserRole.Owner;

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

    /// <summary>Telegram notifications enabled (the channel used in the live demo).</summary>
    public bool ChannelTelegramEnabled { get; set; }

    /// <summary>Email notifications enabled.</summary>
    public bool ChannelEmailEnabled { get; set; }

    /// <summary>Linked Telegram chat id, set through a one-time linking code.</summary>
    public string? TelegramChatId { get; set; }

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
}
