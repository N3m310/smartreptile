using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Notifications;
using SmartReptile.Infrastructure.Observability;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Notifications;

/// <summary>
/// Runs <see cref="NotificationDispatcher"/> on a timer (roadmap task 3.5).
/// </summary>
/// <remarks>
/// A timer rather than a queue, for the reason the dispatcher's own remarks give: the four writers that move an
/// alert are in three layers, and the database is the only place that sees all of them. The consequence that
/// matters for this class is that <b>a sweep is idempotent</b> — nothing is consumed, so a crash halfway through
/// loses no event, and the next tick re-derives the same work from the same rows.
/// <para>
/// A failing sweep is logged and left for the next tick, exactly like the silence watchdog: the alerts are already
/// stored, and a retry loop against a database that is already unhappy is how a notifier turns into a load source.
/// </para>
/// </remarks>
public sealed class NotificationDispatcherWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationOptions> options,
    SmartReptileMetrics metrics,
    ILogger<NotificationDispatcherWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.SweepSeconds));

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Notification dispatcher started; sweeping every {Seconds}s", _interval.TotalSeconds);

        using var timer = new PeriodicTimer(_interval);

        try
        {
            // The first sweep waits one interval rather than running immediately, which is the pattern the silence
            // watchdog uses and not an accident: hosted services start before the database initializer has applied
            // its migrations, so an immediate pass would log a failed query against a schema that is about to
            // exist. Fifteen seconds of latency on a restart is the cheaper half of that trade.
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Notification dispatcher stopped");
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();

            var outcome = await dispatcher.SweepAsync(cancellationToken).ConfigureAwait(false);

            if (outcome.IsQuiet)
            {
                logger.LogDebug("Notification sweep found nothing to send");

                return;
            }

            metrics.Notifications(outcome);

            logger.LogInformation(
                "Notifications: {Delivered} delivered, {Failed} refused, {Suppressed} suppressed, {Digests} digest(s), "
                + "{Retired} push token(s) retired, over {Candidates} alert(s)",
                outcome.Delivered,
                outcome.Failed,
                outcome.Suppressed,
                outcome.Digests,
                outcome.PushTokensRetired,
                outcome.Candidates);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The notification sweep threw; the next tick retries");
        }
    }
}
