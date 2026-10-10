using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Alerts;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Application.Alerts;

/// <summary>A clock frozen at one instant, so a lifecycle test dates its rows and never waits.</summary>
internal sealed class AlertTestClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; } = now;

    /// <inheritdoc />
    public DateTimeOffset InZone(DateTimeOffset instantUtc, string timeZoneId) => instantUtc;

    /// <inheritdoc />
    public DateTimeOffset NowIn(string timeZoneId) => UtcNow;
}

/// <summary>
/// In-memory <see cref="IAlertStore"/>. It answers with what it was given rather than filtering: ordering, cursor
/// arithmetic and the joins belong to the EF adapter, and the integration suite is where those are asserted against
/// real SQL. What this fake is for is the service's rules — who may touch what, which rows get staged, which state
/// row gets re-armed.
/// </summary>
internal sealed class FakeAlertStore : IAlertStore
{
    /// <summary>Account that owns every alert in this fake.</summary>
    public Guid Owner { get; set; } = Guid.NewGuid();

    /// <summary>Alerts the list and the lookups answer with, in the order the test wants them returned.</summary>
    public List<AlertRecord> Alerts { get; } = [];

    /// <summary>Audit rows staged by the service, in order.</summary>
    public List<AuditLog> AuditEntries { get; } = [];

    /// <summary>Dwell keys the fake knows about; a resolution may re-arm one of them.</summary>
    public List<EvaluationState> States { get; } = [];

    /// <summary>Readings the detail's series excerpt is served from.</summary>
    public List<AlertSeriesPoint> Series { get; } = [];

    /// <summary>Commits the service asked for.</summary>
    public int SaveCount { get; private set; }

    /// <summary>Rows the service asked the list for, so paging can be asserted.</summary>
    public int LastListTake { get; private set; }

    /// <summary>Dwell-key lookups the service performed, so "nothing to re-arm" can be asserted.</summary>
    public int StateLookups { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlertRecord>> ListAsync(
        Guid userId,
        AlertQuery query,
        int take,
        CancellationToken cancellationToken)
    {
        LastListTake = take;

        IReadOnlyList<AlertRecord> rows = userId == Owner ? [.. Alerts.Take(take)] : [];

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<AlertRecord?> FindOwnedAsync(long alertId, Guid userId, CancellationToken cancellationToken)
    {
        var row = userId == Owner
            ? Alerts.FirstOrDefault(record => record.Alert.Id == alertId)
            : null;

        return Task.FromResult(row);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlertSeriesPoint>> ReadEpisodeSeriesAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AlertSeriesPoint>>([.. Series.Take(maxPoints)]);

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditLog>> ReadAuditAsync(long alertId, CancellationToken cancellationToken)
    {
        var entityId = alertId.ToString();

        return Task.FromResult<IReadOnlyList<AuditLog>>(
            [.. AuditEntries.Where(entry => entry.EntityId == entityId)]);
    }

    /// <inheritdoc />
    public Task<EvaluationState?> FindExcursionStateAsync(
        Guid terrariumId,
        MetricCode metric,
        ThresholdPhase phase,
        CancellationToken cancellationToken)
    {
        StateLookups++;

        return Task.FromResult(States.FirstOrDefault(state =>
            state.TerrariumId == terrariumId && state.Metric == metric && state.Phase == phase));
    }

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => AuditEntries.Add(entry);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IMetricSilenceStore"/>. Ownership is a flag rather than a table: who may silence what is a
/// query in the adapter and a rule in the service, and this asserts the rule.
/// </summary>
internal sealed class FakeMetricSilenceStore : IMetricSilenceStore
{
    /// <summary>Terrarium every silence in this fake belongs to.</summary>
    public Guid TerrariumId { get; set; } = Guid.NewGuid();

    /// <summary>Account that owns <see cref="TerrariumId"/>.</summary>
    public Guid Owner { get; set; } = Guid.NewGuid();

    /// <summary>Silences the store holds.</summary>
    public List<MetricSilence> Silences { get; } = [];

    /// <summary>Audit rows staged by the service, in order.</summary>
    public List<AuditLog> AuditEntries { get; } = [];

    /// <summary>Commits the service asked for.</summary>
    public int SaveCount { get; private set; }

    /// <inheritdoc />
    public Task<bool> IsMemberAsync(Guid terrariumId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(terrariumId == TerrariumId && userId == Owner);

    /// <inheritdoc />
    public Task<IReadOnlyList<MetricSilence>> ListActiveAsync(
        Guid terrariumId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSilence>>(
            [.. Silences.Where(silence => silence.TerrariumId == terrariumId && silence.IsActiveAt(atUtc))]);

    /// <inheritdoc />
    public Task<MetricSilence?> FindAsync(
        Guid terrariumId,
        Guid silenceId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Silences.FirstOrDefault(silence =>
            silence.TerrariumId == terrariumId && silence.Id == silenceId));

    /// <inheritdoc />
    public void Add(MetricSilence silence) => Silences.Add(silence);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => AuditEntries.Add(entry);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Records what was pushed, and can be made to fail, so a best-effort push can be asserted as best-effort.
/// </summary>
internal sealed class RecordingAlertBroadcaster : ITelemetryBroadcaster
{
    /// <summary>Alert moves pushed, in order.</summary>
    public List<AlertChangedPayload> Alerts { get; } = [];

    /// <summary>Status transitions pushed, in order.</summary>
    public List<DeviceStatusChanged> Statuses { get; } = [];

    /// <summary>Exception every push throws, when a test wants the failure path.</summary>
    public Exception? Failure { get; set; }

    /// <inheritdoc />
    public Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken) =>
        Failure is null ? Task.CompletedTask : Task.FromException(Failure);

    /// <inheritdoc />
    public Task BroadcastStatusAsync(DeviceStatusChanged statusChanged, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Statuses.Add(statusChanged);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task BroadcastAlertsAsync(
        IReadOnlyList<AlertChangedPayload> changes,
        CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Alerts.AddRange(changes);

        return Task.CompletedTask;
    }
}
