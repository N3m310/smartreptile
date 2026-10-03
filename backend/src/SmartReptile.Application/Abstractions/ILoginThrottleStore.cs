namespace SmartReptile.Application.Abstractions;

/// <summary>How many recent failures are on record for one identifier and one address.</summary>
/// <param name="UsernameFailures">Failures for this identifier inside the window.</param>
/// <param name="IpFailures">Failures from this address inside the window.</param>
public sealed record LoginFailureWindow(int UsernameFailures, int IpFailures);

/// <summary>
/// Failed-login counters (BR-01.4). The thresholds live in the domain
/// (<see cref="Domain.Identity.LoginThrottlePolicy"/>); this port only counts.
/// </summary>
public interface ILoginThrottleStore
{
    /// <summary>Counts failures at or after <paramref name="sinceUtc"/>.</summary>
    LoginFailureWindow GetWindow(string usernameOrEmail, string ipAddress, DateTimeOffset sinceUtc);

    /// <summary>Records one failure for both counters.</summary>
    void RecordFailure(string usernameOrEmail, string ipAddress, DateTimeOffset nowUtc);

    /// <summary>Clears the identifier's counter after a successful login.</summary>
    void ResetUsername(string usernameOrEmail);
}
