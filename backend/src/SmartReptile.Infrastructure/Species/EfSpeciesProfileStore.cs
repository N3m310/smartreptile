using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Species;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Species;

/// <summary>
/// The <see cref="ISpeciesProfileStore"/> adapter (FR-10, §07-appendices/02 §3.13). Ownership is the
/// <c>CreatedByUserId</c> column rather than a membership table, because the product is single-owner in v1
/// (limitation L-07): null marks a built-in everyone may see, and anything else belongs to its author.
/// </summary>
public sealed class EfSpeciesProfileStore(SmartReptileDbContext db) : ISpeciesProfileStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SpeciesProfile>> ListVisibleAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var profiles = await db.SpeciesProfiles
            .Where(profile => profile.IsBuiltIn || profile.CreatedByUserId == userId)
            .Include(profile => profile.Thresholds)
            .OrderBy(profile => profile.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return profiles;
    }

    /// <inheritdoc />
    public Task<SpeciesProfile?> FindAsync(Guid profileId, CancellationToken cancellationToken) =>
        db.SpeciesProfiles
            .Include(profile => profile.Thresholds)
            .FirstOrDefaultAsync(profile => profile.Id == profileId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> NameTakenAsync(
        Guid? ownerUserId,
        string name,
        Guid? excludingProfileId,
        CancellationToken cancellationToken) =>
        db.SpeciesProfiles.AnyAsync(
            profile => profile.CreatedByUserId == ownerUserId
                && profile.Name == name
                && (excludingProfileId == null || profile.Id != excludingProfileId),
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> IsAssignedAsync(Guid profileId, CancellationToken cancellationToken) =>
        // The soft-delete filter is ignored deliberately: a deleted terrarium's row still holds the foreign key, so
        // counting only live ones would let a delete through that the database then refuses (DI-10).
        db.Terrariums
            .IgnoreQueryFilters()
            .AnyAsync(terrarium => terrarium.SpeciesProfileId == profileId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public void Add(SpeciesProfile profile) => db.SpeciesProfiles.Add(profile);

    /// <inheritdoc />
    public void ReplaceBands(SpeciesProfile profile, IReadOnlyList<Threshold> bands)
    {
        foreach (var outgoing in profile.Thresholds.ToList())
        {
            db.Thresholds.Remove(outgoing);
        }

        profile.Thresholds.Clear();

        foreach (var band in bands)
        {
            // DbSet.Add, not profile.Thresholds.Add: the explicit call is what marks a band with a client-assigned
            // key as a row to insert. Discovered through the navigation instead, EF treats it as one to update.
            band.SpeciesProfileId = profile.Id;
            db.Thresholds.Add(band);
            profile.Thresholds.Add(band);
        }
    }

    /// <inheritdoc />
    public void Remove(SpeciesProfile profile) => db.SpeciesProfiles.Remove(profile);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // The rowversion moved between the read and this write: the caller's copy is stale, and the edit it
            // never saw must not be overwritten. Answered rather than thrown so the route can say "reload and try
            // again" instead of "server fault".
            return false;
        }
    }
}
