using SmartReptile.Application.Abstractions;

namespace SmartReptile.Application.Auditing;

/// <summary>
/// The audit query (FR-18, NFR-12 — roadmap task 3.7): one page of the trail, newest first, filtered by entity,
/// entity id and a time window.
/// </summary>
/// <remarks>
/// The service is deliberately thin — validate the cursor, clamp the page, ask for one row more than the page — for
/// the same reason <c>AlertService.ListAsync</c> is: the interesting decisions are the cursor's order and the
/// scoping, and both belong where they can be tested. The scoping itself lives in the store, because it is a query
/// the database has to answer rather than a filter over loaded rows.
/// </remarks>
public sealed class AuditService(IAuditStore store)
{
    /// <summary>One page of the caller's audit trail.</summary>
    /// <param name="userId">Signed-in account the trail is scoped to.</param>
    /// <param name="query">Filters, with <see cref="AuditQuery.Entity"/> already canonicalised.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AuditOutcome> ListAsync(
        Guid userId,
        AuditQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Cursor is { Length: > 0 } cursor && !AuditCursor.TryDecode(cursor, out _))
        {
            return AuditOutcome.Failed(new AuditProblem("invalid_cursor", "cursor is not one this API issued."));
        }

        if (query.FromUtc is { } from && query.ToUtc is { } to && to < from)
        {
            return AuditOutcome.Failed(new AuditProblem("invalid_range", "to must not precede from."));
        }

        var pageSize = Math.Clamp(query.PageSize, 1, AuditPaging.MaxPageSize);

        // One row more than the page: the extra row is how "there is more" is known without a second count query.
        var rows = await store
            .ListAsync(userId, query, pageSize + 1, cancellationToken)
            .ConfigureAwait(false);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).Select(Describe).ToArray();

        var next = hasMore && items.Length > 0
            ? AuditCursor.Encode(items[^1].OccurredAt, items[^1].Id)
            : null;

        return new AuditOutcome { Page = new AuditPage(items, next) };
    }

    /// <summary>
    /// Projects one row onto the wire shape. <c>before</c> and <c>after</c> travel as the JSON text that was stored
    /// rather than as re-parsed objects: the trail is a faithful record of what was written, and re-shaping four
    /// different entities' snapshots into one schema is a job for whoever renders them.
    /// </summary>
    private static AuditEntryView Describe(AuditRecord record) => new(
        record.Entry.Id,
        record.Entry.Action,
        record.Entry.EntityName,
        record.Entry.EntityId,
        record.DevicePublicId,
        record.Entry.UserId,
        record.Entry.OccurredAt,
        record.Entry.IpAddress,
        record.Entry.UserAgent,
        record.Entry.CorrelationId,
        record.Entry.BeforeJson,
        record.Entry.AfterJson);
}
