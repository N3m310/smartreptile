using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Abstractions;

/// <summary>A signed access token and the instant it stops being accepted.</summary>
/// <param name="Value">The compact JWT.</param>
/// <param name="ExpiresAtUtc">Expiry, as embedded in the <c>exp</c> claim.</param>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Issues user access tokens. Claims are fixed by <c>07-appendices/03</c> / BR-01.3: <c>sub</c>, <c>role</c>,
/// <c>iat</c>, <c>exp</c>, <c>jti</c>, <c>ver</c> — plus, since FR-18 moved the matrix into the database, one
/// <c>perm</c> claim per permission the account's role holds.
/// </summary>
public interface IAccessTokenService
{
    /// <summary>
    /// Issues a token for the user, valid for the configured access-token lifetime.
    /// </summary>
    /// <param name="user">Account the token is about. Its <see cref="User.Role"/> is read for the display claim.</param>
    /// <param name="permissions">
    /// The codes the account's role grants. They are the authorisation decision the API reads, which is why changing
    /// a role's matrix or an account's role ends that account's sessions rather than waiting for the next login: a
    /// token carries the answer it was minted with.
    /// </param>
    AccessToken Issue(User user, IReadOnlyCollection<string> permissions);
}
