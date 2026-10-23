using System.ComponentModel;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using SmartReptile.Api.Security;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The terrarium read surface and creation (FR-03, FR-08, FR-09 — roadmap task 2.8). Contract:
/// <c>07-appendices/03</c> §4.2.
/// </summary>
/// <remarks>
/// Every route is authenticated and every query is scoped to the caller. A terrarium that belongs to someone else
/// is refused with <c>403 forbidden_terrarium</c>, and one that was deleted with <c>410 terrarium_deleted</c>, so a
/// keeper can tell a mistyped id from an enclosure that is not theirs or is gone. This narrows BR-02.2 — whose
/// uniform <c>404</c> the other resources still answer — for this one resource, on the review request recorded in
/// TASKS_2026-10-14 items 3 and 5. Reads name <c>terrarium.read</c> and the writes <c>terrarium.write</c>, and
/// "administrator" is no longer a case the routes know about: the cross-account answer is now the
/// <c>data.read.any</c>/<c>data.write.any</c> permissions, which the seeds grant to the administrator role and to
/// nothing else.
/// <para>
/// Role gating is deliberately light: the two mutations and the threshold writes name a policy, and nothing else
/// does. The scoping is the security boundary here — a role grants what may be asked for, never whose data is
/// answered.
/// </para>
/// </remarks>
public static class TerrariumEndpoints
{
    /// <summary>Maps list, create, detail, readings/latest, readings and coverage.</summary>
    public static IEndpointRouteBuilder MapTerrariumEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/terrariums")
            .WithTags("terrariums")
            .RequireAuthorization(Permissions.TerrariumRead);

        // An empty suffix makes the group prefix the whole route, so this answers "/api/v1/terrariums" — the path
        // the clients, the spec and the report snapshots all use.
        group.MapGet(string.Empty, async (
            Guid? speciesProfileId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount(),
                speciesProfileId);

            return outcome.Succeeded
                ? Results.Ok(new { items = outcome.Terrariums })
                : Problem(outcome.Problem!);
        })
        .WithSummary("List the signed-in account's terrariums")
        .WithDescription("Each entry carries the device state, the newest sample time and the open-alert count. An "
                       + "administrator sees every account's terrariums, and each entry then names its owner "
                       + "(`ownerUserId`). `?speciesProfileId=` narrows the answer to the enclosures assigned to one "
                       + "species profile; it filters the set the caller may already see, so an unknown or foreign "
                       + "profile answers an empty list rather than someone else's terrariums.");

        group.MapPost(string.Empty, async (
            CreateTerrariumRequest request,
            HttpResponse response,
            HttpContext context,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CreateAsync(
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                RequestActor.From(context),
                principal.CanWriteAnyAccount());

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Created($"/api/v1/terrariums/{outcome.Terrarium!.Id}", TerrariumPayload(outcome));
        })
        .RequireAuthorization(Permissions.TerrariumWrite)
        .WithSummary("Create a terrarium")
        .WithDescription("Requires `terrarium.write`. Name a board with `deviceId` (its public id, `sr-3f9a2c`) or "
                       + "`macAddress` to bind it in the same request — the two are one act, because a board with no "
                       + "credential can neither report nor be claimed afterwards, so the response carries the "
                       + "one-time `deviceSecret` exactly as `POST /devices/claim` does. Leaving both out is the "
                       + "behaviour that existed before: create the enclosure, claim a board later. A board that is "
                       + "unknown, already bound or revoked is refused before anything is written. The created item "
                       + "comes with the version to update it from: the `ETag` header and the `rowVersion` field "
                       + "carry the same value. **An account holding `data.write.any` creates an unowned enclosure**: it "
                       + "appears in no keeper's list until the owner is assigned on `PATCH /terrariums/{id}`.");

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
                cancellationToken,
                principal.CanReadAnyAccount());

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Ok(TerrariumPayload(outcome));
        })
        .WithSummary("One terrarium")
        .WithDescription("This is the route to read the version an update has to send back: it answers the rowversion "
                       + "as an `ETag` response header and, for a client that cannot read headers or is calling from "
                       + "Swagger UI, as the `rowVersion` field of the body. Send that value as `If-Match` on "
                       + "`PATCH /api/v1/terrariums/{id}`. An id that belongs to another account is `403 "
                       + "forbidden_terrarium`, and one that was deleted is `410 terrarium_deleted` — except for an "
                       + "administrator, who reads any account's terrarium.");

        group.MapPatch("/{terrariumId:guid}", async (
            Guid terrariumId,
            UpdateTerrariumRequest request,
            [FromHeader(Name = "If-Match")]
            [Description("The terrarium's current version: the `ETag` response header or the `rowVersion` field of "
                       + "`GET /api/v1/terrariums/{id}`, with or without the surrounding quotes. `*` accepts any "
                       + "version. Required — without it the answer is `428 precondition_required`.")]
            string? ifMatch,
            HttpResponse response,
            HttpContext context,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            // No token, no update: the whole point of the round trip is that the client states which version it
            // edited, and a missing header means it did not read one.
            if (ParseIfMatch(ifMatch) is not { } expectedRowVersion)
            {
                return PreconditionRequired();
            }

            var outcome = await service.UpdateAsync(
                terrariumId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                expectedRowVersion,
                cancellationToken,
                principal.CanWriteAnyAccount(),
                RequestActor.From(context));

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            SetETag(response, outcome.RowVersion);
            return Results.Ok(TerrariumPayload(outcome));
        })
        .RequireAuthorization(Permissions.TerrariumWrite)
        .WithSummary("Update a terrarium")
        .WithDescription("Requires `terrarium.write`. Needs an `If-Match` header carrying the "
                       + "current version. Read it first — `GET /api/v1/terrariums/{id}` answers it as both the "
                       + "`ETag` response header and the `rowVersion` field of the body, and this route answers the "
                       + "new one the same two ways. Omitted fields are left unchanged; an empty `location` or "
                       + "`description` clears it. A missing or unparseable value answers `428 "
                       + "precondition_required`, a stale one `412 precondition_failed`, and `*` accepts any "
                       + "version. An administrator editing another account's terrarium is recorded in the audit "
                       + "trail.");

        group.MapDelete("/{terrariumId:guid}", async (
            Guid terrariumId,
            bool? allowUnboundDevice,
            HttpContext context,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DeleteAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                allowUnboundDevice ?? false,
                cancellationToken,
                principal.CanWriteAnyAccount(),
                RequestActor.From(context));

            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.TerrariumWrite)
        .WithSummary("Delete a terrarium")
        .WithDescription("Requires `terrarium.write`. A soft delete: the row and its readings and "
                       + "alert history survive, the enclosure is hidden from reads, and a later read of its id "
                       + "answers `410 terrarium_deleted`. When a live device is bound the request must pass "
                       + "`?allowUnboundDevice=true`, which detaches the board; without it the answer is `409 "
                       + "conflict_device_bound`. An administrator deleting another account's terrarium is recorded "
                       + "in the audit trail.");

        group.MapGet("/{terrariumId:guid}/thresholds", async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.EffectiveThresholdsAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(ThresholdsPayload(outcome.Thresholds!)) : Problem(outcome.Problem!);
        })
        .WithSummary("The effective band per metric")
        .WithDescription("Resolves terrarium override → species profile (BR-10.3) at this instant and reports "
                       + "`source` per metric, so a limit can be explained rather than guessed at. A metric with "
                       + "no band for the current phase is absent. The administrator may read any account's "
                       + "terrarium; anyone else gets `403 forbidden_terrarium` for one that is not theirs.");

        group.MapPut("/{terrariumId:guid}/thresholds", async (
            Guid terrariumId,
            UpdateThresholdsRequest request,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.UpsertThresholdsAsync(
                terrariumId,
                request,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanWriteAnyAccount());

            return outcome.Succeeded ? Results.Ok(ThresholdsPayload(outcome.Thresholds!)) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.TerrariumWrite)
        .WithSummary("Write band overrides")
        .WithDescription("Requires `terrarium.write`. Upserts the band for each (metric, phase) the body names and "
                       + "answers with the resulting effective set — the same shape `GET` returns. Every band is "
                       + "validated before anything is written, so one incoherent band changes nothing. A change "
                       + "records a `ThresholdSnapshot`, which is how an alert already open keeps the band it was "
                       + "judged against. A band identical to the stored one is a no-op and writes no snapshot.");

        group.MapDelete("/{terrariumId:guid}/thresholds/{metric}", async (
            Guid terrariumId,
            string metric,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.DeleteThresholdOverrideAsync(
                terrariumId,
                metric,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanWriteAnyAccount());

            return outcome.Succeeded ? Results.Ok(ThresholdsPayload(outcome.Thresholds!)) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.TerrariumWrite)
        .WithSummary("Revert one metric to its profile band")
        .WithDescription("Requires `terrarium.write`. Removes every override the terrarium holds for the metric, "
                       + "whichever phase, and answers with the resulting effective set. Idempotent: a metric that "
                       + "carries no override is not an error.");

        group.MapGet("/{terrariumId:guid}/readings/latest", async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            TerrariumService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.LatestReadingsAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount());

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
                cancellationToken,
                principal.CanReadAnyAccount());

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
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(outcome.Coverage) : Problem(outcome.Problem!);
        })
        .WithSummary("Expected versus received samples")
        .WithDescription("Coverage for a window, derived from the bound device's sampling interval.");

        return app;
    }

    /// <summary>
    /// Shapes the effective set for the wire. It is named <c>effectiveThresholds</c> because that is the field name
    /// `07-appendices/03` §4.2 and BR-10.3 use, and the write routes answer with the same shape as the read so a
    /// client re-renders its editor from one payload rather than two.
    /// </summary>
    private static object ThresholdsPayload(TerrariumThresholds thresholds) => new
    {
        terrariumId = thresholds.TerrariumId,
        capturedAtUtc = thresholds.CapturedAtUtc,
        timeZoneId = thresholds.TimeZoneId,
        effectiveThresholds = thresholds.EffectiveThresholds,
    };

    /// <summary>
    /// Maps a read failure to RFC 7807. Ownership has three answers rather than BR-02.2's one: the terrarium is not
    /// there (<c>404</c>), it is someone else's (<c>403</c>) or it was deleted (<c>410</c>). Beyond those: no device
    /// is bound so coverage has no meaning, a live one blocks a delete, the caller's version is stale, or the caller
    /// sent something the API could not use — which covers a bad metric and a bad range under one status, the way the
    /// contract documents them.
    /// </summary>
    private static IResult Problem(TerrariumProblem problem)
    {
        var status = problem.Code switch
        {
            "unauthenticated" => StatusCodes.Status401Unauthorized,
            "forbidden_terrarium" => StatusCodes.Status403Forbidden,
            "not_found" => StatusCodes.Status404NotFound,
            "device_not_bound" => StatusCodes.Status409Conflict,
            "conflict_device_bound" => StatusCodes.Status409Conflict,
            "terrarium_deleted" => StatusCodes.Status410Gone,
            "precondition_failed" => StatusCodes.Status412PreconditionFailed,
            _ => StatusCodes.Status400BadRequest,
        };

        var extensions = ApiProblem.Extensions(problem.Code, problem.Errors);

        return Results.Problem(
            title: problem.Message,
            statusCode: status,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: extensions);
    }

    /// <summary>
    /// Shapes one terrarium for the body, with the rowversion the client sends back as <c>If-Match</c> beside it.
    /// </summary>
    /// <remarks>
    /// The value is published twice on purpose. A standards-following client reads the <c>ETag</c> header and never
    /// looks here; Swagger UI renders no header box for this route and shows no response headers, so before the
    /// field existed the route could be read but not legitimately updated from it (TASKS 2026-10-14, item 4).
    /// Plain base64 rather than the quoted header form, because a quoted value has to be unescaped to be pasted
    /// back, and <c>ParseIfMatch</c> accepts it either way.
    /// </remarks>
    private static TerrariumSummary TerrariumPayload(TerrariumOutcome outcome) =>
        outcome.RowVersion is { Length: > 0 } rowVersion
            ? outcome.Terrarium! with { RowVersion = Convert.ToBase64String(rowVersion) }
            : outcome.Terrarium!;

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

    /// <summary>
    /// An update without <c>If-Match</c>, or with one that is not a base64 token: the client has not stated which
    /// version it edited (FR-03).
    /// </summary>
    /// <remarks>
    /// The title names the route that hands the value out, in both places it is carried: Swagger UI renders no
    /// <c>If-Match</c> box for a route that does not declare one, so a caller reading only this answer had no way
    /// to learn where the ETag comes from (TASKS 2026-10-14, item 4).
    /// </remarks>
    private static IResult PreconditionRequired() =>
        Results.Problem(
            title: "An If-Match header carrying the terrarium's current version is required to update it. Read "
                 + "GET /api/v1/terrariums/{id} first — it returns that version as its ETag header and as the "
                 + "rowVersion field of the body — and send it back in If-Match.",
            statusCode: StatusCodes.Status428PreconditionRequired,
            type: "https://smartreptile.example/problems/precondition_required",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "precondition_required",
            });
}
