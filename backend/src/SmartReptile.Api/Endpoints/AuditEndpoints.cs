using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Auditing;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The audit query (FR-18, NFR-12 — roadmap task 3.7). Contract: <c>07-appendices/03</c> §4.5.
/// </summary>
/// <remarks>
/// Owner-gated, and scoped to the caller by the store: the route tells the trail apart from a debug endpoint by
/// answering only with rows the caller owns or caused. The filter vocabulary is canonicalised here rather than in
/// the service, the same way <c>GET /alerts</c> parses its own state and severity, because an unknown value is a
/// client mistake and deserves the list of what would have worked.
/// </remarks>
public static class AuditEndpoints
{
    /// <summary>Maps the trail query.</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit")
            .WithTags("audit")
            .RequireAuthorization(AuthorizationPolicies.Owner);

        group.MapGet(string.Empty, async (
            string? entity,
            string? entityId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? cursor,
            int? pageSize,
            ClaimsPrincipal principal,
            AuditService service,
            CancellationToken cancellationToken) =>
        {
            string? entityFilter = null;

            if (!string.IsNullOrWhiteSpace(entity))
            {
                if (!AuditEntity.TryParse(entity, out var parsed))
                {
                    return Problem(new AuditProblem(
                        "entity_invalid",
                        $"entity must be one of: {string.Join(", ", AuditEntity.All)}."));
                }

                entityFilter = parsed;
            }

            var outcome = await service.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                new AuditQuery(entityFilter, entityId, from, to, cursor, pageSize ?? AuditPaging.DefaultPageSize),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Page) : Problem(outcome.Problem!);
        })
        .WithSummary("Query the audit trail")
        .WithDescription("Requires the Owner role and answers with the caller's own rows: what their account did, "
                       + "plus what anyone did to a device in one of their terrariums. Newest first, and **paged** "
                       + "— `?cursor=&pageSize=` (default 50, max 200) with the next position returned as "
                       + "`nextCursor` and absent on the last page. Filter by `entity` ("
                       + "`Device`, `Alert`, `MetricSilence`), `entityId` and a window `from`/`to`. `before` and "
                       + "`after` are the stored JSON text, not a re-shaped schema.");

        return app;
    }

    /// <summary>
    /// Maps an audit failure to RFC 7807. One shape exists: the caller sent something the API could not use — a
    /// cursor it never issued, an entity it never writes, or a reversed window.
    /// </summary>
    /// <param name="problem">Failure to map.</param>
    private static IResult Problem(AuditProblem problem) =>
        Results.Problem(
            title: problem.Message,
            statusCode: StatusCodes.Status400BadRequest,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = problem.Code,
            });
}
