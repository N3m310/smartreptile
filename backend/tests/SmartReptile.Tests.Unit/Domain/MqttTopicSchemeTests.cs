using FluentAssertions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The topic ACL of §07-appendices/03 §3.1. This is tenant isolation on the MQTT side: a device that publishes
/// under another device's topic would write into a terrarium it does not own, so every refusal is asserted here.
/// </summary>
public class MqttTopicSchemeTests
{
    private const string Prefix = MqttTopicScheme.DefaultPrefix;
    private const string Device = "sr-3f9a2c";

    [Theory]
    [InlineData("sr/v1/d/sr-3f9a2c/telemetry", "sr-3f9a2c", "telemetry")]
    [InlineData("sr/v1/d/sr-3f9a2c/health", "sr-3f9a2c", "health")]
    [InlineData("sr/v1/d/sr-3f9a2c/status", "sr-3f9a2c", "status")]
    [InlineData("sr/v1/d/sr-3f9a2c/cmd", "sr-3f9a2c", "cmd")]
    public void Parses_a_device_topic(string topic, string expectedDevice, string expectedChannel)
    {
        MqttTopicScheme.TryParse(topic, Prefix, out var parsed).Should().BeTrue();

        parsed.DeviceId.Should().Be(expectedDevice);
        parsed.Channel.Should().Be(expectedChannel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sr/v1/d/sr-3f9a2c")]
    [InlineData("sr/v1/d/sr-3f9a2c/telemetry/extra")]
    [InlineData("sr/v2/d/sr-3f9a2c/telemetry")]
    [InlineData("sr/v1/d//telemetry")]
    [InlineData("sr/v1/d/sr-3f9a2c/")]
    [InlineData("sr/v1/d/+/telemetry")]
    [InlineData("sr/v1/d/sr-3f9a2c/#")]
    [InlineData("other/v1/d/sr-3f9a2c/telemetry")]
    public void Refuses_anything_that_is_not_a_device_topic(string topic) =>
        MqttTopicScheme.TryParse(topic, Prefix, out _).Should().BeFalse();

    [Theory]
    [InlineData("telemetry")]
    [InlineData("health")]
    [InlineData("status")]
    [InlineData("events")]
    [InlineData("ack")]
    public void A_device_may_publish_on_its_own_upload_channels(string channel) =>
        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/{channel}", Prefix, Device, TopicDirection.Publish)
            .Allowed.Should().BeTrue();

    [Fact]
    public void A_device_may_not_publish_on_the_command_channel() =>
        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/cmd", Prefix, Device, TopicDirection.Publish)
            .Rejection.Should().Be(TopicRejection.UnknownChannel);

    [Fact]
    public void A_device_may_subscribe_only_to_its_own_command_channel()
    {
        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/cmd", Prefix, Device, TopicDirection.Subscribe)
            .Allowed.Should().BeTrue();

        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/telemetry", Prefix, Device, TopicDirection.Subscribe)
            .Rejection.Should().Be(TopicRejection.UnknownChannel);
    }

    [Fact]
    public void A_device_may_not_touch_another_devices_prefix()
    {
        MqttTopicScheme
            .Authorize("sr/v1/d/sr-aaaaaa/telemetry", Prefix, Device, TopicDirection.Publish)
            .Rejection.Should().Be(TopicRejection.ForeignDevice);

        MqttTopicScheme
            .Authorize("sr/v1/d/sr-aaaaaa/cmd", Prefix, Device, TopicDirection.Subscribe)
            .Rejection.Should().Be(TopicRejection.ForeignDevice);
    }

    [Theory]
    [InlineData("sr/v1/d/+/telemetry")]
    [InlineData("sr/v1/d/#")]
    [InlineData("sr/v1/d/sr-3f9a2c/telemetry/#")]
    public void A_wildcard_is_never_a_device_topic(string topic)
    {
        // The wildcard shapes are exactly what a curious client would try in order to read the whole fleet.
        MqttTopicScheme
            .Authorize(topic, Prefix, Device, TopicDirection.Subscribe)
            .Rejection.Should().Be(TopicRejection.Malformed);

        MqttTopicScheme
            .Authorize(topic, Prefix, Device, TopicDirection.Publish)
            .Rejection.Should().Be(TopicRejection.Malformed);
    }

    [Fact]
    public void An_unauthenticated_client_owns_no_topic() =>
        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/telemetry", Prefix, null, TopicDirection.Publish)
            .Rejection.Should().Be(TopicRejection.ForeignDevice);

    [Fact]
    public void The_device_id_is_matched_case_insensitively_but_the_channel_is_not()
    {
        // Ids are lower-case by construction, so accepting a differently cased copy cannot collide with a real
        // device; a channel is a literal from the scheme, so a wrong case is a wrong topic.
        MqttTopicScheme
            .Authorize("sr/v1/d/SR-3F9A2C/telemetry", Prefix, Device, TopicDirection.Publish)
            .Allowed.Should().BeTrue();

        MqttTopicScheme
            .Authorize($"sr/v1/d/{Device}/Telemetry", Prefix, Device, TopicDirection.Publish)
            .Rejection.Should().Be(TopicRejection.UnknownChannel);
    }

    [Fact]
    public void The_documented_prefix_is_pinned() =>
        MqttTopicScheme.DefaultPrefix.Should().Be("sr/v1/d");
}
