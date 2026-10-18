using SmartReptile.Application.Reference;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The reference-data reads a client needs before it can render a form (TASKS 2026-10-14, item 11). Contract:
/// <c>07-appendices/03</c> §4.3.
/// </summary>
/// <remarks>
/// A group of its own rather than a route under a feature, because the data is not a feature's: the same catalogue
/// fills the terrarium form and the account form, and it will fill the next one too. Read-only, and readable by
/// every authenticated account — a picker is not privileged data, and a client that cannot read it cannot create
/// anything.
/// </remarks>
public static class ReferenceEndpoints
{
    /// <summary>Maps the reference-data reads: currently the time-zone catalogue.</summary>
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("reference")
            .RequireAuthorization(Permissions.TerrariumRead);

        group.MapGet("/time-zones", async (ReferenceService reference, CancellationToken cancellationToken) =>
        {
            var zones = await reference.ListTimeZonesAsync(cancellationToken);

            return Results.Ok(new { items = zones });
        })
        .WithSummary("The time-zone catalogue")
        .WithDescription("Every zone a terrarium or an account may name, ordered west to east, each with the value "
                       + "to send back (`ianaId`), a label, its country and its standard offset — pre-formatted as "
                       + "`utcOffset` and in minutes as `utcOffsetMinutes`. Send `ianaId` as `timeZoneId` on "
                       + "`POST /api/v1/terrariums` or `PATCH /api/v1/auth/me`; anything else is refused as "
                       + "`terrarium_timezone_invalid`.");

        return app;
    }
}
