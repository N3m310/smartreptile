using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Tests.Unit.Application.Notifications;

/// <summary>
/// In-memory <see cref="INotificationStore"/>. It behaves like the database in the one way the dispatcher's
/// idempotence depends on: the delivery rows the last sweep wrote are visible to the next sweep as an alert's
/// attempt history. Everything else — the joins, the ordering, the hourly window — belongs to the EF adapter and is
/// asserted in the integration suite.
/// </summary>
internal sealed class FakeNotificationStore : INotificationStore
{
    /// <summary>Alerts the sweep is offered; their <c>Attempts</c> are recomputed from <see cref="Rows"/>.</summary>
    public List<NotificationCandidate> Candidates { get; set; } = [];

    /// <summary>Delivery rows written so far.</summary>
    public List<NotificationLog> Rows { get; } = [];

    /// <summary>What the hourly cap sees.</summary>
    public int DeliveredThisHour { get; set; }

    /// <summary>Terrariums with coalesced entries in the hour the digest covers.</summary>
    public List<NotificationDigestInput> DigestInputs { get; } = [];

    /// <summary>Accounts whose push channel was switched off.</summary>
    public List<Guid> PushDisabled { get; } = [];

    /// <summary>Commits the dispatcher asked for.</summary>
    public int SaveCount { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationCandidate>> ListCandidatesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationCandidate> rows =
        [
            .. Candidates.Select(candidate => candidate with
            {
                Attempts =
                [
                    .. Rows
                        .Where(row => row.AlertId == candidate.AlertId && row.UserId is { } userId)
                        .Select(row => new NotificationAttempt(
                            row.UserId!.Value,
                            row.Channel,
                            row.Step,
                            row.Attempts,
                            row.Status,
                            row.CreatedAt)),
                ],
            }),
        ];

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<int> CountDeliveredAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(DeliveredThisHour);

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationDigestInput>> ListDigestInputsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NotificationDigestInput>>([.. DigestInputs]);

    /// <inheritdoc />
    public Task<bool> DigestExistsAsync(
        Guid terrariumId,
        DateTimeOffset coveredHourEndUtc,
        CancellationToken cancellationToken) =>
        // The same rule the EF adapter applies: a row that names no alert and records the top of the hour is the
        // digest of that hour. Deriving it from the rows rather than from a set is what makes the second-sweep test
        // mean something.
        Task.FromResult(Rows.Any(row =>
            row.TerrariumId == terrariumId
            && row.AlertId == null
            && row.CreatedAt == coveredHourEndUtc));

    /// <inheritdoc />
    public Task AddAsync(IReadOnlyList<NotificationLog> rows, CancellationToken cancellationToken)
    {
        Rows.AddRange(rows);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationLog>> ListInboxAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationLog> rows =
        [
            .. Rows
                .Where(row => row.UserId == userId
                    && row.Channel == NotificationChannel.InApp
                    && row.Status == NotificationStatus.Sent
                    && (!unreadOnly || !row.IsRead))
                .Take(limit),
        ];

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<NotificationLog?> FindInboxRowAsync(
        Guid userId,
        long notificationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Rows.FirstOrDefault(row =>
            row.Id == notificationId
            && row.UserId == userId
            && row.Channel == NotificationChannel.InApp
            && row.Status == NotificationStatus.Sent));

    /// <inheritdoc />
    public Task<int> MarkReadAsync(
        Guid userId,
        long? notificationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var matches = Rows
            .Where(row => row.UserId == userId
                && row.Channel == NotificationChannel.InApp
                && row.Status == NotificationStatus.Sent
                && !row.IsRead
                && (notificationId is null || row.Id == notificationId))
            .ToArray();

        foreach (var row in matches)
        {
            row.IsRead = true;
            row.ReadAt = atUtc;
        }

        return Task.FromResult(matches.Length);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Rows.Count(row => row.UserId == userId
            && row.Channel == NotificationChannel.InApp
            && row.Status == NotificationStatus.Sent
            && !row.IsRead));

    /// <inheritdoc />
    public Task DisablePushAsync(Guid userId, CancellationToken cancellationToken)
    {
        PushDisabled.Add(userId);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationLog>> ListForAlertAsync(
        long alertId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationLog> rows = [.. Rows.Where(row => row.AlertId == alertId)];

        return Task.FromResult(rows);
    }
}

/// <summary>A channel that answers whatever a test tells it to, and remembers what it was handed.</summary>
internal sealed class FakeNotificationChannel(NotificationChannel channel) : INotificationChannel
{
    /// <summary>What the channel answers.</summary>
    public Func<NotificationDelivery, NotificationSendResult> Behaviour { get; set; } =
        _ => NotificationSendResult.Ok();

    /// <summary>Messages the channel was handed, in order.</summary>
    public List<NotificationDelivery> Delivered { get; } = [];

    /// <inheritdoc />
    public NotificationChannel Channel { get; } = channel;

    /// <inheritdoc />
    public Task<NotificationSendResult> SendAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        Delivered.Add(delivery);

        return Task.FromResult(Behaviour(delivery));
    }
}

/// <summary>A clock a test moves by hand, so the retry schedule needs no waiting.</summary>
internal sealed class NotificationTestClock(DateTimeOffset now) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; } = now;

    /// <inheritdoc />
    public DateTimeOffset InZone(DateTimeOffset instantUtc, string timeZoneId) => instantUtc;

    /// <inheritdoc />
    public DateTimeOffset NowIn(string timeZoneId) => UtcNow;
}
