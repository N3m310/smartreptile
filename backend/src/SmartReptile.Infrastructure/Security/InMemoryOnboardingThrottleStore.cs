using System.Collections.Concurrent;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Self-registration attempt counters held in memory.
/// </summary>
/// <remarks>
/// The same trade-off as <see cref="InMemoryLoginThrottleStore"/>, for the same deployment (ADR-012): correct for
/// one process, lost on restart, not shared across replicas. The global counter is the one that would matter under
/// a distributed flood, so it is the first thing to move to a shared store if the API is ever scaled out.
/// </remarks>
public sealed class InMemoryOnboardingThrottleStore : IOnboardingThrottleStore
{
    /// <summary>Entries older than the longest window are dropped, so the process cannot grow without bound.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _byAddress =
        new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<DateTimeOffset> _global = new();

    /// <inheritdoc />
    public OnboardingAttemptWindow GetWindow(
        string ipAddress,
        DateTimeOffset sinceIpUtc,
        DateTimeOffset sinceGlobalUtc) =>
        new(
            CountSince(_byAddress.TryGetValue(Normalise(ipAddress), out var queue) ? queue : null, sinceIpUtc),
            CountSince(_global, sinceGlobalUtc));

    /// <inheritdoc />
    public void RecordAttempt(string ipAddress, DateTimeOffset nowUtc)
    {
        var queue = _byAddress.GetOrAdd(Normalise(ipAddress), _ => new ConcurrentQueue<DateTimeOffset>());

        queue.Enqueue(nowUtc);
        _global.Enqueue(nowUtc);

        Prune(queue, nowUtc);
        Prune(_global, nowUtc);
    }

    private static string Normalise(string? value) => (value ?? string.Empty).Trim();

    private static void Prune(ConcurrentQueue<DateTimeOffset> queue, DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - Retention;

        while (queue.TryPeek(out var oldest) && oldest < cutoff)
        {
            queue.TryDequeue(out _);
        }
    }

    private static int CountSince(ConcurrentQueue<DateTimeOffset>? queue, DateTimeOffset sinceUtc)
    {
        if (queue is null)
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
