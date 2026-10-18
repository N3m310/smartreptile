using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Species;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// The species-profile catalogue's persistence port (FR-10, `07-appendices/03` §4.3). One aggregate per call: a
/// profile and its bands, because every rule about a profile is a rule about the set as a whole — a band set is
/// replaced, validated and copied as a unit.
/// </summary>
/// <remarks>
/// Ownership is a column rather than a membership table, because the product is single-owner in v1 (limitation
/// L-07): <c>CreatedByUserId</c> null marks a built-in and everyone may see it, and anything else belongs to the
/// account that wrote it.
/// </remarks>
public interface ISpeciesProfileStore
{
    /// <summary>
    /// The profiles the caller may pick from: the built-ins plus their own, ordered by name, each with its bands
    /// loaded. Someone else's custom profiles are absent rather than hidden after the fact.
    /// </summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SpeciesProfile>> ListVisibleAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>One profile with its bands, whoever owns it. Null when no such profile exists.</summary>
    /// <param name="profileId">Profile identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SpeciesProfile?> FindAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// True when the account already holds a profile with this name. The table's unique index is
    /// <c>(CreatedByUserId, Name)</c>, so this is a courtesy check that lets the route answer `409 profile_name_taken`
    /// with the offending name instead of letting the index fail the insert. Comparison is case-insensitive.
    /// </summary>
    /// <param name="userId">Owner the name would belong to.</param>
    /// <param name="name">Candidate name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> NameTakenAsync(Guid userId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// True when a terrarium is assigned to this profile. Soft-deleted terrariums count: their rows still hold the
    /// foreign key, so a delete that ignored them would be refused by the database instead of by this rule (DI-10).
    /// </summary>
    /// <param name="profileId">Profile identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsAssignedAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>
    /// True when an account with this id exists — the same check <see cref="ITerrariumStore"/> makes, for the same
    /// reason: a token this server signed can name an account it does not have, and the write paths refuse that as
    /// `401` rather than writing a row nobody will ever see (BUG-11).
    /// </summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stages a new profile with its bands.</summary>
    /// <param name="profile">The aggregate to insert.</param>
    void Add(SpeciesProfile profile);

    /// <summary>
    /// Replaces a profile's band set in one unit of work: the rows it held are deleted and the rows given are
    /// inserted, with the collection on <paramref name="profile"/> left holding them afterwards so the caller can
    /// describe the profile it just changed.
    /// </summary>
    /// <remarks>
    /// Staged through the adapter rather than by clearing and refilling the loaded collection, for the trap roadmap
    /// 3.1 recorded for <c>ThresholdOverride</c>: a band carries a client-assigned key, and EF reads a new band
    /// found in the navigation of a tracked profile as an existing row — emitting
    /// <c>UPDATE [Threshold] … WHERE [Id] = …</c> that matches nothing and fails the whole save with a
    /// <c>DbUpdateConcurrencyException</c>. The unit suite cannot see it; only a real database can.
    /// </remarks>
    /// <param name="profile">The profile whose set is being replaced.</param>
    /// <param name="bands">The complete set of bands the profile should hold.</param>
    void ReplaceBands(SpeciesProfile profile, IReadOnlyList<Threshold> bands);

    /// <summary>Stages the deletion of a profile; its bands follow through the cascade.</summary>
    /// <param name="profile">The aggregate to delete.</param>
    void Remove(SpeciesProfile profile);

    /// <summary>Stages an audit row on the same unit of work as the change it describes (BR-10.4).</summary>
    /// <param name="entry">The row to write.</param>
    void AddAuditEntry(AuditLog entry);

    /// <summary>Commits the staged work.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
