namespace SmartReptile.Application.Readings;

/// <summary>Granularity of a chart series (BR-09.1). The name is echoed to the client so it can label the axis.</summary>
public enum ReadingBucket
{
    /// <summary>One point per stored sample.</summary>
    Raw = 0,

    /// <summary>Five-minute aggregates.</summary>
    FiveMinutes = 1,

    /// <summary>Hourly aggregates, computed from raw samples until the rollup worker exists (M3).</summary>
    Hourly = 2,
}

/// <summary>
/// The range/bucketing contract of FR-09, in one place: bucket selection from the requested width, the point
/// budget that stops a chart from pulling a whole retention window, and the width limits of the two range
/// endpoints.
/// </summary>
/// <remarks>
/// Pure arithmetic on purpose. TC-I-10 asserts the bucket chosen for each offered range ("1 h → raw; 24 h → 5-min;
/// 30 d → hourly with ≤ 720 points"), and that assertion is worth having without a database behind it.
/// </remarks>
public static class RangeQueryRules
{
    /// <summary>Longest range that still returns raw samples (BR-09.1).</summary>
    public static readonly TimeSpan RawMaxWidth = TimeSpan.FromHours(6);

    /// <summary>Longest range served by five-minute buckets (BR-09.1).</summary>
    public static readonly TimeSpan FiveMinuteMaxWidth = TimeSpan.FromHours(48);

    /// <summary>
    /// Longest range the chart endpoint accepts. Hourly buckets over 30 days are exactly the documented
    /// 720-point ceiling, so a wider window could only break the point budget or silently change bucket size.
    /// </summary>
    public static readonly TimeSpan MaxChartWidth = TimeSpan.FromDays(30);

    /// <summary>Longest range the coverage endpoint accepts — one raw retention window (FR-15).</summary>
    public static readonly TimeSpan MaxCoverageWidth = TimeSpan.FromDays(90);

    /// <summary>Point budget per series (NFR-02, TC-I-10).</summary>
    public const int MaxBucketPoints = 720;

    /// <summary>Bucket size, or <c>null</c> for <see cref="ReadingBucket.Raw"/>.</summary>
    public static TimeSpan? BucketSize(ReadingBucket bucket) => bucket switch
    {
        ReadingBucket.FiveMinutes => TimeSpan.FromMinutes(5),
        ReadingBucket.Hourly => TimeSpan.FromHours(1),
        _ => null,
    };

    /// <summary>The wire name of a bucket, echoed in the response so the client can label it.</summary>
    public static string BucketName(ReadingBucket bucket) => bucket switch
    {
        ReadingBucket.FiveMinutes => "5min",
        ReadingBucket.Hourly => "hourly",
        _ => "raw",
    };

    /// <summary>Bucket size chosen from the requested width (BR-09.1).</summary>
    public static ReadingBucket ChooseBucket(TimeSpan width) =>
        width <= RawMaxWidth ? ReadingBucket.Raw
        : width <= FiveMinuteMaxWidth ? ReadingBucket.FiveMinutes
        : ReadingBucket.Hourly;

    /// <summary>
    /// Validates a requested window. Failures are returned as a stable problem code rather than an exception:
    /// a bad range is user input, and <c>from ≥ to</c> must not reach the query.
    /// </summary>
    /// <param name="fromUtc">Requested start (inclusive), UTC.</param>
    /// <param name="toUtc">Requested end, UTC.</param>
    /// <param name="maxWidth">Longest acceptable width for this endpoint.</param>
    /// <param name="maxLabel">How that limit is described to the user, e.g. <c>30 days</c>.</param>
    /// <param name="code">Problem code when the window is refused.</param>
    /// <param name="message">Message explaining the refusal.</param>
    public static bool TryValidateWindow(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan maxWidth,
        string maxLabel,
        out string? code,
        out string? message)
    {
        code = null;
        message = null;

        if (toUtc <= fromUtc)
        {
            code = "invalid_range";
            message = "The range must end after it starts.";
            return false;
        }

        if (toUtc - fromUtc > maxWidth)
        {
            code = "range_too_large";
            message = $"The range must not exceed {maxLabel}.";
            return false;
        }

        return true;
    }
}
