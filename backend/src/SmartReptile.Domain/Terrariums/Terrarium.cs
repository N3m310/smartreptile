namespace SmartReptile.Domain.Terrariums;

/// <summary>
/// The monitored enclosure. Soft-deleted so alert and summary history stays auditable (§02-design/02 DI-10).
/// </summary>
public class Terrarium
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner.</summary>
    public Guid UserId { get; set; }

    /// <summary>Display name, e.g. "Linh's gecko box".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Species profile whose bands apply unless overridden (FR-10).</summary>
    public Guid SpeciesProfileId { get; set; }

    /// <summary>Species profile navigation.</summary>
    public Species.SpeciesProfile? SpeciesProfile { get; set; }

    /// <summary>Free-text location label ("desk", "study corner") — users are advised not to enter an address.</summary>
    public string? Location { get; set; }

    /// <summary>Keeper notes (e.g. shedding/grazing observations).</summary>
    public string? Description { get; set; }

    /// <summary>Timezone used for local-day bucketing in the summary layer only (ADR-015).</summary>
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Soft-delete marker; non-null means the terrarium was removed by its owner.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>True while the terrarium is active.</summary>
    public bool IsActive => DeletedAt is null;

    /// <summary>Bound device (at most one, enforced by a filtered unique index — DI-04).</summary>
    public Devices.Device? Device { get; set; }

    /// <summary>Per-terrarium band overrides.</summary>
    public ICollection<Species.ThresholdOverride> ThresholdOverrides { get; set; } = new List<Species.ThresholdOverride>();
}
