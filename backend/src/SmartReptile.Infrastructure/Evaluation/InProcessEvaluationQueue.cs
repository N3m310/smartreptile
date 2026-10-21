using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Evaluation;

/// <summary>
/// The in-process hand-off between the ingest worker and the evaluator: the recorder enqueues committed samples,
/// which are released to <see cref="EvaluatorWorker"/> after the per-device reorder window.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> back-pressured, unlike <see cref="InProcessTelemetryBus"/>. Ingest's acknowledgement is
/// allowed to wait for the database — the device retries, which is the right answer — but it must never wait for
/// the evaluator, because evaluation is a derived opinion and a stalled opinion must not stop measurements being
/// stored (§03-implementation/03 §6, "never blocks ingest"). A full queue drops the batch and logs an error;
/// capacity exhaustion means the evaluator is not keeping up.
/// </remarks>
public sealed class InProcessEvaluationQueue(IClock clock, ILogger<InProcessEvaluationQueue> logger) : IEvaluationQueue
{
    /// <summary>Batches that may wait. At the design's one batch per minute per node this is over a day of traffic.</summary>
    public const int Capacity = 2_000;

    private static readonly TimeSpan ReorderWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReleaseCheckInterval = TimeSpan.FromMilliseconds(100);

    private readonly Channel<IReadOnlyList<PersistedSample>> _channel =
        Channel.CreateBounded<IReadOnlyList<PersistedSample>>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>Batches waiting to be evaluated — the backlog, exposed so it can be watched rather than guessed at.</summary>
    public int Backlog => _channel.Reader.Count;

    /// <inheritdoc />
    public Task EnqueueAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!_channel.Writer.TryWrite(samples))
        {
            logger.LogError(
                "Evaluation queue is full ({Capacity} batches); dropped {Count} committed sample(s) from evaluation. "
                + "The evaluator is not keeping up, which is a defect rather than expected load.",
                Capacity,
                samples.Count);

            throw new InvalidOperationException(
                $"Evaluation queue capacity ({Capacity} batches) was exceeded; committed samples remain stored but were not queued for evaluation.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Streams released samples to the single reader (the evaluator worker).</summary>
    public async IAsyncEnumerable<IReadOnlyList<PersistedSample>> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var reorderBuffer = new EvaluationReorderBuffer(ReorderWindow);
        using var timer = new PeriodicTimer(ReleaseCheckInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var batch))
                {
                    reorderBuffer.Add(batch);
                }

                var result = reorderBuffer.ReleaseReady(clock.UtcNow);

                if (result.LateSampleCount > 0)
                {
                    logger.LogWarning(
                        "Skipped {Count} late sample(s) from live evaluation because their RecordedAt precedes "
                        + "the per-device evaluation watermark; the samples remain stored for history and summaries",
                        result.LateSampleCount);
                }

                if (result.ReadySamples.Count > 0)
                {
                    yield return result.ReadySamples;
                }
            }
        }
        finally
        {
            if (reorderBuffer.PendingCount > 0 || _channel.Reader.Count > 0)
            {
                logger.LogWarning(
                    "Evaluator stopped with {Buffered} sample(s) inside the 30-second reorder window and "
                    + "{Queued} queued batch(es); committed readings remain stored",
                    reorderBuffer.PendingCount,
                    _channel.Reader.Count);
            }
        }
    }
}
