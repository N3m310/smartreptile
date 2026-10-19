namespace SmartReptile.Domain.Signals;

/// <summary>
/// The <c>GradientWarning</c> rule (§02-design/03 §4.3, FR-11): a basking surface far hotter than the air around it
/// is a burn risk that neither the surface band nor the air band would catch on its own.
/// </summary>
/// <remarks>
/// Pure and dependency-free, like the other derived-signal rules, so <c>TC-U-30</c>'s boundary is decidable without a
/// clock, a store or a wait. It is a SHOULD signal and it never replaces the per-metric bands: the surface band still
/// judges the surface on its own, and this answers the different question of how far apart the two readings are.
/// </remarks>
public static class GradientWarningPolicy
{
    /// <summary>
    /// The surface-to-air difference that counts as a warning, in °C. Strictly greater than: a gradient of exactly
    /// 12 °C is the boundary and does not raise.
    /// </summary>
    public const decimal ThresholdC = 12m;

    /// <summary>Surface minus air, or null when either reading is missing.</summary>
    /// <param name="surfaceTempC">Basking-surface temperature, if the sample carried one.</param>
    /// <param name="airTempC">Air temperature, if the sample carried one.</param>
    public static decimal? Difference(decimal? surfaceTempC, decimal? airTempC) =>
        surfaceTempC is { } surface && airTempC is { } air ? surface - air : null;

    /// <summary>
    /// True when both readings exist and the surface is more than <see cref="ThresholdC"/> hotter than the air.
    /// A missing reading is not a gradient: the rule needs both numbers to have an opinion, and inventing a value for
    /// the absent one would report a fault the evidence does not support.
    /// </summary>
    /// <param name="surfaceTempC">Basking-surface temperature, if the sample carried one.</param>
    /// <param name="airTempC">Air temperature, if the sample carried one.</param>
    public static bool IsExceeded(decimal? surfaceTempC, decimal? airTempC) =>
        Difference(surfaceTempC, airTempC) > ThresholdC;
}
