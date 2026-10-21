using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Signals;

/// <summary>
/// Writes the two habitat-level derived signals of §02-design/03 §4.3: a basking surface too far above the air, and a
/// day that fell short of the profile's light requirement.
/// </summary>
/// <remarks>
/// The same split <see cref="Evaluation.ThresholdAlertWriter"/> and <see cref="Devices.DeviceSignalAlertWriter"/> use:
/// the rule decides, this says which fields that decision writes. Static and dependency-free so a test can assert the
/// row without a store, a clock or a sweep.
/// <para>
/// Both alerts name a metric even though neither is a band excursion. The metric is what the row is <b>about</b> —
/// the surface probe in one case, the light sensor in the other — and it is what the clients key their sentence and
/// their navigation on. Neither row carries a band, because no band was compared.
/// </para>
/// </remarks>
public static class HabitatSignalAlertWriter
{
    /// <summary>The severity a gradient warning carries. Warning, not Critical: it is a burn risk, not a burn.</summary>
    public const AlertSeverity GradientSeverity = AlertSeverity.Warning;

    /// <summary>The severity a light deficit carries. Warning, because a dark day is a welfare problem, not an emergency.</summary>
    public const AlertSeverity LightDeficitSeverity = AlertSeverity.Warning;

    /// <summary>
    /// A basking surface more than <see cref="Domain.Signals.GradientWarningPolicy.ThresholdC"/> degrees above the air.
    /// </summary>
    /// <param name="terrariumId">Terrarium the signal belongs to.</param>
    /// <param name="deviceId">Device that produced the two readings.</param>
    /// <param name="triggeredAt">Instant of the first sample that exceeded the gradient.</param>
    /// <param name="differenceC">Surface minus air, in °C, as the triggering value.</param>
    public static Alert OpenGradient(
        Guid terrariumId,
        Guid deviceId,
        DateTimeOffset triggeredAt,
        decimal differenceC) =>
        new()
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,

            // The surface probe is the half of the pair the keeper would move, and it is what makes this row
            // explainable next to the surface band's own alerts.
            Metric = MetricCode.SurfaceTempC,
            Family = Alert.FamilyFor(MetricCode.SurfaceTempC),
            Phase = ThresholdPhase.Any,
            Severity = GradientSeverity,
            DedupeKey = Alert.DedupeKeyFor(
                terrariumId,
                MetricCode.SurfaceTempC,
                GradientSeverity,
                ThresholdPhase.Any,
                AlertSource.Gradient),
            State = AlertState.Open,
            Source = AlertSource.Gradient,
            TriggeredAt = triggeredAt,
            LastObservedAt = triggeredAt,

            // No band: the number that matters is the difference, and both bounds would be null because nothing was
            // compared against one.
            TriggeringValue = differenceC,
            PeakValue = differenceC,
        };

    /// <summary>The gradient came back under the threshold: the row closes where it was first observed normal.</summary>
    /// <param name="alert">The open gradient alert.</param>
    /// <param name="recoveredAt">Instant of the first sample back under the threshold.</param>
    public static void ResolveGradient(Alert alert, DateTimeOffset recoveredAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = recoveredAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
        alert.LastObservedAt = recoveredAt;
    }

    /// <summary>
    /// A day that ended below the profile's light requirement.
    /// </summary>
    /// <param name="terrariumId">Terrarium the signal belongs to.</param>
    /// <param name="deviceId">Device whose light sensor produced the day's readings.</param>
    /// <param name="triggeredAt">
    /// The instant the day became judgeable — 21:00 in the terrarium's zone — rather than the instant the sweep
    /// noticed, so the duration on the row describes the day and not the sweep interval.
    /// </param>
    /// <param name="deficitHours">Hours the day fell short by, as the triggering value.</param>
    public static Alert OpenLightDeficit(
        Guid terrariumId,
        Guid deviceId,
        DateTimeOffset triggeredAt,
        decimal deficitHours) =>
        new()
        {
            TerrariumId = terrariumId,
            DeviceId = deviceId,

            // Light is the enclosure's own condition, which is the family the metric dictionary already assigns.
            Metric = MetricCode.LightLux,
            Family = Alert.FamilyFor(MetricCode.LightLux),
            Phase = ThresholdPhase.Any,
            Severity = LightDeficitSeverity,
            DedupeKey = Alert.DedupeKeyFor(
                terrariumId,
                MetricCode.LightLux,
                LightDeficitSeverity,
                ThresholdPhase.Any,
                AlertSource.LightDeficit),
            State = AlertState.Open,
            Source = AlertSource.LightDeficit,
            TriggeredAt = triggeredAt,
            LastObservedAt = triggeredAt,
            TriggeringValue = deficitHours,
            PeakValue = deficitHours,
        };

    /// <summary>A day that met the requirement, or the next day's rollover: the row closes.</summary>
    /// <param name="alert">The open light-deficit alert.</param>
    /// <param name="resolvedAt">Instant the deficit stopped being true.</param>
    public static void ResolveLightDeficit(Alert alert, DateTimeOffset resolvedAt)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.State = AlertState.Resolved;
        alert.ResolvedAt = resolvedAt;
        alert.ResolvedReason = ResolvedReason.Recovered;
        alert.LastObservedAt = resolvedAt;
    }

    /// <summary>
    /// The condition is still true on a later observation: move the last-observed instant and widen the recorded
    /// deficit to the worst the episode has seen. No new row — one entry is what a keeper reads as "still like this".
    /// </summary>
    /// <param name="alert">The open alert.</param>
    /// <param name="observedAt">Instant being recorded.</param>
    /// <param name="value">The measurement to widen the peak with, when it is worse than what is recorded.</param>
    public static void Touch(Alert alert, DateTimeOffset observedAt, decimal value)
    {
        ArgumentNullException.ThrowIfNull(alert);

        alert.LastObservedAt = observedAt;

        if (alert.PeakValue is null || value > alert.PeakValue)
        {
            alert.PeakValue = value;
        }
    }
}
