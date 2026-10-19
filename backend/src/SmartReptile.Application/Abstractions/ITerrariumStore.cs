using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// What the list view needs to know about a terrarium's activity, resolved in one query per collection rather than
/// one query per row. Absent from the dictionary means "nothing to report yet".
/// </summary>
/// <param name="LatestSampleAt">Newest sample in the terrarium, null before the first report.</param>
/// <param name="OpenAlertCount">Alerts not yet resolved.</param>
public sealed record TerrariumActivity(DateTimeOffset? LatestSampleAt, int OpenAlertCount)
{
    /// <summary>A terrarium with no samples and no open alerts.</summary>
    public static readonly TerrariumActivity None = new(null, 0);
}

/// <summary>
/// Persistence port for the terrarium read surface (FR-03, FR-08, FR-09 — roadmap task 2.8).
/// </summary>
/// <remarks>
/// One port rather than one per entity, for the same reason as <see cref="IProvisioningStore"/>: these queries exist
/// to serve one set of use cases, and splitting them would spread the read path across four interfaces
/// (§03-implementation/02 §2.1).
/// <para>
/// Every <b>serving</b> lookup is scoped by owner. Ownership is a parameter, not a post-filter, so a query for a
/// foreign terrarium returns nothing at all — there is no code path where a row is loaded and then discarded, which
/// is what keeps a future refactor from leaking one (BR-02.2).
/// </para>
/// <para>
/// <see cref="FindAnyAsync"/> is the one deliberate exception, and it exists to be answered with a refusal rather
/// than with data: it is how the service tells "missing" from "not yours" from "deleted" (TASKS 2026-10-14, item 3).
/// Its result is either served to the administrator or turned into a problem — never handed to an ordinary caller.
/// </para>
/// </remarks>
public interface ITerrariumStore
{
    /// <summary>The caller's terrariums, ordered by name, with the bound device and species profile loaded.</summary>
    Task<IReadOnlyList<Terrarium>> ListOwnedAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Every active terrarium, whatever account owns it, ordered by name — the administrator's list
    /// (TASKS 2026-10-14, item 5). Separate from <see cref="ListOwnedAsync"/> rather than a flag on it, so a call
    /// site states in its own name whether it is asking for one account's rows or for all of them.
    /// </summary>
    Task<IReadOnlyList<Terrarium>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One terrarium with everything the read path needs: the device, the species profile with its bands, and the
    /// per-terrarium overrides. Null when it does not exist, is soft-deleted, or belongs to someone else.
    /// </summary>
    Task<Terrarium?> FindOwnedAsync(Guid terrariumId, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// One terrarium by id with the same navigations, <b>ignoring both</b> the owner scope and the soft-delete
    /// filter. This is the query that answers "is it missing, or is it just not yours?" — a live row belonging to
    /// another account is a <c>403</c>, and a deleted one is <c>410</c>, where the owner-scoped lookup answers
    /// <c>404</c> for all three (TASKS 2026-10-14, items 3 and 5).
    /// </summary>
    /// <remarks>
    /// Deliberately not used to serve data on its own: the caller has already been refused by the time this is
    /// consulted, or is an administrator — and even then the row it returns is the one the answer describes.
    /// </remarks>
    Task<Terrarium?> FindAnyAsync(Guid terrariumId, CancellationToken cancellationToken);

    /// <summary>True when the species profile exists — the create path refuses an unknown band source.</summary>
    Task<bool> SpeciesProfileExistsAsync(Guid speciesProfileId, CancellationToken cancellationToken);

    /// <summary>
    /// True when an account with this id exists. The create path checks it because it is the one write that keys a
    /// new row on the caller's own subject: a token this server signed can still name an account it does not have,
    /// and the insert would otherwise report that as a foreign-key failure.
    /// </summary>
    Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Stages a new terrarium.</summary>
    void AddTerrarium(Terrarium terrarium);

    /// <summary>
    /// Stages a new per-terrarium override. The row also has to join the loaded aggregate, because the snapshot the
    /// change path writes in the same unit of work resolves the set the change produced — the adapter attaches it to
    /// the terrarium through the relationship, so the caller does not touch the collection itself.
    /// </summary>
    void AddThresholdOverride(ThresholdOverride row);

    /// <summary>Stages the removal of an override and drops it from the loaded aggregate's collection.</summary>
    void RemoveThresholdOverride(ThresholdOverride row);

    /// <summary>Stages a threshold snapshot (roadmap 3.1's write half).</summary>
    void AddThresholdSnapshot(ThresholdSnapshot snapshot);

    /// <summary>
    /// Stages an audit row. It lives on this port rather than one of its own so the row is committed by the same
    /// <see cref="SaveChangesAsync"/> as the change it describes (BR-18.4): an audited change that commits without
    /// its entry is worse than no trail at all, because the gap reads as "nothing happened".
    /// </summary>
    void AddAuditEntry(Domain.Auditing.AuditLog entry);

    /// <summary>
    /// Newest sample timestamp and open-alert count for each id, in one query per collection. Ids with nothing to
    /// report are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TerrariumActivity>> SummariseActivityAsync(
        IReadOnlyCollection<Guid> terrariumIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// The newest samples of a terrarium with their readings, newest first. A bounded lookback rather than one
    /// query per metric: a sample carries several metrics, and a metric that was absent from the newest sample
    /// (light at night, say) is found in an earlier one inside the window.
    /// </summary>
    Task<IReadOnlyList<TelemetrySample>> RecentSamplesAsync(
        Guid terrariumId,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Samples of one terrarium inside a window, oldest first, carrying only the requested metric's reading.
    /// </summary>
    Task<IReadOnlyList<TelemetrySample>> SamplesInRangeAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Number of samples stored in a window — the "received" half of the coverage ratio.</summary>
    Task<int> CountSamplesAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Commits staged changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits staged changes for an update, reporting a lost optimistic-concurrency race instead of throwing
    /// (FR-03). False means another writer changed the row after it was read, so the caller's <c>If-Match</c> is
    /// stale and the update must be refused rather than allowed to overwrite the other change.
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
