using System.Security.Claims;

namespace SmartReptile.Api.Security;

/// <summary>Reads this API's own claims off the principal.</summary>
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
}
