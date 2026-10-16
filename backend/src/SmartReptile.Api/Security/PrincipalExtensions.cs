using System.Security.Claims;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Security;

/// <summary>Reads this API's own claims off the principal.</summary>
/// <remarks>
/// Two kinds of question live here, and the split matters. <see cref="GetUserId"/> and <see cref="RoleCode"/> read
/// the token for what it says about the caller. The four permission helpers answer authorisation questions, and they
/// read the <c>perm</c> claims the token was minted with rather than querying the matrix — see
/// <c>JwtAccessTokenService</c> for why, and for what bounds how stale that answer can be.
/// </remarks>
public static class PrincipalExtensions
{
    /// <summary>
    /// The authenticated account id, or <c>null</c> when the token carries no usable <c>sub</c>.
    /// </summary>
    /// <remarks>
    /// The JWT handler is configured with <c>MapInboundClaims = false</c>, so <c>sub</c> is read by its issued
    /// name rather than through a remapped URI claim.
    /// </remarks>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out var id) ? id : null;

    /// <summary>The caller's role code, for display and for an audit entry — never for an authorisation decision.</summary>
    /// <param name="principal">Authenticated principal.</param>
    public static string? RoleCode(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(AuthorizationClaims.Role) is { Length: > 0 } code ? code : null;

    /// <summary>
    /// True when the caller's token carries <paramref name="permissionCode"/>. This is the same answer the policy
    /// handler gave, read without a second database lookup, which is why a service may use it to widen its own query
    /// rather than trusting the route gate alone.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    /// <param name="permissionCode">Code to test, from <see cref="Permissions"/>.</param>
    public static bool HasPermission(this ClaimsPrincipal principal, string permissionCode) =>
        principal.HasClaim(AuthorizationClaims.Permission, permissionCode);

    /// <summary>Every permission the caller holds, in catalogue order — what a client renders its screens from.</summary>
    /// <param name="principal">Authenticated principal.</param>
    public static IReadOnlyList<string> PermissionCodes(this ClaimsPrincipal principal) =>
        [.. principal.FindAll(AuthorizationClaims.Permission)
            .Select(claim => claim.Value)
            .Where(Domain.Identity.Permissions.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(Domain.Identity.Permissions.SortOrderOf)];

    /// <summary>
    /// True when the caller may read data belonging to accounts other than their own — the administrator's overview,
    /// and the reason a terrarium read can be answered for somebody else's enclosure.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    public static bool CanReadAnyAccount(this ClaimsPrincipal principal) =>
        principal.HasPermission(Permissions.DataReadAny);

    /// <summary>
    /// True when the caller may edit another account's enclosure. Checked by the services that scope their query by
    /// owner, because the route gate cannot express it: <c>terrarium.write</c> is held by every keeper.
    /// </summary>
    /// <param name="principal">Authenticated principal.</param>
    public static bool CanWriteAnyAccount(this ClaimsPrincipal principal) =>
        principal.HasPermission(Permissions.DataWriteAny);
}

