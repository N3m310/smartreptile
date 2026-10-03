using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Abstractions;

/// <summary>A signed access token and the instant it stops being accepted.</summary>
/// <param name="Value">The compact JWT.</param>
/// <param name="ExpiresAtUtc">Expiry, as embedded in the <c>exp</c> claim.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Issues user access tokens. Claims are fixed by `<c>07-appendices/03</c>` / BR-01.3: <c>sub</c>, <c>role</c>,
/// <c>iat</c>, <c>exp</c>, <c>jti</c>, <c>ver</c>.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>Issues a token for the user, valid for the configured access-token lifetime.</summary>
    AccessToken Issue(User user);
}
