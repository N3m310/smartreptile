using System.Threading.Channels;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// The in-process hop between the MQTT broker and the ingest worker: the broker's publish interceptor writes,
/// the worker reads.
/// </summary>
/// <remarks>
/// The broker is hosted in the API process (ADR-012), so no socket is needed between the two halves. That makes
/// the "stop acking so the broker holds the messages" rule of §02-design/03 §1 mean something slightly different
/// here, and the difference is worth naming: MQTTnet sends the QoS 1 PUBACK only after
/// <see cref="PublishAsync"/> returns, so <b>awaiting this write is the delay</b>. A full channel therefore
/// withholds the acknowledgement and the device retries, which is exactly the intended back-pressure — nothing is
/// dropped to keep the queue short. The wait is deliberately unbounded: a timeout would have to choose between
/// losing the batch and failing the broker, and "the device retries" is already the correct answer.
/// <para>
/// The capacity is the 2 000 batches of the design table: at one batch per minute per node that is well over a
/// day of traffic from a large fleet, so reaching it means the database is down rather than that the fleet grew.
/// </para>
/// </remarks>
public sealed class InProcessTelemetryBus
{
    /// <summary>Batches that may wait before the broker stops acknowledging (design capacity).</summary>
    public const int Capacity = 2_000;

    private readonly Channel<TelemetryEnvelope> _channel = Channel.CreateBounded<TelemetryEnvelope>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly ILogger<InProcessTelemetryBus> _logger;

    /// <summary>Creates the bus.</summary>
    public InProcessTelemetryBus(ILogger<InProcessTelemetryBus> logger) => _logger = logger;

    /// <summary>Batches waiting to be ingested — exposed so the backlog is observable rather than invisible.</summary>
    public int Backlog => _channel.Reader.Count;

    /// <summary>
    /// Hands one message to the ingest worker. Awaited by the broker's publish interceptor, so the caller's
    /// acknowledgement is deliberately tied to this call completing.
    /// </summary>
    public ValueTask PublishAsync(TelemetryEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_channel.Writer.TryWrite(envelope))
        {
            return ValueTask.CompletedTask;
        }

        _logger.LogWarning(
            "Ingest backlog is at capacity ({Capacity}); holding MQTT acknowledgements until it drains",
            Capacity);

        return _channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    /// <summary>Streams messages to the single reader (the ingest worker).</summary>
    public IAsyncEnumerable<TelemetryEnvelope> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
