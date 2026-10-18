using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>One account as the administrator's list and detail answers describe it (FR-18).</summary>
/// <param name="Id">Account id.</param>
/// <param name="Username">Stored (lower-case) username.</param>
/// <param name="Email">Stored (lower-case) email.</param>
/// <param name="Role">The role's code, e.g. <c>Owner</c>.</param>
/// <param name="RoleId">The role's identity, so the console can move the account onto another role.</param>
/// <param name="Permissions">
/// The codes the account's role grants. Carried on the row rather than fetched per account: the role is loaded
/// with the account anyway, and "what can this account do" is the first question an administrator asks of a list.
/// </param>
/// <param name="PreferredLanguage">UI language the account chose.</param>
/// <param name="TimeZoneId">Zone the account chose.</param>
/// <param name="QuietHoursStart">Quiet-hours start as <c>HH:mm</c>, or null when none is configured.</param>
/// <param name="QuietHoursEnd">Quiet-hours end as <c>HH:mm</c>, or null.</param>
/// <param name="MinNotifySeverity">Lowest severity that produces a notification for this account (BR-13.2).</param>
/// <param name="CreatedAt">When the account was created.</param>
/// <param name="LastLoginAt">Last successful sign-in, null before the first one.</param>
/// <param name="DisabledAt">
/// When the account was disabled, null while it is usable. Read as "this account cannot sign in" rather than as a
/// deletion marker — a soft delete is a disable, and a hard delete removes the row.
/// </param>
public sealed record UserSummary(
    Guid Id,
    string Username,
    string Email,
    string Role,
    Guid RoleId,
    IReadOnlyList<string> Permissions,
    string PreferredLanguage,
    string TimeZoneId,
    string? QuietHoursStart,
    string? QuietHoursEnd,
    string MinNotifySeverity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DisabledAt)
{
    /// <summary>True while the account may sign in — the negated form the clients render a badge from.</summary>
    public bool IsActive => DisabledAt is null;
}

/// <summary>One page of accounts, with the total so a client can render a pager (FR-18).</summary>
/// <param name="Items">The accounts on this page, ordered by username.</param>
/// <param name="Total">How many accounts match the same search.</param>
/// <param name="Skip">Rows skipped to reach this page.</param>
/// <param name="Take">Page size this answer was asked for.</param>
public sealed record UserPage(IReadOnlyList<UserSummary> Items, int Total, int Skip, int Take);

/// <summary>Role-change input.</summary>
/// <param name="Role">
/// Code of the role to assign — <c>Owner</c>, <c>Technician</c>, <c>Viewer</c>, <c>Admin</c> or any custom role's
/// code. Matched case-insensitively, and an unknown code is a refusal rather than a fallback.
/// </param>
public sealed record UpdateUserRoleRequest(string? Role);

/// <summary>
/// Account-creation input (FR-18). The administrator supplies the first password rather than the server
/// generating one: a generated password would have to be shown exactly once and stored nowhere, which is a
/// delivery problem this deployment has no channel for (<c>R-13</c>).
/// </summary>
/// <param name="Username">Login name; refused when taken.</param>
/// <param name="Email">Email address; refused when taken.</param>
/// <param name="Password">First password. Validated by the same policy registration uses.</param>
/// <param name="Role">Code of the role to grant; <c>Owner</c> when omitted.</param>
/// <param name="PreferredLanguage"><c>vi</c> or <c>en</c>; <c>vi</c> when omitted.</param>
public sealed record CreateUserRequest(
    string? Username,
    string? Email,
    string? Password,
    string? Role = null,
    string? PreferredLanguage = null);

/// <summary>
/// Account-edit input (FR-18). An omitted member keeps its value, so a client may send only what it changed.
/// </summary>
/// <param name="Username">New login name, or null to keep. Refused when another account holds it.</param>
/// <param name="Email">New email address, or null to keep. Refused when another account holds it.</param>
/// <param name="PreferredLanguage"><c>vi</c> or <c>en</c>, or null to keep.</param>
/// <param name="MinNotifySeverity"><c>info</c>, <c>warning</c> or <c>critical</c>, or null to keep.</param>
/// <param name="QuietHoursStart">Quiet-hours start as <c>HH:mm</c>; empty string clears the pair.</param>
/// <param name="QuietHoursEnd">Quiet-hours end as <c>HH:mm</c>; empty string clears the pair.</param>
public sealed record UpdateAccountRequest(
    string? Username,
    string? Email,
    string? PreferredLanguage,
    string? MinNotifySeverity,
    string? QuietHoursStart,
    string? QuietHoursEnd);

/// <summary>Administrator password reset (FR-18).</summary>
/// <param name="NewPassword">The password to store. Every session of the account ends.</param>
public sealed record AdminResetPasswordRequest(string? NewPassword);

/// <summary>
/// Outcome of an account-management use case. At most one payload is set; a null <see cref="Problem"/> means success.
/// </summary>
/// <remarks>
/// Its own type rather than <see cref="AuthOutcome"/>, because the two failure vocabularies are different: an
/// authentication failure discloses as little as possible, while an administrator's request is refused with the
/// reason it was refused ("that is the last administrator", "you cannot demote yourself").
/// </remarks>
public sealed record UserOutcome
{
    /// <summary>Set by the reads.</summary>
    public UserPage? Page { get; init; }

    /// <summary>Set by a read of one account and by a successful change.</summary>
    public UserSummary? User { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public AuthProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A page of accounts.</summary>
    public static UserOutcome Listed(UserPage page) => new() { Page = page };

    /// <summary>One account.</summary>
    public static UserOutcome Found(UserSummary user) => new() { User = user };

    /// <summary>A failure with a code and a message an administrator can act on.</summary>
    public static UserOutcome Failure(string code, string message) =>
        new() { Problem = new AuthProblem(code, message, []) };

    /// <summary>A failure carrying field-level violations.</summary>
    public static UserOutcome Invalid(string code, string message, IReadOnlyList<IdentityViolation> errors) =>
        new() { Problem = new AuthProblem(code, message, errors) };
}
