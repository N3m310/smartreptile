using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Notifications;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Notifications;

/// <summary>
/// The SMTP channel (FR-13, §02-design/05 §3): the out-of-band path, useful precisely because it arrives on a
/// different device than the one the app is open on.
/// </summary>
/// <remarks>
/// <b>Off by default, and its absence is visible.</b> A deployment that enables <c>ChannelEmailEnabled</c> for an
/// account without a relay can only fail; the settings therefore have to be switched on deliberately
/// (<c>Notifications:Smtp:Enabled</c>), and the development compose file switches them on against **Mailpit**, the
/// local sink TC-I-12 asks for — which is also how the 120-minute escalation step of §6 can be demonstrated
/// without a real mail account.
/// <para>
/// A refused recipient, a refused conversation or an unreachable relay are all refusals with a message, handed back
/// for the dispatcher's retry schedule. Nothing is retried here: BR-13.5's 5 s / 30 s / 2 min belongs to the one
/// place that knows what has already been attempted.
/// </para>
/// </remarks>
public sealed class SmtpNotificationChannel(
    IOptions<NotificationOptions> options,
    ILogger<SmtpNotificationChannel> logger) : INotificationChannel
{
    private readonly SmtpOptions _settings = options.Value.Smtp;

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Smtp;

    /// <inheritdoc />
    public async Task<NotificationSendResult> SendAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            return NotificationSendResult.Failed("the SMTP channel is disabled in this deployment");
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName),
                Subject = delivery.Title,
                Body = delivery.Body,
                IsBodyHtml = false,
            };

            message.To.Add(delivery.Destination);

            if (delivery.DeepLink is { Length: > 0 } deepLink)
            {
                message.Body = $"{delivery.Body}{Environment.NewLine}{Environment.NewLine}{deepLink}";
            }

            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.UseStartTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };

            if (!string.IsNullOrWhiteSpace(_settings.Username))
            {
                client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
            }

            await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);

            return NotificationSendResult.Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SMTP delivery failed; the attempt is recorded as a failure");

            return NotificationSendResult.Failed(ex.Message);
        }
    }
}
