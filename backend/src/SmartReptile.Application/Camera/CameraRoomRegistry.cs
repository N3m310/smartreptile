using System.Collections.Concurrent;

namespace SmartReptile.Application.Camera;

/// <summary>Which side of a camera link a socket is: the board/phone holding the lens, or somebody watching.</summary>
public enum CameraRole
{
    Camera,
    Viewer,
}

/// <summary>
/// One signalling peer. The transport is deliberately not part of this contract: the API implements it over a
/// WebSocket, and a test over a list, so the room rules can be exercised without a socket at all.
/// </summary>
public interface ICameraPeer
{
    /// <summary>Stable id the other side addresses its messages to.</summary>
    string ConnectionId { get; }

    /// <summary>Camera or viewer.</summary>
    CameraRole Role { get; }

    /// <summary>Sends one signalling message (already serialised).</summary>
    /// <param name="payload">Text frame to deliver.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string payload, CancellationToken cancellationToken);
}

/// <summary>A message the registry decided to deliver, resolved after the room lock was released.</summary>
/// <param name="To">Peer that should receive it.</param>
/// <param name="Payload">The message, forwarded as it arrived.</param>
public sealed record CameraOutbound(ICameraPeer To, object Payload);

/// <summary>What a join produced: either a refusal, or the messages the peers should be told.</summary>
/// <param name="Refusal">Machine-readable refusal code, or null when the join was accepted.</param>
/// <param name="Outbound">Messages to deliver once the room lock is released.</param>
public sealed record CameraJoinResult(string? Refusal, IReadOnlyList<CameraOutbound> Outbound);

/// <summary>
/// The signalling rooms behind the live camera view: one camera and any number of viewers per terrarium.
/// </summary>
/// <remarks>
/// Signalling only. The registry never touches media — it tells the camera a viewer arrived, hands the answer back and
/// passes ICE candidates both ways, and the two peers then talk directly. That is the property that keeps a video
/// stream out of the API process and out of the telemetry hub, which carries values, not pictures (FR-17 is about
/// pictures).
///
/// A room lives in memory and dies with the process: a demo's camera reconnects on its own, and a room that outlived
/// its sockets would only accumulate garbage.
///
/// The sender never chooses a destination socket, only which side of the room to reach: a viewer's message can only
/// reach this room's camera, and the camera's only reaches viewers actually in this room. Otherwise a client could
/// address any connection id it guessed and the room would relay into another tenant's call.
/// </remarks>
public sealed class CameraRoomRegistry
{
    private readonly ConcurrentDictionary<Guid, CameraRoom> _rooms = new();

    /// <summary>Registers a peer in a terrarium's room and returns what the peers should be told.</summary>
    /// <param name="terrariumId">Room key.</param>
    /// <param name="peer">The joining peer.</param>
    public CameraJoinResult Join(Guid terrariumId, ICameraPeer peer)
    {
        var room = _rooms.GetOrAdd(terrariumId, _ => new CameraRoom());
        var outbound = new List<CameraOutbound>();

        lock (room)
        {
            if (peer.Role == CameraRole.Camera)
            {
                if (room.Camera is { } existing && existing.ConnectionId != peer.ConnectionId)
                {
                    // One lens per enclosure: a second camera would leave viewers unable to say whose picture they see.
                    return new CameraJoinResult("camera_busy", []);
                }

                room.Camera = peer;
                foreach (var viewer in room.Viewers.Values)
                {
                    outbound.Add(new CameraOutbound(
                        viewer,
                        new { type = "camera-joined", cameraId = peer.ConnectionId }));
                    // A viewer that was already waiting never produced an offer, so the camera has to be told it is
                    // there — otherwise the room is joined but the call never starts.
                    outbound.Add(new CameraOutbound(
                        peer,
                        new { type = "viewer-joined", viewerId = viewer.ConnectionId }));
                }
            }
            else
            {
                room.Viewers[peer.ConnectionId] = peer;

                if (room.Camera is { } camera)
                {
                    outbound.Add(new CameraOutbound(
                        camera,
                        new { type = "viewer-joined", viewerId = peer.ConnectionId }));
                    outbound.Add(new CameraOutbound(
                        peer,
                        new { type = "camera-present", cameraId = camera.ConnectionId }));
                }
            }
        }

        return new CameraJoinResult(null, outbound);
    }

    /// <summary>Removes a peer and returns what its counterpart should be told.</summary>
    /// <param name="terrariumId">Room key.</param>
    /// <param name="peer">The peer that is going away.</param>
    public IReadOnlyList<CameraOutbound> Leave(Guid terrariumId, ICameraPeer peer)
    {
        if (!_rooms.TryGetValue(terrariumId, out var room)) return [];

        var outbound = new List<CameraOutbound>();

        lock (room)
        {
            if (peer.Role == CameraRole.Camera)
            {
                if (room.Camera?.ConnectionId != peer.ConnectionId) return [];

                room.Camera = null;
                foreach (var viewer in room.Viewers.Values)
                {
                    outbound.Add(new CameraOutbound(viewer, new { type = "camera-left" }));
                }
            }
            else
            {
                if (!room.Viewers.Remove(peer.ConnectionId)) return [];

                if (room.Camera is { } camera)
                {
                    outbound.Add(new CameraOutbound(
                        camera,
                        new { type = "viewer-left", viewerId = peer.ConnectionId }));
                }
            }

            if (room.Camera is null && room.Viewers.Count == 0) _rooms.TryRemove(terrariumId, out _);
        }

        return outbound;
    }

    /// <summary>Relays one signalling message from <paramref name="from"/> to the peer it names.</summary>
    /// <param name="terrariumId">Room key.</param>
    /// <param name="from">Sender.</param>
    /// <param name="payload">The message as received, forwarded verbatim.</param>
    /// <param name="target">For the camera: the viewer to reach, or null for every viewer in the room.</param>
    public IReadOnlyList<CameraOutbound> Relay(
        Guid terrariumId,
        ICameraPeer from,
        object payload,
        string? target)
    {
        if (!_rooms.TryGetValue(terrariumId, out var room)) return [];

        var outbound = new List<CameraOutbound>();

        lock (room)
        {
            if (from.Role == CameraRole.Camera)
            {
                if (target is not null && room.Viewers.TryGetValue(target, out var viewer))
                {
                    outbound.Add(new CameraOutbound(viewer, payload));
                }
                else
                {
                    foreach (var candidate in room.Viewers.Values)
                    {
                        outbound.Add(new CameraOutbound(candidate, payload));
                    }
                }
            }
            else if (room.Camera is { } camera)
            {
                outbound.Add(new CameraOutbound(camera, payload));
            }
        }

        return outbound;
    }

    /// <summary>How many peers are registered for a terrarium; used by tests.</summary>
    /// <param name="terrariumId">Room key.</param>
    public (bool HasCamera, int Viewers) Describe(Guid terrariumId)
    {
        if (!_rooms.TryGetValue(terrariumId, out var room)) return (false, 0);

        lock (room)
        {
            return (room.Camera is not null, room.Viewers.Count);
        }
    }

    private sealed class CameraRoom
    {
        public ICameraPeer? Camera { get; set; }

        public Dictionary<string, ICameraPeer> Viewers { get; } = new(StringComparer.Ordinal);
    }
}
