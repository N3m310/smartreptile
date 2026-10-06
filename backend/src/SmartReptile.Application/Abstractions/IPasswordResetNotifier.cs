namespace SmartReptile.Application.Abstractions;

/// <summary>What a delivery channel needs in order to tell a keeper their reset code.</summary>
/// <param name="Username">Stored (lower-case) username, for the message and the log.</param>
/// <param name="Email">Stored (lower-case) address, where a mail channel sends it.</param>
/// <param name="PreferredLanguage"><c>vi</c> or <c>en</c>, so the message can be written in the keeper's language.</param>
/// <param name="Code">The code itself. This is the only copy: nothing stores the plaintext.</param>
/// <param name="ExpiresAtUtc">When the code stops being accepted, stated in the message.</param>
public sealed record PasswordResetDelivery(
    string Username,
    string Email,
    string PreferredLanguage,
    string Code,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Delivers a password-reset code (BR-01.5). A port because the channel is a deployment decision rather than a
/// code decision — the demo host has no mail server, and pretending otherwise would be worse than saying so.
/// </summary>
/// <remarks>
/// Implementations must not throw for a delivery failure. A forgotten password must not become a 500, and the
/// caller's answer is deliberately identical whether the message arrived or not — a response that changed when the
/// mail server was down would be an oracle for whether the address exists. Report the failure by logging it, and
/// let the keeper ask for a new code.
/// </remarks>
public interface IPasswordResetNotifier
{
    /// <summary>Sends the code to the account's owner.</summary>
    Task NotifyAsync(PasswordResetDelivery delivery, CancellationToken cancellationToken);
}
