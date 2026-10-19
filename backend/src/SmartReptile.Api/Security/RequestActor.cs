using SmartReptile.Api.Middleware;
using SmartReptile.Domain.Auditing;

namespace SmartReptile.Api.Security;

/// <summary>
/// Builds the audit trail's view of who is calling (§02-design/02 §3.18).
/// </summary>
/// <remarks>
/// Only the transport knows an address, a user agent and a correlation id, so this is the one place an
/// <see cref="AuditActor"/> is assembled from a request. Shared rather than repeated per endpoint file, so two
/// features cannot disagree about what "the caller" means.
/// <para>
/// Truncation of the two client-supplied values happens in <see cref="AuditLog"/>'s builders, not here: the limits
/// are column widths, and the layer that knows the column is the layer that enforces it.
/// </para>
/// </remarks>
public static class RequestActor
{
    /// <summary>Provenance of one request.</summary>
    /// <param name="context">Request being served.</param>
    public static AuditActor From(HttpContext context) => new(
        ClientAddress(context),
        context.Request.Headers.UserAgent.ToString() is { Length: > 0 } userAgent ? userAgent : null,
        context.Items[CorrelationIdMiddleware.ItemKey] as string ?? context.TraceIdentifier);

    /// <summary>Caller address, or <c>unknown</c> when the transport cannot name one.</summary>
    /// <param name="context">Request being served.</param>
    public static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
