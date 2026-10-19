using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// The <see cref="ITerrariumStore"/> adapter. Reads are projection-free (whole entities) because the use cases need
/// the device, the species profile with its bands and the overrides together, and the aggregate roots involved are
/// small enough that a projection would trade readability for nothing.
/// </summary>
/// <remarks>
/// Every method that takes an owner filters by it in SQL rather than in memory, so a foreign terrarium is never
/// even loaded (BR-02.2). The soft-delete filter on <c>Terrarium</c> is a query filter on the model, so it applies
/// to all of these without being repeated — except <see cref="FindAnyAsync"/>, which drops it on purpose because
/// telling a deleted row from a missing one is the question it answers.
/// </remarks>
public sealed class EfTerrariumStore(SmartReptileDbContext db) : ITerrariumStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Terrarium>> ListOwnedAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        await db.Terrariums
            .Where(terrarium => terrarium.UserId == ownerUserId)
            .Include(terrarium => terrarium.Device)
            .Include(terrarium => terrarium.SpeciesProfile)
            .OrderBy(terrarium => terrarium.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Terrarium>> ListAllAsync(CancellationToken cancellationToken) =>
        await db.Terrariums
            .Include(terrarium => terrarium.Device)
            .Include(terrarium => terrarium.SpeciesProfile)
            .OrderBy(terrarium => terrarium.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Terrarium?> FindOwnedAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        db.Terrariums
            .Where(terrarium => terrarium.Id == terrariumId && terrarium.UserId == ownerUserId)
            .Include(terrarium => terrarium.Device)
            .Include(terrarium => terrarium.SpeciesProfile!)
                .ThenInclude(profile => profile.Thresholds)
            .Include(terrarium => terrarium.ThresholdOverrides)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<Terrarium?> FindAnyAsync(Guid terrariumId, CancellationToken cancellationToken) =>
        // The soft-delete filter is dropped on purpose: telling "deleted" apart from "never existed" is the whole
        // reason this query exists, and the filter would collapse the two back into one answer.
        db.Terrariums
            .IgnoreQueryFilters()
            .Where(terrarium => terrarium.Id == terrariumId)
            .Include(terrarium => terrarium.Device)
            .Include(terrarium => terrarium.SpeciesProfile!)
                .ThenInclude(profile => profile.Thresholds)
            .Include(terrarium => terrarium.ThresholdOverrides)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> SpeciesProfileExistsAsync(Guid speciesProfileId, CancellationToken cancellationToken) =>
        db.SpeciesProfiles.AnyAsync(profile => profile.Id == speciesProfileId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    /// <inheritdoc />
    public void AddTerrarium(Terrarium terrarium) => db.Terrariums.Add(terrarium);

    /// <inheritdoc />
    public void AddThresholdOverride(ThresholdOverride row) => db.ThresholdOverrides.Add(row);

    /// <inheritdoc />
    public void RemoveThresholdOverride(ThresholdOverride row) => db.ThresholdOverrides.Remove(row);

    /// <inheritdoc />
    public void AddThresholdSnapshot(ThresholdSnapshot snapshot) => db.ThresholdSnapshots.Add(snapshot);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, TerrariumActivity>> SummariseActivityAsync(
        IReadOnlyCollection<Guid> terrariumIds,
        CancellationToken cancellationToken)
    {
        var activity = terrariumIds.ToDictionary(id => id, _ => TerrariumActivity.None);

        if (activity.Count == 0)
        {
            return activity;
        }

        var ids = activity.Keys.ToList();

        // Two grouped queries rather than two per terrarium: a dashboard with twenty enclosures must not turn its
        // list call into forty round trips.
        var latestSamples = await db.TelemetrySamples
            .Where(sample => ids.Contains(sample.TerrariumId))
            .GroupBy(sample => sample.TerrariumId)
            .Select(group => new { TerrariumId = group.Key, At = group.Max(sample => sample.RecordedAt) })
            .ToListAsync(cancellationToken);

        var openAlerts = await db.Alerts
            .Where(alert => ids.Contains(alert.TerrariumId) && alert.State != AlertState.Resolved)
            .GroupBy(alert => alert.TerrariumId)
            .Select(group => new { TerrariumId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        foreach (var row in latestSamples)
        {
            activity[row.TerrariumId] = activity[row.TerrariumId] with { LatestSampleAt = row.At };
        }

        foreach (var row in openAlerts)
        {
            activity[row.TerrariumId] = activity[row.TerrariumId] with { OpenAlertCount = row.Count };
        }

        return activity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetrySample>> RecentSamplesAsync(
        Guid terrariumId,
        int take,
        CancellationToken cancellationToken) =>
        await db.TelemetrySamples
            .Where(sample => sample.TerrariumId == terrariumId)
            .Include(sample => sample.Readings)
            .OrderByDescending(sample => sample.RecordedAt)
            .ThenByDescending(sample => sample.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetrySample>> SamplesInRangeAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        await db.TelemetrySamples
            .Where(sample => sample.TerrariumId == terrariumId
                             && sample.RecordedAt >= fromUtc
                             && sample.RecordedAt <= toUtc)
            // Filtered include: the series only ever needs one metric, and loading the other three would multiply
            // the rows for nothing.
            .Include(sample => sample.Readings.Where(reading => reading.Metric == metric))
            .OrderBy(sample => sample.RecordedAt)
            .ThenBy(sample => sample.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountSamplesAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        db.TelemetrySamples.CountAsync(
            sample => sample.TerrariumId == terrariumId
                      && sample.RecordedAt >= fromUtc
                      && sample.RecordedAt <= toUtc,
            cancellationToken);

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
            // The row's rowversion moved between the read and this write, which is exactly what the token is for:
            // the caller is told its copy is stale instead of clobbering an edit it never saw (FR-03).
            return false;
        }
    }
}
