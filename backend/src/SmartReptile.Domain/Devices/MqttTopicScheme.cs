namespace SmartReptile.Domain.Devices;

/// <summary>The direction a topic is being used in, because the two directions allow different channels.</summary>
public enum TopicDirection
{
    /// <summary>Device → server: telemetry, health, status, events, ack.</summary>
    Publish,

    /// <summary>Server → device: command, and nothing else.</summary>
    Subscribe,
}

/// <summary>Why a topic was refused. Kept as an enum so the broker can count refusals without parsing text.</summary>
public enum TopicRejection
{
    /// <summary>Allowed.</summary>
    None = 0,

    /// <summary>Not a device topic at all, has the wrong number of segments, or uses a wildcard.</summary>
    Malformed,

    /// <summary>Well-formed, but the channel is not one a device may use in this direction.</summary>
    UnknownChannel,

    /// <summary>Well-formed, but it belongs to a different device than the authenticated one.</summary>
    ForeignDevice,
}

/// <summary>The result of an ACL question, with the reason a refusal happened.</summary>
public readonly record struct TopicDecision(bool Allowed, TopicRejection Rejection)
{
    /// <summary>Allow.</summary>
    public static TopicDecision Permit() => new(true, TopicRejection.None);

    /// <summary>Refuse, with a reason.</summary>
    public static TopicDecision Refuse(TopicRejection rejection) => new(false, rejection);
}

/// <summary>A parsed device topic: which device it names and which channel it uses.</summary>
public readonly record struct DeviceTopic(string DeviceId, string Channel);

/// <summary>
/// The topic scheme and the per-device ACL of §07-appendices/03 §3.1. Pure, so the rule "a device may only touch
/// its own prefix" has one implementation and one test rather than living inside the broker's event handlers.
/// </summary>
/// <remarks>
/// The broker is the last line of defence for tenant isolation on the MQTT side: a device that publishes under
/// another device's topic would write into a terrarium it does not own, so the topic is checked on the way in
/// rather than the message being trusted and discarded later.
/// </remarks>
public static class MqttTopicScheme
{
    /// <summary>Topic prefix every device topic starts with.</summary>
    public const string DefaultPrefix = "sr/v1/d";

    private static readonly string[] UploadChannels = ["telemetry", "health", "status", "events", "ack"];

    private static readonly string[] DownloadChannels = ["cmd"];

    /// <summary>
    /// Splits <c>sr/v1/d/{deviceId}/{channel}</c> into its parts. A wildcard (<c>+</c> or <c>#</c>), an empty
    /// segment or a different depth is not a device topic and is rejected as malformed.
    /// </summary>
    public static bool TryParse(string? topic, string prefix, out DeviceTopic parsed)
    {
        parsed = default;

        if (string.IsNullOrEmpty(topic) || string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        var segments = topic.Split('/');
        var prefixSegments = prefix.Split('/');

        // {prefix}/{deviceId}/{channel} — the prefix may itself contain slashes.
        var prefixDepth = prefixSegments.Length;

        if (segments.Length != prefixDepth + 2)
        {
            return false;
        }

        for (var i = 0; i < prefixDepth; i++)
        {
            if (!string.Equals(segments[i], prefixSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        var deviceId = segments[prefixDepth];
        var channel = segments[prefixDepth + 1];

        if (deviceId.Length == 0 || channel.Length == 0
            || deviceId.Contains('+', StringComparison.Ordinal)
            || deviceId.Contains('#', StringComparison.Ordinal)
            || channel.Contains('+', StringComparison.Ordinal)
            || channel.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        parsed = new DeviceTopic(deviceId, channel);
        return true;
    }

    /// <summary>True when the channel is one a device may use. Case-sensitive: the scheme is lower-case.</summary>
    public static bool IsUploadChannel(string channel) => UploadChannels.Contains(channel, StringComparer.Ordinal);

    /// <summary>True when the channel is server → device.</summary>
    public static bool IsDownloadChannel(string channel) => DownloadChannels.Contains(channel, StringComparer.Ordinal);

    /// <summary>
    /// The ACL question: may <paramref name="devicePublicId"/> use <paramref name="topic"/> in
    /// <paramref name="direction"/>?
    /// </summary>
    public static TopicDecision Authorize(string? topic, string prefix, string? devicePublicId, TopicDirection direction)
    {
        if (string.IsNullOrEmpty(devicePublicId))
        {
            return TopicDecision.Refuse(TopicRejection.ForeignDevice);
        }

        if (!TryParse(topic, prefix, out var parsed))
        {
            return TopicDecision.Refuse(TopicRejection.Malformed);
        }

        if (!string.Equals(parsed.DeviceId, devicePublicId, StringComparison.OrdinalIgnoreCase))
        {
            return TopicDecision.Refuse(TopicRejection.ForeignDevice);
        }

        var known = direction == TopicDirection.Publish
            ? IsUploadChannel(parsed.Channel)
            : IsDownloadChannel(parsed.Channel);

        return known ? TopicDecision.Permit() : TopicDecision.Refuse(TopicRejection.UnknownChannel);
    }
}
