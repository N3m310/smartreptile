using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Issues the access token described in BR-01.3: HS256, 15-minute lifetime, claims <c>sub</c>, <c>role</c>,
/// <c>iat</c>, <c>exp</c>, <c>jti</c>, <c>ver</c>, and one <c>perm</c> per permission the role holds. The API
/// validates the same claims with the same key, so the two halves live in one place only by contract —
/// <c>Program.cs</c> configures the validator.
/// </summary>
/// <remarks>
/// <b>Why the permissions travel in the token rather than being looked up per request.</b> A permission check would
/// otherwise be a database read on every authorised request, and a cache in front of it would need invalidating the
/// moment an administrator edits a matrix — the one moment it matters. Putting the answer in the token makes the
/// check free and the staleness explicit: a token is valid for its lifetime, and the two operations that change what
/// it says — a role change and a matrix edit — end the affected accounts' sessions instead
/// (<see cref="IUserStore.RevokeAllUserTokensAsync"/>), so the stale window is bounded by that, not by the lifetime.
/// The <c>role</c> claim stays because the clients display it; nothing authorises on it.
/// </remarks>
public sealed class JwtAccessTokenService(IOptions<JwtOptions> options, IClock clock) : IAccessTokenService
{
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <inheritdoc />
    public AccessToken Issue(User user, IReadOnlyCollection<string> permissions)
    {
        var settings = options.Value;
        var now = clock.UtcNow;
        var expiresAt = now.AddMinutes(settings.AccessTokenMinutes);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("role", user.Role?.Code ?? SystemRoles.CodeOf(user.RoleId) ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("ver", "2"),
        };

        // One claim per code rather than a delimited list: a claim's value is compared by the policy handler, and a
        // joined string would make "does this account hold alert.write" a substring question — which is exactly the
        // kind of check that passes for the wrong reason.
        claims.AddRange(permissions
            .Where(Permissions.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code => new Claim(AuthorizationClaims.Permission, code)));

        // issuedAt is set explicitly: the tests assert exp == iat + AccessTokenMinutes, and the default
        // constructor would stamp iat from the wall clock rather than from the injected clock (TC-U-33).
        var payload = new JwtPayload(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            issuedAt: now.UtcDateTime);

        var token = new JwtSecurityToken(new JwtHeader(credentials), payload);

        return new AccessToken(_handler.WriteToken(token), expiresAt);
    }
}
