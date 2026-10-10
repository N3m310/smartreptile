using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The device-facing ingest fallback (FR-06; contract in <c>07-appendices/03</c> §3.6).
/// </summary>
/// <remarks>
/// Anonymous in the JWT sense and authenticated by the device credential in the header instead, because the caller
/// is a board rather than a keeper. The batch then runs through the <b>same</b> <see cref="IngestPipeline"/> as
/// MQTT, so the only difference visible in the data is <see cref="IngestSource.HttpFallback"/> on the row — which
/// is the whole point of the fallback: a sample that arrives late over HTTPS is the same sample.
/// </remarks>
public static class IngestEndpoints
{
    /// <summary>Maps the HTTPS fallback.</summary>
    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ingest")
            .WithTags("ingest")
            .RequireRateLimiting("ingest");

        group.MapPost("/http", async (
            HttpContext context,
            IngestPipeline pipeline,
            IngestOutcomeRecorder recorder,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!DeviceCredentialHeader.TryParse(
                    context.Request.Headers.Authorization.ToString(),
                    out var devicePublicId,
                    out var presentedSecret))
            {
                // A missing credential and a wrong one are one answer for the same reason the pipeline gives only
                // `auth_failed`: two different messages would tell a caller which public ids exist (BR-02.2).
                return AuthFailed();
            }

            var envelope = new TelemetryEnvelope(
                devicePublicId,
                await ReadBodyAsync(context.Request, cancellationToken),
                clock.UtcNow,
                IngestSource.HttpFallback,
                presentedSecret);

            var outcome = await pipeline.ProcessAsync(envelope, cancellationToken);

            // The counters, the log line and the post-commit fan-out live in one class so the fallback is visible
            // on /metrics exactly like MQTT, and its stored samples reach the same consumers (FR-09, FR-18).
            await recorder.RecordAsync(outcome, devicePublicId, cancellationToken);

            if (!outcome.Succeeded)
            {
                return Problem(outcome.Problem!);
            }

            // 202 rather than 200 because the contract says so (`07-appendices/03` §3.6) and the firmware is
            // written against it. The counts are the pipeline's own outcome rather than a guess, which is what
            // lets a device clear its ring buffer after an outage instead of re-sending until someone looks.
            return Results.Json(
                new
                {
                    accepted = outcome.Persisted,
                    duplicates = outcome.Duplicates,

                    // Always zero today, and not because nothing was refused: the validator decides a batch all or
                    // nothing, so a refusal leaves through Problem() below as a 4xx and this body only ever
                    // describes a batch that was stored. The field stays because the contract has it and a future
                    // partial-acceptance path is the only thing that could fill it.
                    rejected = 0,
                    reasons = Array.Empty<string>(),
                },
                statusCode: StatusCodes.Status202Accepted);
        })
        .WithSummary("Submit a telemetry batch over HTTPS")
        .WithDescription("The fallback for a node whose broker is unreachable: same pipeline as MQTT, 6 requests "
                       + "per minute per device. A 4xx means the device must not re-send this payload; a 5xx or a "
                       + "transport failure means it must keep it.");

        return app;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        // The 32 KB rule is deliberately *not* re-implemented here: JsonTelemetryPayloadParser owns it, so MQTT and
        // HTTPS refuse the same payload with the same code rather than drifting apart by one adapter.
        return buffer.ToArray();
    }

    /// <summary>
    /// Maps an ingest refusal to RFC 7807. A 4xx is the contract with the device: the payload or the credential is
    /// wrong, so re-sending it unchanged will fail identically. Anything else would invite a back-fill loop to
    /// retry a payload that can never be accepted.
    /// </summary>
    private static IResult Problem(IngestProblem problem)
    {
        // The refusal's own message is dropped for an authentication failure and kept for everything else. It
        // describes the *payload* in the one case, which is the caller's own data, and the *device* in the other -
        // "not bound to a terrarium" and "secret does not match" are exactly the distinction BR-02.2 forbids
        // handing back. The specific reason still reaches the operator through IngestOutcomeRecorder's log line.
        var refusedCredential = problem.Code == "auth_failed";

        return Results.Problem(
            title: refusedCredential ? "The device credential was refused." : problem.Message,
            statusCode: refusedCredential
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status400BadRequest,
            type: $"https://smartreptile.example/problems/{problem.Code}",
            extensions: new Dictionary<string, object?> { ["code"] = problem.Code });
    }

    private static IResult AuthFailed() => Problem(new IngestProblem(
        "auth_failed",
        "The device credential was missing or malformed."));
}
