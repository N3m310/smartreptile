using SmartReptile.Domain.Auditing;

namespace SmartReptile.Application.Auditing;

/// <summary>Paging defaults of <c>GET /audit</c>, the same convention <c>GET /alerts</c> uses.</summary>
public static class AuditPaging
{
    /// <summary>Page size when the caller does not ask for one.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>Largest page a caller may ask for.</summary>
    public const int MaxPageSize = 200;
}

/// <summary>
/// The keyset cursor of <c>GET /audit</c>: the position of the last row the caller already has, so the next page
/// starts after it rather than using offset paging that repeats or skips rows as the trail keeps growing.
/// </summary>
/// <remarks>
/// Opaque on purpose — base64 of <c>occurredAt|id</c>, and nothing about its shape is a promise. The pair is what
/// makes the order total: a claim writes one row, but a resolution can land in the same millisecond as a sibling
/// action, and an identity alone would not say which came first.
/// </remarks>
public static class AuditCursor
{
    private const char Separator = '|';

    /// <summary>Encodes the position of one row.</summary>
    /// <param name="occurredAt">When the recorded action happened.</param>
    /// <param name="id">Identity of the audit row.</param>
    public static string Encode(DateTimeOffset occurredAt, long id) =>
        Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{occurredAt.UtcTicks}{Separator}{id}"));

    /// <summary>Reads a cursor back, or false when it is not one this build wrote.</summary>
    /// <param name="cursor">Value as it arrived in the query string.</param>
    /// <param name="position">Decoded position.</param>
    public static bool TryDecode(string? cursor, out (DateTimeOffset OccurredAt, long Id) position)
    {
        position = default;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(Separator);

            if (parts.Length == 2 && long.TryParse(parts[0], out var ticks) && long.TryParse(parts[1], out var id))
            {
                position = (new DateTimeOffset(ticks, TimeSpan.Zero), id);

                return true;
            }
        }
        catch (FormatException)
        {
            // A cursor a client made up is a bad request, not an incident: report it as "not a cursor" and let the
            // caller decide, rather than turning it into a 500.
        }

        return false;
    }
}

/// <summary>Filters of <c>GET /audit</c>, already parsed out of the query string.</summary>
/// <param name="Entity">
/// Only rows about this entity — a canonical name from <see cref="AuditEntity"/>, resolved by the endpoint so an
/// unknown one is a refusal rather than an empty page.
/// </param>
/// <param name="EntityId">Only rows about this entity identity (a device's public id, an alert id, a silence id).</param>
/// <param name="FromUtc">Only rows recorded at or after this instant.</param>
/// <param name="ToUtc">Only rows recorded at or before this instant.</param>
/// <param name="Cursor">Position to continue from, or null for the first page.</param>
/// <param name="PageSize">Rows requested, clamped to <see cref="AuditPaging.MaxPageSize"/>.</param>
public sealed record AuditQuery(
    string? Entity = null,
    string? EntityId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Cursor = null,
    int PageSize = AuditPaging.DefaultPageSize);

/// <summary>One entry of the audit trail as <c>GET /audit</c> responds (FR-18, <c>07-appendices/03</c> §4.5).</summary>
/// <param name="Id">Identity of the row; also the tie-breaker of the listing order.</param>
/// <param name="Action">Verb from the closed vocabulary, e.g. <c>alert.resolved</c>.</param>
/// <param name="Entity">Kind of entity acted on: <c>Device</c>, <c>Alert</c> or <c>MetricSilence</c>.</param>
/// <param name="EntityId">Affected entity named the way the API names it, so a row matches a request without a join.</param>
/// <param name="DeviceId">
/// Public device id the row concerned, when it concerned one — the surrogate key stays in the database.
/// </param>
/// <param name="ActorUserId">Who did it; null when no user did (a sweep, an unattributed write).</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="IpAddress">Caller address, or <c>unknown</c> when it could not be determined.</param>
/// <param name="UserAgent">Caller user agent, truncated to the column width.</param>
/// <param name="CorrelationId">Correlation id of the request that caused it (BR-18.3).</param>
/// <param name="Before">What the entity looked like before, as the JSON that was stored.</param>
/// <param name="After">What it looked like after, as the JSON that was stored.</param>
public sealed record AuditEntryView(
    long Id,
    string Action,
    string Entity,
    string EntityId,
    string? DeviceId,
    Guid? ActorUserId,
    DateTimeOffset OccurredAt,
    string IpAddress,
    string? UserAgent,
    string? CorrelationId,
    string? Before,
    string? After);

/// <summary>One page of audit rows, newest first.</summary>
/// <param name="Items">The rows.</param>
/// <param name="NextCursor">Cursor for the next page, or null when this page was the last.</param>
public sealed record AuditPage(IReadOnlyList<AuditEntryView> Items, string? NextCursor);

/// <summary>An audit row with the one field of its device a client needs: the public id.</summary>
/// <param name="Entry">The stored row.</param>
/// <param name="DevicePublicId">Public device id the row's <c>DeviceId</c> points at, null when it names no device.</param>
public sealed record AuditRecord(AuditLog Entry, string? DevicePublicId);

/// <summary>An expected failure of the audit query.</summary>
/// <param name="Code">Stable problem code, e.g. <c>invalid_cursor</c>.</param>
/// <param name="Message">Message safe to show the caller.</param>
public sealed record AuditProblem(string Code, string Message);

/// <summary>Outcome of the audit query. A null <see cref="Problem"/> means the page was produced.</summary>
public sealed record AuditOutcome
{
    /// <summary>Set when the query succeeded.</summary>
    public AuditPage? Page { get; init; }

    /// <summary>Set when the query failed.</summary>
    public AuditProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A failure the caller caused or should see.</summary>
    /// <param name="problem">What went wrong.</param>
    public static AuditOutcome Failed(AuditProblem problem) => new() { Problem = problem };
}
