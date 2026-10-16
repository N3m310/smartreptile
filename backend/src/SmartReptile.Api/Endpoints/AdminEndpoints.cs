using SmartReptile.Application.Admin;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The <c>/api/v1/admin</c> group: the administrator's get-all dashboard (TASKS 2026-10-14, item 13).
/// </summary>
/// <remarks>
/// <b>Why a group of its own rather than a query parameter on the account's own routes.</b> Everything here reads
/// other accounts' data, and the rest of the API is written so that it cannot: a keeper's list query is scoped to
/// <c>UserId</c> in the store, and a flag that widened it would put the two behaviours one branch apart. These four
/// routes therefore go to a store that has no owner parameter at all, and every one of them names
/// <see cref="Permissions.DataReadAny"/>.
/// <para>
/// Account management is deliberately <em>not</em> here: those routes live under <c>/api/v1/users</c>, where they are
/// gated by <c>user.read</c>/<c>user.manage</c> and can therefore be granted to an auditor role without granting
/// sight of every enclosure in the deployment. The dashboard calls both groups; the split is what makes that possible.
/// </para>
/// </remarks>
public static class AdminEndpoints
{
    /// <summary>Maps the overview and the three cross-account lists.</summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("admin")
            .RequireAuthorization(Permissions.DataReadAny);

        group.MapGet("/overview", async (AdminService admin, CancellationToken cancellationToken) =>
            Results.Ok(await admin.OverviewAsync(cancellationToken)))
        .WithSummary("The dashboard's header and its get-all table")
        .WithDescription("Requires `data.read.any`. One answer with everything the first screen needs: "
                       + "deployment-wide counts (accounts, enclosures, boards, open alerts split by severity and by "
                       + "the two families, species profiles), every account with its holdings, and the description "
                       + "of each family — which metric keys belong to it and how many of its alerts are open, so a "
                       + "client can label its two tabs without carrying a list of metric keys of its own.");

        group.MapGet("/terrariums", async (
            string? search,
            int? skip,
            int? take,
            AdminService admin,
            CancellationToken cancellationToken) =>
        {
            var page = await admin.ListTerrariumsAsync(search, skip, take, cancellationToken);

            return Results.Ok(page);
        })
        .WithSummary("List every account's enclosures")
        .WithDescription("Requires `data.read.any`. Newest first, paged with `skip`/`take` (default 50, max 200), "
                       + "and `total` counts the whole filtered set. `search` matches the enclosure's name or its "
                       + "owner's username. Each row carries the owner, the species profile in force, the bound "
                       + "board's status and last-seen instant, and how many alerts are open on it. Enclosures whose "
                       + "owner deleted them are not listed — the same rule every other read follows.");

        group.MapGet("/devices", async (
            string? search,
            int? skip,
            int? take,
            AdminService admin,
            CancellationToken cancellationToken) =>
        {
            var page = await admin.ListDevicesAsync(search, skip, take, cancellationToken);

            return Results.Ok(page);
        })
        .WithSummary("List every board in the deployment")
        .WithDescription("Requires `data.read.any`. Last seen first, with the boards that have never published "
                       + "last, paged with `skip`/`take`. `search` matches the public id, the device name or the "
                       + "owner's username. Unclaimed boards are included — an administrator looking for a board "
                       + "that was never set up would not find it in a list of bound ones.");

        group.MapGet("/alerts", async (
            string? family,
            string? state,
            int? skip,
            int? take,
            AdminService admin,
            CancellationToken cancellationToken) =>
        {
            // Same vocabulary as the keeper's own alert list, so a client parses one thing: an unknown key is a bad
            // request, and an absent one means "both families".
            AlertFamily? familyFilter = null;

            if (!string.IsNullOrWhiteSpace(family))
            {
                if (!AlertFamilies.TryParseApiKey(family, out var parsedFamily))
                {
                    return Problem(
                        "family_invalid",
                        $"family must be one of: {string.Join(", ", AlertFamilies.All.Select(AlertFamilies.ApiKey))}.");
                }

                familyFilter = parsedFamily;
            }

            AlertState? stateFilter = null;

            if (!string.IsNullOrWhiteSpace(state))
            {
                if (!Enum.TryParse<AlertState>(state, ignoreCase: true, out var parsedState)
                    || !Enum.IsDefined(parsedState))
                {
                    return Problem(
                        "state_invalid",
                        $"state must be one of: {string.Join(", ", Enum.GetNames<AlertState>())}.");
                }

                stateFilter = parsedState;
            }

            var page = await admin.ListAlertsAsync(familyFilter, stateFilter, skip, take, cancellationToken);

            return Results.Ok(page);
        })
        .WithSummary("List every account's alerts, both families")
        .WithDescription("Requires `data.read.any`. Newest first, paged with `skip`/`take`, filtered by `family` "
                       + "(enclosure or species) and `state` (Open, Acknowledged, Resolved). Each row names its "
                       + "owner and enclosure, which is what makes the two families reviewable across the "
                       + "deployment rather than one account at a time.");

        return app;
    }

    /// <summary>Maps a filter refusal to RFC 7807, in the same shape the rest of the API uses.</summary>
    private static IResult Problem(string code, string message) =>
        Results.Problem(
            title: message,
            statusCode: StatusCodes.Status400BadRequest,
            type: $"https://smartreptile.example/problems/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
