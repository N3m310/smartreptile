namespace SmartReptile.Domain.Metrics;

/// <summary>
/// The metric dictionary codes (§02-design/02 §3.8). Adding a metric here (plus a row in the seeded
/// dictionary table at M2) is the whole cost of supporting a new measurement — see ADR-004.
/// </summary>
public enum MetricCode
{
    TempC = 1,
    HumidityPct = 2,
    LightLux = 3,
    UvIndex = 4,
    SurfaceTempC = 5,
    BatteryPct = 6,
    RssiDbm = 7,
}

/// <summary>Unit, precision and plausibility bounds for one metric (§02-design/03 §3, rules V-06, DI-06).</summary>
/// <param name="Code">Metric code.</param>
/// <param name="ApiKey">Short key used in the MQTT payload and REST responses.</param>
/// <param name="DisplayName">Human-readable name (localised in the client, not stored).</param>
/// <param name="Unit">Unit string shown next to the value.</param>
/// <param name="Precision">Decimal places kept when formatting.</param>
/// <param name="PlausibleMin">Lowest physically plausible value; outside ⇒ flagged, never rejected.</param>
/// <param name="PlausibleMax">Highest physically plausible value; outside ⇒ flagged, never rejected.</param>
/// <param name="IsCore">Core metrics are required in every species profile.</param>
public sealed record MetricDefinition(
    MetricCode Code,
    string ApiKey,
    string DisplayName,
    string Unit,
    int Precision,
    decimal PlausibleMin,
    decimal PlausibleMax,
    bool IsCore);

/// <summary>Static access to the metric dictionary. Kept in the domain so firmware, API and UI agree.</summary>
public static class MetricDictionary
{
    private static readonly Dictionary<MetricCode, MetricDefinition> Definitions = new()
    {
        [MetricCode.TempC] = new(MetricCode.TempC, "tempC", "Air temperature", "°C", 2, -10m, 60m, true),
        [MetricCode.HumidityPct] = new(MetricCode.HumidityPct, "humidityPct", "Relative humidity", "%RH", 2, 0m, 100m, true),
        [MetricCode.LightLux] = new(MetricCode.LightLux, "lightLux", "Illuminance", "lx", 1, 0m, 200_000m, true),
        [MetricCode.UvIndex] = new(MetricCode.UvIndex, "uvIndex", "UV index", "UVI", 2, 0m, 15m, true),
        [MetricCode.SurfaceTempC] = new(MetricCode.SurfaceTempC, "surfaceTempC", "Surface temperature", "°C", 2, -10m, 80m, false),
        [MetricCode.BatteryPct] = new(MetricCode.BatteryPct, "batteryPct", "Battery", "%", 1, 0m, 100m, false),
        [MetricCode.RssiDbm] = new(MetricCode.RssiDbm, "rssiDbm", "Wi-Fi signal", "dBm", 0, -120m, 0m, false),
    };

    /// <summary>All metric definitions.</summary>
    public static IReadOnlyCollection<MetricDefinition> All => Definitions.Values;

    /// <summary>Looks up a definition; throws if the code is unknown (programming error).</summary>
    public static MetricDefinition Get(MetricCode code) =>
        Definitions.TryGetValue(code, out var definition)
            ? definition
            : throw new Common.DomainValidationException("unknown_metric", $"Metric {code} is not in the dictionary.");

    /// <summary>True when the value lies inside the plausibility bounds (rule V-06, quality flag 2 otherwise).</summary>
    public static bool IsPlausible(MetricCode code, decimal value)
    {
        var definition = Get(code);
        return value >= definition.PlausibleMin && value <= definition.PlausibleMax;
    }
}
