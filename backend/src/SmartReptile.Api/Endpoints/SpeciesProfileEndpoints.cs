using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Species;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The species-profile catalogue (FR-10 — roadmap 3.9). Contract: <c>07-appendices/03</c> §4.3.
/// </summary>
/// <remarks>
/// Reads need a valid token; every write names the Admin policy, which is the one role allowed to author the
/// catalogue. The catalogue is unpaged on purpose: it is four built-ins plus whatever has been authored, and the
/// picker in <c>POST /terrariums</c> needs all of them in one answer.
/// <para>
/// Create and duplicate answer <c>200</c> with the profile rather than <c>201</c> with a <c>Location</c>: the profile
/// comes back whole, bands included, so a client renders what it just made without a second request. The read-by-id
/// route this surface gained later would have allowed a <c>Location</c>; the answer stays <c>200</c> on purpose,
/// because both writes have always replied in the shape the reads use and a client written against that must not
/// have to change.
/// </para>
/// <para>
/// A built-in is <c>403 builtin_immutable</c>, a profile that belongs to someone else is <c>404 not_found</c>, and a
/// profile a terrarium is assigned to is <c>409 profile_in_use</c>. The three are distinguished because the client
/// does something different with each: offer "duplicate it instead", reload the list, or point at the terrariums.
/// Both refusals are for the ordinary caller: an administrator (the holder of <c>data.write.any</c>) may edit a
/// built-in or another account's profile in place, and the client learns that from <c>canEdit</c> rather than from
/// re-deriving it (TASKS 2026-10-19).
/// </para>
/// <para>
/// The picture is the one part of a profile that is not JSON: it arrives as a multipart part and leaves as bytes,
/// on routes of its own. Both writes answer the whole profile in the same shape the reads use — the threshold edits
/// set that precedent — so a client re-renders from the payload it was just handed instead of reading again.
/// </para>
/// </remarks>
public static class SpeciesProfileEndpoints
{
    /// <summary>Maps list, read, create, duplicate, patch and delete.</summary>
    public static IEndpointRouteBuilder MapSpeciesProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/species-profiles")
            .WithTags("species-profiles")
            .RequireAuthorization(Permissions.SpeciesRead);

        group.MapGet(string.Empty, async (
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.Ok(new SpeciesProfileList(outcome.Profiles!))
                : Problem(outcome.Problem!);
        })
        .Produces<SpeciesProfileList>(StatusCodes.Status200OK)
        .WithSummary("List the species profiles this account may use")
        .WithDescription("The built-in profiles plus the account's own, each with its bands. This is the route that "
                       + "supplies the speciesProfileId POST /api/v1/terrariums requires.");

        group.MapGet("/{profileId:guid}", async (
            Guid profileId,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.GetAsync(
                profileId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("One species profile, bands included")
        .WithDescription("The built-ins and the account's own, the same set the list returns. A profile of another "
                       + "account answers 404 not_found, exactly like a made-up id, so ids cannot be probed "
                       + "(BR-02.2). `canEdit` says whether this caller may patch it: true for a profile of their "
                       + "own, false for a built-in, which is edited by duplicating it.");

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
        .RequireAuthorization(Permissions.SpeciesWrite)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Create a custom species profile")
        .WithDescription("Requires `species.write`. Every band is validated as a whole (BR-10.2); a name this account "
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
        .RequireAuthorization(Permissions.SpeciesWrite)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Copy a profile into one this account owns")
        .WithDescription("Requires `species.write`. The copy is always a custom profile: a built-in is read-only "
                       + "(FR-10 u2), so this is the way to tune one. The source may be a built-in or a profile of "
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
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SpeciesWrite)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Edit a species profile")
        .WithDescription("Requires `species.write`. Omitted fields are left alone; sending bands replaces the whole "
                       + "set. The caller must own the profile, or be an administrator (`data.write.any`), who may "
                       + "edit a built-in or another account's profile too. Two requests racing on one profile answer "
                       + "409 profile_conflict, which asks the editor to reload.");

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
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.NoContent()
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SpeciesWrite)
        .Produces(StatusCodes.Status204NoContent)
        .WithSummary("Delete a species profile")
        .WithDescription("Requires `species.write`. The caller must own the profile, or be an administrator, who may "
                       + "delete a built-in too. A profile assigned to a terrarium answers 409 profile_in_use, "
                       + "including terrariums that were soft-deleted, because their rows still point at it.");

        group.MapPost("/{profileId:guid}/image", async (
            Guid profileId,
            IFormFile? file,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            // Refused here rather than in the service because "the part is missing" is a fact about the multipart
            // body, which the transport is the only layer that can see.
            if (file is null)
            {
                return Problem(new SpeciesProfileProblem(
                    "image_required",
                    "A picture is required: send one file part named 'file'.",
                    [new IdentityViolation("file", "image_required", "Choose a picture.")]));
            }

            var outcome = await service.SetImageAsync(
                profileId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                new ProfileImageUpload(file.OpenReadStream(), file.ContentType),
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SpeciesWrite)
        // The credential here is a bearer token, never a cookie, so there is no ambient authority for a cross-site
        // request to borrow: the antiforgery token the framework would demand protects a session this API does not
        // have. Nothing else on this surface is exempt, because nothing else takes a form body.
        .DisableAntiforgery()
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Set or replace a profile's picture")
        .WithDescription("Requires `species.write`. `multipart/form-data` with one part named `file` — PNG, JPEG or "
                       + "WebP, at most 2 MB, and the type is read from the bytes rather than from the part's own "
                       + "Content-Type. Answers the updated profile, the same shape the reads return, so a client "
                       + "renders the new picture from one payload. A built-in answers 403 builtin_immutable.");

        group.MapGet("/{profileId:guid}/image", async (
            Guid profileId,
            HttpResponse response,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.OpenImageAsync(
                profileId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            var image = outcome.Image!;

            // Private, because the route needs the caller's token: a shared cache holding a picture it cannot
            // re-authorise would serve it to whoever asks next. `max-age` is short and the URL carries
            // `?v={imageUpdatedAt}` in the view, so a replaced picture is fetched rather than remembered.
            response.Headers.CacheControl = "private, max-age=300";

            return Results.File(image.Content, image.ContentType, lastModified: image.UpdatedAt);
        })
        .Produces(StatusCodes.Status200OK, contentType: "image/png")
        .WithSummary("A profile's picture")
        .WithDescription("The built-in profiles and the account's own, exactly the set the list returns. Needs the "
                       + "caller's bearer token like every other read, so a client fetches the bytes rather than "
                       + "pointing an `img` tag at this URL. `404 image_not_set` when the profile has no picture, "
                       + "and `404 not_found` when the profile is not one the caller may see.");

        group.MapDelete("/{profileId:guid}/image", async (
            Guid profileId,
            HttpContext context,
            ClaimsPrincipal principal,
            SpeciesProfileService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.RemoveImageAsync(
                profileId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken,
                principal.CanEditAnySpeciesProfile());

            return outcome.Succeeded
                ? Results.Ok(outcome.Profile)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SpeciesWrite)
        .Produces<SpeciesProfileView>(StatusCodes.Status200OK)
        .WithSummary("Remove a profile's picture")
        .WithDescription("Requires `species.write`. Idempotent: a profile with no picture is not an error, and "
                       + "nothing is written to the audit trail because nothing changed. Answers the updated "
                       + "profile rather than a 204, since the two picture routes answer the same shape and a "
                       + "client re-renders from one payload.");

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
            "profile_conflict" => StatusCodes.Status409Conflict,
            "not_found" => StatusCodes.Status404NotFound,
            // A visible profile with no picture: the same 404 status as a profile that does not exist, because the
            // client's request succeeded in both cases and the caches should treat them alike — but a code of its
            // own, because "nothing to show" and "no such profile" are different sentences.
            "image_not_set" => StatusCodes.Status404NotFound,
            "unauthenticated" => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest,
        };

        var extensions = ApiProblem.Extensions(problem.Code, problem.Errors);

        return Results.Problem(
            title: problem.Message,
            statusCode: status,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: extensions);
    }
}
