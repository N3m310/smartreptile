using Microsoft.Extensions.DependencyInjection;

namespace SmartReptile.Infrastructure.Summaries;

/// <summary>Periodically recomputes recent rollups and local-day summaries from raw telemetry.</summary>
public sealed class SummaryWorker(
    IServiceScopeFactory scopes,
    ILogger<SummaryWorker> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Environmental summary worker started; recomputing every {Minutes} minutes",
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

        logger.LogInformation("Environmental summary worker stopped");
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var recomputer = scope.ServiceProvider.GetRequiredService<SummaryRecomputer>();
            var outcome = await recomputer.RecomputeAsync(stoppingToken).ConfigureAwait(false);

            logger.LogInformation(
                "Environmental summaries recomputed for {Terrariums} terrarium(s): "
                + "{Rollups} hourly rollup(s), {Summaries} daily summary row(s)",
                outcome.Terrariums,
                outcome.HourlyRollups,
                outcome.DailySummaries);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Environmental summary recomputation failed; the next sweep will retry");
        }
    }
}
