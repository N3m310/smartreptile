using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Terrariums;

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
/// Every lookup is scoped by owner. Ownership is a parameter, not a post-filter, so a query for a foreign
/// terrarium returns nothing at all — there is no code path where a row is loaded and then discarded, which is
/// what keeps a future refactor from leaking one (BR-02.2).
/// </para>
/// </remarks>
public interface ITerrariumStore
{
    /// <summary>The caller's terrariums, ordered by name, with the bound device and species profile loaded.</summary>
    Task<IReadOnlyList<Terrarium>> ListOwnedAsync(Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>
    /// One terrarium with everything the read path needs: the device, the species profile with its bands, and the
    /// per-terrarium overrides. Null when it does not exist, is soft-deleted, or belongs to someone else.
    /// </summary>
    Task<Terrarium?> FindOwnedAsync(Guid terrariumId, Guid ownerUserId, CancellationToken cancellationToken);

    /// <summary>True when the species profile exists — the create path refuses an unknown band source.</summary>
    Task<bool> SpeciesProfileExistsAsync(Guid speciesProfileId, CancellationToken cancellationToken);

    /// <summary>Stages a new terrarium.</summary>
    void AddTerrarium(Terrarium terrarium);

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
}
