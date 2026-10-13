using SmartReptile.Application.Auditing;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Persistence port for the audit query (FR-18, roadmap task 3.7).
/// </summary>
/// <remarks>
/// A port of its own rather than a method on <see cref="IAlertStore"/>: the alert store reads the audit rows of
/// <i>one</i> alert to build its timeline, while this one answers a query across the trail, and the two have
/// different scoping rules — the timeline trusts the caller's ownership of the alert, this one has to establish
/// ownership of whatever each row concerns.
/// </remarks>
public interface IAuditStore
{
    /// <summary>
    /// One page of the trail, newest first, scoped to the caller: the rows their account caused, plus the rows about
    /// a device in one of their terrariums however they were caused — a technician acknowledging an alert on the
    /// caller's enclosure is the caller's business.
    /// </summary>
    /// <param name="userId">Signed-in account the trail is scoped to.</param>
    /// <param name="query">Filters, already canonicalised by the caller.</param>
    /// <param name="take">Rows to return; the caller asks for one more than the page so it can detect a next page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<AuditRecord>> ListAsync(
        Guid userId,
        AuditQuery query,
        int take,
        CancellationToken cancellationToken);
}
