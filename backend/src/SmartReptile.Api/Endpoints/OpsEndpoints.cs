using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartReptile.Infrastructure.Health;
using SmartReptile.Infrastructure.Mqtt;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// Operational endpoints (FR-18, NFR-12): liveness, readiness, version and a metrics snapshot.
/// They are unauthenticated but return no sensitive detail (§02-design/06 §6).
/// </summary>
public static class OpsEndpoints
{
    /// <summary>Maps <c>/health</c>, <c>/health/live</c>, <c>/health/ready</c>, <c>/version</c> and <c>/metrics</c>.</summary>
    public static IEndpointRouteBuilder MapOpsEndpoints(this IEndpointRouteBuilder app, string applicationVersion)
    {
        // Liveness: is the process up? Never touches dependencies, so a database outage does not restart the pod.
        app.MapGet("/health/live", () => Results.Ok(new
        {
            status = "Healthy",
            timestamp = DateTimeOffset.UtcNow,
        })).WithTags("ops");

        // Readiness: can we serve real traffic? This is what the app/dashboard probes to show
        // "live updates paused" instead of pretending telemetry is flowing (UC-02 A1).
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthReport,
        }).WithTags("ops");

        // Alias kept for convenience in scripts (§03-implementation/01 §6).
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthReport,
        }).WithTags("ops");

        app.MapGet("/version", () => Results.Ok(new
        {
            api = applicationVersion,
            schema = "InitialSchema",
            minFirmware = "0.1.0",
            timestamp = DateTimeOffset.UtcNow,
        })).WithTags("ops");

        app.MapGet("/metrics", (SmartReptileMetrics metrics, IMqttBrokerStatus broker) => Results.Ok(new
        {
            counters = metrics.Snapshot(broker),
            brokerRunning = broker.IsRunning,
            timestamp = DateTimeOffset.UtcNow,
        })).WithTags("ops");

        return app;
    }

    private static Task WriteHealthReport(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            timestamp = DateTimeOffset.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                error = entry.Value.Exception?.Message,
                data = entry.Value.Data.ToDictionary(pair => pair.Key, pair => pair.Value),
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
