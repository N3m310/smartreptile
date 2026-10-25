namespace SmartReptile.Domain.Terrariums;

/// <summary>The enclosure's lifecycle state (§02-design/02 DI-10).</summary>
/// <remarks>
/// Values are persisted as integers, so a member is only ever appended — renumbering one silently rewrites the
/// state of every existing row.
/// </remarks>
public enum TerrariumStatus
{
    /// <summary>Readable, editable and judged by the evaluator.</summary>
    Active = 0,

    /// <summary>
    /// Removed by its owner. The row and its history stay in the database for audit; reads hide it, and a request
    /// that names it is told it was deleted rather than that it never existed.
    /// </summary>
    Deleted = 1,
}

/// <summary>
/// The monitored enclosure. Soft-deleted so alert and summary history stays auditable (§02-design/02 DI-10).
/// </summary>
public class Terrarium
{
    /// <summary>Surrogate key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owner, or null for an enclosure an administrator created and has not assigned yet (2026-10-23). An ownerless
    /// enclosure appears in no keeper's list and answers <c>404</c> on the owner-scoped routes; it is reachable only
    /// by an account holding the cross-account permission, which is also the only account that can assign it.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>Display name, e.g. "Linh's gecko box".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Species profile whose bands apply unless overridden (FR-10), or null for an enclosure that has none
    /// (2026-10-25). A profile is no longer required to create an enclosure: a keeper may set the enclosure up
    /// before deciding what lives in it, and the evaluation layer then simply has no bands to judge against — the
    /// readings are still stored, but no temperature, humidity or UV alert can open for it until a profile is
    /// assigned.
    /// </summary>
    public Guid? SpeciesProfileId { get; set; }

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

    /// <summary>
    /// SQL Server <c>rowversion</c>, the optimistic-concurrency token FR-03's update half is built on. A client
    /// reads it from the <c>ETag</c> of a detail response and returns it as <c>If-Match</c>; a concurrent edit
    /// changes the value, so the stale update affects zero rows instead of silently overwriting the other edit.
    /// Null only before the row has been inserted.
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>
    /// Lifecycle state, and the source of truth for "has this been deleted" (DI-10). A state column rather than a
    /// nullable timestamp because every read asks the question, and a null timestamp answers it only by convention —
    /// while also having to mean "never deleted" and "deleted at an unknown moment" at the same time.
    /// </summary>
    public TerrariumStatus Status { get; set; } = TerrariumStatus.Active;

    /// <summary>
    /// When the soft delete happened. A detail of <see cref="Status"/>, never a second source of truth: the state
    /// decides visibility, so a row whose timestamp is set while its state is <see cref="TerrariumStatus.Active"/>
    /// is still active. Null while the terrarium has never been deleted.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>True while the terrarium is active.</summary>
    public bool IsActive => Status == TerrariumStatus.Active;

    /// <summary>Bound device (at most one, enforced by a filtered unique index — DI-04).</summary>
    public Devices.Device? Device { get; set; }

    /// <summary>Per-terrarium band overrides.</summary>
    public ICollection<Species.ThresholdOverride> ThresholdOverrides { get; set; } = new List<Species.ThresholdOverride>();
}
