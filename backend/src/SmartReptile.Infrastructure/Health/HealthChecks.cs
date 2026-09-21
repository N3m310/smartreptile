using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartReptile.Infrastructure.Mqtt;

namespace SmartReptile.Infrastructure.Health;

/// <summary>
/// Readiness check for the database (FR-18 BR-18.1): can we currently reach SQL Server?
/// Deliberately separate from liveness — a database outage degrades the API, it does not kill it.
/// </summary>
public sealed class DatabaseHealthCheck(Persistence.SmartReptileDbContext db) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable")
                : HealthCheckResult.Unhealthy("Database is not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}

/// <summary>
/// Readiness check for the in-process MQTT broker (FR-18 BR-18.1).
/// <c>/health/ready</c> returns 503 while the broker is down so the app can show "live updates paused"
/// instead of pretending telemetry is flowing (UC-02 A1).
/// </summary>
public sealed class MqttBrokerHealthCheck(IMqttBrokerStatus status) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["connectedClients"] = status.ConnectedClients,
            ["rejectedConnections"] = status.RejectedConnections,
            ["publishedMessages"] = status.PublishedMessages,
        };

        if (!status.IsRunning)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                status.LastError ?? "MQTT broker is not running",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("MQTT broker is accepting connections", data));
    }
}
