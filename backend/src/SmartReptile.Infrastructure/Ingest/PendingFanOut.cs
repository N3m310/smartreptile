using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// The SignalR push of FR-09, not yet written — roadmap task 2.9.
/// </summary>
/// <remarks>
/// This type exists so the pipeline's fan-out boundary is real rather than commented out: the worker already
/// calls it, already counts stored samples, and already treats its failure as a lost push rather than a lost
/// measurement. Replacing the body with a hub broadcast is 2.9's whole job.
/// <para>
/// It does <b>not</b> silently pretend to work: nothing is broadcast, and 2.9 is still open in the roadmap.
/// </para>
/// </remarks>
public sealed class PendingTelemetryBroadcaster : ITelemetryBroadcaster
{
    /// <inheritdoc />
    public Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// The hand-off to the threshold engine, not yet written — roadmap tasks 3.2/3.3.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="PendingTelemetryBroadcaster"/>: the boundary exists, the work behind it does not.
/// Until it does, an implausible or out-of-range sample is stored and evaluated by nobody, which is why TC-I-03's
/// "zero alerts created" holds trivially rather than by the evaluator skipping the flagged row.
/// </remarks>
public sealed class PendingEvaluationQueue : IEvaluationQueue
{
    /// <inheritdoc />
    public Task EnqueueAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
