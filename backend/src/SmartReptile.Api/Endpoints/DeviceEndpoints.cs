using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Devices;

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
        var group = app.MapGroup("/api/v1/devices").WithTags("devices");

        // Anonymous by necessity — the board has no credential yet — so it carries the strongest anti-abuse
        // limits in the system, applied in the service (§02-design/06 §5).
        group.MapPost("/self-register", async (
            SelfRegisterRequest request,
            HttpContext context,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.SelfRegisterAsync(request, ClientAddress(context), cancellationToken);

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
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.ClaimAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

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
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Claim a device for a terrarium")
        .WithDescription("Returns the device secret exactly once; only its hash is stored. Requires the Owner role.");

        group.MapPost("/{deviceId}/rotate-secret", async (
            string deviceId,
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.RotateSecretAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

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
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Rotate a device secret")
        .WithDescription("Returns the replacement once. The previous secret keeps working through a 10-minute grace window.");

        group.MapPost("/{deviceId}/revoke", async (
            string deviceId,
            ClaimsPrincipal principal,
            DeviceProvisioningService provisioning,
            CancellationToken cancellationToken) =>
        {
            var outcome = await provisioning.RevokeAsync(
                deviceId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Revoke a device")
        .WithDescription("Invalidates every credential. Idempotent. Requires the Owner role.");

        return app;
    }

    /// <summary>
    /// Maps an onboarding failure to RFC 7807. <c>claim_code_invalid</c> and <c>not_found</c> deliberately share
    /// the 404 class so a caller cannot tell a wrong code from a missing terrarium, or a foreign terrarium from a
    /// non-existent one (BR-02.2, BR-04.3).
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

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = problem.Code,
        };

        if (problem.DeviceId is { } deviceId)
        {
            extensions["deviceId"] = deviceId;
        }

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

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
