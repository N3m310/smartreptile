using Microsoft.Extensions.DependencyInjection;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Infrastructure.Devices;

/// <summary>
/// Runs <see cref="DeviceSilenceMonitor"/> on a timer (FR-07, roadmap task 3.3).
/// </summary>
/// <remarks>
/// A timer rather than a queue, because the watchdog's input is the *absence* of messages: there is nothing to
/// subscribe to and nothing arrives to trigger a pass. The period is short against the rules it serves — half a
/// minute against thresholds measured in minutes — so the alert is opened within a sweep of the instant the rule
/// says it should be, which is the accuracy the QA sheet checks by unplugging a node.
/// <para>
/// A sweep that throws is logged and the timer keeps ticking: no sample is at stake, so the next sweep sees the
/// same silence and opens the same alert. That is also why the counters are recorded here rather than inside the
/// monitor — the application layer says what happened, the infrastructure layer decides that it is a metric.
/// </para>
/// </remarks>
public sealed class SilenceWatchdogWorker(
    IServiceScopeFactory scopes,
    SmartReptileMetrics metrics,
    ITelemetryBroadcaster broadcaster,
    ILogger<SilenceWatchdogWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often the fleet is judged. Not a setting: it is the granularity of "within a minute", which is what the
    /// acceptance asks for, and a deployment that needed a different one would be changing the rule rather than
    /// tuning it (the rule itself is <see cref="DeviceSilencePolicy"/>).
    /// </summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Silence watchdog started; sweeping every {Seconds}s", SweepInterval.TotalSeconds);

        using var timer = new PeriodicTimer(SweepInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Silence watchdog stopped");
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var monitor = scope.ServiceProvider.GetRequiredService<DeviceSilenceMonitor>();

            var outcome = await monitor.SweepAsync(stoppingToken).ConfigureAwait(false);

            if (outcome.WarningAlertsOpened > 0)
            {
                metrics.AlertOpened("warning", outcome.WarningAlertsOpened);
            }

            if (outcome.CriticalAlertsOpened > 0)
            {
                metrics.AlertOpened("critical", outcome.CriticalAlertsOpened);
            }

            if (outcome.MarkedOffline + outcome.AlertsOpened + outcome.AlertsEscalated + outcome.AlertsResolved > 0)
            {
                logger.LogInformation(
                    "Silence sweep: {Watched} watched, {Offline} moved offline, {Opened} alert(s) opened, "
                    + "{Escalated} escalated, {Resolved} resolved",
                    outcome.Watched,
                    outcome.MarkedOffline,
                    outcome.AlertsOpened,
                    outcome.AlertsEscalated,
                    outcome.AlertsResolved);
            }

            await PushAlertsAsync(outcome, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A missed sweep is not a missed sample: the silence is still there next time and the alert it deserves
            // is opened then. Retrying inside the sweep would only stack against a database that is already unhappy.
            logger.LogError(ex, "The silence sweep threw; the next sweep retries");
        }
    }

    /// <summary>
    /// Pushes the alerts this sweep moved, best-effort and after the commit the sweep itself performed: the rows
    /// exist, so a SignalR outage is a lost push and never a lost alert.
    /// </summary>
    /// <param name="outcome">What the sweep did.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PushAlertsAsync(DeviceSilenceOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome.AlertChanges.Count == 0)
        {
            return;
        }

        try
        {
            await broadcaster
                .BroadcastAlertsAsync(
                    outcome.AlertChanges.Select(AlertChangedPayload.From).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Broadcasting {Count} alert move(s) failed", outcome.AlertChanges.Count);
        }
    }
}
