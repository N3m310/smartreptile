using Microsoft.Extensions.DependencyInjection;
using SmartReptile.Application.Evaluation;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Infrastructure.Evaluation;

/// <summary>
/// Reads batches of committed samples off <see cref="InProcessEvaluationQueue"/> and hands them to
/// <see cref="SampleEvaluator"/>.
/// </summary>
/// <remarks>
/// One batch at a time, single reader, ordered: the same shape as <see cref="Ingest.IngestWorker"/>, and for the
/// same reason. Evaluation is stateful per <c>(terrarium, metric, phase)</c>, so a batch must be applied in ingest
/// order or a dwell window would be measured against the wrong predecessor.
/// <para>
/// A batch that throws is logged and the loop continues. Nothing is acknowledged here — the samples are already
/// committed, so there is nothing to un-ack — and the design's rule that "the next sample retries" is what keeps a
/// failed pass from being retried in a tight loop against a database that is already unhappy.
/// </para>
/// <para>
/// The counters are recorded here rather than inside the evaluator, the same split `IngestOutcomeRecorder` uses:
/// the application layer says what happened, the infrastructure layer decides that it is a metric. It is also what
/// makes §02-design/03 §8's monitoring heuristic readable — <c>alerts_opened_total</c> flat while
/// <c>out_of_range_minutes</c> rises is how a swallowed evaluator exception shows up.
/// </para>
/// </remarks>
public sealed class EvaluatorWorker(
    InProcessEvaluationQueue queue,
    IServiceScopeFactory scopes,
    SmartReptileMetrics metrics,
    ILogger<EvaluatorWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Evaluator worker started; queue capacity {Capacity}", InProcessEvaluationQueue.Capacity);

        try
        {
            await foreach (var batch in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var evaluator = scope.ServiceProvider.GetRequiredService<SampleEvaluator>();

                    var outcome = await evaluator.EvaluateAsync(batch, stoppingToken).ConfigureAwait(false);

                    Record(outcome);

                    logger.LogDebug(
                        "Evaluated {Count} sample(s): {Advanced} reading(s) advanced a state key, {Skipped} skipped, "
                        + "{Opened} alert(s) opened, {Escalated} escalated, {Resolved} resolved",
                        batch.Count,
                        outcome.ReadingsAdvanced,
                        outcome.ReadingsSkipped,
                        outcome.AlertsOpened,
                        outcome.AlertsEscalated,
                        outcome.AlertsResolved);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Not a lost measurement: the samples are committed. A lost evaluation pass is caught by the
                    // next batch, which is why this is an error on a loop rather than a retry around this one.
                    logger.LogError(
                        ex,
                        "Evaluation threw for a batch of {Count} sample(s); the next batch retries",
                        batch.Count);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Evaluator worker stopped");
    }

    private void Record(EvaluationOutcome outcome)
    {
        if (outcome.WarningAlertsOpened > 0)
        {
            metrics.AlertOpened("warning", outcome.WarningAlertsOpened);
        }

        if (outcome.CriticalAlertsOpened > 0)
        {
            metrics.AlertOpened("critical", outcome.CriticalAlertsOpened);
        }

        if (outcome.AlertsOpened > 0 || outcome.AlertsEscalated > 0 || outcome.AlertsResolved > 0)
        {
            logger.LogInformation(
                "Alerts: {Opened} opened ({Critical} critical), {Escalated} escalated, {Resolved} resolved",
                outcome.AlertsOpened,
                outcome.CriticalAlertsOpened,
                outcome.AlertsEscalated,
                outcome.AlertsResolved);
        }
    }
}
