using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Identity;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The <c>/api/v1/users</c> group: account management (FR-18). Contract: <c>07-appendices/03</c> §4.5.
/// </summary>
/// <remarks>
/// A group of its own rather than routes under <c>/auth</c>, because these are not the caller's own account: every
/// route here acts on somebody else. Reads name <c>user.read</c> and every write names <c>user.manage</c>, so a
/// deployment that wants an auditor account can grant the first without the second — which is the whole reason the
/// permission exists separately from the role code the clients display.
/// <para>
/// The two guards the service owns are visible in the refusal vocabulary rather than on the route:
/// <c>self_change_refused</c> for any action on the caller's own account, and <c>last_administrator_refused</c> when
/// the change would leave nobody able to manage accounts. A route cannot express either, because both depend on the
/// target rather than on the caller.
/// </para>
/// </remarks>
public static class UserEndpoints
{
    /// <summary>Maps the account list, creation, one account, the edits, the role change and the removal pair.</summary>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("users")
            .RequireAuthorization(Permissions.UserRead);

        group.MapGet(string.Empty, async (
            string? search,
            int? skip,
            int? take,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.ListAsync(search, skip, take, cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(new
                {
                    items = outcome.Page!.Items,
                    total = outcome.Page.Total,
                    skip = outcome.Page.Skip,
                    take = outcome.Page.Take,
                })
                : Problem(outcome.Problem!);
        })
        .WithSummary("List accounts")
        .WithDescription("Requires `user.read`. `search` matches username or email case-insensitively; `skip`/`take` "
                       + "page the result, and `total` is how many accounts match the same search. Each entry "
                       + "carries the role's code and the permission codes it grants, so the list answers \"what can "
                       + "this account do\" without a second call per row.");

        group.MapPost(string.Empty, async (
            CreateUserRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.Created($"/api/v1/users/{outcome.User!.Id}", outcome.User)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Create an account")
        .WithDescription("Requires `user.manage`. `{username, email, password, role?, preferredLanguage?}`: the "
                       + "password must satisfy the login policy, and `role` defaults to `Owner`. A taken identifier "
                       + "is `400 account_invalid` with the field to fix. Audited as `account.created`.");

        group.MapGet("/{userId:guid}", async (
            Guid userId,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.GetAsync(userId, cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .WithSummary("One account")
        .WithDescription("Requires `user.read`. 404 `user_not_found` for an id no account holds.");

        group.MapPatch("/{userId:guid}", async (
            Guid userId,
            UpdateAccountRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.UpdateAsync(
                userId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Edit an account")
        .WithDescription("Requires `user.manage`. A patch: an omitted member keeps its value. `quietHoursStart` and "
                       + "`quietHoursEnd` are a pair — both empty clears the window, one alone is "
                       + "`400 quiet_hours_incomplete`. The account's time zone is edited from its own screen "
                       + "(`PATCH /auth/me`), where it is validated against the seeded catalogue. Audited as "
                       + "`account.updated`, and a no-op writes nothing.");

        group.MapPost("/{userId:guid}/reset-password", async (
            Guid userId,
            AdminResetPasswordRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.ResetPasswordAsync(
                userId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Replace an account's password")
        .WithDescription("Requires `user.manage`. The new password is validated by the same policy registration "
                       + "uses, and **every session of that account ends**, so a stolen session cannot outlive the "
                       + "reset performed to kill it. Audited as `account.password_reset`.");

        group.MapPatch("/{userId:guid}/role", async (
            Guid userId,
            UpdateUserRoleRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.ChangeRoleAsync(
                userId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Change an account's role")
        .WithDescription("Requires `user.manage`. `role` is a role **code** — `Owner`, `Technician`, `Viewer`, "
                       + "`Admin` or a custom role's code, matched case-insensitively. Refused with "
                       + "`self_change_refused` for the caller's own account and with `last_administrator_refused` "
                       + "when it would leave no enabled account holding `user.manage`. **The target's sessions "
                       + "end**, because permissions travel in the token. Audited as `account.role_changed` with the "
                       + "code before and after; a no-op writes nothing.");

        group.MapPost("/{userId:guid}/disable", async (
            Guid userId,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.SetDisabledAsync(
                userId,
                disabled: true,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Disable an account")
        .WithDescription("Requires `user.manage`. The account can no longer sign in and every session it holds ends "
                       + "at once. Refused with `self_change_refused` for the caller's own account and with "
                       + "`last_administrator_refused` when it would leave no usable administrator. Audited as "
                       + "`account.disabled`. Idempotent: disabling twice changes nothing and records nothing.");

        group.MapPost("/{userId:guid}/enable", async (
            Guid userId,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.SetDisabledAsync(
                userId,
                disabled: false,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Re-enable an account")
        .WithDescription("Requires `user.manage`. Reverses the disable; the account's sessions stay ended, so it "
                       + "signs in again. Audited as `account.enabled`. Idempotent.");

        group.MapDelete("/{userId:guid}", async (
            Guid userId,
            bool? purge,
            HttpContext context,
            ClaimsPrincipal principal,
            UserAdminService users,
            CancellationToken cancellationToken) =>
        {
            var outcome = await users.DeleteAsync(
                userId,
                purge ?? false,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.User) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.UserManage)
        .WithSummary("Remove an account")
        .WithDescription("Requires `user.manage`. Without `?purge=true` this is a **soft delete**: the account is "
                       + "disabled and every row that names it — enclosures, alerts, audit trail — is kept. With "
                       + "`?purge=true` the row is erased, which is allowed only when nothing references it; "
                       + "otherwise `409 account_in_use` names what still does, because a board whose owner no "
                       + "longer exists is worse than an account that is merely disabled. Audited as "
                       + "`account.deleted` with the mode used. Answers the account as it was, so the console can "
                       + "confirm which one went.");

        return app;
    }

    /// <summary>
    /// Maps an account-management failure to RFC 7807. Unlike the authentication group, these messages are meant to
    /// be read by an administrator: a refusal says which rule it hit, because the caller is the one person entitled
    /// to know what the deployment's account rules are.
    /// </summary>
    private static IResult Problem(AuthProblem problem)
    {
        var status = problem.Code switch
        {
            "user_not_found" => StatusCodes.Status404NotFound,
            "self_change_refused" or "last_administrator_refused" or "account_in_use" =>
                StatusCodes.Status409Conflict,
            "role_invalid" or "password_invalid" or "account_invalid" => StatusCodes.Status400BadRequest,
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
