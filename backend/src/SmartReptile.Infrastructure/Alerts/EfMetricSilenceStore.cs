using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Alerts;

/// <summary>
/// The <see cref="IMetricSilenceStore"/> adapter (§07-appendices/02 §3.15, FR-13).
/// </summary>
/// <remarks>
/// Ownership is a query over the terrarium rather than a membership table, because the product is single-owner in
/// v1 (limitation L-07): the person who can act on a terrarium is the person it belongs to.
/// </remarks>
public sealed class EfMetricSilenceStore(SmartReptileDbContext db) : IMetricSilenceStore
{
    /// <inheritdoc />
    public Task<bool> IsMemberAsync(Guid terrariumId, Guid userId, CancellationToken cancellationToken) =>
        db.Terrariums.AnyAsync(
            terrarium => terrarium.Id == terrariumId && terrarium.UserId == userId,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricSilence>> ListActiveAsync(
        Guid terrariumId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        await db.MetricSilences
            .Where(silence => silence.TerrariumId == terrariumId
                           && silence.CancelledAt == null
                           && silence.UntilUtc > atUtc)
            .OrderByDescending(silence => silence.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<MetricSilence?> FindAsync(Guid terrariumId, Guid silenceId, CancellationToken cancellationToken) =>
        db.MetricSilences.FirstOrDefaultAsync(
            silence => silence.TerrariumId == terrariumId && silence.Id == silenceId,
            cancellationToken);

    /// <inheritdoc />
    public void Add(MetricSilence silence) => db.MetricSilences.Add(silence);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
