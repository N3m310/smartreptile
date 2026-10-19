using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Evaluation;

/// <summary>Orders committed telemetry per device and releases it only after the configured lateness window.</summary>
public sealed class EvaluationReorderBuffer(TimeSpan window)
{
    private readonly Dictionary<Guid, PriorityQueue<PersistedSample, SampleOrder>> _pending = [];
    private readonly Dictionary<Guid, SampleOrder> _lastReleased = [];
    private readonly HashSet<long> _pendingSampleIds = [];
    private int _lateSampleCount;

    public int PendingCount => _pending.Values.Sum(queue => queue.Count);

    public void Add(IEnumerable<PersistedSample> samples)
    {
        foreach (var sample in samples)
        {
            var order = SampleOrder.For(sample);

            if (_lastReleased.TryGetValue(sample.DeviceId, out var lastReleased) &&
                order.CompareTo(lastReleased) <= 0)
            {
                _lateSampleCount++;
                continue;
            }

            if (!_pendingSampleIds.Add(sample.SampleId))
            {
                continue;
            }

            if (!_pending.TryGetValue(sample.DeviceId, out var queue))
            {
                queue = new PriorityQueue<PersistedSample, SampleOrder>();
                _pending.Add(sample.DeviceId, queue);
            }

            queue.Enqueue(sample, order);
        }
    }

    public EvaluationReorderResult ReleaseReady(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc - window;
        var ready = new List<PersistedSample>();
        var late = _lateSampleCount;
        _lateSampleCount = 0;

        foreach (var (deviceId, queue) in _pending)
        {
            while (queue.TryPeek(out var sample, out var order) &&
                   sample.RecordedAt <= cutoff)
            {
                queue.Dequeue();
                _pendingSampleIds.Remove(sample.SampleId);

                if (_lastReleased.TryGetValue(deviceId, out var lastReleased) &&
                    order.CompareTo(lastReleased) <= 0)
                {
                    late++;
                    continue;
                }

                _lastReleased[deviceId] = order;
                ready.Add(sample);
            }
        }

        foreach (var deviceId in _pending.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToArray())
        {
            _pending.Remove(deviceId);
        }

        ready.Sort((left, right) =>
        {
            var deviceOrder = left.DeviceId.CompareTo(right.DeviceId);
            return deviceOrder != 0
                ? deviceOrder
                : SampleOrder.For(left).CompareTo(SampleOrder.For(right));
        });

        return new EvaluationReorderResult(ready, late);
    }

    private readonly record struct SampleOrder(long RecordedAtTicks, long Sequence, long SampleId)
        : IComparable<SampleOrder>
    {
        public static SampleOrder For(PersistedSample sample) =>
            new(sample.RecordedAt.UtcTicks, sample.Sequence, sample.SampleId);

        public int CompareTo(SampleOrder other)
        {
            var recordedAtOrder = RecordedAtTicks.CompareTo(other.RecordedAtTicks);
            if (recordedAtOrder != 0)
            {
                return recordedAtOrder;
            }

            var sequenceOrder = Sequence.CompareTo(other.Sequence);
            return sequenceOrder != 0 ? sequenceOrder : SampleId.CompareTo(other.SampleId);
        }
    }
}

public sealed record EvaluationReorderResult(
    IReadOnlyList<PersistedSample> ReadySamples,
    int LateSampleCount);
