using System.Text.Json;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// Turns the three non-telemetry device payloads (`07-appendices/03` §3.1/§3.3/§3.4) into a
/// <see cref="DeviceMessage"/>.
/// </summary>
/// <remarks>
/// Tolerant in the same way as <see cref="JsonTelemetryPayloadParser"/>: an absent or unusable <c>at</c> becomes a
/// null the pipeline replaces with the arrival time rather than a parse failure, because a node with an unsynced
/// clock still has to be able to report. Two things are <b>not</b> tolerated, because they are the payload's whole
/// purpose: a status that is not one of the three documented words, and an event <c>type</c> outside the closed
/// vocabulary. Both are refused as <c>schema_invalid</c> so a firmware typo is visible in
/// <c>ingest_rejected_total</c> instead of quietly becoming a new category.
/// </remarks>
public sealed class JsonDeviceChannelParser : IDeviceChannelParser
{
    /// <inheritdoc />
    public DeviceMessageParseResult Parse(ReadOnlyMemory<byte> payload, DeviceChannel channel)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return DeviceMessageParseResult.Rejected("schema_invalid", $"The payload is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return DeviceMessageParseResult.Rejected("schema_invalid", "The payload is not a JSON object.");
            }

            var deviceId = ReadString(root, "deviceId");
            var recordedAt = ReadTimestamp(root, "at") ?? ReadTimestamp(root, "ts");
            var firmwareVersion = ReadString(root, "fw");

            return channel switch
            {
                DeviceChannel.Status => ParseStatus(root, deviceId, recordedAt, firmwareVersion),
                DeviceChannel.Health => ParseHealth(root, deviceId, recordedAt, firmwareVersion),
                DeviceChannel.Events => ParseEvent(root, deviceId, recordedAt, firmwareVersion),
                _ => DeviceMessageParseResult.Rejected(
                    "schema_invalid",
                    $"channel '{channel}' does not carry device messages"),
            };
        }
    }

    private static DeviceMessageParseResult ParseStatus(
        JsonElement root,
        string? deviceId,
        DateTimeOffset? recordedAt,
        string? firmwareVersion)
    {
        var status = ReadString(root, "status")?.Trim().ToLowerInvariant() switch
        {
            "online" => DeviceStatus.Online,
            "offline" => DeviceStatus.Offline,
            "maintenance" => DeviceStatus.Maintenance,
            _ => (DeviceStatus?)null,
        };

        if (status is null)
        {
            return DeviceMessageParseResult.Rejected(
                "schema_invalid",
                "A status message must carry status = online, offline or maintenance.");
        }

        return DeviceMessageParseResult.Parsed(new DeviceMessage(
            DeviceChannel.Status,
            deviceId,
            recordedAt,
            firmwareVersion,
            status,
            null,
            null));
    }

    private static DeviceMessageParseResult ParseHealth(
        JsonElement root,
        string? deviceId,
        DateTimeOffset? recordedAt,
        string? firmwareVersion) =>
        DeviceMessageParseResult.Parsed(new DeviceMessage(
            DeviceChannel.Health,
            deviceId,
            recordedAt,
            firmwareVersion,
            null,
            new DeviceHealthDocument(
                ReadInt32(root, "rssi"),
                ReadInt64(root, "up_s"),
                ReadInt32(root, "heap_kb"),
                ReadDecimal(root, "bat"),
                ReadString(root, "src")),
            null));

    private static DeviceMessageParseResult ParseEvent(
        JsonElement root,
        string? deviceId,
        DateTimeOffset? recordedAt,
        string? firmwareVersion)
    {
        var rawType = ReadString(root, "type")?.Trim();

        var type = rawType switch
        {
            "boot" => DeviceEventType.Boot,
            "sensor_fault" => DeviceEventType.SensorFault,
            "sensor_recovered" => DeviceEventType.SensorRecovered,
            "buffer_overflow" => DeviceEventType.BufferOverflow,
            "clock_unsynced" => DeviceEventType.ClockUnsynced,
            "calibrated" => DeviceEventType.Calibrated,
            _ => (DeviceEventType?)null,
        };

        if (type is null)
        {
            return DeviceMessageParseResult.Rejected(
                "schema_invalid",
                $"'{rawType}' is not a known event type.");
        }

        MetricCode? metric = null;

        if (ReadString(root, "metric") is { Length: > 0 } metricKey
            && MetricDictionary.TryParseApiKey(metricKey, out var parsedMetric))
        {
            metric = parsedMetric;
        }

        return DeviceMessageParseResult.Parsed(new DeviceMessage(
            DeviceChannel.Events,
            deviceId,
            recordedAt,
            firmwareVersion,
            null,
            null,
            // The event's own fields are kept whole: the payloads differ per type, and a sparse wide table would
            // turn every new firmware field into a migration. Task 3.3 reads what it needs out of here.
            new DeviceEventDraft(type.Value, metric, ReadInt32(root, "consecutiveFailures"), root.GetRawText())));
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static long? ReadInt64(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static decimal? ReadDecimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDecimal(out var parsed)
            ? parsed
            : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string name) =>
        ReadString(root, name) is { } text && DateTimeOffset.TryParse(text, out var parsed)
            ? parsed
            : null;
}
