using FluentAssertions;
using SmartReptile.Infrastructure.Mqtt;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>
/// The session registry behind BR-05.4 ("a revoked device is disconnected"): it has to answer who is connected,
/// survive a device holding two connections at once, and never throw when the broker is not running.
/// </summary>
public class DeviceSessionRegistryTests
{
    private const string Device = "sr-3f9a2c";

    [Fact]
    public void Knows_nothing_before_a_connection()
    {
        var registry = new DeviceSessionRegistry();

        registry.ConnectedDevices.Should().Be(0);
        registry.IsConnected(Device).Should().BeFalse();
    }

    [Fact]
    public void Tracks_a_device_and_its_client_id()
    {
        var registry = new DeviceSessionRegistry();

        registry.Connect(Device, "client-1").Should().Be(1);

        registry.IsConnected(Device).Should().BeTrue();
        registry.ConnectedDevices.Should().Be(1);
        registry.DeviceOf("client-1").Should().Be(Device);
        registry.DeviceOf("someone-else").Should().BeNull();
    }

    [Fact]
    public void Counts_two_live_sessions_of_one_device()
    {
        // A reconnect racing the previous connection's LWT is the case that makes this a set, not a flag.
        var registry = new DeviceSessionRegistry();

        registry.Connect(Device, "client-1").Should().Be(1);
        registry.Connect(Device, "client-2").Should().Be(2);

        registry.ConnectedDevices.Should().Be(1);
        registry.DeviceOf("client-2").Should().Be(Device);
    }

    [Fact]
    public void Forget_a_client_without_forgetting_the_other()
    {
        var registry = new DeviceSessionRegistry();
        registry.Connect(Device, "client-1");
        registry.Connect(Device, "client-2");

        registry.Disconnect(Device, "client-1");

        registry.IsConnected(Device).Should().BeTrue();
        registry.DeviceOf("client-1").Should().BeNull();
        registry.DeviceOf("client-2").Should().Be(Device);

        registry.Disconnect(Device, "client-2");

        registry.IsConnected(Device).Should().BeFalse();
        registry.ConnectedDevices.Should().Be(0);
        registry.DeviceOf("client-2").Should().BeNull();
    }

    [Fact]
    public void Forgetting_an_unknown_client_is_harmless()
    {
        var registry = new DeviceSessionRegistry();

        registry.Disconnect("sr-unknown", "client-1");

        registry.ConnectedDevices.Should().Be(0);
    }

    [Fact]
    public async Task Kick_closes_every_session_the_device_holds()
    {
        var registry = new DeviceSessionRegistry();
        var closed = new List<string>();

        registry.UseDisconnect((deviceId, clientId, _) =>
        {
            deviceId.Should().Be(Device);
            closed.Add(clientId);
            return Task.CompletedTask;
        });

        registry.Connect(Device, "client-1");
        registry.Connect(Device, "client-2");

        var count = await registry.KickAsync(Device, "device_revoked");

        count.Should().Be(2);
        closed.Should().BeEquivalentTo(["client-1", "client-2"]);
        registry.IsConnected(Device).Should().BeFalse("a kicked device must not linger in the registry");
    }

    [Fact]
    public async Task Kick_reports_nothing_when_the_device_has_no_session()
    {
        var registry = new DeviceSessionRegistry();
        registry.UseDisconnect((_, _, _) => throw new InvalidOperationException("must not be called"));

        (await registry.KickAsync("sr-unknown", "device_revoked")).Should().Be(0);
    }

    [Fact]
    public async Task Kick_reports_nothing_when_the_broker_never_started()
    {
        // No disconnect delegate: the broker could not start (NFR-03 degraded mode), and revoking still succeeds.
        var registry = new DeviceSessionRegistry();
        registry.Connect(Device, "client-1");

        (await registry.KickAsync(Device, "device_revoked")).Should().Be(0);
    }

    [Fact]
    public async Task A_disconnect_that_throws_does_not_stop_the_others_or_the_revoke()
    {
        var registry = new DeviceSessionRegistry();
        var attempts = 0;

        registry.UseDisconnect((_, _, _) =>
        {
            attempts++;
            return attempts == 1 ? Task.FromException(new InvalidOperationException("socket gone")) : Task.CompletedTask;
        });

        registry.Connect(Device, "client-1");
        registry.Connect(Device, "client-2");

        var count = await registry.KickAsync(Device, "device_revoked");

        attempts.Should().Be(2);
        count.Should().Be(1, "the session that failed to close is not counted as closed");
        registry.IsConnected(Device).Should().BeFalse("the credential is what stops the device, not the socket");
    }
}
