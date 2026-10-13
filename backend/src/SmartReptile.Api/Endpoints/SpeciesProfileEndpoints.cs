using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Species;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The species-profile catalogue (FR-10 — roadmap 3.9). Contract: <c>07-appendices/03</c> §4.3.
/// </summary>
/// <remarks>
/// Reads need a valid token; every write names the Owner policy, which is what the contract's <c>O</c> column means.
/// The catalogue is unpaged on purpose: it is four built-ins plus whatever the account authored, and the picker in
/// <c>POST /terrariums</c> needs all of them in one answer.
/// <para>
/// Create and duplicate answer <c>200</c> with the profile rather than <c>201</c> with a <c>Location</c>, because
/// the contract has no read-by-id route to point one at. The profile comes back whole, bands included, so a client
/// can render what it just made without that route ever existing — the reading the silence routes took for the same
/// reason.
/// </para>
/// <para>
/// A built-in is <c>403 builtin_immutable</c>, a profile that belongs to someone else is <c>404 not_found</c>, and a
/// profile a terrarium is assigned to is <c>409 profile_in_use</c>. The three are distinguished because the client
/// does something different with each: offer "duplicate it instead", reload the list, or point at the terrariums.
/// </para>
/// </remarks>
public static class SpeciesProfileEndpoints
{
    /// <summary>Maps list, create, duplicate, patch and delete.</summary>
    public static IEndpointRouteBuilder MapSpeciesProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/species-profiles")
            .WithTags("species-profiles")
            .RequireAuthorization();

        group.MapGet(string.Empty, async (
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(principal.GetUserId() ?? Guid.Empty, cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(new SpeciesProfileList(outcome.Profiles!))
                : Problem(outcome.Problem!);
        })
        .Produces<SpeciesProfileList>(StatusCodes.Status200OK)
        .WithSummary("List the species profiles this account may use")
        .WithDescription("The built-in profiles plus the account's own, each with its bands. This is the route that "
                       + "supplies the speciesProfileId POST /api/v1/terrariums requires.");

        group.MapPost(string.Empty, async (
            CreateSpeciesProfileRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Create a custom species profile")
        .WithDescription("Requires the Owner role. Every band is validated as a whole (BR-10.2); a name this account "
                       + "already uses is a 409 profile_name_taken.");

        group.MapPost("/{profileId:guid}/duplicate", async (
            Guid profileId,
            DuplicateSpeciesProfileRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DuplicateAsync(
                profileId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Copy a profile into one this account owns")
        .WithDescription("Requires the Owner role. The copy is always a custom profile: a built-in is read-only "
                       + "(FR-10 A2), so this is the way to tune one. The source may be a built-in or a profile of "
                       + "the caller's own.");

        group.MapPatch("/{profileId:guid}", async (
            Guid profileId,
            UpdateSpeciesProfileRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.UpdateAsync(
                profileId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Edit one of this account's own profiles")
        .WithDescription("Requires the Owner role. Omitted fields are left alone; sending bands replaces the whole "
                       + "set. A built-in answers 403 builtin_immutable.");

        group.MapDelete("/{profileId:guid}", async (
            Guid profileId,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DeleteAsync(
                profileId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded
                ? Results.NoContent()
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .Produces(StatusCodes.Status204NoContent)
        .WithSummary("Delete one of this account's own profiles")
        .WithDescription("Requires the Owner role. A profile assigned to a terrarium answers 409 profile_in_use, "
                       + "including terrariums that were soft-deleted, because their rows still point at it.");

        return app;
    }

    /// <summary>
    /// Maps a profile failure to RFC 7807. The status follows the code rather than the message: a validation failure
    /// is a 400, a built-in that cannot be edited is a 403, a name already in use and a profile still in use are
    /// both 409, and anything else is a 404 — the one answer a profile that is missing, soft-deleted or someone
    /// else's all share (BR-02.2).
    /// </summary>
    private static IResult Problem(SpeciesProfileProblem problem)
    {
        var status = problem.Code switch
        {
            "builtin_immutable" => StatusCodes.Status403Forbidden,
            "profile_in_use" => StatusCodes.Status409Conflict,
            "profile_name_taken" => StatusCodes.Status409Conflict,
            "not_found" => StatusCodes.Status404NotFound,
            "unauthenticated" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = problem.Code,
        };

        if (problem.Errors is { Count: > 0 } errors)
        {
            extensions["errors"] = errors
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
