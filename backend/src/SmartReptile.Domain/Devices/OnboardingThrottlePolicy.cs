namespace SmartReptile.Domain.Devices;

/// <summary>Outcome of the self-registration anti-abuse check (§02-design/06 §5).</summary>
/// <param name="IsThrottled">True when the attempt must be refused before any row is written.</param>
/// <param name="Code">Problem code when refused: <c>rate_limited</c> for either window.</param>
public sealed record OnboardingThrottleDecision(bool IsThrottled, string? Code)
{
    /// <summary>The attempt may proceed.</summary>
    public static readonly OnboardingThrottleDecision Allowed = new(false, null);
}

/// <summary>
/// Anti-abuse limits for <c>POST /devices/self-register</c>, which is unauthenticated by necessity.
/// </summary>
/// <remarks>
/// Two windows guard it: one per client address, so a single host cannot create rows on demand, and one global,
/// so a distributed flood still cannot fill the table. Neither can read or write another tenant's data — an
/// unclaimed device has no capability at all — so this is a storage-hygiene control, not an authorisation one.
/// </remarks>
public static class OnboardingThrottlePolicy
{
    /// <summary>Decides whether an attempt is refused.</summary>
    public static OnboardingThrottleDecision Evaluate(
        int ipAttemptsInWindow,
        int globalAttemptsInWindow,
        OnboardingLimits limits)
    {
        if (ipAttemptsInWindow >= limits.MaxAttemptsPerIp
            || globalAttemptsInWindow >= limits.MaxAttemptsPerHourGlobal)
        {
            return new OnboardingThrottleDecision(true, "rate_limited");
        }

        return OnboardingThrottleDecision.Allowed;
    }
}

/// <summary>
/// The onboarding limits themselves, supplied by configuration (<c>OnboardingProtection</c>) so a demo setting can
/// be changed without a rebuild. <see cref="Documented"/> is what the documents pin.
/// </summary>
/// <param name="MaxAttemptsPerIp">Self-registrations allowed per address inside the window.</param>
/// <param name="IpWindowMinutes">Length of the per-address window, in minutes.</param>
/// <param name="MaxAttemptsPerHourGlobal">Self-registrations allowed globally inside the window.</param>
/// <param name="GlobalWindowMinutes">
/// Length of the global window, in minutes. One hour by default, which is what makes the property above "per hour".
/// </param>
public sealed record OnboardingLimits(
    int MaxAttemptsPerIp,
    int IpWindowMinutes,
    int MaxAttemptsPerHourGlobal,
    int GlobalWindowMinutes = 60)
{
    /// <summary>The values §07-appendices/03 §2.2 specifies (1 per address per 5 min, 20 per hour globally).</summary>
    public static readonly OnboardingLimits Documented = new(1, 5, 20);
}
