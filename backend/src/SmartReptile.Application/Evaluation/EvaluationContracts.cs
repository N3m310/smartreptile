using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Evaluation;

/// <summary>
/// Everything the evaluator needs about one terrarium to judge a sample: the bands that could apply, the schedule
/// that decides the phase, the state rows that already exist and the alerts that are still open.
/// </summary>
/// <remarks>
/// Loaded once per terrarium per batch rather than per sample: a batch is up to 120 samples of the same node, and a
/// query per sample would turn one ingest into 120 round trips.
/// <para>
/// The bands arrive as the configured rows rather than as a resolved answer, because a batch spans samples at
/// different instants and — across a day boundary — at different phases. Resolution is
/// <c>ThresholdResolver</c>'s job, and doing it here would freeze one instant's answer for a whole batch.
/// </para>
/// </remarks>
/// <param name="TerrariumId">Terrarium being evaluated.</param>
/// <param name="TimeZoneId">Zone the phase is resolved in (ADR-015 puts the conversion on the server).</param>
/// <param name="LightsOnLocalTime">Local time the photoperiod opens.</param>
/// <param name="PhotoperiodHours">Photoperiod length; 24 means "always day".</param>
/// <param name="ProfileBands">The assigned species profile's bands, enabled rows already dropped.</param>
/// <param name="Overrides">The terrarium's own overrides, enabled rows already dropped.</param>
/// <param name="States">
/// The state rows already stored for this terrarium, keyed by <c>(metric, phase)</c>. Mutable so a row created
/// while the batch is being processed is found by the next sample of the same batch instead of being staged twice.
/// </param>
/// <param name="OpenAlerts">
/// The alerts for this terrarium that are not resolved, so the evaluator can tell an escalation from a new episode
/// and can recover the pointer of an episode whose state row was lost. An alert acknowledged by a human is still
/// open: acknowledgement is ownership, not closure.
/// </param>
public sealed record EvaluationContext(
    Guid TerrariumId,
    string TimeZoneId,
    TimeOnly LightsOnLocalTime,
    decimal PhotoperiodHours,
    IReadOnlyList<Threshold> ProfileBands,
    IReadOnlyList<ThresholdOverride> Overrides,
    Dictionary<(MetricCode Metric, ThresholdPhase Phase), EvaluationState> States,
    List<Alert> OpenAlerts);

/// <summary>
/// Persistence for the evaluator (FR-11, roadmap task 3.2). A port of its own rather than a method on
/// <c>ITelemetryStore</c>: the two are used by different workers, and the ingest pipeline must not be able to
/// reach the evaluator's tables.
/// </summary>
public interface IEvaluationStore
{
    /// <summary>
    /// The context for one terrarium, or null when it no longer exists — which makes a queued sample for a
    /// just-deleted terrarium a skip rather than a crash.
    /// </summary>
    Task<EvaluationContext?> FindContextAsync(Guid terrariumId, CancellationToken cancellationToken);

    /// <summary>Stages a state row that does not exist yet.</summary>
    void AddState(EvaluationState state);

    /// <summary>Stages a new alert.</summary>
    void AddAlert(Alert alert);

    /// <summary>
    /// The values one metric took inside a window, oldest first. Used to fill in an alert's triggering value and
    /// peak, which are facts about readings the evaluator has already seen but deliberately does not keep a second
    /// copy of: the stored readings are the evidence, and the dwell window is a handful of rows.
    /// </summary>
    Task<IReadOnlyList<decimal>> ReadEpisodeValuesAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Commits everything this scope staged.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>What one evaluation pass did, so the worker can log it and count it without re-reading the database.</summary>
/// <param name="ReadingsAdvanced">
/// Watermark movements: one per metric reading the pass judged and recorded. A batch with two readings on one key
/// moves it twice, which is why this counts readings rather than distinct state rows.
/// </param>
/// <param name="ReadingsSkipped">Readings excluded from evaluation (faulted, implausible, or no band configured).</param>
/// <param name="AlertsOpened">New alert rows written.</param>
/// <param name="CriticalAlertsOpened">Of those, how many opened straight into the critical band.</param>
/// <param name="AlertsEscalated">Open alerts whose severity was raised.</param>
/// <param name="AlertsResolved">Open alerts closed by recovery.</param>
public sealed record EvaluationOutcome(
    int ReadingsAdvanced,
    int ReadingsSkipped,
    int AlertsOpened = 0,
    int CriticalAlertsOpened = 0,
    int AlertsEscalated = 0,
    int AlertsResolved = 0)
{
    /// <summary>Nothing to do.</summary>
    public static readonly EvaluationOutcome None = new(0, 0);

    /// <summary>
    /// Alerts this pass opened, escalated or resolved, to push after the pass commits (<c>alertChanged</c> of
    /// `07-appendices/03` §6). A touch is not one of them: the documented event reports lifecycle moves, and the
    /// same notice arriving once per sample would be noise rather than news.
    /// </summary>
    public IReadOnlyList<AlertChange> AlertChanges { get; init; } = [];

    /// <summary>Of the alerts opened, the ones that were warnings.</summary>
    public int WarningAlertsOpened => AlertsOpened - CriticalAlertsOpened;
}
