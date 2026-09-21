using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Time;

/// <summary>Production <see cref="IClock"/>: wall clock in UTC, converting to a time zone on demand.</summary>
public sealed class SystemClock : IClock
{
    private static readonly Dictionary<string, TimeZoneInfo> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock CacheLock = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public DateTimeOffset NowIn(string timeZoneId)
    {
        var zone = ResolveZone(timeZoneId);
        return TimeZoneInfo.ConvertTime(UtcNow, zone);
    }

    /// <summary>
    /// Resolves an IANA/Windows zone id, falling back to UTC when the id is unknown (a bad stored timezone
    /// must not take the whole request down — it degrades to UTC and is visible in the response).
    /// </summary>
    public static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(timeZoneId, out var cached))
            {
                return cached;
            }
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        lock (CacheLock)
        {
            Cache[timeZoneId] = zone;
        }

        return zone;
    }
}
