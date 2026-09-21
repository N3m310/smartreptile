using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartReptile.Infrastructure.Mqtt;

namespace SmartReptile.Infrastructure.Health;

/// <summary>
/// Readiness check for the database (FR-18 BR-18.1): can we currently reach SQL Server?
/// Deliberately separate from liveness — a database outage degrades the API, it does not kill it.
/// </summary>
public sealed class DatabaseHealthCheck(Persistence.SmartReptileDbContext db) : IHealthCheck
{
    /// <summary>
    /// Readiness probes must fail fast, and the bound has to be enforced here rather than delegated to SqlClient.
    /// Measured during M1 verification with SQL Server stopped: a linked <see cref="CancellationTokenSource"/>
    /// with a 3 s deadline still produced a 16.1 s <c>/health/ready</c> response, because a token is not honoured
    /// during SqlClient's pre-login/TCP phase — its own <c>Connect Timeout</c> (15 s) decides, so a load balancer
    /// would read the API as dead instead of reading the database as down.
    /// <para>
    /// Racing the check against a delay is not enough on its own either: <c>CanConnectAsync</c> blocks its caller
    /// synchronously for those 15 s before it returns a task at all (the first attempt with the race below still
    /// measured 16.1 s), so the timer was only started once the wait was already over. Running the probe on a
    /// thread-pool thread with <c>Task.Run</c> lets the race begin immediately, which is what makes the timeout
    /// provider-independent.
    /// </para>
    /// <para>
    /// The abandoned attempt is left to finish on its own (it dies with the driver's connect timeout) and its
    /// fault is observed so it cannot surface as an unobserved task exception.
    /// </para>
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            // Task.Run is load-bearing, not decoration: it keeps the synchronous part of CanConnectAsync off
            // this thread so Task.WhenAny can actually fire. The async lambda picks the Func<Task<T>> overload
            // deliberately, so `probe` completes only when the connection attempt really finishes and any fault
            // is observable on the same task that the continuation below inspects.
            var probe = Task.Run(async () => await db.Database.CanConnectAsync(timeout.Token));

            if (await Task.WhenAny(probe, Task.Delay(ProbeTimeout, CancellationToken.None)) != probe)
            {
                // Observe the eventual fault so the orphaned attempt stays silent.
                _ = probe.ContinueWith(
                    t => _ = t.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                return HealthCheckResult.Unhealthy(
                    $"Database probe timed out after {ProbeTimeout.TotalSeconds:0} s");
            }

            var canConnect = await probe;
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable")
                : HealthCheckResult.Unhealthy("Database is not reachable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"Database probe timed out after {ProbeTimeout.TotalSeconds:0} s");
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
