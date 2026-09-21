namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Injected time source. Everything time-dependent (dwell windows, staleness, local-day bucketing) goes
/// through this interface so tests can advance time instead of sleeping — the pattern that keeps the
/// threshold-engine suite fast and deterministic (§04-quality/01 §4).
/// </summary>
public interface IClock
{
    /// <summary>Current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Current instant converted to the supplied IANA time zone (falls back to UTC if unknown).</summary>
    DateTimeOffset NowIn(string timeZoneId);
}
