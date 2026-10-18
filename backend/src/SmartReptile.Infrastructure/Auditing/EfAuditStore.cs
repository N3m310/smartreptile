using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Auditing;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Auditing;

/// <summary>
/// The <see cref="IAuditStore"/> adapter (FR-18, roadmap task 3.7).
/// </summary>
/// <remarks>
/// Two joins and no projections of the entity body: the trail is small, ordered by one column, and every filter the
/// route offers is a predicate the provider can translate. Ownership is a predicate in SQL rather than a post-filter,
/// so a row the caller may not see is never loaded (BR-02.2).
/// </remarks>
public sealed class EfAuditStore(SmartReptileDbContext db) : IAuditStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> ListAsync(
        Guid userId,
        AuditQuery query,
        int take,
        CancellationToken cancellationToken)
    {
        // The devices the caller owns. The terrarium join deliberately ignores the soft-delete filter: an audit trail
        // is the one place a removed enclosure must not erase its own history, and a device unbound from everything
        // is nobody's but its own account's (the UserId half below still shows what the caller did to it).
        var ownedDeviceIds =
            from device in db.Devices
            join terrarium in db.Terrariums.IgnoreQueryFilters() on device.TerrariumId equals terrarium.Id
            where terrarium.UserId == userId
            select device.Id;

        // Two ways a row is the caller's: their account caused it, or it concerns a device in one of their
        // terrariums — a Technician acknowledging an alert on the caller's enclosure is the caller's business too,
        // which is what "own terrariums only" means for a trail whose rows carry an actor rather than an owner.
        var rows =
            from entry in db.AuditLogs
            join device in db.Devices on entry.DeviceId equals (Guid?)device.Id into devices
            from device in devices.DefaultIfEmpty()
            where entry.UserId == userId
                  || (entry.DeviceId != null && ownedDeviceIds.Contains(entry.DeviceId.Value))
            select new { Entry = entry, DevicePublicId = device == null ? null : device.PublicId };

        if (query.Entity is { Length: > 0 } entity)
        {
            rows = rows.Where(row => row.Entry.EntityName == entity);
        }

        if (query.EntityId is { Length: > 0 } entityId)
        {
            rows = rows.Where(row => row.Entry.EntityId == entityId);
        }

        if (query.FromUtc is { } from)
        {
            rows = rows.Where(row => row.Entry.OccurredAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(row => row.Entry.OccurredAt <= to);
        }

        if (AuditCursor.TryDecode(query.Cursor, out var position))
        {
            // Strictly after the cursor in the list's own order: instant first, identity to break the tie, which is
            // what makes paging over a trail that keeps receiving rows neither repeat nor skip one.
            rows = rows.Where(row =>
                row.Entry.OccurredAt < position.OccurredAt
                || (row.Entry.OccurredAt == position.OccurredAt && row.Entry.Id < position.Id));
        }

        return await rows
            .OrderByDescending(row => row.Entry.OccurredAt)
            .ThenByDescending(row => row.Entry.Id)
            .Take(take)
            .Select(row => new AuditRecord(row.Entry, row.DevicePublicId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
