namespace SmartReptile.Domain.Alerts;

/// <summary>Why a request to silence a terrarium was refused.</summary>
public enum SilenceRefusal
{
    /// <summary>The request is acceptable.</summary>
    None = 0,

    /// <summary>No reason was given (§02-design/05 §5: "reason required").</summary>
    ReasonMissing = 1,

    /// <summary>The window ends before it starts — a silence of nothing, most often a client clock that is behind.</summary>
    UntilNotInTheFuture = 2,

    /// <summary>The window is longer than <see cref="MetricSilence.MaxWindowHours"/>.</summary>
    WindowTooLong = 3,

    /// <summary>The reason is longer than the column holds.</summary>
    ReasonTooLong = 4,
}

/// <summary>
/// The validation rule of a silence request (§02-design/05 §5), as a pure function.
/// </summary>
/// <remarks>
/// <b>Why a cap at all.</b> The design's sentence is "silence window ≤ 24 h, per metric, reason required, visible on
/// the dashboard" — the cap is what keeps silence an acknowledgement of a known condition rather than a way to turn
/// monitoring off. A keeper who wants a fortnight of quiet is asking for a different product decision, and the
/// endpoint should make them come back tomorrow rather than let one click hide a week.
/// <para>
/// The order of the checks is deliberate: the reason is checked first because it is the one the client always sends,
/// so a request missing it gets the most useful answer instead of a window complaint.
/// </para>
/// </remarks>
public static class MetricSilencePolicy
{
    /// <summary>Judges a proposed window.</summary>
    /// <param name="nowUtc">Server instant the window is measured from.</param>
    /// <param name="untilUtc">Proposed end of the window.</param>
    /// <param name="reason">Proposed reason, as the caller sent it.</param>
    /// <returns>The refusal, or <see cref="SilenceRefusal.None"/> when the request may proceed.</returns>
    public static SilenceRefusal Validate(DateTimeOffset nowUtc, DateTimeOffset untilUtc, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return SilenceRefusal.ReasonMissing;
        }

        if (reason.Trim().Length > MetricSilence.ReasonMaxLength)
        {
            return SilenceRefusal.ReasonTooLong;
        }

        if (untilUtc <= nowUtc)
        {
            return SilenceRefusal.UntilNotInTheFuture;
        }

        return untilUtc - nowUtc > TimeSpan.FromHours(MetricSilence.MaxWindowHours)
            ? SilenceRefusal.WindowTooLong
            : SilenceRefusal.None;
    }
}
