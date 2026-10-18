using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Domain.Evaluation;

/// <summary>
/// The dwell/hysteresis state machine of one <c>(terrarium, metric, phase)</c> key (§02-design/03 §4.1),
/// persisted so a restart does not forget an excursion that was already in progress.
/// </summary>
/// <remarks>
/// The primary key is <c>(TerrariumId, Metric, Phase)</c> rather than a surrogate id, because the state is a
/// singleton per key and a second row for one key would mean two evaluators disagreeing about the same excursion
/// (§07-appendices/02 §3.9). <see cref="RowVersion"/> is what makes concurrent evaluation of one key impossible.
/// <para>
/// <b>Built so far (roadmap 3.2's scaffolding).</b> Only <see cref="LastEvaluatedSampleId"/> is written: the queue's
/// consumer advances it as samples reach the evaluator, which is what turns the fan-out boundary into a real
/// hand-off. The decision fields are declared here — and their columns created — so that the decision procedure
/// itself needs no second migration.
/// </para>
/// </remarks>
public class EvaluationState
{
    /// <summary>Terrarium the excursion belongs to.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Metric being judged.</summary>
    public MetricCode Metric { get; set; }

    /// <summary>Phase the band in force was resolved in.</summary>
    public ThresholdPhase Phase { get; set; }

    /// <summary>Direction of the excursion in progress, or <see cref="ViolationKind.None"/> when inside the band.</summary>
    public ViolationKind Violation { get; set; } = ViolationKind.None;

    /// <summary>Start of the current excursion; what dwell is measured from.</summary>
    public DateTimeOffset? FirstOutOfBandAt { get; set; }

    /// <summary>Start of the current critical excursion.</summary>
    public DateTimeOffset? CriticalSinceAt { get; set; }

    /// <summary>Consecutive in-band ticks; three of them resolve an open alert (recovery hysteresis).</summary>
    public int ConsecutiveRecoveryTicks { get; set; }

    /// <summary>Alert currently open for this key, so the next sample updates it instead of opening a second one.</summary>
    public long? OpenAlertId { get; set; }

    /// <summary>Newest sample handed to the evaluator for this key.</summary>
    public long LastEvaluatedSampleId { get; set; }

    /// <summary>When this key last notified, so an episode cannot notify twice inside the policy window.</summary>
    public DateTimeOffset? LastNotificationAt { get; set; }

    /// <summary>Concurrency token: two evaluators must not advance one key at the same time.</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// A state that has never decided anything, which is what the first sample of a key starts from and what the
    /// decision tests drive the engine with. A factory rather than a constructor call so "the state starts as
    /// nothing rather than as a plausible-looking default" is one statement, in one place.
    /// </summary>
    public static EvaluationState Empty() => new();

    /// <summary>
    /// Ends the episode in progress and re-arms the dwell window, leaving the watermark alone.
    /// </summary>
    /// <remarks>
    /// Called when a human closes an alert through the lifecycle API (roadmap 3.4). Clearing only
    /// <see cref="OpenAlertId"/> would not be enough: <see cref="FirstOutOfBandAt"/> still holds the excursion's
    /// start, so the evaluator would find its dwell window already satisfied and open a fresh alert on the very next
    /// out-of-band reading. Re-arming is what makes "resolved" mean resolved — and the watermark
    /// (<see cref="LastEvaluatedSampleId"/>) is deliberately untouched, because the samples before this instant have
    /// been judged and must not be judged twice.
    /// </remarks>
    public void Rearm()
    {
        Violation = ViolationKind.None;
        FirstOutOfBandAt = null;
        CriticalSinceAt = null;
        ConsecutiveRecoveryTicks = 0;
        OpenAlertId = null;
    }
}
