using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Notifications;

/// <summary>One row of the in-app inbox (FR-12's S6).</summary>
/// <param name="Id">Row identity, used by the mark-read route.</param>
/// <param name="Channel">Channel the row records; the inbox only ever shows <c>InApp</c> rows.</param>
/// <param name="Title">Rendered title, in the language it was sent in.</param>
/// <param name="Body">Rendered body.</param>
/// <param name="DeepLink">Where the app should open.</param>
/// <param name="AlertId">Alert it belongs to, when it belongs to one.</param>
/// <param name="TerrariumId">Terrarium it is about.</param>
/// <param name="Step">Escalation step, so a client can label a repeat.</param>
/// <param name="IsRead">Whether the keeper has opened it.</param>
/// <param name="SentAt">When it was delivered.</param>
/// <param name="ReadAt">When it was opened.</param>
public sealed record NotificationInboxItem(
    long Id,
    string Channel,
    string Title,
    string Body,
    string? DeepLink,
    long? AlertId,
    Guid? TerrariumId,
    byte Step,
    bool IsRead,
    DateTimeOffset SentAt,
    DateTimeOffset? ReadAt);

/// <summary>The inbox page and its badge count.</summary>
/// <param name="Items">Rows, newest first.</param>
/// <param name="Unread">How many unread rows the account has in total, not in this page.</param>
public sealed record NotificationInboxPage(IReadOnlyList<NotificationInboxItem> Items, int Unread);

/// <summary>
/// The in-app inbox of FR-12 (roadmap task 3.5): what was delivered, and the two writes a keeper has over it.
/// </summary>
/// <remarks>
/// Read-only over the delivery records the dispatcher writes — there is deliberately no route that creates an inbox
/// row, because a notification the system did not decide to send is not a notification. The channel filter is
/// duplicated from the store on purpose rather than trusted: an implementation that stopped filtering would leak
/// push and email rows into a screen whose whole promise is "what happened to my enclosure".
/// </remarks>
public sealed class NotificationInboxService(INotificationStore store, IClock clock)
{
    /// <summary>Rows one page may carry.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Rows a page carries when the caller does not say.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The caller's inbox, newest first.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="unreadOnly">True to return only what has not been opened.</param>
    /// <param name="pageSize">Requested page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NotificationInboxPage> ListAsync(
        Guid userId,
        bool unreadOnly,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        var rows = await store.ListInboxAsync(userId, unreadOnly, size, cancellationToken).ConfigureAwait(false);
        var unread = await store.CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false);

        return new NotificationInboxPage(rows.Select(Describe).ToArray(), unread);
    }

    /// <summary>Marks one row as read.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="notificationId">Row the caller named.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>False when the row is not the caller's, which the route answers exactly as "no such row".</returns>
    public async Task<bool> MarkReadAsync(Guid userId, long notificationId, CancellationToken cancellationToken)
    {
        var row = await store.FindInboxRowAsync(userId, notificationId, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return false;
        }

        // Idempotent: marking a read row read again changes nothing and records nothing, so the second call answers
        // the same way the first did (the same rule the silence cancel route follows).
        if (!row.IsRead)
        {
            await store.MarkReadAsync(userId, notificationId, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Marks every unread row as read.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows changed.</returns>
    public Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken) =>
        store.MarkReadAsync(userId, null, clock.UtcNow, cancellationToken);

    private static NotificationInboxItem Describe(NotificationLog row) =>
        new(
            row.Id,
            row.Channel.ToString(),
            row.Title,
            row.Body,
            row.DeepLink,
            row.AlertId,
            row.TerrariumId,
            row.Step,
            row.IsRead,
            row.SentAt ?? row.CreatedAt,
            row.ReadAt);
}
