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
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded
                ? Results.Created($"/api/v1/terrariums/{outcome.Terrarium!.Id}", outcome.Terrarium)
                : Problem(outcome.Problem!);
        })
        .RequireAuthorization(AuthorizationPolicies.Owner)
        .WithSummary("Create a terrarium")
        .WithDescription("Requires the Owner role. A device cannot be claimed before a terrarium exists to bind it to.");

        group.MapGet("/{terrariumId:guid}", async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.GetAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Terrarium) : Problem(outcome.Problem!);
        })
        .WithSummary("One terrarium");

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
