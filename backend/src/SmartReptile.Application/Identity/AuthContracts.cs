using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>Registration input. Nullable members so validation reports a missing field rather than throwing.</summary>
public sealed record RegisterRequest(string? Username, string? Email, string? Password, string? PreferredLanguage);

/// <summary>Login input.</summary>
public sealed record LoginRequest(string? UsernameOrEmail, string? Password, string? DeviceInfo);

/// <summary>Refresh and logout input: the opaque token the client stored.</summary>
public sealed record RefreshRequest(string? RefreshToken);

/// <summary>Change-password input; the account comes from the access token, never from the body.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>The caller's own account. Never carries a hash, a salt or a token.</summary>
/// <param name="Id">Account id.</param>
/// <param name="Username">Stored (lower-case) username.</param>
/// <param name="Email">Stored (lower-case) email.</param>
/// <param name="Role">Owner, Technician or Viewer (§02-design/06 §3).</param>
/// <param name="PreferredLanguage">UI language, <c>vi</c> or <c>en</c> (ADR-013).</param>
/// <param name="TimeZoneId">Timezone used for rendering and quiet hours.</param>
public sealed record UserProfile(
    Guid Id,
    string Username,
    string Email,
    string Role,
    string PreferredLanguage,
    string TimeZoneId);

/// <summary>A session: the tokens a client stores plus the profile it may render immediately.</summary>
/// <param name="User">Owner of the session.</param>
/// <param name="AccessToken">Short-lived JWT.</param>
/// <param name="AccessTokenExpiresAtUtc">When the JWT stops being accepted.</param>
/// <param name="RefreshToken">Opaque token, returned exactly once per rotation.</param>
/// <param name="RefreshTokenExpiresAtUtc">When the refresh token stops being accepted.</param>
public sealed record AuthSession(
    UserProfile User,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

/// <summary>An expected authentication failure, mapped to RFC 7807 by the endpoint.</summary>
/// <param name="Code">Stable problem code, e.g. <c>invalid_credentials</c>.</param>
/// <param name="Message">Message safe to show the user — never reveals whether an account exists.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
public sealed record AuthProblem(string Code, string Message, IReadOnlyList<IdentityViolation> Errors);

/// <summary>
/// Outcome of an authentication use case. Carries a session, a problem, or neither — the last case being a
/// command that simply succeeded (logout, change-password, and registration, which issues no session).
/// </summary>
public sealed record AuthOutcome
{
    /// <summary>The issued session, when the use case created one.</summary>
    public AuthSession? Session { get; init; }

    /// <summary>The expected failure, when there was one.</summary>
    public AuthProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A success, optionally with a session.</summary>
    public static AuthOutcome Success(AuthSession? session = null) => new() { Session = session };

    /// <summary>A failure with a code and a non-disclosing message.</summary>
    public static AuthOutcome Failure(string code, string message) =>
        new() { Problem = new AuthProblem(code, message, []) };

    /// <summary>A failure carrying field-level validation errors.</summary>
    public static AuthOutcome Invalid(string code, string message, IReadOnlyList<IdentityViolation> errors) =>
        new() { Problem = new AuthProblem(code, message, errors) };
}
