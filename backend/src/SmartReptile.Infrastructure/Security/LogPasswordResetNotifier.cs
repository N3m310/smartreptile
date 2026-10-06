using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// The delivery channel of a deployment with no mail server: the log.
/// </summary>
/// <remarks>
/// Two modes, and the difference is the whole point of the class.
/// <list type="bullet">
/// <item><c>PasswordReset:LogCode = true</c> (development) writes the code, so the reset flow can be completed end
/// to end without a mail server.</item>
/// <item>Off (the default, and always in release) writes that a code was issued and that no channel is configured
/// — and the code reaches nobody. Silently succeeding while nothing is delivered would be the worse failure: the
/// keeper would wait for a message that is never coming, with nothing anywhere saying why.</item>
/// </list>
/// It never throws: see <see cref="IPasswordResetNotifier"/> for why a delivery failure must not reach the caller.
/// </remarks>
public sealed class LogPasswordResetNotifier(
    IOptions<PasswordResetOptions> options,
    ILogger<LogPasswordResetNotifier> logger) : IPasswordResetNotifier
{
    /// <inheritdoc />
    public Task NotifyAsync(PasswordResetDelivery delivery, CancellationToken cancellationToken)
    {
        if (options.Value.LogCode)
        {
            logger.LogWarning(
                "Password reset for {Username} <{Email}>: code {Code}, valid until {ExpiresAt:u}. "
                + "Delivered to the log because no other channel is configured (development only).",
                delivery.Username,
                delivery.Email,
                delivery.Code,
                delivery.ExpiresAtUtc);
        }
        else
        {
            logger.LogWarning(
                "A password reset code was issued for {Username}, but no delivery channel is configured, so it was "
                + "not sent anywhere. Configure PasswordReset:Smtp, or set PasswordReset:LogCode in a development "
                + "environment to complete the flow.",
                delivery.Username);
        }

        return Task.CompletedTask;
    }
}
