using System.Threading.Channels;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Evaluation;

/// <summary>
/// The in-process hand-off between the ingest worker and the evaluator: the recorder enqueues committed samples,
/// <see cref="EvaluatorWorker"/> drains them.
/// </summary>
/// <remarks>
/// Deliberately <b>not</b> back-pressured, unlike <see cref="InProcessTelemetryBus"/>. Ingest's acknowledgement is
/// allowed to wait for the database — the device retries, which is the right answer — but it must never wait for
/// the evaluator, because evaluation is a derived opinion and a stalled opinion must not stop measurements being
/// stored (§03-implementation/03 §6, "never blocks ingest"). A full queue therefore drops the batch and says so in
/// the log; reaching the capacity means the evaluator is stuck, which is a bug to be found rather than load to be
/// absorbed.
/// </remarks>
public sealed class InProcessEvaluationQueue : IEvaluationQueue
{
    /// <summary>Batches that may wait. At the design's one batch per minute per node this is over a day of traffic.</summary>
    public const int Capacity = 2_000;

    private readonly Channel<IReadOnlyList<PersistedSample>> _channel =
        Channel.CreateBounded<IReadOnlyList<PersistedSample>>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ILogger<InProcessEvaluationQueue> _logger;

    /// <summary>Creates the queue.</summary>
    public InProcessEvaluationQueue(ILogger<InProcessEvaluationQueue> logger) => _logger = logger;

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
            _logger.LogError(
                "Evaluation queue is full ({Capacity} batches); dropped {Count} committed sample(s) from evaluation. "
                + "The evaluator is not keeping up, which is a defect rather than expected load.",
                Capacity,
                samples.Count);
        }

        return Task.CompletedTask;
    }

    /// <summary>Streams batches to the single reader (the evaluator worker).</summary>
    public IAsyncEnumerable<IReadOnlyList<PersistedSample>> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
