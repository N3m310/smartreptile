using SmartReptile.Application.Alerts;
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

        await PushAlertsAsync(outcome.AlertChanges, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts, logs and pushes one health, status or events outcome. Refusals share the telemetry path's counter,
    /// because "the firmware sent something we could not use" is one condition regardless of which topic it
    /// arrived on; a stored message only has something to push when it moved the device's state.
    /// </summary>
    public async Task RecordDeviceChannelAsync(
        DeviceChannelOutcome outcome,
        string devicePublicId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (!outcome.Succeeded)
        {
            metrics.BatchRejected(outcome.Problem!.Code);

            logger.LogWarning(
                "Refused a device message from {DeviceId}: {Code} — {Message}",
                devicePublicId,
                outcome.Problem.Code,
                outcome.Problem.Message);

            return;
        }

        if (outcome.StatusChanged is { } statusChanged)
        {
            try
            {
                await broadcaster.BroadcastStatusAsync(statusChanged, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Best-effort, like the sample push: a client that misses this polls readings/latest instead, and the
                // stored transition is already correct.
                logger.LogError(ex, "Broadcasting the status change of device {DeviceId} failed", devicePublicId);
            }
        }

        await PushAlertsAsync(outcome.AlertChanges, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Pushes the alert moves a message produced — the clock-skew notice and the sensor fault of 3.3 — after the
    /// commit that gave them their identities. Best-effort like the other two pushes, and caught separately for the
    /// same reason: a hub outage must not make a stored change look like a failed request.
    /// </summary>
    /// <param name="changes">Moves the producer recorded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PushAlertsAsync(IReadOnlyList<AlertChange> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            await broadcaster
                .BroadcastAlertsAsync(
                    changes.Select(AlertChangedPayload.From).ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Broadcasting {Count} alert move(s) failed", changes.Count);
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
