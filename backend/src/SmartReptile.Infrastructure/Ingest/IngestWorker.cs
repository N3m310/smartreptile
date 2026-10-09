using Microsoft.Extensions.DependencyInjection;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// Reads batches off <see cref="InProcessTelemetryBus"/> and runs each one through <see cref="IngestPipeline"/>,
/// reporting the result through <see cref="IngestOutcomeRecorder"/>.
/// </summary>
/// <remarks>
/// One batch at a time, single reader, over a bounded channel: that is the whole concurrency design. Ingest order
/// matters (a device's samples are ordered by <c>seq</c>), and parallelism per device would buy nothing at one
/// batch per minute while making ordering a problem to solve. Headroom comes from the pipeline being short, not
/// from fanning it out (§02-design/03 §1).
/// <para>
/// A batch that throws — the database went away mid-commit — is logged and the loop continues. The message is
/// already acknowledged by the time it is here, so there is nothing to un-ack; what matters is that one bad batch
/// cannot stop the worker, and that the failure is visible rather than counted as a rejection.
/// </para>
/// </remarks>
public sealed class IngestWorker(
    InProcessTelemetryBus bus,
    IServiceScopeFactory scopes,
    IngestOutcomeRecorder recorder,
    ILogger<IngestWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Ingest worker started; queue capacity {Capacity}", InProcessTelemetryBus.Capacity);

        try
        {
            await foreach (var envelope in bus.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await ProcessOneAsync(envelope, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        logger.LogInformation("Ingest worker stopped");
    }

    private async Task ProcessOneAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        // Telemetry carries samples and is judged; the other three channels carry device state, health and events
        // and are only stored (§07-appendices/03 §3.1). Splitting the routing here is what lets both paths reuse
        // the same worker, queue and failure policy.
        if (envelope.Channel is not DeviceChannel.Telemetry)
        {
            await ProcessDeviceChannelAsync(envelope, cancellationToken).ConfigureAwait(false);
            return;
        }

        IngestOutcome outcome;

        try
        {
            using var scope = scopes.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<IngestPipeline>();

            outcome = await pipeline.ProcessAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Deliberately not counted as a rejection: the batch may well be stored, and "rejected" would point the
            // operator at the device instead of at the database.
            logger.LogError(ex, "Ingest threw for device {DeviceId}; the batch was not stored", envelope.DevicePublicId);
            return;
        }

        await recorder.RecordAsync(outcome, envelope.DevicePublicId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one health, status or events message. The failure policy is the telemetry path's, for the same reason:
    /// the message is already acknowledged, so there is nothing to un-ack, and one bad message must not stop the
    /// worker.
    /// </summary>
    private async Task ProcessDeviceChannelAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        DeviceChannelOutcome outcome;

        try
        {
            using var scope = scopes.CreateScope();
            var pipeline = scope.ServiceProvider.GetRequiredService<DeviceChannelPipeline>();

            outcome = await pipeline.ProcessAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "The {Channel} message from device {DeviceId} was not stored",
                envelope.Channel,
                envelope.DevicePublicId);

            return;
        }

        await recorder.RecordDeviceChannelAsync(outcome, envelope.DevicePublicId, cancellationToken).ConfigureAwait(false);
    }
}
