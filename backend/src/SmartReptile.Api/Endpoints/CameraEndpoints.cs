using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using SmartReptile.Api.Camera;
using SmartReptile.Api.Security;
using SmartReptile.Application.Camera;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Api.Endpoints;

/// <summary>
/// WebSocket signalling for the live camera view (FR-17's optional camera, used here as a live feed).
/// </summary>
/// <remarks>
/// A raw WebSocket rather than a second SignalR hub: the only traffic is offer/answer/ICE relay between exactly two
/// kinds of peer, and every client that can open a socket can speak this protocol — including the Dart app, which
/// already holds an access token for REST.
///
/// The endpoint is a relay, never a media path. It does not decode, store or forward video: it tells the camera a
/// viewer arrived, hands the viewer's answer back, and passes ICE candidates both ways. The video itself goes
/// peer-to-peer, which is why nothing in the API's request pipeline or its bandwidth budget grows with the stream.
/// </remarks>
public static class CameraEndpoints
{
    /// <summary>Largest signalling frame accepted; an SDP with many candidates is a few kilobytes.</summary>
    private const int MaxMessageBytes = 64 * 1024;

    /// <summary>Maps <c>/ws/camera/{terrariumId}</c>.</summary>
    /// <param name="app">Application under construction.</param>
    public static IEndpointRouteBuilder MapCameraEndpoints(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/camera/{terrariumId:guid}", HandleAsync)
            .WithTags("camera")
            .WithSummary("Live camera signalling")
            .WithDescription(
                "WebSocket. `?role=camera` publishes this device's lens, `?role=viewer` (the default) watches. The "
                + "socket carries only WebRTC offer/answer/ICE messages; the video itself is peer-to-peer. The caller "
                + "must own the terrarium (or hold `data.read.any`), and membership is re-checked here because a "
                + "browser cannot put an Authorization header on the handshake — the token travels in `access_token`.")
            .RequireAuthorization();

        return app;
    }

    private static async Task HandleAsync(
        Guid terrariumId,
        HttpContext context,
        TerrariumService terrariums,
        CameraRoomRegistry rooms,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("SmartReptile.Api.CameraSignaling");

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = "upgrade_required" });
            return;
        }

        var userId = context.User.GetUserId();
        if (userId is null)
        {
            // [RequireAuthorization] already refused an anonymous handshake; this keeps the safe direction explicit.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var mayReachAnyAccount = context.User.HasPermission(Permissions.DataReadAny);
        if (!await terrariums.IsMemberAsync(terrariumId, userId.Value, context.RequestAborted, mayReachAnyAccount))
        {
            // Same answer for "not yours" and "does not exist" (BR-02.2): a socket that told them apart could be
            // used to probe for ids.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { code = "not_found" });
            return;
        }

        var role = IsCamera(context.Request.Query["role"]) ? CameraRole.Camera : CameraRole.Viewer;

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var participant = new CameraParticipant
        {
            ConnectionId = Guid.NewGuid().ToString("N")[..12],
            Role = role,
            Socket = socket,
        };

        var joined = rooms.Join(terrariumId, participant);
        if (joined.Refusal is { } refusal)
        {
            await participant.SendAsync(Json(new { type = "error", code = refusal }), context.RequestAborted);
            await socket.CloseOutputAsync(
                WebSocketCloseStatus.PolicyViolation,
                "camera_busy",
                context.RequestAborted);
            return;
        }

        logger.LogInformation(
            "Camera {Role} {ConnectionId} joined terrarium {TerrariumId}",
            role,
            participant.ConnectionId,
            terrariumId);

        await participant.SendAsync(
            Json(new { type = "welcome", role = role == CameraRole.Camera ? "camera" : "viewer", peerId = participant.ConnectionId }),
            context.RequestAborted);
        await DeliverAsync(joined.Outbound, context.RequestAborted);

        try
        {
            await PumpAsync(terrariumId, participant, rooms, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            // The client went away; the finally block still removes it from the room.
        }
        finally
        {
            await DeliverAsync(rooms.Leave(terrariumId, participant), CancellationToken.None);
            logger.LogInformation(
                "Camera {Role} {ConnectionId} left terrarium {TerrariumId}",
                role,
                participant.ConnectionId,
                terrariumId);
        }
    }

    private static async Task PumpAsync(
        Guid terrariumId,
        CameraParticipant participant,
        CameraRoomRegistry rooms,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxMessageBytes];
        var message = new MemoryStream();

        while (participant.Socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await participant.Socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await participant.Socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    null,
                    cancellationToken);
                return;
            }

            message.Write(buffer, 0, result.Count);

            if (!result.EndOfMessage)
            {
                // A frame larger than the buffer is drained rather than accepted: signalling payloads are small.
                if (message.Length > MaxMessageBytes)
                {
                    message.SetLength(0);
                    await DrainAsync(participant.Socket, cancellationToken);
                }

                continue;
            }

            var raw = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);

            await HandleMessageAsync(terrariumId, participant, rooms, raw, cancellationToken);
        }
    }

    private static async Task HandleMessageAsync(
        Guid terrariumId,
        CameraParticipant participant,
        CameraRoomRegistry rooms,
        string raw,
        CancellationToken cancellationToken)
    {
        JsonObject? envelope;
        try
        {
            envelope = JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            await participant.SendAsync(Json(new { type = "error", code = "malformed" }), cancellationToken);
            return;
        }

        if (envelope is null)
        {
            await participant.SendAsync(Json(new { type = "error", code = "malformed" }), cancellationToken);
            return;
        }

        var type = envelope["type"]?.GetValue<string>() ?? string.Empty;

        if (type == "ping")
        {
            await participant.SendAsync(Json(new { type = "pong" }), cancellationToken);
            return;
        }

        if (type is not ("offer" or "answer" or "ice" or "bye"))
        {
            await participant.SendAsync(Json(new { type = "error", code = "unknown_type" }), cancellationToken);
            return;
        }

        // Stamp the sender so the peer can pick the right RTCPeerConnection out of several viewers, and so a viewer
        // knows which camera an offer came from. The client never supplies these itself.
        if (participant.Role == CameraRole.Camera)
        {
            envelope["cameraId"] = participant.ConnectionId;
        }
        else
        {
            envelope["viewerId"] = participant.ConnectionId;
        }

        var target = envelope["target"]?.GetValue<string>();
        envelope.Remove("target");

        await DeliverAsync(
            rooms.Relay(terrariumId, participant, envelope, target),
            cancellationToken);

        if (type == "bye" && participant.Role == CameraRole.Viewer)
        {
            // The viewer keeps its socket: closing the peer connection is not leaving the screen.
            return;
        }
    }

    private static async Task DeliverAsync(
        IReadOnlyList<CameraOutbound> outbound,
        CancellationToken cancellationToken)
    {
        foreach (var delivery in outbound)
        {
            await delivery.To.SendAsync(Json(delivery.Payload), cancellationToken);
        }
    }

    private static async Task DrainAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.EndOfMessage) return;
        }
    }

    private static bool IsCamera(string? role) =>
        string.Equals(role, "camera", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "publisher", StringComparison.OrdinalIgnoreCase);

    private static string Json(object payload) => JsonSerializer.Serialize(payload);
}
