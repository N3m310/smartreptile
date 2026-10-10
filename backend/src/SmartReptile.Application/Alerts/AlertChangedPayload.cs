using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Alerts;

/// <summary>
/// The lifecycle moves the <c>alertChanged</c> event reports (`07-appendices/03` §6), spelled the way the event
/// spells them.
/// </summary>
public static class AlertEvent
{
    /// <summary>A new episode started.</summary>
    public const string Opened = "opened";

    /// <summary>An open episode's severity was raised to critical.</summary>
    public const string Escalated = "escalated";

    /// <summary>A human took ownership of an open episode.</summary>
    public const string Acknowledged = "acknowledged";

    /// <summary>An episode closed, by recovery or by hand.</summary>
    public const string Resolved = "resolved";
}

/// <summary>
/// The <c>alertChanged</c> event of `07-appendices/03` §6: what a subscribed client receives when an alert opens,
/// escalates, is acknowledged or is resolved, so a badge or a banner can update without polling.
/// </summary>
/// <remarks>
/// The payload carries the fields the documented event names and nothing else. Vocabulary follows the REST surface
/// rather than inventing a second one: <c>state</c> is <c>Open</c>/<c>Acknowledged</c>/<c>Resolved</c>, <c>severity</c>
/// is <c>Info</c>/<c>Warning</c>/<c>Critical</c> and <c>metric</c> is the metric's REST key (<c>tempC</c>), which is
/// what the same client reads from <c>GET /alerts</c>. Only the <c>event</c> name is lower case, because it names a
/// move rather than a stored value.
/// </remarks>
/// <param name="TerrariumId">Terrarium group the event is pushed to.</param>
/// <param name="AlertId">Alert that moved, for the detail or timeline request that follows.</param>
/// <param name="Event">Move from <see cref="AlertEvent"/>.</param>
/// <param name="State">Lifecycle state after the move.</param>
/// <param name="Severity">Severity after the move.</param>
/// <param name="Metric">Metric the alert is about as the REST key, or null for a device-level alert.</param>
/// <param name="TriggeredAt">Start of the episode, so a client can date what it just received.</param>
/// <param name="ResolvedAt">When it closed, on a <c>resolved</c> event.</param>
public sealed record AlertChangedPayload(
    Guid TerrariumId,
    long AlertId,
    string Event,
    string State,
    string Severity,
    string? Metric,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? ResolvedAt)
{
    /// <summary>Projects one committed move onto the documented shape.</summary>
    /// <param name="change">The move as the producer recorded it.</param>
    public static AlertChangedPayload From(AlertChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return From(change.Alert, change.Event);
    }

    /// <summary>Projects one committed alert onto the documented shape.</summary>
    /// <param name="alert">Row as it now stands.</param>
    /// <param name="event">Move from <see cref="AlertEvent"/>.</param>
    public static AlertChangedPayload From(Alert alert, string @event)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return new AlertChangedPayload(
            alert.TerrariumId,
            alert.Id,
            @event,
            alert.State.ToString(),
            alert.Severity.ToString(),
            alert.Metric is { } metric ? MetricDictionary.Get(metric).ApiKey : null,
            alert.TriggeredAt,
            alert.ResolvedAt);
    }
}
