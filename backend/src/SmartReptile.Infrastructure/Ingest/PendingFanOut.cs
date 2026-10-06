using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// The hand-off to the threshold engine, not yet written — roadmap tasks 3.2/3.3.
/// </summary>
/// <remarks>
/// The boundary exists so the pipeline's fan-out is real rather than commented out; the work behind it is not.
/// Until it does, an implausible or out-of-range sample is stored and evaluated by nobody, which is why TC-I-03's
/// "zero alerts created" holds trivially rather than by the evaluator skipping the flagged row.
/// <para>
/// The broadcast half of this fan-out no longer lives here: <c>SignalRTelemetryBroadcaster</c> replaced the
/// placeholder in task 2.9, and it belongs to the API project because that is where the hub is.
/// </para>
/// </remarks>
public sealed class PendingEvaluationQueue : IEvaluationQueue
{
    /// <inheritdoc />
    public Task EnqueueAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
