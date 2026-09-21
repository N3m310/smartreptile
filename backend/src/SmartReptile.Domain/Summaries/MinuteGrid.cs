using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Summaries;

/// <summary>
/// One cell of the 1-minute analysis grid used by the summary and exposure maths
/// (§03-implementation/06 §5). A null value means "no data for this minute" and is never treated as a
/// healthy reading — honest gaps are a product requirement (standing rule 3 in the roadmap).
/// </summary>
/// <param name="AtUtc">Minute start, UTC.</param>
/// <param name="Value">Interpolated value, or null when the gap is too wide to interpolate.</param>
public readonly record struct MinuteSample(DateTimeOffset AtUtc, decimal? Value);

/// <summary>Builds the 1-minute grid from stored samples, interpolating only across short gaps.</summary>
public static class MinuteGrid
{
    /// <summary>
    /// Default maximum gap that may be interpolated. Wider gaps stay null: inventing 10 minutes of data
    /// between two readings would fabricate evidence.
    /// </summary>
    public static readonly TimeSpan DefaultMaxInterpolationGap = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Produces one cell per <paramref name="step"/> between <paramref name="fromUtc"/> (inclusive) and
    /// <paramref name="toUtc"/> (exclusive), linearly interpolating between bracketing samples when the gap
    /// is at most <paramref name="maxGap"/>.
    /// </summary>
    /// <param name="orderedSamples">Samples already ordered by timestamp, gaps allowed.</param>
    /// <param name="fromUtc">Window start.</param>
    /// <param name="toUtc">Window end (exclusive).</param>
    /// <param name="step">Cell width; defaults to one minute.</param>
    /// <param name="maxGap">Widest gap that may be interpolated.</param>
    public static List<MinuteSample> Build(
        IEnumerable<(DateTimeOffset AtUtc, decimal? Value)> orderedSamples,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan? step = null,
        TimeSpan? maxGap = null)
    {
        var width = step ?? TimeSpan.FromMinutes(1);
        var maxInterpolationGap = maxGap ?? DefaultMaxInterpolationGap;
        var points = orderedSamples
            .Where(p => p.Value.HasValue)
            .Select(p => (AtUtc: p.AtUtc, Value: p.Value!.Value))
            .OrderBy(p => p.AtUtc)
            .ToList();

        var grid = new List<MinuteSample>();
        var index = 0;

        for (var at = fromUtc; at < toUtc; at += width)
        {
            while (index + 1 < points.Count && points[index + 1].AtUtc <= at)
            {
                index++;
            }

            grid.Add(new MinuteSample(at, Interpolate(points, index, at, maxInterpolationGap)));
        }

        return grid;
    }

    private static decimal? Interpolate(
        List<(DateTimeOffset AtUtc, decimal Value)> points,
        int index,
        DateTimeOffset at,
        TimeSpan maxGap)
    {
        if (points.Count == 0)
        {
            return null;
        }

        // The caller advanced 'index' to the last sample at or before 'at', or left it at 0 when 'at' is
        // earlier than every sample.
        if (points[index].AtUtc > at)
        {
            // Before the first sample: hold the first value only inside the interpolation tolerance.
            return points[0].AtUtc - at <= maxGap ? points[index].Value : null;
        }

        if (index >= points.Count - 1)
        {
            // At or after the last sample: hold it only inside the tolerance, then report no data. Claiming a
            // constant reading for hours after the device went silent is exactly the failure this prevents.
            return at - points[index].AtUtc <= maxGap ? points[index].Value : null;
        }

        var left = points[index];
        var right = points[index + 1];

        // An exact hit is a real reading, not an interpolation: it must survive even when the *next* sample is
        // hours away (otherwise the first reading after a gap would be reported as missing data).
        if (at == left.AtUtc)
        {
            return left.Value;
        }

        var gap = right.AtUtc - left.AtUtc;

        if (gap > maxGap)
        {
            // Inside a genuine data gap: report no data rather than a straight line through it (BR-09.5).
            return null;
        }

        var position = (at - left.AtUtc).Ticks / (double)gap.Ticks;
        return left.Value + ((right.Value - left.Value) * (decimal)position);
    }
}
