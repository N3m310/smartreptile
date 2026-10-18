using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Abstractions;

/// <summary>One message, addressed and rendered, handed to a channel.</summary>
/// <param name="UserId">Recipient.</param>
/// <param name="Channel">Channel that will carry it.</param>
/// <param name="Destination">Push token or email address the channel must use.</param>
/// <param name="Title">Rendered title.</param>
/// <param name="Body">Rendered body.</param>
/// <param name="DeepLink">Client deep link, or null.</param>
/// <param name="Language">Language the text was rendered in, for a channel that localises around it.</param>
public sealed record NotificationDelivery(
    Guid UserId,
    NotificationChannel Channel,
    string Destination,
    string Title,
    string Body,
    string? DeepLink,
    string Language);

/// <summary>What a channel did with a message.</summary>
/// <param name="Delivered">True when the channel accepted it.</param>
/// <param name="Error">Channel error text when it did not.</param>
/// <param name="DestinationRejected">
/// True when the destination itself is gone — FCM's <c>UNREGISTERED</c> — which is the one failure that must
/// change stored state rather than be retried (TC-U-44).
/// </param>
public readonly record struct NotificationSendResult(bool Delivered, string? Error, bool DestinationRejected)
{
    /// <summary>The channel accepted the message.</summary>
    public static NotificationSendResult Ok() => new(true, null, false);

    /// <summary>The channel refused it, and may accept it on a retry.</summary>
    /// <param name="error">What the channel said.</param>
    public static NotificationSendResult Failed(string error) => new(false, error, false);

    /// <summary>The destination is gone; the channel must be switched off for this account.</summary>
    /// <param name="error">What the channel said.</param>
    public static NotificationSendResult Gone(string error) => new(false, error, true);
}

/// <summary>
/// The delivery seam of FR-13: everything that talks to a third party or to a mail relay sits behind this one
/// interface, so the dispatcher and its policy can be tested without a network (§02-design/05 §8).
/// </summary>
public interface INotificationChannel
{
    /// <summary>Which channel this is.</summary>
    NotificationChannel Channel { get; }

    /// <summary>Attempts delivery once. Throwing is allowed and is treated exactly like a refusal.</summary>
    /// <param name="delivery">The message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NotificationSendResult> SendAsync(NotificationDelivery delivery, CancellationToken cancellationToken);
}
