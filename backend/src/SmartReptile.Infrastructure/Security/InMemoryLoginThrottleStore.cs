using System.Collections.Concurrent;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Failed-login counters held in memory (BR-01.4).
/// </summary>
/// <remarks>
/// Correct for the single-host Compose deployment of ADR-012, and honest about what it is: counters are lost on
/// restart and not shared across replicas. That is acceptable here because the deployment is one process and a
/// restart already clears the in-flight lockout; moving to a shared store is required the moment the API is
/// scaled out or run behind a rolling deploy, and is recorded as a v1.1 item.
/// </remarks>
public sealed class InMemoryLoginThrottleStore : ILoginThrottleStore
{
    /// <summary>Entries older than this are dropped, so a long-running process cannot grow without bound.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _byIdentifier =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _byAddress =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public LoginFailureWindow GetWindow(string usernameOrEmail, string ipAddress, DateTimeOffset sinceUtc) =>
        new(
            CountSince(_byIdentifier, Normalise(usernameOrEmail), sinceUtc),
            CountSince(_byAddress, Normalise(ipAddress), sinceUtc));

    /// <inheritdoc />
    public void RecordFailure(string usernameOrEmail, string ipAddress, DateTimeOffset nowUtc)
    {
        Enqueue(_byIdentifier, Normalise(usernameOrEmail), nowUtc);
        Enqueue(_byAddress, Normalise(ipAddress), nowUtc);
    }

    /// <inheritdoc />
    public void ResetUsername(string usernameOrEmail) =>
        _byIdentifier.TryRemove(Normalise(usernameOrEmail), out _);

    private static string Normalise(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static void Enqueue(
        ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> store,
        string key,
        DateTimeOffset at)
    {
        var queue = store.GetOrAdd(key, _ => new ConcurrentQueue<DateTimeOffset>());
        queue.Enqueue(at);

        var cutoff = at - Retention;

        while (queue.TryPeek(out var oldest) && oldest < cutoff)
        {
            queue.TryDequeue(out _);
        }
    }

    private static int CountSince(
        ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> store,
        string key,
        DateTimeOffset sinceUtc)
    {
        if (!store.TryGetValue(key, out var queue))
        {
            return 0;
        }

        var count = 0;

        foreach (var at in queue)
        {
            if (at >= sinceUtc)
            {
                count++;
            }
        }

        return count;
    }
}
