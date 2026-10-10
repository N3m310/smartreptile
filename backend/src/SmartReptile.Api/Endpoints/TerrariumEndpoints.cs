using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The terrarium read surface and creation (FR-03, FR-08, FR-09 — roadmap task 2.8). Contract:
/// <c>07-appendices/03</c> §4.2.
/// </summary>
/// <remarks>
/// Every route is authenticated and every query is scoped to the caller. A terrarium that belongs to someone else
/// answers exactly like one that does not exist, so the endpoints cannot be used to enumerate ids (BR-02.2) — which
/// is why there is no <c>forbidden</c> code here.
/// <para>
/// Role gating is deliberately light: reads need a valid token and ownership, and only <c>create</c> names a policy
/// (Owner). The Technician/Viewer split lands with the membership model in M3, because there is nothing for a
/// technician to be a member of yet.
/// </para>
/// </remarks>
public static class TerrariumEndpoints
{
    /// <summary>Maps list, create, detail, readings/latest, readings and coverage.</summary>
    public static IEndpointRouteBuilder MapTerrariumEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/terrariums")
            .WithTags("terrariums")
            .RequireAuthorization();

        // An empty suffix makes the group prefix the whole route, so this answers "/api/v1/terrariums" — the path
        // the clients, the spec and the report snapshots all use.
        group.MapGet(string.Empty, async (
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(principal.GetUserId() ?? Guid.Empty, cancellationToken);

            return outcome.Succeeded
                ? Results.Ok(new { items = outcome.Terrariums })
                : Problem(outcome.Problem!);
        })
        .WithSummary("List the signed-in account's terrariums")
        .WithDescription("Each entry carries the device state, the newest sample time and the open-alert count.");

        group.MapPost(string.Empty, async (
            CreateTerrariumRequest request,
            HttpResponse response,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Created($"/api/v1/terrariums/{outcome.Terrarium!.Id}", outcome.Terrarium);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Create a terrarium")
        .WithDescription("Requires the Owner role. A device cannot be claimed before a terrarium exists to bind it to.");

        group.MapGet("/{terrariumId:guid}", async (
            Guid terrariumId,
            HttpResponse response,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.GetAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Ok(outcome.Terrarium);
        })
        .WithSummary("One terrarium")
        .WithDescription("Carries the rowversion as an `ETag`; send it back as `If-Match` to update the terrarium.");

        group.MapPatch("/{terrariumId:guid}", async (
            Guid terrariumId,
            UpdateTerrariumRequest request,
            HttpRequest httpRequest,
            HttpResponse response,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            // No token, no update: the whole point of the round trip is that the client states which version it
            // edited, and a missing header means it did not read one.
            if (ParseIfMatch(httpRequest.Headers.IfMatch) is not { } expectedRowVersion)
            {
                return PreconditionRequired();
            }

            var outcome = await service.UpdateAsync(
                terrariumId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                expectedRowVersion,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Ok(outcome.Terrarium);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Update a terrarium")
        .WithDescription("Requires the Owner role and an `If-Match` header carrying the current `ETag`. Omitted "
                       + "fields are left unchanged; an empty `location` or `description` clears it. A stale ETag "
                       + "answers `412 precondition_failed`.");

        group.MapDelete("/{terrariumId:guid}", async (
            Guid terrariumId,
            bool? allowUnboundDevice,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DeleteAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                allowUnboundDevice ?? false,
                cancellationToken);

            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Delete a terrarium")
        .WithDescription("Requires the Owner role. A soft delete: readings and alert history survive. When a live "
                       + "device is bound the request must pass `?allowUnboundDevice=true`, which detaches the "
                       + "board; without it the answer is `409 conflict_device_bound`.");

        group.MapGet("/{terrariumId:guid}/thresholds", async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.EffectiveThresholdsAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            var thresholds = outcome.Thresholds!;

            // Named `effectiveThresholds` on the wire, which is the field name `07-appendices/03` §4.2 and
            // BR-10.3 use; the container's member is spelled the same so the two cannot drift.
            return Results.Ok(new
            {
                terrariumId = thresholds.TerrariumId,
                capturedAtUtc = thresholds.CapturedAtUtc,
                timeZoneId = thresholds.TimeZoneId,
                effectiveThresholds = thresholds.EffectiveThresholds,
            });
        })
        .WithSummary("The effective band per metric")
        .WithDescription("Resolves terrarium override → species profile (BR-10.3) at this instant and reports "
                       + "`source` per metric, so a limit can be explained rather than guessed at. A metric with "
                       + "no band for the current phase is absent. Read-only: `PUT`/`DELETE` overrides are not "
                       + "built yet.");

        group.MapGet("/{terrariumId:guid}/readings/latest", async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.LatestReadingsAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Readings) : Problem(outcome.Problem!);
        })
        .WithSummary("The newest reading per metric")
        .WithDescription("Adds the device state and the effective band each value is judged against.");

        group.MapGet("/{terrariumId:guid}/readings", async (
            Guid terrariumId,
            string? metric,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            if (!MetricDictionary.TryParseApiKey(metric, out var metricCode))
            {
                return Problem(new TerrariumProblem(
                    "metric_invalid",
                    $"metric is required and must be one of: {string.Join(", ", MetricDictionary.All.Select(d => d.ApiKey))}."));
            }

            if (from is not { } fromUtc || to is not { } toUtc)
            {
                return Problem(new TerrariumProblem(
                    "invalid_range",
                    "from and to are required ISO-8601 timestamps."));
            }

            var outcome = await service.ReadingsAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                metricCode,
                fromUtc,
                toUtc,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Series) : Problem(outcome.Problem!);
        })
        .WithSummary("Bucketed history for one metric")
        .WithDescription("Bucket size follows the range width — raw ≤ 6 h, 5-minute ≤ 48 h, hourly ≤ 30 d — and is "
                       + "echoed back as `bucket`. Intervals with no data are null points with count 0, never "
                       + "interpolated (BR-09.5).");

        group.MapGet("/{terrariumId:guid}/coverage", async (
            Guid terrariumId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            if (from is not { } fromUtc || to is not { } toUtc)
            {
                return Problem(new TerrariumProblem(
                    "invalid_range",
                    "from and to are required ISO-8601 timestamps."));
            }

            var outcome = await service.CoverageAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                fromUtc,
                toUtc,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Coverage) : Problem(outcome.Problem!);
        })
        .WithSummary("Expected versus received samples")
        .WithDescription("Coverage for a window, derived from the bound device's sampling interval.");

        return app;
    }

    /// <summary>
    /// Maps a read failure to RFC 7807. Only three shapes exist: the terrarium is not yours (or not there), no
    /// device is bound so coverage has no meaning, or the caller sent something the API could not use — which
    /// covers a bad metric and a bad range under one status, the way the contract documents them.
    /// </summary>
    private static IResult Problem(TerrariumProblem problem)
    {
        var status = problem.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "device_not_bound" => StatusCodes.Status409Conflict,
            "conflict_device_bound" => StatusCodes.Status409Conflict,
            "precondition_failed" => StatusCodes.Status412PreconditionFailed,
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

    /// <summary>
    /// Publishes the rowversion as an <c>ETag</c>. Quoted and strong, not weak: the value is the database's own
    /// byte-exact concurrency token, so a weak validator would misdescribe it.
    /// </summary>
    private static void SetETag(HttpResponse response, byte[]? rowVersion)
    {
        if (rowVersion is { Length: > 0 })
        {
            response.Headers.ETag = $"\"{Convert.ToBase64String(rowVersion)}\"";
        }
    }

    /// <summary>
    /// Reads the concurrency token out of <c>If-Match</c>. Null means the header was missing or unusable, which the
    /// caller sees as <c>428</c>; an empty array is the <c>*</c> wildcard, meaning "any current version will do"
    /// and leaving the token unchecked.
    /// </summary>
    private static byte[]? ParseIfMatch(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var value = header.Trim();

        if (value == "*")
        {
            return [];
        }

        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..].Trim();
        }

        value = value.Trim('"');

        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            // A token that is not even base64 cannot match any rowversion, so it is reported as "you did not tell
            // me which version" rather than being decoded into a token that is guaranteed to fail the compare.
            return null;
        }
    }

    /// <summary>An update without <c>If-Match</c>: the client has not stated which version it edited (FR-03).</summary>
    private static IResult PreconditionRequired() =>
        Results.Problem(
            title: "An If-Match header carrying the terrarium's current ETag is required to update it.",
            statusCode: StatusCodes.Status428PreconditionRequired,
            type: "https://smartreptile.example/problems/precondition_required",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "precondition_required",
            });
}
