using System.Security.Claims;
using SmartReptile.Api.Middleware;
using SmartReptile.Api.Security;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The device-onboarding endpoints of UC-01 (FR-04, FR-16). Contract: <c>07-appendices/03</c> §2 and §4.4.
/// </summary>
/// <remarks>
/// Every route identifies a device by its <b>public id</b> (<c>sr-3f9a2c</c>) rather than its surrogate key,
/// because the public id is the only identifier this API ever hands out — a client cannot be asked to know a
/// GUID it was never given.
/// </remarks>
public static class DeviceEndpoints
{
    /// <summary>Maps self-register, claim, rotate-secret and revoke.</summary>
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/devices")
            .WithTags("devices")
            .RequireAuthorization(Permissions.DeviceRead);

        group.MapGet(string.Empty, async (
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(new { items = outcome.Devices }) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.DeviceRead)
        .WithSummary("List the signed-in account's boards")
        .WithDescription("Requires `device.read`. Each entry carries the identity, the settings, the stored "
                       + "lifecycle state and the derived online/offline badge, plus the enclosure it is bound to. A "
                       + "board that registered but was never claimed belongs to no fleet yet and is absent.")
        .Produces(StatusCodes.Status200OK);

        group.MapGet("/{deviceId}", async (
            string deviceId,
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.GetAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Device) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.DeviceRead)
        .WithSummary("One board")
        .WithDescription("Requires `device.read`. Identified by public id (`sr-3f9a2c`). A board belonging to "
                       + "another account answers `404 not_found`, identically to one that does not exist.");

        group.MapPatch("/{deviceId}", async (
            string deviceId,
            UpdateDeviceRequest request,
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.UpdateAsync(
                deviceId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Device) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.DeviceWrite)
        .WithSummary("Rename a board or change its intervals")
        .WithDescription("Requires `device.write`. A patch: an omitted field keeps its value. `deviceName` is at "
                       + "most 60 characters (an empty string restores the default label) and both intervals are "
                       + "10–300 seconds. Changing an interval is a request the board picks up on its next "
                       + "connection — the API stores the desired value rather than reaching into the node. Not "
                       + "audited: `BR-18.4` names the credential operations, and this is the ordinary use of a "
                       + "settings screen.");

        group.MapDelete("/{deviceId}", async (
            string deviceId,
            HttpContext context,
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.DeleteAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context));

            return outcome.Succeeded ? Results.Ok(outcome.Device) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.DeviceWrite)
        .WithSummary("Remove a board from the account's fleet")
        .WithDescription("Requires `device.write`. Revokes every credential, closes the live session and detaches "
                       + "the board from its enclosure, so the DI-04 slot is free for a replacement — the same "
                       + "credential effect as `POST /devices/{id}/revoke`, plus the unbinding. **Not a row "
                       + "delete**: the readings, health reports, events and audit rows that name the board survive, "
                       + "which is why the answer describes the revoked board rather than being empty. Recorded as "
                       + "`device.revoked` with `detached: true`.");

        // Anonymous by necessity — the board has no credential yet — so it carries the strongest anti-abuse
        // limits in the system, applied in the service (§02-design/06 §5).
        group.MapPost("/self-register", async (
            SelfRegisterRequest request,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.SelfRegisterAsync(request, RequestActor.ClientAddress(context), cancellationToken);

            if (outcome.Problem is { } problem)
            {
                return Problem(problem);
            }

            var result = outcome.Registration!;

            var body = new
            {
                deviceId = result.DeviceId,
                claimCode = result.ClaimCode,
                expiresAtUtc = result.ExpiresAtUtc,
            };

            // A repeat is a 409 that still carries a fresh code: a board that rebooted before claiming can
            // recover, while the log shows the repeat instead of it looking like a first registration.
            return result.AlreadyRegistered
                ? Results.Json(body, statusCode: StatusCodes.Status409Conflict)
                : Results.Json(body, statusCode: StatusCodes.Status201Created);
        })
        .AllowAnonymous()
        .WithSummary("Register a board and obtain a claim code")
        .WithDescription("Anonymous and rate-limited. Re-registering an unclaimed board re-issues its code; "
                       + "a board that is already claimed answers 409 with its deviceId and no code.");

        group.MapPost("/claim", async (
            ClaimRequest request,
            ClaimsPrincipal principal,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.ClaimAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context));

            if (outcome.Problem is { } problem)
            {
                return Problem(problem);
            }

            var claim = outcome.Claim!;

            return Results.Ok(new
            {
                deviceId = claim.DeviceId,
                secret = claim.Secret,
                terrariumId = claim.TerrariumId,
                boundAtUtc = claim.BoundAtUtc,
            });
        })
        .RequireAuthorization(Permissions.DeviceProvision)
        .WithSummary("Claim a device for a terrarium")
        .WithDescription("Returns the device secret exactly once; only its hash is stored. Requires `device.provision`.");

        group.MapPost("/claim-by-mac", async (
            ClaimByMacRequest request,
            ClaimsPrincipal principal,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.ClaimByMacAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context));

            if (outcome.Problem is { } problem)
            {
                return Problem(problem);
            }

            var claim = outcome.Claim!;

            return Results.Ok(new
            {
                deviceId = claim.DeviceId,
                secret = claim.Secret,
                terrariumId = claim.TerrariumId,
                boundAtUtc = claim.BoundAtUtc,
            });
        })
        .RequireAuthorization(Permissions.DeviceProvision)
        .WithSummary("Claim a device by the MAC address printed on it")
        .WithDescription("The lower-trust pairing path: a MAC is not a secret, so this route is for a board the "
                       + "caller has in hand when the claim code cannot be read off its screen. The board must have "
                       + "self-registered already, must not be bound, and must not be revoked; a MAC that is not "
                       + "claimable answers a single `device_not_found`, so the route cannot be used to test which "
                       + "addresses belong to this fleet. Returns the secret exactly once, like `claim`, and records "
                       + "on the audit row that the MAC path was used. Requires `device.provision`.");

        group.MapPost("/{deviceId}/rotate-secret", async (
            string deviceId,
            ClaimsPrincipal principal,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.RotateSecretAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context));

            if (outcome.Problem is { } problem)
            {
                return Problem(problem);
            }

            var rotation = outcome.Rotation!;

            return Results.Ok(new
            {
                deviceId = rotation.DeviceId,
                secret = rotation.Secret,
                previousUsableUntilUtc = rotation.PreviousUsableUntilUtc,
            });
        })
        .RequireAuthorization(Permissions.DeviceProvision)
        .WithSummary("Rotate a device secret")
        .WithDescription("Returns the replacement once. The previous secret keeps working through a 10-minute grace window.");

        group.MapPost("/{deviceId}/revoke", async (
            string deviceId,
            ClaimsPrincipal principal,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.RevokeAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context));

            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.DeviceProvision)
        .WithSummary("Revoke a device")
        .WithDescription("Invalidates every credential. Idempotent. Requires `device.provision`.");

        return app;
    }

    /// <summary>
    /// Maps an onboarding failure to RFC 7807. <c>claim_code_invalid</c>, <c>device_not_found</c> and
    /// <c>not_found</c> deliberately share the 404 class so a caller cannot tell a wrong code from a missing
    /// terrarium, a foreign terrarium from a non-existent one (BR-02.2, BR-04.3), or — on the MAC path — an unknown
    /// address from one that is already bound.
    /// </summary>
    private static IResult Problem(DeviceProblem problem)
    {
        var status = problem.Code switch
        {
            "rate_limited" => StatusCodes.Status429TooManyRequests,
            "registration_invalid" => StatusCodes.Status400BadRequest,
            "device_already_registered" or "terrarium_already_bound" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status404NotFound,
        };

        var extensions = ApiProblem.Extensions(problem.Code, problem.Errors);

        if (problem.DeviceId is { } deviceId)
        {
            extensions["deviceId"] = deviceId;
        }

        return Results.Problem(
            title: problem.Message,
            statusCode: status,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: extensions);
    }
}
