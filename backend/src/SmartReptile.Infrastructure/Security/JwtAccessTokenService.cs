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
/// <c>iat</c>, <c>exp</c>, <c>jti</c>, <c>ver</c>. The API validates the same claims with the same key, so the
/// two halves live in one place only by contract — <c>Program.cs</c> configures the validator.
/// </summary>
public sealed class JwtAccessTokenService(IOptions<JwtOptions> options, IClock clock) : IAccessTokenService
{
    private readonly JwtSecurityTokenHandler _handler = new();

    /// <inheritdoc />
    public AccessToken Issue(User user)
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
            new("role", user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("ver", "1"),
        };

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
