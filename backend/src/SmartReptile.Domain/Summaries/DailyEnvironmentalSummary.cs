namespace SmartReptile.Domain.Summaries;

/// <summary>Computed environmental statistics and exposure for one terrarium-local calendar day.</summary>
public class DailyEnvironmentalSummary
{
    public long Id { get; set; }

    public Guid TerrariumId { get; set; }

    public DateOnly LocalDate { get; set; }

    public string TimeZoneId { get; set; } = string.Empty;

    public decimal CoveragePct { get; set; }

    public decimal? TempMin { get; set; }

    public decimal? TempMax { get; set; }

    public decimal? TempAvg { get; set; }

    public decimal? HumidityMin { get; set; }

    public decimal? HumidityMax { get; set; }

    public decimal? HumidityAvg { get; set; }

    public decimal? LuxAvg { get; set; }

    public decimal LightHours { get; set; }

    public decimal? UvMax { get; set; }

    public string OutOfRangeMinutesJson { get; set; } = "{}";

    public decimal TempExposureDegCHours { get; set; }

    public decimal HumidityDryExposurePctHours { get; set; }

    public decimal HumidityWetExposurePctHours { get; set; }

    public decimal LightDeficitHours { get; set; }

    public int AlertCount { get; set; }

    public int CriticalAlertCount { get; set; }

    public DateTimeOffset ComputedAt { get; set; }

    public bool IsLowConfidence { get; set; }
}
