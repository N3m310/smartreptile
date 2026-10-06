using System.Text.Json;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// The per-device calibration of BR-07.4, parsed from <c>Device.CalibrationJson</c>:
/// <c>{"tempOffsetC":-0.4,"rhOffsetPct":2.1,"luxGain":1.03}</c>.
/// </summary>
/// <remarks>
/// Two properties make this worth a type rather than three loose numbers. It is <b>tolerant</b>: a device whose
/// JSON is unreadable is treated as uncalibrated rather than failing ingest, because refusing to store a real
/// measurement over a diagnostic offset would be the wrong trade. And it is <b>explicit about absence</b>:
/// <c>luxGain</c> defaults to 1, so "no gain set" cannot be confused with "gain of zero", which would silently
/// turn every light reading into darkness.
/// </remarks>
/// <param name="TempOffsetC">Added to <see cref="MetricCode.TempC"/>.</param>
/// <param name="HumidityOffsetPct">Added to <see cref="MetricCode.HumidityPct"/>.</param>
/// <param name="LuxGain">Multiplied into <see cref="MetricCode.LightLux"/>.</param>
public sealed record DeviceCalibration(decimal TempOffsetC, decimal HumidityOffsetPct, decimal LuxGain)
{
    /// <summary>No offsets: the values are stored exactly as measured.</summary>
    public static DeviceCalibration None { get; } = new(0m, 0m, 1m);

    /// <summary>True when applying this calibration would change nothing, so the quality bit can be left clear.</summary>
    public bool IsNoOp => TempOffsetC == 0m && HumidityOffsetPct == 0m && LuxGain == 1m;

    /// <summary>
    /// Parses the stored JSON. Unknown keys are ignored (only the three documented offsets exist), a missing key
    /// means "no offset", and malformed JSON means "uncalibrated" — never a failed batch.
    /// </summary>
    public static DeviceCalibration Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return None;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return None;
            }

            return new DeviceCalibration(
                ReadDecimal(document.RootElement, "tempOffsetC", 0m),
                ReadDecimal(document.RootElement, "rhOffsetPct", 0m),
                ReadDecimal(document.RootElement, "luxGain", fallback: 1m));
        }
        catch (JsonException)
        {
            return None;
        }
    }

    /// <summary>
    /// Applies the offsets a metric has. <see cref="MetricCode.SurfaceTempC"/> is deliberately absent: the
    /// documented contract has no offset for it, and inventing one would silently shift a burn-risk reading.
    /// </summary>
    public decimal Apply(MetricCode code, decimal value) => code switch
    {
        MetricCode.TempC => value + TempOffsetC,
        MetricCode.HumidityPct => value + HumidityOffsetPct,
        MetricCode.LightLux => value * LuxGain,
        _ => value,
    };

    private static decimal ReadDecimal(JsonElement root, string name, decimal fallback) =>
        root.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetDecimal(out var value)
            ? value
            : fallback;
}
