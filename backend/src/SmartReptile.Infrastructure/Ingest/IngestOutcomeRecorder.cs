using SmartReptile.Application.Ingest;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// Turns one <see cref="IngestOutcome"/> into the counters, log lines and fan-out the rest of the system reads.
/// </summary>
/// <remarks>
/// Separated from <see cref="IngestWorker"/> because this is where the FR-18 counter contract lives, and a
/// background loop is a poor place to assert one. The worker keeps the responsibility it is good at — reading the
/// queue without ever stopping — and this class owns "what happened is now visible".
/// </remarks>
public sealed class IngestOutcomeRecorder(
    SmartReptileMetrics metrics,
    ITelemetryBroadcaster broadcaster,
    IEvaluationQueue evaluationQueue,
    ILogger<IngestOutcomeRecorder> logger)
{
    /// <summary>Counts, logs and fans out one batch's outcome.</summary>
    public async Task RecordAsync(
        IngestOutcome outcome,
        string devicePublicId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (!outcome.Succeeded)
        {
            metrics.BatchRejected(outcome.Problem!.Code);

            logger.LogWarning(
                "Refused a telemetry batch from device {DeviceId}: {Code} — {Message}",
                devicePublicId,
                outcome.Problem.Code,
                outcome.Problem.Message);

            return;
        }

        metrics.SampleIngested(outcome.Persisted);
        metrics.DuplicateDetected(outcome.Duplicates);

        foreach (var key in outcome.DroppedMetricKeys.Distinct(StringComparer.Ordinal))
        {
            logger.LogInformation(
                "Dropped unknown metric '{MetricKey}' from device {DeviceId} (rule V-10: a newer firmware must not become a rejected batch)",
                key,
                devicePublicId);
        }

        if (outcome.Stored.Count > 0)
        {
            await FanOutAsync(outcome.Stored, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Pushes stored samples to the two consumers. Both are best-effort and happen <b>after</b> the commit, so a
    /// failure is a lost push, never a lost measurement (FR-09). Each is caught separately: a SignalR outage must
    /// not stop the evaluator from being handed the same samples.
    /// </summary>
    private async Task FanOutAsync(IReadOnlyList<PersistedSample> stored, CancellationToken cancellationToken)
    {
        try
        {
            await broadcaster.BroadcastAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcasting {Count} stored sample(s) failed", stored.Count);
        }

        try
        {
            await evaluationQueue.EnqueueAsync(stored, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Queueing {Count} stored sample(s) for evaluation failed", stored.Count);
        }
    }
}
