namespace SmartReptile.Application.Abstractions;

/// <summary>Recent self-registration attempts, counted over two independent windows.</summary>
/// <param name="IpAttempts">Attempts from this address inside the per-address window.</param>
/// <param name="GlobalAttempts">Attempts from everywhere inside the global window.</param>
public sealed record OnboardingAttemptWindow(int IpAttempts, int GlobalAttempts);

/// <summary>
/// Counters behind the <c>self-register</c> anti-abuse limits (§02-design/06 §5). The thresholds live in
/// <see cref="Domain.Devices.OnboardingThrottlePolicy"/>; this port only counts.
/// </summary>
public interface IOnboardingThrottleStore
{
    /// <summary>Counts attempts at or after each window start.</summary>
    OnboardingAttemptWindow GetWindow(string ipAddress, DateTimeOffset sinceIpUtc, DateTimeOffset sinceGlobalUtc);

    /// <summary>Records one attempt for both windows.</summary>
    void RecordAttempt(string ipAddress, DateTimeOffset nowUtc);
}
