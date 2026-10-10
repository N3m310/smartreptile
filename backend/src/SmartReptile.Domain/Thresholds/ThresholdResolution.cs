using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;

namespace SmartReptile.Domain.Thresholds;

/// <summary>
/// One metric's band together with the layer it came from, which is what makes "why is my limit 32 and not 30?"
/// answerable in the app rather than in a debugger (BR-10.3).
/// </summary>
/// <param name="Metric">Metric the band judges.</param>
/// <param name="Phase">Phase the band applies in — <see cref="ThresholdPhase.Any"/> when the metric is not split.</param>
/// <param name="Source">Layer the band was found in.</param>
/// <param name="Band">The band itself, ready for comparison.</param>
public sealed record EffectiveThreshold(
    MetricCode Metric,
    ThresholdPhase Phase,
    ThresholdSource Source,
    ThresholdBand Band);

/// <summary>
/// The resolution order of `03-implementation/06` §1 — terrarium override, then the assigned profile — as one pure
/// function, so the read surface's band label, the threshold editor's preview table and (from 3.2) the evaluator
/// cannot disagree about which band was in force.
/// </summary>
/// <remarks>
/// <b>Why this type exists.</b> The rule used to live inside <c>TerrariumService</c> as a private method that
/// returned only the band, which was enough while the band was only used to colour a card. A caller that has to
/// *report* where the band came from needs the same rule with the provenance attached, and a second copy of a
/// precedence rule is a second place for it to drift.
/// <para>
/// <b>Two ambiguities in the specification, resolved here and recorded in the document.</b> First, precedence is
/// applied <i>per instant</i>, resolving the phase inside one layer rather than across both at once: the override
/// layer is consulted first and decides whenever it has a band for the current phase, so a phase-agnostic profile
/// row can never reach over an override that applies. The other half of that sentence is deliberate too — a layer
/// holding only the <i>other</i> phase's row is silent rather than decisive, so a profile Night band is still used
/// when the override covers only Day time, which is the behaviour the read surface already had and which
/// <c>readings/latest</c>'s <c>target</c> must not lose. Second, a layer holding <b>both</b> an
/// <see cref="ThresholdPhase.Any"/> row and a phase-specific one for the same metric resolves to
/// <see cref="ThresholdPhase.Any"/>, because "configured with an Any band" means the metric is not split
/// day/night — the phase-resolution pseudocode's rule, and the one the evaluator's state keys already follow.
/// </para>
/// </remarks>
public static class ThresholdResolver
{
    /// <summary>
    /// The band in force for one metric at one local time, or null when nothing configured for this instant judges
    /// it — a metric whose only row is a Day band therefore has no band at night.
    /// </summary>
    /// <param name="metric">Metric to resolve.</param>
    /// <param name="overrides">The terrarium's overrides; disabled rows are ignored.</param>
    /// <param name="profileBands">The assigned profile's bands; disabled rows are ignored.</param>
    /// <param name="localTimeOfDay">Time of day in the terrarium's own zone (ADR-015).</param>
    /// <param name="lightsOnLocalTime">Local time the photoperiod opens.</param>
    /// <param name="photoperiodHours">Photoperiod length; 24 means "always day".</param>
    public static EffectiveThreshold? Resolve(
        MetricCode metric,
        IEnumerable<ThresholdOverride> overrides,
        IEnumerable<Threshold> profileBands,
        TimeOnly localTimeOfDay,
        TimeOnly lightsOnLocalTime,
        decimal photoperiodHours)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(profileBands);

        // The override layer is consulted first and wins outright when it has a band for this instant: within a
        // layer the phase decides, between layers the override decides (BR-10.3).
        return FromLayer(metric, ThresholdSource.Override, Enabled(overrides, metric), localTimeOfDay, lightsOnLocalTime, photoperiodHours)
            ?? FromLayer(metric, ThresholdSource.Profile, Enabled(profileBands, metric), localTimeOfDay, lightsOnLocalTime, photoperiodHours);
    }

    /// <summary>The rows one layer holds for a metric, disabled ones dropped.</summary>
    private static IReadOnlyList<(ThresholdPhase Phase, ThresholdBand Band)> Enabled(
        IEnumerable<ThresholdOverride> rows,
        MetricCode metric) =>
        rows.Where(row => row.Metric == metric && row.Enabled)
            .Select(row => (Phase: row.Phase, Band: row.ToBand()))
            .ToList();

    /// <summary>The rows one layer holds for a metric, disabled ones dropped.</summary>
    private static IReadOnlyList<(ThresholdPhase Phase, ThresholdBand Band)> Enabled(
        IEnumerable<Threshold> rows,
        MetricCode metric) =>
        rows.Where(row => row.Metric == metric && row.Enabled)
            .Select(row => (Phase: row.Phase, Band: row.ToBand()))
            .ToList();

    /// <summary>
    /// The band one layer supplies for this instant, or null when that layer has nothing to say about it — rows
    /// that exist for the metric but for the other phase, and an empty layer, are both silence rather than a
    /// fallback to the other phase's band.
    /// </summary>
    private static EffectiveThreshold? FromLayer(
        MetricCode metric,
        ThresholdSource source,
        IReadOnlyList<(ThresholdPhase Phase, ThresholdBand Band)> rows,
        TimeOnly localTimeOfDay,
        TimeOnly lightsOnLocalTime,
        decimal photoperiodHours)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var phase = ThresholdPhaseResolver.ResolveFor(
            rows.Select(row => row.Phase),
            localTimeOfDay,
            lightsOnLocalTime,
            photoperiodHours);

        foreach (var row in rows)
        {
            if (row.Phase == phase)
            {
                return new EffectiveThreshold(metric, phase, source, row.Band);
            }
        }

        return null;
    }
}
