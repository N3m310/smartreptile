namespace SmartReptile.Domain.Thresholds;

/// <summary>
/// Decides which phase a band is evaluated in (BR-11.2): <see cref="ThresholdPhase.Day"/> while the local time is
/// inside the photoperiod window, <see cref="ThresholdPhase.Night"/> otherwise.
/// </summary>
/// <remarks>
/// Metrics configured with <see cref="ThresholdPhase.Any"/> never reach this rule — the caller looks for an
/// <c>Any</c> band first — because a band that ignores the split must not be phase-shifted by a lighting schedule
/// it does not use.
/// <para>
/// Deliberately pure and clock-free. The caller converts the instant into the terrarium's time zone (ADR-015 puts
/// that conversion on the server) and passes a local time of day in, so this rule is testable without a time-zone
/// database and without a wall clock.
/// </para>
/// </remarks>
public static class ThresholdPhaseResolver
{
    /// <summary>
    /// The phase one metric is evaluated in: <see cref="ThresholdPhase.Any"/> when the metric is configured that
    /// way, otherwise the day/night phase of <paramref name="localTimeOfDay"/>.
    /// </summary>
    /// <remarks>
    /// Shared by the read surface (which colours a card) and the evaluator (which keys its state rows), so the two
    /// cannot disagree about which band was in force at a given instant.
    /// </remarks>
    /// <param name="configuredPhases">Phases in which the metric has an enabled band.</param>
    /// <param name="localTimeOfDay">Time of day in the terrarium's own time zone.</param>
    /// <param name="lightsOnLocalTime">Local time the photoperiod starts (from the species profile).</param>
    /// <param name="photoperiodHours">Window length in hours; 24 means "always day".</param>
    public static ThresholdPhase ResolveFor(
        IEnumerable<ThresholdPhase> configuredPhases,
        TimeOnly localTimeOfDay,
        TimeOnly lightsOnLocalTime,
        decimal photoperiodHours) =>
        configuredPhases.Contains(ThresholdPhase.Any)
            ? ThresholdPhase.Any
            : Resolve(localTimeOfDay, lightsOnLocalTime, photoperiodHours);

    /// <summary>
    /// The phase at <paramref name="localTimeOfDay"/> for a window that opens at
    /// <paramref name="lightsOnLocalTime"/> and runs for <paramref name="photoperiodHours"/> hours.
    /// </summary>
    /// <param name="localTimeOfDay">Time of day in the terrarium's own time zone.</param>
    /// <param name="lightsOnLocalTime">Local time the photoperiod starts (from the species profile).</param>
    /// <param name="photoperiodHours">Window length in hours; 24 means "always day".</param>
    public static ThresholdPhase Resolve(
        TimeOnly localTimeOfDay,
        TimeOnly lightsOnLocalTime,
        decimal photoperiodHours)
    {
        // A non-positive photoperiod is not a schedule, it is the absence of one: night. The species-profile rules
        // keep this inside 0…24, so this only guards a row edited outside the application.
        if (photoperiodHours <= 0m)
        {
            return ThresholdPhase.Night;
        }

        var hours = photoperiodHours >= 24m ? 24m : photoperiodHours;
        var lightsOffLocalTime = lightsOnLocalTime.Add(TimeSpan.FromHours((double)hours));

        // TimeOnly wraps within the day, so a window crossing midnight appears as lightsOff <= lightsOn.
        return lightsOffLocalTime > lightsOnLocalTime
            ? localTimeOfDay >= lightsOnLocalTime && localTimeOfDay < lightsOffLocalTime
                ? ThresholdPhase.Day
                : ThresholdPhase.Night
            : localTimeOfDay >= lightsOnLocalTime || localTimeOfDay < lightsOffLocalTime
                ? ThresholdPhase.Day
                : ThresholdPhase.Night;
    }
}
