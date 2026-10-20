using System.Security.Claims;
using SmartReptile.Api.Security;
using SmartReptile.Application.Notifications;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// The in-app inbox (FR-12's S6, roadmap task 3.5). Contract: <c>07-appendices/03</c> §4.5.
/// </summary>
/// <remarks>
/// Three routes, all <c>U</c>: reading one's own inbox needs a token and nothing else, and every row is scoped to
/// the caller by the store, so an id that belongs to somebody else answers exactly as an id that does not exist
/// (BR-02.2). There is deliberately no route that creates a row: a notification the system did not decide to send
/// is not a notification, and the delivery records are the dispatcher's to write.
/// </remarks>
public static class NotificationEndpoints
{
    /// <summary>Maps the inbox routes.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithTags("notifications")
            .RequireAuthorization(Permissions.NotificationRead);

        group.MapGet(string.Empty, async (
            bool? unreadOnly,
            int? pageSize,
            ClaimsPrincipal principal,
            NotificationInboxService service,
            CancellationToken cancellationToken) =>
        {
            var page = await service.ListAsync(
                principal.GetUserId() ?? Guid.Empty,
                unreadOnly ?? false,
                pageSize,
                cancellationToken);

            return Results.Ok(new
            {
                items = page.Items.Select(item => new
                {
                    id = item.Id,
                    channel = item.Channel,
                    title = item.Title,
                    body = item.Body,
                    deepLink = item.DeepLink,
                    alertId = item.AlertId,
                    terrariumId = item.TerrariumId,
                    step = item.Step,
                    isRead = item.IsRead,
                    sentAt = item.SentAt,
                    readAt = item.ReadAt,
                }),
                unread = page.Unread,
            });
        })
        .WithSummary("The caller's notification inbox")
        .WithDescription("Newest first. `?unreadOnly=true` filters to what has not been opened, and `pageSize` "
                       + "(default 50, max 200) bounds the page; `unread` is the account's total, not the page's. "
                       + "Undelivered and withheld entries are never listed here — this screen answers \"what was I "
                       + "told\", and the suppression record is the report's, not the keeper's.");

        group.MapPost("/{notificationId:long}/read", async (
            long notificationId,
            ClaimsPrincipal principal,
            NotificationInboxService service,
            CancellationToken cancellationToken) =>
        {
            var found = await service.MarkReadAsync(
                principal.GetUserId() ?? Guid.Empty,
                notificationId,
                cancellationToken);

            return found ? Results.NoContent() : NotFound();
        })
        .WithSummary("Mark one inbox entry as read")
        .WithDescription("Idempotent: a second call on an already-read entry answers `204` too. `404` means the "
                       + "entry is not the caller's — the same answer a nonexistent id gets.");

        group.MapPost("/read-all", async (
            ClaimsPrincipal principal,
            NotificationInboxService service,
            CancellationToken cancellationToken) =>
        {
            var changed = await service.MarkAllReadAsync(principal.GetUserId() ?? Guid.Empty, cancellationToken);

            return Results.Ok(new { markedRead = changed });
        })
        .WithSummary("Mark every unread inbox entry as read")
        .WithDescription("Answers how many rows changed, so a client can prove the badge cleared without a second "
                       + "read.");

        return app;
    }

    private static IResult NotFound() =>
        ApiProblem.Create("not_found", "The notification does not exist.", StatusCodes.Status404NotFound);
}
