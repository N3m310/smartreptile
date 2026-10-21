using Microsoft.Extensions.DependencyInjection;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Ingest;
using SmartReptile.Application.Signals;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Infrastructure.Signals;

/// <summary>
/// Runs <see cref="LightDeficitMonitor"/> on a timer (FR-11/FR-14, roadmap task 3.6).
/// </summary>
/// <remarks>
/// A timer for the same reason the summary worker has one: the rule's input is a completed local day, and nothing
/// arrives to announce that a day ended. Fifteen minutes is deliberately much finer than the rule's granularity — the
/// rule only fires once per day, after 21:00 local — so a pass is cheap and the alert lands in the first sweep after
/// the day closes rather than at a fixed hour that would have to be configured per terrarium zone.
/// <para>
/// A sweep that throws is logged and the timer keeps ticking: no sample is at stake, and the next pass sees the same
/// day. The counters are recorded here rather than inside the monitor, the same split the other workers use.
/// </para>
/// </remarks>
public sealed class LightDeficitWorker(
    IServiceScopeFactory scopes,
    SmartReptileMetrics metrics,
    ITelemetryBroadcaster broadcaster,
    ILogger<LightDeficitWorker> logger) : BackgroundService
{
    /// <summary>How often the light-deficit rule is given a chance to judge a closed day.</summary>
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Light-deficit sweep started; sweeping every {Minutes} min",
            SweepInterval.TotalMinutes);

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

        logger.LogInformation("Light-deficit sweep stopped");
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var monitor = scope.ServiceProvider.GetRequiredService<LightDeficitMonitor>();

            var outcome = await monitor.SweepAsync(stoppingToken).ConfigureAwait(false);

            if (outcome.Opened > 0)
            {
                metrics.AlertOpened("warning", outcome.Opened);
            }

            if (outcome.Opened + outcome.Resolved > 0)
            {
                logger.LogInformation(
                    "Light-deficit sweep: {Judged} terrarium(s) judged, {Opened} opened, {Resolved} resolved, "
                    + "{Touched} still short",
                    outcome.Terrariums,
                    outcome.Opened,
                    outcome.Resolved,
                    outcome.Touched);
            }

            await PushAlertsAsync(outcome, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The light-deficit sweep threw; the next sweep retries");
        }
    }

    private async Task PushAlertsAsync(LightDeficitOutcome outcome, CancellationToken cancellationToken)
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
            logger.LogError(ex, "Broadcasting {Count} light-deficit move(s) failed", outcome.AlertChanges.Count);
        }
    }
}
