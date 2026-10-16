namespace SmartReptile.Domain.Identity;

/// <summary>
/// The claim names this API's own authorisation reads, in one place because the token is minted in
/// <c>Infrastructure</c> and checked in <c>Api</c> — and a typo in either would be an unauthenticated endpoint or a
/// locked-out administrator rather than a compile error.
/// </summary>
public static class AuthorizationClaims
{
    /// <summary>
    /// One claim per permission code the account's role holds (<see cref="Permissions"/>). Repeating the claim
    /// rather than joining codes into one value is what lets a policy compare by equality.
    /// </summary>
    public const string Permission = "perm";

    /// <summary>
    /// The role's code. Displayed by the clients and recorded in audit entries; no authorisation decision is made
    /// on it, so an administrator renaming a role cannot widen anybody's access.
    /// </summary>
    public const string Role = "role";
}
