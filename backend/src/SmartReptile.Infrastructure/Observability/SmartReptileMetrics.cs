using System.Diagnostics.Metrics;
using SmartReptile.Infrastructure.Mqtt;

namespace SmartReptile.Infrastructure.Observability;

/// <summary>
/// Counters required by FR-18/NFR-12. They are kept in process and exposed as JSON by <c>/metrics</c> at M1;
/// wiring a Prometheus/OTLP exporter is part of the M5 hardening milestone.
/// </summary>
public sealed class SmartReptileMetrics
{
    private readonly Meter _meter = new("SmartReptile", "0.1.0");
    private readonly Counter<long> _samplesIngested;
    private readonly Counter<long> _duplicates;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _alertsOpened;
    private readonly Counter<long> _notificationsSent;
    private readonly Counter<long> _notificationsFailed;
    private readonly Counter<long> _notificationsSuppressed;

    private long _samplesIngestedCount;
    private long _duplicateCount;
    private long _rejectedCount;
    private long _alertsOpenedCount;
    private long _notificationsSentCount;
    private long _notificationsFailedCount;
    private long _notificationsSuppressedCount;

    /// <summary>Creates the meters and counters.</summary>
    public SmartReptileMetrics()
    {
        _samplesIngested = _meter.CreateCounter<long>("ingest_samples_total", "samples", "Telemetry samples persisted");
        _duplicates = _meter.CreateCounter<long>("ingest_duplicates_total", "samples", "Samples rejected as duplicates of (deviceId, seq)");
        _rejected = _meter.CreateCounter<long>("ingest_rejected_total", "batches", "Batches rejected by validation or authentication");
        _alertsOpened = _meter.CreateCounter<long>("alerts_opened_total", "alerts", "Alerts opened by the evaluator");
        _notificationsSent = _meter.CreateCounter<long>("notifications_sent_total", "notifications", "Notification attempts a channel accepted");
        _notificationsFailed = _meter.CreateCounter<long>("notifications_failed_total", "notifications", "Notification attempts a channel refused");
        _notificationsSuppressed = _meter.CreateCounter<long>("notifications_suppressed_total", "notifications", "Notifications withheld by policy, by reason");
    }

    /// <summary>Records a persisted sample.</summary>
    public void SampleIngested(int count = 1)
    {
        _samplesIngested.Add(count);
        Interlocked.Add(ref _samplesIngestedCount, count);
    }

    /// <summary>Records a duplicate delivery (idempotent path, BR-06.3).</summary>
    public void DuplicateDetected(int count = 1)
    {
        _duplicates.Add(count);
        Interlocked.Add(ref _duplicateCount, count);
    }

    /// <summary>Records a rejected batch, with the machine-readable reason.</summary>
    public void BatchRejected(string reason, int count = 1)
    {
        _rejected.Add(count, new KeyValuePair<string, object?>("reason", reason));
        Interlocked.Add(ref _rejectedCount, count);
    }

    /// <summary>Records an alert opened by the evaluator or by the silence watchdog, by the severity it opened at.</summary>
    public void AlertOpened(string severity, int count = 1)
    {
        _alertsOpened.Add(count, new KeyValuePair<string, object?>("severity", severity));
        Interlocked.Add(ref _alertsOpenedCount, count);
    }

    /// <summary>
    /// Records what one notification sweep did (roadmap 3.5). The suppression counter carries its reason because
    /// that is the figure the report's alert-fatigue table is built from (§02-design/05 §4).
    /// </summary>
    /// <param name="outcome">What the sweep decided and what the channels did with it.</param>
    public void Notifications(Application.Notifications.NotificationSweepOutcome outcome)
    {
        if (outcome.Delivered > 0)
        {
            _notificationsSent.Add(outcome.Delivered);
            Interlocked.Add(ref _notificationsSentCount, outcome.Delivered);
        }

        if (outcome.Failed > 0)
        {
            _notificationsFailed.Add(outcome.Failed);
            Interlocked.Add(ref _notificationsFailedCount, outcome.Failed);
        }

        if (outcome.Suppressed > 0)
        {
            _notificationsSuppressed.Add(outcome.Suppressed);
            Interlocked.Add(ref _notificationsSuppressedCount, outcome.Suppressed);
        }
    }

    /// <summary>Snapshot used by the <c>/metrics</c> endpoint.</summary>
    public IReadOnlyDictionary<string, long> Snapshot(IMqttBrokerStatus broker) => new Dictionary<string, long>
    {
        ["ingest_samples_total"] = Interlocked.Read(ref _samplesIngestedCount),
        ["ingest_duplicates_total"] = Interlocked.Read(ref _duplicateCount),
        ["ingest_rejected_total"] = Interlocked.Read(ref _rejectedCount),
        ["alerts_opened_total"] = Interlocked.Read(ref _alertsOpenedCount),
        ["notifications_sent_total"] = Interlocked.Read(ref _notificationsSentCount),
        ["notifications_failed_total"] = Interlocked.Read(ref _notificationsFailedCount),
        ["notifications_suppressed_total"] = Interlocked.Read(ref _notificationsSuppressedCount),
        ["mqtt_connected_clients"] = broker.ConnectedClients,
        ["mqtt_rejected_connections_total"] = broker.RejectedConnections,
        ["mqtt_published_messages_total"] = broker.PublishedMessages,
        ["mqtt_refused_publications_total"] = broker.RefusedPublications,
        ["mqtt_refused_subscriptions_total"] = broker.RefusedSubscriptions,
        ["mqtt_kicked_sessions_total"] = broker.KickedSessions,
    };
}
