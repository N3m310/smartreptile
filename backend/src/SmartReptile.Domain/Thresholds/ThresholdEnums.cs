namespace SmartReptile.Domain.Thresholds;

/// <summary>Evaluation window for a band. <see cref="Any"/> means the metric is not split day/night.</summary>
public enum ThresholdPhase
{
    Any = 0,
    Day = 1,
    Night = 2,
}

/// <summary>Where an effective band came from — surfaced to the UI so limits are never unexplained (BR-10.3).</summary>
public enum ThresholdSource
{
    Override = 0,
    Profile = 1,
    SystemDefault = 2,
}

/// <summary>Direction of a violation, used for peak tracking and message wording.</summary>
public enum ViolationKind
{
    None = 0,
    Hot = 1,
    Cold = 2,
}

/// <summary>One validation problem on a candidate band, mapped to a field-level error in the API response.</summary>
/// <param name="Field">Field name, camelCase, e.g. <c>targetMin</c>.</param>
/// <param name="Code">Stable problem code, e.g. <c>threshold_ordering_invalid</c>.</param>
/// <param name="Message">Human-readable explanation (the client localises its own text from the code).</param>
public sealed record BandViolation(string Field, string Code, string Message);
