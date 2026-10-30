using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Infrastructure.Persistence;

namespace SmartReptile.Infrastructure.Alerts;

/// <summary>
/// The <see cref="IAlertStore"/> adapter: the caller's alerts, one of them by id, the values one episode was judged
/// on, and the dwell key a resolution has to re-arm.
/// </summary>
/// <remarks>
/// Every query starts from the terrarium's owner rather than from the alert, which is what makes a foreign alert
/// indistinguishable from a missing one: a client cannot learn which ids exist (BR-02.2). The soft-delete filter on
/// <c>Terrarium</c> is a model-level query filter, so an alert of a deleted terrarium is invisible for the same
/// reason a deleted terrarium is.
/// <para>
/// The device's public id reaches the response through an explicit join: <c>Alert</c> has a foreign key to
/// <c>Device</c> and no navigation to it (the schema keeps the column, not the object graph), and a client must
/// never see the surrogate key.
/// </para>
/// </remarks>
public sealed class EfAlertStore(SmartReptileDbContext db) : IAlertStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertRecord>> ListAsync(
        Guid userId,
        AlertQuery query,
        int take,
        CancellationToken cancellationToken,
        bool anyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Both joins are explicit: `Alert` carries foreign keys and no navigations (the schema keeps the columns
        // rather than the object graph), and starting from the terrarium's owner is what scopes the read.
        var rows = from alert in db.Alerts
                   join terrarium in db.Terrariums on alert.TerrariumId equals terrarium.Id
                   join device in db.Devices on alert.DeviceId equals device.Id
                   select new { Alert = alert, DevicePublicId = device.PublicId, terrarium.UserId };

        // Dropped for an administrator, for the reason `FindAnyAsync` gives about the single-alert read. It is also
        // what makes an unowned terrarium reachable at all: `UserId` is nullable since 2026-10-23, and `NULL = <id>`
        // matches no account, so a scoped read would hide its alerts from everyone — the administrator included.
        if (!anyAccount)
        {
            rows = rows.Where(row => row.UserId == userId);
        }

        if (query.State is { } state)
        {
            rows = rows.Where(row => row.Alert.State == state);
        }

        if (query.InProgress)
        {
            // "Not resolved" rather than "open": an acknowledged alert is still somebody's problem, so the tab that
            // lists what needs attention keeps it instead of dropping it the moment it is picked up.
            rows = rows.Where(row => row.Alert.State != AlertState.Resolved);
        }

        if (query.Severity is { } severity)
        {
            rows = rows.Where(row => row.Alert.Severity == severity);
        }

        if (query.Metric is { } metric)
        {
            rows = rows.Where(row => row.Alert.Metric == metric);
        }

        if (query.Family is { } family)
        {
            rows = rows.Where(row => row.Alert.Family == family);
        }

        if (query.FromUtc is { } from)
        {
            rows = rows.Where(row => row.Alert.TriggeredAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(row => row.Alert.TriggeredAt <= to);
        }

        if (AlertCursor.TryDecode(query.Cursor, out var position))
        {
            // Strictly after the cursor in the list's own order: instant first, identity to break the tie, which is
            // what makes paging over a table that keeps receiving rows neither repeat nor skip one.
            rows = rows.Where(row =>
                row.Alert.TriggeredAt < position.TriggeredAt
                || (row.Alert.TriggeredAt == position.TriggeredAt && row.Alert.Id < position.AlertId));
        }

        return await rows
            .OrderByDescending(row => row.Alert.TriggeredAt)
            .ThenByDescending(row => row.Alert.Id)
            .Take(take)
            .Select(row => new AlertRecord(row.Alert, row.DevicePublicId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AlertCounts> CountByStateAsync(
        Guid userId,
        AlertQuery query,
        CancellationToken cancellationToken,
        bool anyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The same joins and the same owner predicate as the list: a count that disagreed with the page it labels
        // would be worse than no count at all. The device join is kept for the same reason it is in the list — the
        // list drops an alert whose board row is gone, so its count has to drop it too.
        var rows = from alert in db.Alerts
                   join terrarium in db.Terrariums on alert.TerrariumId equals terrarium.Id
                   join device in db.Devices on alert.DeviceId equals device.Id
                   where anyAccount || terrarium.UserId == userId
                   select alert;

        // Everything the caller filtered on, except the state itself — including `InProgress`, which is a state
        // filter wearing another name: a count that dropped the resolved rows could not label the resolved tab.
        if (query.Severity is { } severity)
        {
            rows = rows.Where(alert => alert.Severity == severity);
        }
        if (query.Metric is { } metric)
        {
            rows = rows.Where(alert => alert.Metric == metric);
        }

        if (query.Family is { } family)
        {
            rows = rows.Where(alert => alert.Family == family);
        }

        if (query.FromUtc is { } from)
        {
            rows = rows.Where(alert => alert.TriggeredAt >= from);
        }

        if (query.ToUtc is { } to)
        {
            rows = rows.Where(alert => alert.TriggeredAt <= to);
        }

        var counts = await rows
            .GroupBy(alert => alert.State)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AlertCounts(
            counts.Where(count => count.State == AlertState.Open).Sum(count => count.Count),
            counts.Where(count => count.State == AlertState.Acknowledged).Sum(count => count.Count),
            counts.Where(count => count.State == AlertState.Resolved).Sum(count => count.Count));
    }

    /// <inheritdoc />
    public async Task<AlertRecord?> FindOwnedAsync(
        long alertId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await (from alert in db.Alerts
               join terrarium in db.Terrariums on alert.TerrariumId equals terrarium.Id
               join device in db.Devices on alert.DeviceId equals device.Id
               where alert.Id == alertId && terrarium.UserId == userId
               select new AlertRecord(alert, device.PublicId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    /// <remarks>
    /// The same query with the owner predicate dropped. The joins are kept for the reason they exist in the scoped
    /// one: the terrarium join is what applies the soft-delete filter, and the device join is what names the board on
    /// the answer.
    /// </remarks>
    public async Task<AlertRecord?> FindAnyAsync(long alertId, CancellationToken cancellationToken) =>
        await (from alert in db.Alerts
               join terrarium in db.Terrariums on alert.TerrariumId equals terrarium.Id
               join device in db.Devices on alert.DeviceId equals device.Id
               where alert.Id == alertId
               select new AlertRecord(alert, device.PublicId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertSeriesPoint>> ReadEpisodeSeriesAsync(
        Guid terrariumId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken)
    {
        // Newest first in the database, so the cap keeps the end of the episode rather than its beginning — the end
        // is what a keeper looks at — and oldest first in the response, which is the order a chart wants.
        var points = await db.MetricReadings
            .Where(reading => reading.Sample!.TerrariumId == terrariumId
                           && reading.Metric == metric
                           && reading.Sample.RecordedAt >= fromUtc
                           && reading.Sample.RecordedAt <= toUtc)
            .OrderByDescending(reading => reading.Sample!.RecordedAt)
            .Take(maxPoints)
            .Select(reading => new AlertSeriesPoint(reading.Sample!.RecordedAt, reading.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return points.OrderBy(point => point.At).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> ReadAuditAsync(long alertId, CancellationToken cancellationToken)
    {
        var entityId = alertId.ToString(CultureInfo.InvariantCulture);

        return await db.AuditLogs
            .Where(entry => entry.EntityName == AlertEntityName && entry.EntityId == entityId)
            .OrderBy(entry => entry.OccurredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationAttemptRecord>> ReadDeliveriesAsync(
        long alertId,
        CancellationToken cancellationToken) =>
        await db.NotificationLogs
            .Where(row => row.AlertId == alertId)
            .OrderBy(row => row.CreatedAt)
            .ThenBy(row => row.Id)
            .Select(row => new NotificationAttemptRecord(
                row.CreatedAt,
                row.Channel,
                row.Step,
                row.Status,
                row.SuppressedReason,
                row.Title))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task<EvaluationState?> FindExcursionStateAsync(
        Guid terrariumId,
        MetricCode metric,
        ThresholdPhase phase,
        CancellationToken cancellationToken) =>
        db.EvaluationStates
            .FirstOrDefaultAsync(
                state => state.TerrariumId == terrariumId && state.Metric == metric && state.Phase == phase,
                cancellationToken);

    /// <inheritdoc />
    public void AddAuditEntry(AuditLog entry) => db.AuditLogs.Add(entry);

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    /// <summary>Entity name audit rows about alerts carry, as <see cref="AuditLog.ForAlert"/> writes it.</summary>
    private const string AlertEntityName = "Alert";
}
