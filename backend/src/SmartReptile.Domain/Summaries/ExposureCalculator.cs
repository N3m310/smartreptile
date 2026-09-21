namespace SmartReptile.Domain.Summaries;

/// <summary>
/// The exposure maths (§03-implementation/06 §5): accumulated out-of-range magnitude in physically meaningful
/// units, computed on a 1-minute grid with the left-Riemann rule.
/// </summary>
/// <remarks>
/// Why this exists at all: two days can both have "60 minutes out of range" while one accumulated
/// 1.67 °C·h and the other 6.0 °C·h. For an ectotherm, the accumulated magnitude is the meaningful number —
/// a single peak reading is not. Cells without data are skipped, never counted as in-range.
/// </remarks>
public static class ExposureCalculator
{
    /// <summary>Hot exposure for a target band, in °C·hours.</summary>
    public static decimal Hot(IReadOnlyList<MinuteSample> grid, decimal targetMax) =>
        Accumulate(grid, value => Math.Max(0m, value - targetMax));

    /// <summary>Cold exposure: Σ max(0, targetMin − value) × Δt, in °C·hours.</summary>
    public static decimal Cold(IReadOnlyList<MinuteSample> grid, decimal targetMin) =>
        Accumulate(grid, value => Math.Max(0m, targetMin - value));


    /// <summary>Dry exposure: Σ max(0, targetMin − value) × Δt, in %RH·hours.</summary>
    public static decimal HumidityDry(IReadOnlyList<MinuteSample> grid, decimal targetMin) =>
        Accumulate(grid, value => Math.Max(0m, targetMin - value));

    /// <summary>Wet exposure: Σ max(0, value − targetMax) × Δt, in %RH·hours.</summary>
    public static decimal HumidityWet(IReadOnlyList<MinuteSample> grid, decimal targetMax) =>
        Accumulate(grid, value => Math.Max(0m, value - targetMax));

    /// <summary>Hours where the value was at or above the light threshold (FR-14 <c>lightHours</c>).</summary>
    public static decimal LightHours(IReadOnlyList<MinuteSample> grid, decimal luxThreshold) =>
        Accumulate(grid, value => value >= luxThreshold ? 1m : 0m);

    /// <summary>Light deficit: <c>max(0, requiredHours − actualHours)</c>.</summary>
    public static decimal LightDeficitHours(decimal requiredHours, decimal actualHours) =>
        Math.Max(0m, requiredHours - actualHours);

    /// <summary>Time-weighted mean of the available cells; null when there is no data at all.</summary>
    public static decimal? TimeWeightedAverage(IReadOnlyList<MinuteSample> grid)
    {
        decimal sum = 0m;
        var count = 0;

        foreach (var cell in grid)
        {
            if (cell.Value is { } value)
            {
                sum += value;
                count++;
            }
        }

        return count == 0 ? null : sum / count;
    }

    /// <summary>Lowest available value; null when there is no data.</summary>
    public static decimal? Min(IReadOnlyList<MinuteSample> grid)
    {
        decimal? min = null;

        foreach (var cell in grid)
        {
            if (cell.Value is { } value && (min is null || value < min))
            {
                min = value;
            }
        }

        return min;
    }

    /// <summary>Highest available value; null when there is no data.</summary>
    public static decimal? Max(IReadOnlyList<MinuteSample> grid)
    {
        decimal? max = null;

        foreach (var cell in grid)
        {
            if (cell.Value is { } value && (max is null || value > max))
            {
                max = value;
            }
        }

        return max;
    }

    private static decimal Accumulate(IReadOnlyList<MinuteSample> grid, Func<decimal, decimal> weightPerHour)
    {
        if (grid.Count == 0)
        {
            return 0m;
        }

        // Cell width in hours, inferred from the grid spacing (1 minute by default).
        var hoursPerCell = grid.Count > 1
            ? (decimal)(grid[1].AtUtc - grid[0].AtUtc).TotalHours
            : 1m / 60m;

        decimal total = 0m;

        foreach (var cell in grid)
        {
            if (cell.Value is { } value)
            {
                total += weightPerHour(value) * hoursPerCell;
            }
        }

        return total;
    }
}

/// <summary>
/// Coverage, compliance and low-confidence rules (§03-implementation/06 §5.4–5.5). Kept separate from the
/// exposure maths because these numbers qualify the exposure numbers — and a compliance figure without its
/// coverage is misleading (BR-14.6).
/// </summary>
public static class CoverageMath
{
    /// <summary>Summary is flagged low-confidence below this coverage percentage (DI-08).</summary>
    public const decimal LowConfidenceThresholdPct = 80m;

    /// <summary>Received ÷ expected × 100, clamped to 0…100.</summary>
    public static decimal CoveragePct(int samplesReceived, int expectedSamples)
    {
        if (expectedSamples <= 0)
        {
            return 0m;
        }

        var pct = 100m * samplesReceived / expectedSamples;
        return Math.Clamp(pct, 0m, 100m);
    }

    /// <summary>True when the summary must be presented as low confidence (DI-08).</summary>
    public static bool IsLowConfidence(decimal coveragePct) => coveragePct < LowConfidenceThresholdPct;

    /// <summary>
    /// 100 × (1 − outOfRangeMinutes ÷ minutesWithData). Note that this is only meaningful next to coverage:
    /// a day with 40% data can still show 100% compliance, which is why the UI always renders both.
    /// </summary>
    public static decimal CompliancePct(int outOfRangeMinutes, int minutesWithData)
    {
        if (minutesWithData <= 0)
        {
            return 0m;
        }

        var pct = 100m * (1m - ((decimal)outOfRangeMinutes / minutesWithData));
        return Math.Clamp(pct, 0m, 100m);
    }

    /// <summary>Number of cells that actually carry data — the denominator for compliance.</summary>
    public static int MinutesWithData(IReadOnlyList<MinuteSample> grid) =>
        grid.Count(c => c.Value.HasValue);
}
