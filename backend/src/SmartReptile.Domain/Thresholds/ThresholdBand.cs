using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Thresholds;

/// <summary>
/// The environmental envelope for one metric in one phase: a target band (outside ⇒ Warning) and a wider
/// critical band (outside ⇒ Critical), plus the engine parameters that stop alert spam.
/// </summary>
/// <remarks>
/// Pure value object — no EF Core, no clock, no logging. A <c>record</c> because bands are compared and
/// adjusted with-value semantics (<c>band with { TargetMax = … }</c>) in tests and in the threshold editor.
/// The ordering rules here are also enforced by CHECK constraints in the database (DI-05,
/// §07-appendices/02 §4), so a bypass in application code cannot silently create an incoherent band.
/// </remarks>
public sealed record ThresholdBand
{
    /// <summary>Metric this band applies to.</summary>
    public MetricCode Metric { get; init; }

    /// <summary>Phase this band applies to.</summary>
    public ThresholdPhase Phase { get; init; } = ThresholdPhase.Any;

    /// <summary>Lower bound of the target band (inclusive).</summary>
    public decimal TargetMin { get; init; }

    /// <summary>Upper bound of the target band (inclusive).</summary>
    public decimal TargetMax { get; init; }

    /// <summary>Lower bound of the critical band. Null ⇒ derived as <see cref="TargetMin"/> minus the margin.</summary>
    public decimal? CriticalMin { get; init; }

    /// <summary>Upper bound of the critical band. Null ⇒ derived as <see cref="TargetMax"/> plus the margin.</summary>
    public decimal? CriticalMax { get; init; }

    /// <summary>Minutes outside the target band before a Warning alert is opened (BR-11.3).</summary>
    public int DwellWarnMinutes { get; init; } = 5;

    /// <summary>Minutes outside the critical band before the alert escalates to Critical (BR-11.3).</summary>
    public int DwellCritMinutes { get; init; } = 2;

    /// <summary>Distance back inside the band required to count as recovered, preventing flap (BR-11.4).</summary>
    public decimal RecoveryMargin { get; init; } = 0.5m;

    /// <summary>Disabled bands are skipped by the evaluator without deleting the configured values.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Literature or user reference for these numbers — required for built-in profiles (FR-10).</summary>
    public string? SourceRef { get; init; }

    /// <summary>Optional link to the source.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Effective lower critical bound.</summary>
    public decimal EffectiveCriticalMin => CriticalMin ?? TargetMin - DefaultCriticalMargin;

    /// <summary>Effective upper critical bound.</summary>
    public decimal EffectiveCriticalMax => CriticalMax ?? TargetMax + DefaultCriticalMargin;

    /// <summary>Default critical margin when the critical band is not configured: 10% of the band width, min 2 units.</summary>
    private decimal DefaultCriticalMargin => Math.Max(2m, (TargetMax - TargetMin) * 0.10m);

    /// <summary>True when the value is above the target band.</summary>
    public bool IsHot(decimal value) => value > TargetMax;

    /// <summary>True when the value is below the target band.</summary>
    public bool IsCold(decimal value) => value < TargetMin;

    /// <summary>True when the value is outside the target band.</summary>
    public bool IsOutOfBand(decimal value) => IsHot(value) || IsCold(value);

    /// <summary>True when the value is outside the critical band.</summary>
    public bool IsCritical(decimal value) => value > EffectiveCriticalMax || value < EffectiveCriticalMin;

    /// <summary>Direction of the current violation.</summary>
    public ViolationKind Direction(decimal value) => IsHot(value) ? ViolationKind.Hot : IsCold(value) ? ViolationKind.Cold : ViolationKind.None;

    /// <summary>
    /// True when the value is inside the band by at least <see cref="RecoveryMargin"/> (BR-11.4).
    /// Recovery deliberately needs more than merely re-entering the band, or a noisy sensor flaps.
    /// </summary>
    public bool IsRecovered(decimal value) =>
        value >= TargetMin + RecoveryMargin && value <= TargetMax - RecoveryMargin;

    /// <summary>
    /// Validates the band against the rules in §03-implementation/06 §2. Returns every violation so the UI can
    /// show field-level errors instead of one opaque message.
    /// </summary>
    public IReadOnlyList<BandViolation> Validate()
    {
        var violations = new List<BandViolation>();
        var definition = MetricDictionary.Get(Metric);

        // DI-05 / rule 1: target band must be ordered.
        if (TargetMin >= TargetMax)
        {
            violations.Add(new BandViolation(
                "targetMin",
                "threshold_ordering_invalid",
                $"TargetMin ({TargetMin}) must be lower than TargetMax ({TargetMax})."));
        }

        // DI-05 / rule 2: the critical band must enclose the target band.
        if (CriticalMin is { } criticalMin && criticalMin > TargetMin)
        {
            violations.Add(new BandViolation(
                "criticalMin",
                "threshold_critical_invalid",
                $"CriticalMin ({criticalMin}) must not be greater than TargetMin ({TargetMin})."));
        }

        if (CriticalMax is { } criticalMax && criticalMax < TargetMax)
        {
            violations.Add(new BandViolation(
                "criticalMax",
                "threshold_critical_invalid",
                $"CriticalMax ({criticalMax}) must not be lower than TargetMax ({TargetMax})."));
        }

        if (CriticalMin is { } lo && CriticalMax is { } hi && lo >= hi)
        {
            violations.Add(new BandViolation(
                "criticalMin",
                "threshold_critical_invalid",
                $"CriticalMin ({lo}) must be lower than CriticalMax ({hi})."));
        }

        // Rule 4: a critical dwell longer than the warning dwell would be incoherent.
        if (DwellCritMinutes > DwellWarnMinutes)
        {
            violations.Add(new BandViolation(
                "dwellCritMinutes",
                "threshold_dwell_invalid",
                $"DwellCritMinutes ({DwellCritMinutes}) must not exceed DwellWarnMinutes ({DwellWarnMinutes})."));
        }

        if (DwellWarnMinutes <= 0)
        {
            violations.Add(new BandViolation(
                "dwellWarnMinutes",
                "threshold_dwell_invalid",
                "DwellWarnMinutes must be greater than zero."));
        }

        // Rule 5: a recovery margin wider than half the band would make recovery unreachable.
        if (RecoveryMargin >= (TargetMax - TargetMin) / 2m)
        {
            violations.Add(new BandViolation(
                "recoveryMargin",
                "threshold_recovery_margin_invalid",
                $"RecoveryMargin ({RecoveryMargin}) must be smaller than half the target band width ({(TargetMax - TargetMin) / 2m})."));
        }

        if (RecoveryMargin < 0m)
        {
            violations.Add(new BandViolation(
                "recoveryMargin",
                "threshold_recovery_margin_invalid",
                "RecoveryMargin must not be negative."));
        }

        // Rule 7: a band outside the metric's plausible range is almost always a data-entry mistake.
        if (TargetMin < definition.PlausibleMin || TargetMax > definition.PlausibleMax)
        {
            violations.Add(new BandViolation(
                "targetMin",
                "threshold_implausible_band",
                $"Target band must lie within the plausible range for {definition.DisplayName} " +
                $"({definition.PlausibleMin}…{definition.PlausibleMax} {definition.Unit})."));
        }

        return violations;
    }

    /// <summary>True when <see cref="Validate"/> finds nothing.</summary>
    public bool IsValid() => Validate().Count == 0;
}
