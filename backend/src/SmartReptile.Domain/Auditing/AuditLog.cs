using System.Globalization;

namespace SmartReptile.Domain.Auditing;

/// <summary>
/// The verbs an audit row may carry (§02-design/02 §3.18). Closed on purpose: the column is filtered and grouped
/// on, so a free-text action would turn a queryable trail into prose. Only the verbs currently written are
/// declared here — the rest of the vocabulary arrives with the features that perform those actions.
/// </summary>
public static class AuditAction
{
    /// <summary>A board was bound to a terrarium and issued its first secret (BR-18.4).</summary>
    public const string DeviceClaimed = "device.claimed";

    /// <summary>A replacement secret was issued and the previous one put on its rotation grace window (BR-18.4).</summary>
    public const string DeviceSecretRotated = "device.secret_rotated";

    /// <summary>Every credential a device holds was invalidated (BR-18.4).</summary>
    public const string DeviceRevoked = "device.revoked";

    /// <summary>A keeper took ownership of an alert without closing it (FR-12).</summary>
    public const string AlertAcknowledged = "alert.acknowledged";

    /// <summary>A keeper closed an alert, with the reason they gave (FR-12).</summary>
    public const string AlertResolved = "alert.resolved";

    /// <summary>A metric — or every metric of a terrarium — was silenced for a time-boxed window (FR-13).</summary>
    public const string MetricSilenced = "silence.created";

    /// <summary>A silence window was cancelled before it expired (FR-13).</summary>
    public const string SilenceCancelled = "silence.cancelled";
}

/// <summary>
/// Who made a change and from where. The acting user is deliberately not carried here — it is already a parameter
/// of every audited operation — so this holds the request provenance that only the transport can supply.
/// </summary>
/// <param name="IpAddress">Caller address.</param>
/// <param name="UserAgent">Caller user agent.</param>
/// <param name="CorrelationId">Correlation id of the request (BR-18.3).</param>
public sealed record AuditActor(string? IpAddress, string? UserAgent, string? CorrelationId)
{
    /// <summary>Stand-in for a caller with no request behind it, such as a background job.</summary>
    public static AuditActor Unknown { get; } = new("unknown", null, null);
}

/// <summary>
/// One entry in the audit trail (FR-18, BR-18.4): what changed, who changed it and from where.
/// </summary>
/// <remarks>
/// Rows are staged on the same unit of work as the change they describe, so a change cannot commit without its
/// entry. An audit trail with silent gaps is worse than none, because a missing row reads as "nothing happened".
/// </remarks>
public class AuditLog
{
    /// <summary>Column width of <see cref="IpAddress"/>, the longest an IPv6 address can be (§07-appendices/02 §3.11).</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>Column width of <see cref="UserAgent"/>; a user agent is client-supplied and arbitrarily long.</summary>
    public const int UserAgentMaxLength = 200;

    /// <summary>Column width of <see cref="CorrelationId"/>; the id is client-supplied via <c>X-Correlation-ID</c>.</summary>
    public const int CorrelationIdMaxLength = 64;

    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>User who made the change; null when no user did.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Device the change concerned; null when no device was involved.</summary>
    public Guid? DeviceId { get; set; }

    /// <summary>Kind of entity acted on, e.g. <c>Device</c>.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Affected entity named the way the API names it (for a device, its public id), so a row can be read and
    /// matched to a request without joining to the entity table.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Verb from <see cref="AuditAction"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>What the entity looked like before the change, when that is worth keeping.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>What the entity looked like after the change.</summary>
    public string? AfterJson { get; set; }

    /// <summary>Caller address; <c>unknown</c> when it could not be determined.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Caller user agent, truncated to the column width.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Correlation id of the request that caused the change (BR-18.3), truncated to the column width.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>When the change happened.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Builds a row for an action taken on a device.</summary>
    /// <param name="action">Verb from <see cref="AuditAction"/>.</param>
    /// <param name="deviceId">Surrogate key of the device.</param>
    /// <param name="devicePublicId">Device identifier as the API exposes it.</param>
    /// <param name="userId">Acting user; null when no user acted.</param>
    /// <param name="actor">Request provenance; <see cref="AuditActor.Unknown"/> when there is none.</param>
    /// <param name="occurredAt">When the change happened.</param>
    /// <param name="beforeJson">State before the change, when worth keeping.</param>
    /// <param name="afterJson">State after the change.</param>
    /// <returns>The staged row.</returns>
    public static AuditLog ForDevice(
        string action,
        Guid deviceId,
        string devicePublicId,
        Guid? userId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        string? beforeJson = null,
        string? afterJson = null) =>
        Build(
            action,
            "Device",
            devicePublicId,
            deviceId,
            userId,
            actor,
            occurredAt,
            beforeJson,
            afterJson);

    /// <summary>
    /// Builds a row for an action a keeper took on an alert. The entity id is the alert's own identity, spelled the
    /// way the routes spell it (<c>/alerts/{id}</c>), so a row can be matched to a request without a join.
    /// </summary>
    /// <param name="action">Verb from <see cref="AuditAction"/>.</param>
    /// <param name="alertId">Identity of the alert.</param>
    /// <param name="deviceId">Device the alert concerned; the column exists and the alert names one.</param>
    /// <param name="userId">Acting user.</param>
    /// <param name="actor">Request provenance; <see cref="AuditActor.Unknown"/> when there is none.</param>
    /// <param name="occurredAt">When the change happened.</param>
    /// <param name="beforeJson">State before the change, when worth keeping.</param>
    /// <param name="afterJson">State after the change.</param>
    /// <returns>The staged row.</returns>
    public static AuditLog ForAlert(
        string action,
        long alertId,
        Guid? deviceId,
        Guid? userId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        string? beforeJson = null,
        string? afterJson = null) =>
        Build(
            action,
            "Alert",
            alertId.ToString(CultureInfo.InvariantCulture),
            deviceId,
            userId,
            actor,
            occurredAt,
            beforeJson,
            afterJson);

    /// <summary>Builds a row for an action a keeper took on a silence window.</summary>
    /// <param name="action">Verb from <see cref="AuditAction"/>.</param>
    /// <param name="silenceId">Identity of the silence.</param>
    /// <param name="userId">Acting user.</param>
    /// <param name="actor">Request provenance; <see cref="AuditActor.Unknown"/> when there is none.</param>
    /// <param name="occurredAt">When the change happened.</param>
    /// <param name="beforeJson">State before the change, when worth keeping.</param>
    /// <param name="afterJson">State after the change.</param>
    /// <returns>The staged row.</returns>
    public static AuditLog ForSilence(
        string action,
        Guid silenceId,
        Guid? userId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        string? beforeJson = null,
        string? afterJson = null) =>
        Build(action, "MetricSilence", silenceId.ToString(), null, userId, actor, occurredAt, beforeJson, afterJson);

    /// <summary>
    /// The one place the row is built, so every entity's entry truncates, defaults and dates the same way.
    /// Truncating rather than rejecting: these arrive as client-supplied headers of unbounded length, and a refused
    /// insert would turn a valid action into a 500 (§07-appendices/02 §3.11).
    /// </summary>
    private static AuditLog Build(
        string action,
        string entityName,
        string entityId,
        Guid? deviceId,
        Guid? userId,
        AuditActor actor,
        DateTimeOffset occurredAt,
        string? beforeJson,
        string? afterJson) =>
        new()
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            DeviceId = deviceId,
            UserId = userId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            IpAddress = actor.IpAddress ?? "unknown",
            UserAgent = Truncate(actor.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(actor.CorrelationId, CorrelationIdMaxLength),
            OccurredAt = occurredAt,
        };

    /// <summary>
    /// Cuts a caller-supplied value to its column width. Truncation rather than rejection: an over-long header
    /// must not turn an otherwise valid claim into an error, and the leading part still identifies the client.
    /// </summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
