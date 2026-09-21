using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Domain.Species;

/// <summary>Coarse climate grouping that seeds the built-in bands (§07-appendices/05 §4).</summary>
public enum ClimateZone
{
    Tropical = 0,
    SemiArid = 1,
    Arid = 2,
    Temperate = 3,
}

/// <summary>
/// A named set of environmental bands for one species or climate zone. Built-in profiles are read-only in the
/// UI and require a literature reference on every band (FR-10, brief §6).
/// </summary>
public class SpeciesProfile
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Leopard gecko (semi-desert)".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Scientific name, e.g. <c>Eublepharis macularius</c>.</summary>
    public string ScientificName { get; set; } = string.Empty;

    /// <summary>Climate zone used for the non-blocking plausibility hint (BR-10.5).</summary>
    public ClimateZone ClimateZone { get; set; }

    /// <summary>Seeded profiles cannot be edited or deleted — duplicate them into a custom profile instead (FR-10 A2).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Photoperiod length in hours; drives the day/night phase split (BR-11.2).</summary>
    public decimal PhotoperiodHours { get; set; } = 12m;

    /// <summary>Local time the photoperiod starts.</summary>
    public TimeOnly LightsOnLocalTime { get; set; } = new(7, 0);

    /// <summary>Illuminance above which time counts towards <c>lightHours</c>.</summary>
    public decimal LightThresholdLux { get; set; } = 1000m;

    /// <summary>Minimum light hours per day before a <c>LightDeficit</c> signal is raised.</summary>
    public decimal MinLightHoursPerDay { get; set; } = 8m;

    /// <summary>Husbandry summary and caveats (e.g. the humid-hide distinction for semi-arid species).</summary>
    public string? Notes { get; set; }

    /// <summary>Owner for custom profiles; null for seeded profiles.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Optimistic concurrency token, so two editors cannot silently overwrite each other (UC-03 A3).</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Bands defined by this profile.</summary>
    public ICollection<Threshold> Thresholds { get; set; } = new List<Threshold>();
}

/// <summary>A band belonging to a species profile. Unique per (profile, metric, phase).</summary>
public class Threshold
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning profile.</summary>
    public Guid SpeciesProfileId { get; set; }

    /// <summary>Owning profile navigation.</summary>
    public SpeciesProfile? SpeciesProfile { get; set; }

    /// <summary>Metric the band applies to.</summary>
    public Metrics.MetricCode Metric { get; set; }

    /// <summary>Phase the band applies to.</summary>
    public ThresholdPhase Phase { get; set; } = ThresholdPhase.Any;

    /// <summary>Lower bound of the target band.</summary>
    public decimal TargetMin { get; set; }

    /// <summary>Upper bound of the target band.</summary>
    public decimal TargetMax { get; set; }

    /// <summary>Lower bound of the critical band; null ⇒ derived from the target band.</summary>
    public decimal? CriticalMin { get; set; }

    /// <summary>Upper bound of the critical band; null ⇒ derived from the target band.</summary>
    public decimal? CriticalMax { get; set; }

    /// <summary>Minutes outside the target band before a Warning is raised.</summary>
    public int DwellWarnMinutes { get; set; } = 5;

    /// <summary>Minutes outside the critical band before escalation.</summary>
    public int DwellCritMinutes { get; set; } = 2;

    /// <summary>Distance back inside the band required to consider the condition recovered.</summary>
    public decimal RecoveryMargin { get; set; } = 0.5m;

    /// <summary>Disabled bands are skipped without losing the configured numbers.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Literature reference for these numbers. Required for built-in profiles (FR-10, brief §6).</summary>
    public string? SourceRef { get; set; }

    /// <summary>Optional link to the source.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Projects this row into the domain value object the engine evaluates with.</summary>
    public ThresholdBand ToBand() => new()
    {
        Metric = Metric,
        Phase = Phase,
        TargetMin = TargetMin,
        TargetMax = TargetMax,
        CriticalMin = CriticalMin,
        CriticalMax = CriticalMax,
        DwellWarnMinutes = DwellWarnMinutes,
        DwellCritMinutes = DwellCritMinutes,
        RecoveryMargin = RecoveryMargin,
        Enabled = Enabled,
        SourceRef = SourceRef,
        SourceUrl = SourceUrl,
    };
}

/// <summary>
/// A per-terrarium band that wins over the assigned profile (BR-10.3). Same shape as
/// <see cref="Threshold"/> minus the profile link, plus the terrarium it overrides.
/// </summary>
public class ThresholdOverride
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Terrarium the override belongs to.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Metric the override applies to.</summary>
    public Metrics.MetricCode Metric { get; set; }

    /// <summary>Phase the override applies to.</summary>
    public ThresholdPhase Phase { get; set; } = ThresholdPhase.Any;

    /// <summary>Lower bound of the target band.</summary>
    public decimal TargetMin { get; set; }

    /// <summary>Upper bound of the target band.</summary>
    public decimal TargetMax { get; set; }

    /// <summary>Lower bound of the critical band.</summary>
    public decimal? CriticalMin { get; set; }

    /// <summary>Upper bound of the critical band.</summary>
    public decimal? CriticalMax { get; set; }

    /// <summary>Minutes outside the target band before a Warning is raised.</summary>
    public int DwellWarnMinutes { get; set; } = 5;

    /// <summary>Minutes outside the critical band before escalation.</summary>
    public int DwellCritMinutes { get; set; } = 2;

    /// <summary>Distance back inside the band required to consider the condition recovered.</summary>
    public decimal RecoveryMargin { get; set; } = 0.5m;

    /// <summary>Disabled overrides fall back to the profile band.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Why this terrarium differs from the profile (free text, shown in the UI).</summary>
    public string? Note { get; set; }

    /// <summary>Who created the override.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Projects this row into the domain value object.</summary>
    public ThresholdBand ToBand() => new()
    {
        Metric = Metric,
        Phase = Phase,
        TargetMin = TargetMin,
        TargetMax = TargetMax,
        CriticalMin = CriticalMin,
        CriticalMax = CriticalMax,
        DwellWarnMinutes = DwellWarnMinutes,
        DwellCritMinutes = DwellCritMinutes,
        RecoveryMargin = RecoveryMargin,
        Enabled = Enabled,
        SourceRef = Note,
    };
}
