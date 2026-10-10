using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Evaluation;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Evaluation;

/// <summary>
/// The <see cref="IEvaluationStore"/> adapter. Loads the configuration a batch is judged against — the profile's
/// bands, the terrarium's overrides, the state rows and the still-open alerts — in one pass per terrarium.
/// </summary>
/// <remarks>
/// The bands come from the same two places the read surface resolves a band from, both filtered on <c>Enabled</c>,
/// so the evaluator cannot start tracking a key that no band covers, nor miss one that a card is already colouring.
/// They arrive as the configured rows rather than as a resolved answer, because a batch spans instants and the phase
/// has to be resolved per reading.
/// <para>
/// The soft-delete filter on <c>Terrarium</c> is a model-level query filter, so a terrarium deleted between the
/// commit and the evaluation pass is simply absent and the sample is skipped.
/// </para>
/// </remarks>
public sealed class EfEvaluationStore(SmartReptileDbContext db) : IEvaluationStore
{
    /// <inheritdoc />
    public async Task<EvaluationContext?> FindContextAsync(Guid terrariumId, CancellationToken cancellationToken)
    {
        var terrarium = await db.Terrariums
            .Include(t => t.SpeciesProfile!)
                .ThenInclude(profile => profile.Thresholds)
            .Include(t => t.ThresholdOverrides)
            .FirstOrDefaultAsync(t => t.Id == terrariumId, cancellationToken)
            .ConfigureAwait(false);

        if (terrarium is null)
        {
            return null;
        }

        var profileBands = terrarium.SpeciesProfile is { } profile
            ? profile.Thresholds.Where(threshold => threshold.Enabled).ToList()
            : [];

        var overrides = terrarium.ThresholdOverrides.Where(@override => @override.Enabled).ToList();

        var rows = await db.EvaluationStates
            .Where(state => state.TerrariumId == terrariumId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Materialised first and keyed in memory: the key is a tuple, which is not something the SQL translation
        // can group by, and the row count per terrarium is tiny either way.
        var states = rows.ToDictionary(state => (state.Metric, state.Phase));

        // An acknowledged alert is still open — acknowledgement is ownership, not closure — so the filter names the
        // only state that ends an episode.
        var openAlerts = await db.Alerts
            .Where(alert => alert.TerrariumId == terrariumId && alert.State != AlertState.Resolved)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EvaluationContext(
            terrarium.Id,
            terrarium.TimeZoneId,
            terrarium.SpeciesProfile?.LightsOnLocalTime ?? new TimeOnly(7, 0),
            terrarium.SpeciesProfile?.PhotoperiodHours ?? 12m,
            profileBands,
            overrides,
            states,
            openAlerts);
    }

    /// <inheritdoc />
    public void AddState(EvaluationState state) => db.EvaluationStates.Add(state);

    /// <inheritdoc />
    public void AddAlert(Alert alert) => db.Alerts.Add(alert);

    /// <inheritdoc />
    public async Task<IReadOnlyList<decimal>> ReadEpisodeValuesAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        await db.MetricReadings
            .Where(reading => reading.Sample!.TerrariumId == terrariumId
                           && reading.Metric == metric
                           && reading.Sample.RecordedAt >= fromUtc
                           && reading.Sample.RecordedAt <= toUtc)
            .OrderBy(reading => reading.Sample!.RecordedAt)
            .Select(reading => reading.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
