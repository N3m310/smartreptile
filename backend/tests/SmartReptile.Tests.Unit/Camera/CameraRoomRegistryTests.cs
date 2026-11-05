using SmartReptile.Application.Camera;

namespace SmartReptile.Tests.Unit.Camera;

/// <summary>
/// The room rules behind the live camera view: one lens per enclosure, every viewer reachable, and a sender that can
/// only reach the other side of its own room.
/// </summary>
public sealed class CameraRoomRegistryTests
{
    private static readonly Guid Terrarium = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTerrarium = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_viewer_that_joins_first_is_told_when_the_camera_arrives()
    {
        var rooms = new CameraRoomRegistry();
        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);

        Assert.Empty(rooms.Join(Terrarium, viewer).Outbound);

        var camera = new FakePeer("camera-1", CameraRole.Camera);
        var joined = rooms.Join(Terrarium, camera);

        Assert.Equal(2, joined.Outbound.Count);
        Assert.Contains(joined.Outbound, m => ReferenceEquals(m.To, viewer) && Kind(m.Payload) == "camera-joined");
        // The waiting viewer produced no offer, so the camera must be told about it or the call never starts.
        Assert.Contains(joined.Outbound, m => ReferenceEquals(m.To, camera) && Kind(m.Payload) == "viewer-joined");
    }

    [Fact]
    public void A_viewer_joining_a_live_room_tells_the_camera_and_answers_camera_present()
    {
        var rooms = new CameraRoomRegistry();
        var camera = new FakePeer("camera-1", CameraRole.Camera);
        rooms.Join(Terrarium, camera);

        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);
        var joined = rooms.Join(Terrarium, viewer);

        Assert.Equal(2, joined.Outbound.Count);
        Assert.Contains(joined.Outbound, m => ReferenceEquals(m.To, camera) && Kind(m.Payload) == "viewer-joined");
        Assert.Contains(joined.Outbound, m => ReferenceEquals(m.To, viewer) && Kind(m.Payload) == "camera-present");
    }

    [Fact]
    public void A_second_camera_is_refused_rather_than_replacing_the_first()
    {
        var rooms = new CameraRoomRegistry();
        rooms.Join(Terrarium, new FakePeer("camera-1", CameraRole.Camera));

        var second = rooms.Join(Terrarium, new FakePeer("camera-2", CameraRole.Camera));

        Assert.Equal("camera_busy", second.Refusal);
        Assert.Empty(second.Outbound);
        Assert.Equal((true, 0), rooms.Describe(Terrarium));
    }

    [Fact]
    public void Two_terrariums_do_not_share_a_room()
    {
        var rooms = new CameraRoomRegistry();
        rooms.Join(Terrarium, new FakePeer("camera-1", CameraRole.Camera));

        var other = rooms.Join(OtherTerrarium, new FakePeer("viewer-1", CameraRole.Viewer));

        Assert.Empty(other.Outbound);
    }

    [Fact]
    public void A_viewer_message_reaches_the_camera_of_its_own_room_only()
    {
        var rooms = new CameraRoomRegistry();
        var camera = new FakePeer("camera-1", CameraRole.Camera);
        var foreignCamera = new FakePeer("camera-9", CameraRole.Camera);
        rooms.Join(Terrarium, camera);
        rooms.Join(OtherTerrarium, foreignCamera);

        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);
        rooms.Join(Terrarium, viewer);

        var relayed = rooms.Relay(Terrarium, viewer, new { type = "answer", sdp = "x" }, target: null);

        Assert.Same(camera, Assert.Single(relayed).To);
    }

    [Fact]
    public void A_targeted_offer_reaches_one_viewer_and_an_untargeted_one_reaches_all()
    {
        var rooms = new CameraRoomRegistry();
        var camera = new FakePeer("camera-1", CameraRole.Camera);
        rooms.Join(Terrarium, camera);
        var first = new FakePeer("viewer-1", CameraRole.Viewer);
        var second = new FakePeer("viewer-2", CameraRole.Viewer);
        rooms.Join(Terrarium, first);
        rooms.Join(Terrarium, second);

        var targeted = rooms.Relay(Terrarium, camera, new { type = "offer" }, target: "viewer-2");
        var broadcast = rooms.Relay(Terrarium, camera, new { type = "offer" }, target: null);

        Assert.Same(second, Assert.Single(targeted).To);
        Assert.Equal(2, broadcast.Count);
    }

    [Fact]
    public void A_target_naming_a_peer_outside_the_room_falls_back_to_the_room()
    {
        var rooms = new CameraRoomRegistry();
        var camera = new FakePeer("camera-1", CameraRole.Camera);
        var foreign = new FakePeer("viewer-9", CameraRole.Viewer);
        rooms.Join(Terrarium, camera);
        rooms.Join(OtherTerrarium, foreign);
        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);
        rooms.Join(Terrarium, viewer);

        var relayed = rooms.Relay(Terrarium, camera, new { type = "offer" }, target: foreign.ConnectionId);

        Assert.Same(viewer, Assert.Single(relayed).To);
    }

    [Fact]
    public void Leaving_tells_the_counterpart()
    {
        var rooms = new CameraRoomRegistry();
        var camera = new FakePeer("camera-1", CameraRole.Camera);
        rooms.Join(Terrarium, camera);
        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);
        rooms.Join(Terrarium, viewer);

        var left = rooms.Leave(Terrarium, camera);

        Assert.Equal("camera-left", Kind(Assert.Single(left).Payload));
        Assert.Equal((false, 1), rooms.Describe(Terrarium));
    }

    [Fact]
    public void The_last_peer_leaving_removes_the_room()
    {
        var rooms = new CameraRoomRegistry();
        var viewer = new FakePeer("viewer-1", CameraRole.Viewer);
        rooms.Join(Terrarium, viewer);

        rooms.Leave(Terrarium, viewer);

        Assert.Equal((false, 0), rooms.Describe(Terrarium));
    }

    private static string Kind(object payload) =>
        payload.GetType().GetProperty("type")!.GetValue(payload) as string ?? string.Empty;

    private sealed class FakePeer(string connectionId, CameraRole role) : ICameraPeer
    {
        public string ConnectionId { get; } = connectionId;

        public CameraRole Role { get; } = role;

        public List<string> Sent { get; } = [];

        public Task SendAsync(string payload, CancellationToken cancellationToken)
        {
            Sent.Add(payload);
            return Task.CompletedTask;
        }
    }
}
