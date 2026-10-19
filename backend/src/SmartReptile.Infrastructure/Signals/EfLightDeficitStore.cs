using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Signals;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Signals;

/// <summary>
/// The <see cref="ILightDeficitStore"/> adapter: the fleet the rule can judge, the one alert that may be open per
/// terrarium, and the day's light readings.
/// </summary>
/// <remarks>
/// The candidate projection and the alert lookup are indexed queries, and the readings query is the same shape the
/// summary recomputer uses — including filtering the quality flags in memory, because "is this reading evaluable" is
/// a domain rule rather than something SQL should re-express. Rows that fail it are stored evidence, not light.
/// </remarks>
public sealed class EfLightDeficitStore(SmartReptileDbContext db) : ILightDeficitStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LightDeficitCandidate>> FindCandidatesAsync(
        CancellationToken cancellationToken) =>
        await db.Terrariums
            .AsNoTracking()
            .Where(terrarium => terrarium.Status == TerrariumStatus.Active
                             && terrarium.Device != null
                             && terrarium.SpeciesProfile != null)
            .Select(terrarium => new LightDeficitCandidate(
                terrarium.Id,
                terrarium.Device!.Id,
                terrarium.TimeZoneId,
                terrarium.SpeciesProfile!.LightThresholdLux,
                terrarium.SpeciesProfile.MinLightHoursPerDay))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Alert?> FindOpenAlertAsync(Guid terrariumId, CancellationToken cancellationToken) =>
        db.Alerts.FirstOrDefaultAsync(
            alert => alert.TerrariumId == terrariumId
                  && alert.Source == AlertSource.LightDeficit
                  && alert.State != AlertState.Resolved,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LightReadingPoint>> FindLightReadingsAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        var rows = await db.MetricReadings
            .AsNoTracking()
            .Where(reading => reading.Sample != null
                           && reading.Sample.TerrariumId == terrariumId
                           && reading.Metric == MetricCode.LightLux
                           && reading.Sample.RecordedAt >= fromUtc
                           && reading.Sample.RecordedAt < toUtc)
            .Select(reading => new
            {
                reading.Sample!.RecordedAt,
                reading.Value,
                reading.Sample.QualityFlags,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Where(row => QualityRules.IsEvaluable(row.QualityFlags))
            .OrderBy(row => row.RecordedAt)
            .Select(row => new LightReadingPoint(row.RecordedAt, row.Value))
            .ToList();
    }

    /// <inheritdoc />
    public void AddAlert(Alert alert) => db.Alerts.Add(alert);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
