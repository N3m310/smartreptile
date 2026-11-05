using System.Net.WebSockets;
using System.Text;
using SmartReptile.Application.Camera;

namespace SmartReptile.Api.Camera;

/// <summary>
/// A signalling peer backed by a WebSocket. The room rules live in <see cref="CameraRoomRegistry"/> (Application);
/// this type is only the transport, which is what lets those rules be unit-tested without a socket.
/// </summary>
public sealed class CameraParticipant : ICameraPeer
{
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <inheritdoc />
    public required string ConnectionId { get; init; }

    /// <inheritdoc />
    public required CameraRole Role { get; init; }

    /// <summary>The socket itself.</summary>
    public required WebSocket Socket { get; init; }

    /// <inheritdoc />
    public async Task SendAsync(string payload, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Socket.State != WebSocketState.Open) return;
            await Socket.SendAsync(
                    Encoding.UTF8.GetBytes(payload),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }
}
