using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Identity;

/// <summary>One role as the console's matrix renders it (FR-18).</summary>
/// <param name="Id">Role identity.</param>
/// <param name="Code">Stable code, e.g. <c>Owner</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="IsSystem">True for the four the product ships, which cannot be deleted or re-coded.</param>
/// <param name="Rank">Display order, lowest first.</param>
/// <param name="Permissions">The codes this role grants, in catalogue order.</param>
/// <param name="UserCount">How many accounts hold it, so a delete can be refused with a number.</param>
/// <param name="RowVersion">Concurrency token, returned so a second edit can send it back.</param>
public sealed record RoleSummary(
    Guid Id,
    string Code,
    string Name,
    string Description,
    bool IsSystem,
    int Rank,
    IReadOnlyList<string> Permissions,
    int UserCount,
    string? RowVersion)
{
    /// <summary>
    /// The permissions that reach past the caller's own account — the answer to "which data does this role map to"
    /// on the console's own terms, and empty for every role below the administrator. Qualified by its full
    /// namespace because this type has a member of the same name as the catalogue.
    /// </summary>
    public IReadOnlyList<string> CrossAccountPermissions =>
        [.. Permissions.Where(SmartReptile.Domain.Identity.Permissions.CrossAccountCodes.Contains)];
}

/// <summary>One permission in the catalogue, as the console offers it for granting.</summary>
/// <param name="Code">The code itself.</param>
/// <param name="Group">Display group.</param>
/// <param name="Name">Short label.</param>
/// <param name="Description">What granting it allows.</param>
/// <param name="CrossesAccounts">True when it reaches data outside the caller's own account.</param>
public sealed record PermissionSummary(
    string Code,
    string Group,
    string Name,
    string Description,
    bool CrossesAccounts);

/// <summary>Role-creation input.</summary>
/// <param name="Code">Stable code. Refused when it is not unique or is not identifier-shaped.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What the role is for.</param>
/// <param name="Permissions">Codes to grant. An unknown code is refused rather than ignored.</param>
/// <param name="Rank">Display order; 100 when omitted, so a custom role sorts below the shipped four.</param>
public sealed record CreateRoleRequest(
    string? Code,
    string? Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    int? Rank = null);

/// <summary>
/// Role-edit input. An omitted member keeps its value, except <paramref name="Permissions"/>, which replaces the
/// whole set — a permission edit is always "the matrix is now this", never an incremental grant.
/// </summary>
/// <param name="Name">New display name, or null to keep.</param>
/// <param name="Description">New description, or null to keep.</param>
/// <param name="Permissions">The complete set to store, or null to keep the current one.</param>
/// <param name="Rank">New display order, or null to keep.</param>
public sealed record UpdateRoleRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Permissions,
    int? Rank = null);

/// <summary>Outcome of a role-administration use case.</summary>
public sealed record RoleOutcome
{
    /// <summary>Set by a read or a successful write of one role.</summary>
    public RoleSummary? Role { get; init; }

    /// <summary>Set by the list.</summary>
    public IReadOnlyList<RoleSummary>? Roles { get; init; }

    /// <summary>Set by the catalogue read.</summary>
    public IReadOnlyList<PermissionSummary>? Catalog { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public AuthProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>One role.</summary>
    public static RoleOutcome Found(RoleSummary role) => new() { Role = role };

    /// <summary>Every role.</summary>
    public static RoleOutcome Listed(IReadOnlyList<RoleSummary> roles) => new() { Roles = roles };

    /// <summary>The permission catalogue.</summary>
    public static RoleOutcome Catalogue(IReadOnlyList<PermissionSummary> catalog) => new() { Catalog = catalog };

    /// <summary>A failure with a code an administrator can act on.</summary>
    public static RoleOutcome Failure(string code, string message) =>
        new() { Problem = new AuthProblem(code, message, []) };

    /// <summary>A failure carrying field-level violations.</summary>
    public static RoleOutcome Invalid(string code, string message, IReadOnlyList<IdentityViolation> errors) =>
        new() { Problem = new AuthProblem(code, message, errors) };
}
