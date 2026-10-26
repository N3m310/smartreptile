using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The alert lifecycle and the silence windows (FR-12, FR-13 — roadmap task 3.4). Contract: <c>07-appendices/03</c>
/// §4.5 for the alerts and §4.2 for the silences.
/// </summary>
/// <remarks>
/// Reads need a valid token and ownership; acknowledging, resolving and silencing name the Technician policy, which
/// is Owner-or-Technician, so a Viewer reads the same list and cannot change it (§02-design/06 §3). An alert or
/// terrarium that belongs to somebody else answers exactly like one that does not exist, so there is no
/// <c>forbidden</c> code on these routes (BR-02.2) — the only 403 here is the policy's own refusal of a Viewer.
/// </remarks>
public static class AlertEndpoints
{
    /// <summary>Maps the alert routes and the silence routes.</summary>
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var alerts = app.MapGroup("/api/v1/alerts")
            .WithTags("alerts")
            .RequireAuthorization(Permissions.AlertRead);

        alerts.MapGet(string.Empty, async (
            string? state,
            string? severity,
            string? metric,
            string? family,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? cursor,
            int? pageSize,
            ClaimsPrincipal principal,
            AlertService service,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseState(state, out var stateFilter, out var inProgress, out var stateProblem))
            {
                return Problem(stateProblem!);
            }

            if (!TryParseSeverity(severity, out var severityFilter, out var severityProblem))
            {
                return Problem(severityProblem!);
            }

            // An absent metric filter means "every alert", including the device-level ones that have no metric; a
            // present but unknown key is a client error, exactly like the readings route treats it.
            MetricCode? metricFilter = null;

            if (!string.IsNullOrWhiteSpace(metric))
            {
                if (!MetricDictionary.TryParseApiKey(metric, out var parsedMetric))
                {
                    return Problem(new AlertProblem(
                        "metric_invalid",
                        $"metric must be one of: {MetricKeys}."));
                }

                metricFilter = parsedMetric;
            }

            // The family is the coarse filter the alerts screen's two tabs are built on. Absent means both; an
            // unknown key is refused rather than silently treated as "both", because a client that misspells it would
            // otherwise show one family's rows under the other family's heading.
            AlertFamily? familyFilter = null;

            if (!string.IsNullOrWhiteSpace(family))
            {
                if (!AlertFamilies.TryParseApiKey(family, out var parsedFamily))
                {
                    return Problem(new AlertProblem(
                        "family_invalid",
                        $"family must be one of: {FamilyKeys}."));
                }

                familyFilter = parsedFamily;
            }

            var outcome = await service.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                new AlertQuery(
                    stateFilter,
                    severityFilter,
                    metricFilter,
                    familyFilter,
                    from,
                    to,
                    cursor,
                    pageSize ?? AlertPaging.DefaultPageSize,
                    inProgress),
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(outcome.Page) : Problem(outcome.Problem!);
        })
        .WithSummary("List alerts: the signed-in account's terrariums, or every account's for an administrator")
        .WithDescription("Newest first, and **paged**: `?cursor=&pageSize=` (default 50, max 200) with the next "
                       + "position returned as `nextCursor` and absent on the last page. Filter by `state` "
                       + "(`Open`, `Acknowledged`, `Resolved`, or `in_progress` for everything not yet resolved), "
                       + "`severity` (Info, Warning, Critical), `metric`, `family` "
                       + "(enclosure for the enclosure's humidity, light and board; species for the animal's "
                       + "temperature and UV) and a trigger window `from`/`to`. `counts` labels the status bar and is "
                       + "taken with `state` dropped. A caller holding `data.read.any` sees "
                       + "every account's alerts, which is also how an alert on an ownerless terrarium is reached.");

        alerts.MapGet("/{alertId:long}", async (
            long alertId,
            ClaimsPrincipal principal,
            AlertService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.GetAsync(
                alertId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(outcome.Alert) : Problem(outcome.Problem!);
        })
        .WithSummary("One alert with an excerpt of its values")
        .WithDescription("Carries the band that was in force and up to 240 of the newest readings of the episode, "
                       + "oldest first, with `seriesTruncated` when the episode was longer than the excerpt. A "
                       + "device-level alert (silence, sensor fault, clock skew) has no series to carry. An "
                       + "administrator (`data.read.any`) may open any account's alert.");

        alerts.MapGet("/{alertId:long}/timeline", async (
            long alertId,
            ClaimsPrincipal principal,
            AlertService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.TimelineAsync(
                alertId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(new { items = outcome.Timeline }) : Problem(outcome.Problem!);
        })
        .WithSummary("What happened to one alert, in order")
        .WithDescription("`opened`, then `acknowledged` and/or `resolved` with the actor, the reason and the note "
                       + "the keeper wrote. Escalation entries join this list with the notification rows of "
                       + "`02-design/05` §6.");

        alerts.MapPost("/{alertId:long}/ack", async (
            long alertId,
            HttpContext context,
            ClaimsPrincipal principal,
            AlertService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.AcknowledgeAsync(
                alertId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken,
                principal.CanWriteAnyAccount());

            return outcome.Succeeded ? Results.Ok(outcome.Changed) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.AlertWrite)
        .WithSummary("Acknowledge an alert")
        .WithDescription("Requires `alert.write`. Acknowledging takes ownership without closing the "
                       + "alert; an alert that is already acknowledged or resolved answers `409 alert_not_open`. An "
                       + "administrator (`data.write.any`) may acknowledge another account's alert, and the audit "
                       + "row then names the administrator as the actor.");

        alerts.MapPost("/{alertId:long}/resolve", async (
            long alertId,
            ResolveAlertRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            AlertService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ResolveAsync(
                alertId,
                principal.GetUserId() ?? Guid.Empty,
                request,
                RequestActor.From(context),
                cancellationToken,
                principal.CanWriteAnyAccount());

            return outcome.Succeeded ? Results.Ok(outcome.Changed) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.AlertWrite)
        .WithSummary("Resolve an alert")
        .WithDescription("Requires `alert.write`. `reason` is one of `Recovered`, `FalsePositive`, "
                       + "`SensorFault` or `Accepted`; an optional `note` is kept on the alert's audit entry. A "
                       + "resolved alert answers `409 alert_not_open`, and a threshold alert cannot re-open until "
                       + "its band is left for the dwell again. An administrator (`data.write.any`) may resolve "
                       + "another account's alert; the dwell key it re-arms belongs to that other enclosure.");

        // The silences belong to a terrarium (the contract files them under §4.2), so they are mapped into that
        // prefix while living with the alerts they suppress. The group reads with `alert.read`; the two writes name
        // `silence.write` rather than `alert.write`, because going quiet is a decision about being told rather than
        // about an alert's lifecycle — a deployment that wants a technician who may acknowledge but not silence can
        // say so with that one grant.
        var silences = app.MapGroup("/api/v1/terrariums/{terrariumId:guid}/silences")
            .WithTags("terrariums")
            .RequireAuthorization(Permissions.AlertRead);

        silences.MapGet(string.Empty, async (
            Guid terrariumId,
            ClaimsPrincipal principal,
            MetricSilenceService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.ListAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                cancellationToken,
                principal.CanReadAnyAccount());

            return outcome.Succeeded ? Results.Ok(new { items = outcome.Silences }) : Problem(outcome.Problem!);
        })
        .WithSummary("Silences still suppressing a terrarium")
        .WithDescription("Only windows that are still in force and not cancelled: what the dashboard shows, and what "
                       + "the dispatcher checks before it notifies. Expired windows remain in the audit trail.");

        silences.MapPost(string.Empty, async (
            Guid terrariumId,
            CreateSilenceRequest request,
            HttpContext context,
            ClaimsPrincipal principal,
            MetricSilenceService service,
            CancellationToken cancellationToken) =>
        {
            // One field is optional and defaulted here rather than at the endpoint: an absent `metric` means every
            // metric of the terrarium, which is the whole-box case a keeper going away for the weekend wants.
            MetricCode? metric = null;

            if (!string.IsNullOrWhiteSpace(request.Metric))
            {
                if (!MetricDictionary.TryParseApiKey(request.Metric, out var parsedMetric))
                {
                    return Problem(new AlertProblem("metric_invalid", $"metric must be one of: {MetricKeys}."));
                }

                metric = parsedMetric;
            }

            var outcome = await service.CreateAsync(
                terrariumId,
                principal.GetUserId() ?? Guid.Empty,
                metric,
                request,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.Ok(outcome.Silence) : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SilenceWrite)
        .WithSummary("Silence a metric, or a whole terrarium")
        .WithDescription("Requires `silence.write`. `{metric?, untilUtc, reason}`: at most 24 hours, "
                       + "reason required, and omitting `metric` silences every metric. A silence suppresses "
                       + "notifications, not detection — alerts are still raised, counted and shown. Answered with "
                       + "the created window rather than a `Location`, because no read-by-id route exists.");

        silences.MapDelete("/{silenceId:guid}", async (
            Guid terrariumId,
            Guid silenceId,
            HttpContext context,
            ClaimsPrincipal principal,
            MetricSilenceService service,
            CancellationToken cancellationToken) =>
        {
            var outcome = await service.CancelAsync(
                terrariumId,
                silenceId,
                principal.GetUserId() ?? Guid.Empty,
                RequestActor.From(context),
                cancellationToken);

            return outcome.Succeeded ? Results.NoContent() : Problem(outcome.Problem!);
        })
        .RequireAuthorization(Permissions.SilenceWrite)
        .WithSummary("Cancel a silence early")
        .WithDescription("Requires `silence.write`. Idempotent: cancelling a window that is already "
                       + "cancelled changes nothing and still answers `204`.");

        return app;
    }

    /// <summary>Metric keys a `metric` filter accepts, for the message of a refusal.</summary>
    private static string MetricKeys => string.Join(", ", MetricDictionary.All.Select(definition => definition.ApiKey));

    /// <summary>Family keys a `family` filter accepts, for the message of a refusal.</summary>
    private static string FamilyKeys => string.Join(", ", AlertFamilies.All.Select(AlertFamilies.ApiKey));

    private static bool TryParseState(
        string? state,
        out AlertState? parsed,
        out bool inProgress,
        out AlertProblem? problem)
    {
        parsed = null;
        inProgress = false;
        problem = null;

        if (string.IsNullOrWhiteSpace(state))
        {
            return true;
        }

        // `in_progress` is not a lifecycle state, it is the question "does this still need somebody?" — open and
        // acknowledged together, which is what the status bar's first tab asks.
        if (string.Equals(state.Trim(), "in_progress", StringComparison.OrdinalIgnoreCase))
        {
            inProgress = true;

            return true;
        }

        if (Enum.TryParse<AlertState>(state.Trim(), ignoreCase: true, out var value))
        {
            parsed = value;

            return true;
        }

        problem = new AlertProblem("validation_failed", "state must be one of: Open, Acknowledged, Resolved, in_progress.");

        return false;
    }

    private static bool TryParseSeverity(string? severity, out AlertSeverity? parsed, out AlertProblem? problem)
    {
        parsed = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(severity))
        {
            return true;
        }

        if (Enum.TryParse<AlertSeverity>(severity.Trim(), ignoreCase: true, out var value))
        {
            parsed = value;

            return true;
        }

        problem = new AlertProblem("validation_failed", "severity must be one of: Info, Warning, Critical.");

        return false;
    }

    /// <summary>Maps an alert failure to RFC 7807.</summary>
    /// <param name="problem">Failure to map.</param>
    private static IResult Problem(AlertProblem problem) => Problem(problem.Code, problem.Message, problem.Errors);

    /// <summary>Maps a silence failure to RFC 7807.</summary>
    /// <param name="problem">Failure to map.</param>
    private static IResult Problem(SilenceProblem problem) => Problem(problem.Code, problem.Message, problem.Errors);

    /// <summary>
    /// Maps a lifecycle or silence failure to RFC 7807. Three shapes exist: the resource is not the caller's (or not
    /// there), the alert is no longer open — a conflict, because the request was valid for a state the row has left —
    /// or the caller sent something the API could not use.
    /// </summary>
    /// <param name="code">Stable problem code.</param>
    /// <param name="message">Message safe to show the user.</param>
    /// <param name="errors">Field-level violations, when the failure was input validation.</param>
    private static IResult Problem(string code, string message, IReadOnlyList<IdentityViolation>? errors)
    {
        var status = code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "alert_not_open" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };

        return ApiProblem.Create(code, message, status, errors);
    }
}
