using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Reference;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// The <see cref="ITimeZoneStore"/> adapter. Two small queries against a table the seeder owns
/// (TASKS 2026-10-14, item 11).
/// </summary>
/// <remarks>
/// The reference tables are the "simple CRUD goes straight through the context" case of
/// §03-implementation/03 §9, so there is no service between this and the use case that asks.
/// </remarks>
public sealed class EfTimeZoneStore(SmartReptileDbContext db) : ITimeZoneStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TimeZoneReference>> ListAsync(CancellationToken cancellationToken) =>
        await db.TimeZones
            .OrderBy(zone => zone.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<TimeZoneReference?> FindAsync(string ianaId, CancellationToken cancellationToken) =>
        // No extra index: the column is the primary key, and SQL Server's default collation is already
        // case-insensitive, which is the comparison the port asks for.
        db.TimeZones.FirstOrDefaultAsync(zone => zone.IanaId == ianaId, cancellationToken);
}
