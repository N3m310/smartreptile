using Microsoft.AspNetCore.Authorization;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Security;

/// <summary>
/// Registers the authorisation policies (FR-18, §02-design/06 §3).
/// </summary>
/// <remarks>
/// <b>A policy name is a permission code.</b> Before the matrix moved into the database, this file held a table of
/// roles and an endpoint named a role (<c>Owner</c>, <c>Technician</c>, <c>OwnerOrAdmin</c>). That made every route
/// the place where a role was translated into a capability — exactly the translation the database now owns: a
/// deployment may re-point <c>Owner</c> at a different set of permissions, and a route that said "Owner" would
/// silently keep meaning whatever the old table said. So a policy is registered from the catalogue below, its name
/// <em>is</em> the code, and an endpoint asks for the capability it needs.
/// <para>
/// <b>The composite policies are gone, and deliberately.</b> <c>OwnerOrAdmin</c> existed because the administrator
/// was not a superset of the keeper, so a route had to accept either. The administrator's role now holds every
/// permission the keeper's does (<see cref="SystemRoles.Seed"/>), so "the keeper's write, or the administrator's" is
/// simply <c>terrarium.write</c>. The narrower question those routes also answered — <em>whose</em> enclosure? — is
/// checked where it belongs, inside the service that scopes its own query, against the two <c>.any</c> codes.
/// </para>
/// </remarks>
public static class AuthorizationPolicies
{
    /// <summary>
    /// The authenticated-only policy name, for routes that are about the caller's own account and therefore need a
    /// token but no capability — signing yourself out, reading your own profile, changing your own password.
    /// </summary>
    public const string Authenticated = "authenticated";

    /// <summary>
    /// Registers one policy per permission in the catalogue, each demanding that the caller's token carries that
    /// code. Called once, from the API's composition root.
    /// </summary>
    /// <param name="options">Authorisation options to populate.</param>
    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        foreach (var definition in Permissions.All)
        {
            var code = definition.Code;

            options.AddPolicy(code, policy => policy.RequireAssertion(context =>
                context.User.HasClaim(AuthorizationClaims.Permission, code)));
        }
    }
}

