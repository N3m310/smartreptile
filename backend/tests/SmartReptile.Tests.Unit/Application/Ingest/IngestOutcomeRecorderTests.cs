using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;
using SmartReptile.Infrastructure.Mqtt;
using SmartReptile.Infrastructure.Observability;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// The counter and fan-out contract of the ingest worker (FR-18, TC-I-01's <c>ingest_samples_total</c> and
/// TC-I-02's duplicate counter), asserted without a background loop around it.
/// </summary>
public class IngestOutcomeRecorderTests
{
    private readonly SmartReptileMetrics _metrics = new();
    private readonly MqttBrokerStatus _broker = new();
    private readonly RecordingBroadcaster _broadcaster = new();
    private readonly RecordingEvaluationQueue _queue = new();
    private readonly IngestOutcomeRecorder _recorder;

    public IngestOutcomeRecorderTests() =>
        _recorder = new IngestOutcomeRecorder(
            _metrics,
            _broadcaster,
            _queue,
            NullLogger<IngestOutcomeRecorder>.Instance);

    private long Counter(string name) => _metrics.Snapshot(_broker)[name];

    private static PersistedSample Stored(long id) => new(
        id,
        Guid.NewGuid(),
        Guid.NewGuid(),
        IngestTestData.Now,
        id,
        QualityFlags.None,
        [new TelemetryReading(MetricCode.TempC, 28.75m, null)]);

    [Fact]
    [Trait("TestCase", "TC-I-01")]
    public async Task GivenAPersistedBatch_ThenSamplesAreCountedAndBothConsumersReceiveIt()
    {
        var stored = new List<PersistedSample> { Stored(1), Stored(2), Stored(3) };

        await _recorder.RecordAsync(
            IngestOutcome.Accepted(stored, duplicates: 0),
            "sr-3f9a2c",
            CancellationToken.None);

        Counter("ingest_samples_total").Should().Be(3);
        Counter("ingest_duplicates_total").Should().Be(0);
        Counter("ingest_rejected_total").Should().Be(0);

        _broadcaster.Received.Should().BeEquivalentTo(stored);
        _queue.Received.Should().BeEquivalentTo(stored);
    }

    [Fact]
    [Trait("TestCase", "TC-I-02")]
    public async Task GivenARedeliveredBatch_ThenOnlyTheDuplicateCounterMovesAndNothingIsFannedOut()
    {
        await _recorder.RecordAsync(
            IngestOutcome.Accepted([], duplicates: 4),
            "sr-3f9a2c",
            CancellationToken.None);

        Counter("ingest_duplicates_total").Should().Be(4);
        Counter("ingest_samples_total").Should().Be(0);

        _broadcaster.Received.Should().BeEmpty("a duplicate is not news for a connected client");
        _queue.Received.Should().BeEmpty("re-evaluating a sample the evaluator already saw would double-count dwell");
    }

    [Fact]
    public async Task GivenARefusedBatch_ThenItIsCountedUnderItsCodeAndNothingIsFannedOut()
    {
        await _recorder.RecordAsync(
            IngestOutcome.Rejected("schema_invalid", "no sequence"),
            "sr-3f9a2c",
            CancellationToken.None);

        Counter("ingest_rejected_total").Should().Be(1);
        Counter("ingest_samples_total").Should().Be(0);
        _broadcaster.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task GivenABroadcasterThatThrows_ThenTheEvaluatorIsStillHandedTheSamples()
    {
        // Fan-out is after the commit: a SignalR outage is a lost push, not a lost measurement (§02-design/03 §1).
        _broadcaster.Failure = new InvalidOperationException("hub unreachable");
        var stored = new List<PersistedSample> { Stored(7) };

        var act = async () => await _recorder.RecordAsync(
            IngestOutcome.Accepted(stored, duplicates: 0),
            "sr-3f9a2c",
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        _queue.Received.Should().BeEquivalentTo(stored);
        Counter("ingest_samples_total").Should().Be(1, "the sample is stored; the push is what failed");
    }

    [Fact]
    public async Task GivenAnEvaluationQueueThatThrows_ThenTheBatchIsStillCountedAsStored()
    {
        _queue.Failure = new InvalidOperationException("queue closed");
        var stored = new List<PersistedSample> { Stored(8) };

        var act = async () => await _recorder.RecordAsync(
            IngestOutcome.Accepted(stored, duplicates: 0),
            "sr-3f9a2c",
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        _broadcaster.Received.Should().BeEquivalentTo(stored);
        Counter("ingest_samples_total").Should().Be(1);
    }

    private sealed class RecordingBroadcaster : ITelemetryBroadcaster
    {
        public List<PersistedSample> Received { get; } = [];

        public Exception? Failure { get; set; }

        public Task BroadcastAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Received.AddRange(samples);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEvaluationQueue : IEvaluationQueue
    {
        public List<PersistedSample> Received { get; } = [];

        public Exception? Failure { get; set; }

        public Task EnqueueAsync(IReadOnlyList<PersistedSample> samples, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Received.AddRange(samples);
            return Task.CompletedTask;
        }
    }
}
