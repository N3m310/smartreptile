using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Auditing;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Tests.Unit.Application.Auditing;

/// <summary>
/// In-memory <see cref="IAuditStore"/>. It mirrors the adapter's two interesting behaviours — the total order
/// (instant, then identity) and the cursor that pages over it — because that is what these tests are about. The
/// ownership scoping is the adapter's and is asserted against SQL Server instead.
/// </summary>
internal sealed class FakeAuditStore : IAuditStore
{
    private readonly List<AuditRecord> _entries = [];

    /// <summary>How many rows the service asked for, so the clamp and the lookahead row can be asserted.</summary>
    public int LastTake { get; private set; }

    /// <summary>The filters as the service passed them, so canonicalisation can be asserted.</summary>
    public AuditQuery? LastQuery { get; private set; }

    /// <summary>One stored row with only the fields a test cares about made explicit.</summary>
    public FakeAuditStore Add(
        long id,
        string action,
        DateTimeOffset occurredAt,
        string entity = AuditEntity.Device,
        string? entityId = null,
        string? devicePublicId = null,
        Guid? actorUserId = null,
        string? before = null,
        string? after = null)
    {
        _entries.Add(new AuditRecord(
            new AuditLog
            {
                Id = id,
                Action = action,
                EntityName = entity,
                EntityId = entityId ?? id.ToString(),
                UserId = actorUserId,
                OccurredAt = occurredAt,
                IpAddress = "127.0.0.1",
                BeforeJson = before,
                AfterJson = after,
            },
            devicePublicId));

        return this;
    }

    public Task<IReadOnlyList<AuditRecord>> ListAsync(
        Guid userId,
        AuditQuery query,
        int take,
        CancellationToken cancellationToken)
    {
        LastTake = take;
        LastQuery = query;

        var rows = _entries.AsEnumerable();

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
            rows = rows.Where(row =>
                row.Entry.OccurredAt < position.OccurredAt
                || (row.Entry.OccurredAt == position.OccurredAt && row.Entry.Id < position.Id));
        }

        return Task.FromResult<IReadOnlyList<AuditRecord>>(
            [.. rows
                .OrderByDescending(row => row.Entry.OccurredAt)
                .ThenByDescending(row => row.Entry.Id)
                .Take(take)]);
    }
}
