using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>
/// TC-U-33 — the access token carries exactly the documented claims and the documented lifetime.
/// </summary>
public class JwtAccessTokenServiceTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-characters";

    private readonly TestClock _clock = new();
    private readonly JwtAccessTokenService _service;

    public JwtAccessTokenServiceTests() =>
        _service = new JwtAccessTokenService(
            Options.Create(new JwtOptions
            {
                SigningKey = SigningKey,
                AccessTokenMinutes = 15,
                Issuer = "SmartReptile",
                Audience = "SmartReptile",
            }),
            _clock);

    [Fact]
    public void Issues_the_documented_claims()
    {
        var user = new User { Username = "linh", Role = UserRole.Technician };

        var token = _service.Issue(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);

        jwt.Claims.Should().Contain(claim => claim.Type == "sub" && claim.Value == user.Id.ToString());
        jwt.Claims.Should().Contain(claim => claim.Type == "role" && claim.Value == "Technician");
        jwt.Claims.Should().Contain(claim => claim.Type == "jti" && claim.Value.Length > 0);
        jwt.Claims.Should().Contain(claim => claim.Type == "ver" && claim.Value == "1");
        jwt.Subject.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void Expires_exactly_one_access_lifetime_after_it_was_issued()
    {
        var token = _service.Issue(new User { Username = "linh" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        var issuedAt = jwt.IssuedAt;
        var expires = jwt.ValidTo;

        expires.Should().Be(issuedAt.AddMinutes(15));
        token.ExpiresAtUtc.Should().Be(_clock.UtcNow.AddMinutes(15));
    }

    [Fact]
    public void Validates_against_the_parameters_the_api_configures()
    {
        // Anchored to the real clock: this case is about the *validation parameters* matching Program.cs, so the
        // token has to survive the handler's own lifetime check. The fixed-clock cases above cover the lifetime.
        var service = new JwtAccessTokenService(
            Options.Create(new JwtOptions
            {
                SigningKey = SigningKey,
                AccessTokenMinutes = 15,
                Issuer = "SmartReptile",
                Audience = "SmartReptile",
            }),
            new WallClock());

        var user = new User { Username = "linh" };
        var token = service.Issue(user);

        // The same parameters Program.cs uses — if the two drift apart, every authenticated call breaks, and
        // that is worth catching in a unit test rather than in a demo.
        var principal = new JwtSecurityTokenHandler().ValidateToken(
            token.Value,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "SmartReptile",
                ValidateAudience = true,
                ValidAudience = "SmartReptile",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = true,
                NameClaimType = "sub",
                RoleClaimType = "role",
            },
            out _);

        principal.FindFirst("sub")?.Value.Should().Be(user.Id.ToString());
    }

    [Fact]
    public void Refuses_a_token_signed_with_a_different_key()
    {
        var other = new JwtAccessTokenService(
            Options.Create(new JwtOptions
            {
                SigningKey = "a-completely-different-signing-key-32-chars",
                AccessTokenMinutes = 15,
            }),
            _clock);

        var token = other.Issue(new User { Username = "linh" });

        var validate = () => new JwtSecurityTokenHandler().ValidateToken(
            token.Value,
            new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                ValidateLifetime = false,
            },
            out _);

        validate.Should().Throw<SecurityTokenInvalidSignatureException>();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        public DateTimeOffset NowIn(string timeZoneId) => UtcNow;
    }

    /// <summary>A clock anchored to the wall clock, for the case that has to survive real lifetime validation.</summary>
    private sealed class WallClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public DateTimeOffset NowIn(string timeZoneId) => UtcNow;
    }
}
