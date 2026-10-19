namespace SmartReptile.Domain.Identity;

/// <summary>
/// The outcome of a failed-login check (BR-01.4).
/// </summary>
/// <param name="IsLocked">True when the attempt must be refused before the password is even verified.</param>
/// <param name="Code">Problem code when locked: <c>account_locked</c> or <c>ip_blocked</c>.</param>
public sealed record LoginThrottleDecision(bool IsLocked, string? Code)
{
    /// <summary>The attempt may proceed.</summary>
    public static readonly LoginThrottleDecision Allowed = new(false, null);
}

/// <summary>
/// Thresholds for failed-login throttling (§02-design/06 §2). The counters themselves live in a store,
/// because counting is state; this type only says when a count has become a lock, which keeps the rule
/// testable without any clock or database.
/// </summary>
public static class LoginThrottlePolicy
{
    /// <summary>Failed logins per identifier inside the window before the identifier is locked (BR-01.4).</summary>
    public const int MaxFailuresPerUsername = 10;

    /// <summary>Window for the per-identifier counter, in minutes.</summary>
    public const int UsernameWindowMinutes = 15;

    /// <summary>Failed logins from one address inside the window before the address is blocked.</summary>
    public const int MaxFailuresPerIp = 20;

    /// <summary>Window for the per-address counter, in minutes.</summary>
    public const int IpWindowMinutes = 15;

    /// <summary>
    /// Decides whether an attempt is refused. The identifier is checked first: a locked account is a more
    /// specific answer than a blocked address, and it is the one the user can act on.
    /// </summary>
    public static LoginThrottleDecision Evaluate(int usernameFailuresInWindow, int ipFailuresInWindow)
    {
        if (usernameFailuresInWindow >= MaxFailuresPerUsername)
        {
            return new LoginThrottleDecision(true, "account_locked");
        }

        if (ipFailuresInWindow >= MaxFailuresPerIp)
        {
            return new LoginThrottleDecision(true, "ip_blocked");
        }

        return LoginThrottleDecision.Allowed;
    }
}
