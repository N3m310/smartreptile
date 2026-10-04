using System.Text.Json;
using SmartReptile.Application.Ingest;

namespace SmartReptile.Infrastructure.Ingest;

/// <summary>
/// Turns a telemetry payload (§07-appendices/03 §3.2) into a <see cref="TelemetryPayloadDocument"/>.
/// </summary>
/// <remarks>
/// Parsing is deliberately <b>tolerant</b>, and that is the design decision worth stating: this adapter reads the
/// payload structure and nothing else. A missing <c>seq</c>, a non-numeric <c>tf</c> or a <c>ts</c> of
/// <c>"yesterday"</c> is not an exception here — it becomes a null, an entry in <c>InvalidKeys</c>, or a string
/// the validator then judges. The alternative, binding straight onto a strongly typed model, would answer a
/// malformed batch with a deserialisation error and lose the field that explains what the firmware got wrong.
/// Only bytes that are not a JSON object at all stop here.
/// </remarks>
public sealed class JsonTelemetryPayloadParser : ITelemetryPayloadParser
{
    /// <inheritdoc />
    public PayloadParseResult Parse(ReadOnlyMemory<byte> payload, TelemetryValidationLimits limits)
    {
        var maxBytes = limits.MaxPayloadKb * 1024L;

        if (payload.Length > maxBytes)
        {
            return PayloadParseResult.Rejected(
                "payload_too_large",
                $"The payload is {payload.Length} bytes; the limit is {maxBytes} bytes.");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            return PayloadParseResult.Rejected("schema_invalid", $"The payload is not valid JSON: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return PayloadParseResult.Rejected("schema_invalid", "The payload is not a JSON object.");
            }

            return PayloadParseResult.Parsed(new TelemetryPayloadDocument(
                ReadString(root, "deviceId"),
                ReadInt64(root, "seq"),
                ReadString(root, "fw"),
                ReadString(root, "ts"),
                ReadSamples(root),
                ReadHealth(root)));
        }
    }

    private static IReadOnlyList<TelemetrySampleDocument>? ReadSamples(JsonElement root)
    {
        if (!root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parsed = new List<TelemetrySampleDocument>(samples.GetArrayLength());

        foreach (var sample in samples.EnumerateArray())
        {
            parsed.Add(ReadSample(sample));
        }

        return parsed;
    }

    /// <summary>
    /// Reads one sample. Every element that is not <c>t</c>, <c>q</c> or <c>raw</c> is treated as a metric: the
    /// dictionary lookup happens in the validator (rule V-10), so this method cannot drop a metric merely because
    /// this build has never heard of it.
    /// </summary>
    private static TelemetrySampleDocument ReadSample(JsonElement sample)
    {
        if (sample.ValueKind != JsonValueKind.Object)
        {
            return new TelemetrySampleDocument(null, EmptyMetrics, EmptyMetrics, null, EmptyKeys);
        }

        var metrics = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var invalid = new List<string>();

        foreach (var property in sample.EnumerateObject())
        {
            if (property.NameEquals("t") || property.NameEquals("q") || property.NameEquals("raw"))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var value))
            {
                metrics[property.Name] = value;
            }
            else
            {
                invalid.Add(property.Name);
            }
        }

        return new TelemetrySampleDocument(
            ReadInt32(sample, "t"),
            metrics,
            ReadRawValues(sample),
            ReadInt32(sample, "q"),
            invalid);
    }

    /// <summary>
    /// Reads the diagnostics block. A key with no number in it is skipped in silence rather than reported: the raw
    /// block exists for calibration review, and refusing a real measurement over a diagnostic field would be the
    /// wrong trade. Missing and nonsense both simply mean "no raw value".
    /// </summary>
    private static IReadOnlyDictionary<string, decimal> ReadRawValues(JsonElement sample)
    {
        if (!sample.TryGetProperty("raw", out var raw) || raw.ValueKind != JsonValueKind.Object)
        {
            return EmptyMetrics;
        }

        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var property in raw.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var value))
            {
                values[property.Name] = value;
            }
        }

        return values;
    }

    private static DeviceHealthDocument? ReadHealth(JsonElement root)
    {
        if (!root.TryGetProperty("health", out var health) || health.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new DeviceHealthDocument(
            ReadInt32(health, "rssi"),
            ReadInt64(health, "up_s"),
            ReadInt32(health, "heap_kb"),
            ReadDecimal(health, "bat"),
            ReadString(health, "src"));
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt64(out var value)
            ? value
            : null;

    private static int? ReadInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : null;

    private static decimal? ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDecimal(out var value)
            ? value
            : null;

    private static readonly IReadOnlyDictionary<string, decimal> EmptyMetrics =
        new Dictionary<string, decimal>(StringComparer.Ordinal);

    private static readonly IReadOnlyList<string> EmptyKeys = [];
}
