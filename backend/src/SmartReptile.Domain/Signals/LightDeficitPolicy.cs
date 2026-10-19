using SmartReptile.Domain.Summaries;

namespace SmartReptile.Domain.Signals;

/// <summary>
/// The <c>LightDeficit</c> rule (§02-design/03 §4.3, FR-11/FR-14): a day that did not accumulate the profile's
/// required light hours is how a failed lamp or a stuck timer shows up without any actuator integration.
/// </summary>
/// <remarks>
/// Two facts make this rule different from the per-metric bands, and both are here. It is <b>accumulated</b>, so no
/// single sample can violate it — a dark minute at noon is not an alert while a dark day is. And it is judged
/// <b>once</b>, after the light part of the day is over, which is why the rule has a due time instead of a dwell
/// window: evaluating it at 09:00 would report every healthy morning as a deficit.
/// </remarks>
public static class LightDeficitPolicy
{
    /// <summary>
    /// Local time from which the day's light is considered finished and the day may be judged. The design's 21:00
    /// (§4.3), late enough that a 12-hour photoperiod starting at 07:00 has closed even with a slow start.
    /// </summary>
    public static readonly TimeOnly EvaluationLocalTime = new(21, 0);

    /// <summary>
    /// True once the local clock has reached <see cref="EvaluationLocalTime"/>. Inclusive, so exactly 21:00 is
    /// judgeable — which is what makes the boundary in <c>TC-U-31</c> decidable.
    /// </summary>
    /// <param name="localTime">Terrarium-local time of day.</param>
    public static bool IsDue(TimeOnly localTime) => localTime >= EvaluationLocalTime;

    /// <summary>
    /// Hours the day fell short by: <c>max(0, required − actual)</c>, in hours. Delegates to the summary maths so the
    /// alert and the stored <c>DailyEnvironmentalSummary.LightDeficitHours</c> can never disagree about the number
    /// the keeper is shown.
    /// </summary>
    /// <param name="requiredHours">The profile's <c>MinLightHoursPerDay</c>.</param>
    /// <param name="actualHours">Accumulated hours at or above the profile's light threshold.</param>
    public static decimal DeficitHours(decimal requiredHours, decimal actualHours) =>
        ExposureCalculator.LightDeficitHours(requiredHours, actualHours);

    /// <summary>True when the day fell short of the profile's requirement.</summary>
    /// <param name="requiredHours">The profile's <c>MinLightHoursPerDay</c>.</param>
    /// <param name="actualHours">Accumulated hours at or above the profile's light threshold.</param>
    public static bool HasDeficit(decimal requiredHours, decimal actualHours) =>
        DeficitHours(requiredHours, actualHours) > 0m;
}
