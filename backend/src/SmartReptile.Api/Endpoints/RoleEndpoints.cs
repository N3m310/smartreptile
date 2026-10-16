using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The <c>/api/v1/admin/roles</c> and <c>/api/v1/admin/permissions</c> groups: the permission matrix as data
/// (FR-18).
/// </summary>
/// <remarks>
/// The matrix lives under <c>/admin</c> rather than beside <c>/users</c> because it is not an account: it is what
/// the deployment lets accounts do, and the console that edits it is the administrator's. Reads name
/// <c>user.read</c> — an auditor may see which role grants what — and every write names <c>user.manage</c>.
/// <para>
/// A role is deleted, never edited into a different role: its code and its identity are what the seed, the clients
/// and the audit trail name it by, so a rename is a display change and a re-code is impossible.
/// </para>
/// </remarks>
public static class RoleEndpoints
{
    /// <summary>Maps the matrix reads, the catalogue read, and the create/edit/delete trio.</summary>
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        var roles = app.MapGroup("/api/v1/admin/roles")
            .WithTags("admin")
            .RequireAuthorization(Permissions.UserRead);

        roles.MapGet(string.Empty, async (RoleAdminService service, CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(cancellationToken);

            return outcome.Succeeded ? Results.Ok(new { items = outcome.Roles }) : Problem(outcome.Problem!);
        })
        .WithSummary("List roles")
        .WithDescription("Requires `user.read`. Each role carries its permission codes, whether the product ships "
                       + "it, and how many accounts hold it — so the matrix screen needs one call. "
                       + "`crossAccountPermissions` on each entry is the subset that reaches other accounts, which "
                       + "is the short answer to \"which data does this role see\".");

        roles.MapGet("/{roleId:guid}", async (
            Guid roleId,
            RoleAdminService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.GetAsync(roleId, cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Role) : Problem(outcome.Problem!);
        })
        .WithSummary("One role")
        .WithDescription("Requires `user.read`. 404 `role_not_found` for an id no role holds.");

        roles.MapPost(string.Empty, async (
            CreateRoleRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            RoleAdminService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.Created($"/api/v1/admin/roles/{outcome.Role!.Id}", outcome.Role)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Create a role")
        .WithDescription("Requires `user.manage`. `{code, name, description?, permissions?, rank?}`. A code another "
                       + "role uses is `400 role_invalid` with `role_code_taken`, and a permission code that is not "
                       + "in the catalogue is refused rather than dropped — a typo that silently granted nothing is "
                       + "the failure this check exists to prevent. Audited as `role.created`.");

        roles.MapPatch("/{roleId:guid}", async (
            Guid roleId,
            UpdateRoleRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            RoleAdminService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.UpdateAsync(
                roleId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Role) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Edit a role's name, description or permissions")
        .WithDescription("Requires `user.manage`. `permissions` **replaces** the set — granting is adding a code and "
                       + "revoking is leaving one out, because a merge would make a permission impossible to take "
                       + "away. Refused with `last_administrator_refused` when it would remove the last grant of "
                       + "`user.manage`. Audited as `role.updated` with the set before and after, because this is "
                       + "the most consequential edit the API allows. A no-op writes nothing. **Sessions are not "
                       + "ended by this route**: accounts holding the role keep their tokens until they expire, so "
                       + "the console warns that a revocation takes up to one access-token lifetime.");

        roles.MapDelete("/{roleId:guid}", async (
            Guid roleId,
            HttpContext context,
            ClaimsPrincipal principal,
            RoleAdminService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DeleteAsync(
                roleId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Role) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Delete a custom role")
        .WithDescription("Requires `user.manage`. A role the product ships answers `409 system_role_immutable` — "
                       + "deleting one would make the next start-up recreate it with the shipped defaults, silently "
                       + "undoing whatever was tuned — and a role accounts still hold answers `409 role_in_use` with "
                       + "the count. Audited as `role.deleted`.");

        app.MapGet("/api/v1/admin/permissions", async (
            RoleAdminService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CatalogAsync(cancellationToken);

            return outcome.Succeeded ? Results.Ok(new { items = outcome.Catalog }) : Problem(outcome.Problem!);
        })
        .WithTags("admin")
        .RequireAuthorization(Permissions.UserRead)
        .WithSummary("The permission catalogue")
        .WithDescription("Requires `user.read`. Every code this build implements, with its group, its label, what "
                       + "granting it allows, and whether it reaches across accounts. The catalogue is code, not "
                       + "data: a permission the software does not implement cannot be granted by inserting a row.");

        return app;
    }

    /// <summary>Maps a role-administration failure to RFC 7807, with the codes the console acts on.</summary>
    private static IResult Problem(AuthProblem problem)
    {
        var status = problem.Code switch
        {
            "role_not_found" => StatusCodes.Status404NotFound,
            "system_role_immutable" or "role_in_use" or "last_administrator_refused" =>
                StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = problem.Code,
        };

        if (problem.Errors.Count > 0)
        {
            extensions["errors"] = problem.Errors
                .Select(violation => new { violation.Field, violation.Code, violation.Message })
                .ToArray();
        }

        return Results.Problem(
            title: problem.Message,
            statusCode: status,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: extensions);
    }
}
