using Microsoft.EntityFrameworkCore;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// Seeds the metric dictionary, the three built-in species profiles and their bands.
/// Idempotent by name/metric, so re-running never duplicates rows (§03-implementation/03 §2).
/// </summary>
/// <remarks>
/// <b>Threshold provenance.</b> The numbers below are the typical published captive-husbandry ranges collected
/// in <c>docs/07-appendices/05-species-threshold-reference.md</c>. Every seeded band carries a
/// <see cref="Threshold.SourceRef"/>; while the verification checklist in that appendix is unsigned the
/// reference says so, and <see cref="Threshold.SourceUrl"/> stays <c>null</c> rather than pointing at a link
/// nobody has checked. A band with no verified citation does not ship (roadmap gates 1.8 and 3.8), and the
/// checklist and this seeder are kept in step by <see cref="BandsAwaitingVerification"/>.
/// </remarks>
public sealed class ReferenceDataSeeder(SmartReptileDbContext db, ILogger<ReferenceDataSeeder> logger)
{
    /// <summary>Appears in <see cref="Threshold.SourceRef"/> while a range still awaits literature
    /// verification. Public so the integration gate can assert on it.</summary>
    public const string PendingVerificationMarker = "PENDING VERIFICATION";

    /// <summary>
    /// How many seeded bands still carry <see cref="PendingVerificationMarker"/>, i.e. whose range nobody has
    /// checked against a source yet. The light bands are not counted: their numbers are not literature at all
    /// (they are inert — the real rule is accumulated light-hours).
    /// <para><b>Closing a checklist row means editing this file:</b> pass the verified citation at that band's
    /// call site (<c>source: BandSources.…</c>) and decrement this number in the same commit.
    /// <c>SchemaAndSeedingTests.Bands_awaiting_verification_match_the_declared_count</c> asserts that the
    /// database agrees, so the stamp cannot outlive the checklist. Gate: ≥ 60 % of rows cited for M1 (task
    /// 1.8), 100 % for M3 (task 3.8).</para>
    /// </summary>
    public const int BandsAwaitingVerification = 14;

    private const string PendingVerification =
        PendingVerificationMarker + " — typical published husbandry range, see docs/07-appendices/05 §5";

    /// <summary>Dwell time and hysteresis are engineering judgement about sensor noise and alert fatigue, not
    /// biology. Every band says so, so neither the report nor the UI can imply they were sourced (appendix §5
    /// row 13).</summary>
    private const string EngineParametersProvenance =
        "dwell/recovery are team design choices, not literature (docs/07-appendices/05 §5 row 13)";

    /// <summary>Seeds the built-in profiles if they are missing.</summary>
    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var created = 0;

        created += await EnsureProfileAsync(
            name: "Tropical (humid forest)",
            scientificName: "Correlophus ciliatus",
            zone: ClimateZone.Tropical,
            photoperiodHours: 12m,
            lightsOnLocalTime: new TimeOnly(7, 0),
            lightThresholdLux: 500m,
            minLightHours: 10m,
            notes: "Typical ranges for crepuscular humid-forest geckos. Heat, not cold, is the common indoor killer. "
                 + "Humidity is the normal state; a dry spell causes shedding problems. Low-level UVB optional.",
            bands:
            [
                Band(MetricCode.TempC, ThresholdPhase.Day, 24m, 28m, 18m, 31m, margin: 0.5m),
                Band(MetricCode.TempC, ThresholdPhase.Night, 20m, 24m, 16m, 27m, dwellWarn: 10, dwellCrit: 3),
                Band(MetricCode.HumidityPct, ThresholdPhase.Any, 60m, 80m, 40m, 95m, dwellWarn: 15, margin: 3m),
                Band(MetricCode.LightLux, ThresholdPhase.Day, 0m, 200_000m, dwellWarn: 15, dwellCrit: 15, margin: 0m,
                     source: BandSources.HusbandryPracticeLight),
                Band(MetricCode.UvIndex, ThresholdPhase.Day, 0m, 1.0m, 0m, 2.0m, dwellWarn: 15, margin: 0.1m),
            ],
            cancellationToken)
            .ConfigureAwait(false);

        created += await EnsureProfileAsync(
            name: "Leopard gecko (semi-desert)",
            scientificName: "Eublepharis macularius",
            zone: ClimateZone.SemiArid,
            photoperiodHours: 12m,
            lightsOnLocalTime: new TimeOnly(7, 0),
            lightThresholdLux: 1000m,
            minLightHours: 8m,
            notes: "Demo species. Thermal-gradient animal: air temperature near the warm side in the low 30s. "
                 + "A 3-5 °C night drop is beneficial, not a fault. The humidity band describes AIR humidity; "
                 + "the humid hide needs 70-80 %RH, which a single sensor cannot represent.",
            bands:
            [
                Band(MetricCode.TempC, ThresholdPhase.Day, 26m, 32m, 22m, 34.5m, margin: 0.5m),
                Band(MetricCode.TempC, ThresholdPhase.Night, 22m, 27m, 18m, 31m, dwellWarn: 10, dwellCrit: 3),
                Band(MetricCode.SurfaceTempC, ThresholdPhase.Day, 30m, 34m, 22m, 38m, margin: 1m),
                Band(MetricCode.HumidityPct, ThresholdPhase.Any, 30m, 40m, 20m, 60m, dwellWarn: 15, margin: 3m),
                Band(MetricCode.LightLux, ThresholdPhase.Day, 0m, 200_000m, dwellWarn: 15, dwellCrit: 15, margin: 0m,
                     source: BandSources.HusbandryPracticeLight),
                Band(MetricCode.UvIndex, ThresholdPhase.Day, 0m, 1.5m, 0m, 2.5m, dwellWarn: 15, margin: 0.1m),
            ],
            cancellationToken)
            .ConfigureAwait(false);

        created += await EnsureProfileAsync(
            name: "Arid (desert)",
            scientificName: "Pogona vitticeps",
            zone: ClimateZone.Arid,
            photoperiodHours: 12m,
            lightsOnLocalTime: new TimeOnly(8, 0),
            lightThresholdLux: 2000m,
            minLightHours: 10m,
            notes: "True heliotherm: a hot basking spot is a requirement, and air temperature alone understates "
                 + "its needs. Requires a UVB lamp; without one the UVI band cannot be met and the app reports "
                 + "'below target' rather than pretending it is fine.",
            bands:
            [
                Band(MetricCode.TempC, ThresholdPhase.Day, 38m, 42m, 30m, 45m, margin: 1m),
                Band(MetricCode.TempC, ThresholdPhase.Night, 24m, 28m, 18m, 32m, dwellWarn: 10, dwellCrit: 3, margin: 1m),
                Band(MetricCode.SurfaceTempC, ThresholdPhase.Day, 38m, 45m, 28m, 50m, margin: 1m),
                Band(MetricCode.HumidityPct, ThresholdPhase.Any, 30m, 40m, 20m, 55m, dwellWarn: 15, margin: 3m),
                Band(MetricCode.LightLux, ThresholdPhase.Day, 0m, 200_000m, dwellWarn: 15, dwellCrit: 15, margin: 0m,
                     source: BandSources.HusbandryPracticeLight),
                Band(MetricCode.UvIndex, ThresholdPhase.Day, 1.0m, 3.5m, 0m, 5.0m, dwellWarn: 15, margin: 0.2m),
            ],
            cancellationToken)
            .ConfigureAwait(false);

        if (created > 0)
        {
            logger.LogInformation("Seeded {Count} built-in species profile(s)", created);
        }

        return created;
    }

    /// <summary>A citation for one band's range. <c>Url</c> is null for a book: the page reference then belongs
    /// in <c>Reference</c> instead of pretending a link exists (appendix §6 citation hygiene).</summary>
    internal sealed record BandCitation(string Reference, string? Url = null);

    /// <summary>Ranges whose provenance is already decided rather than pending — currently only the inert light
    /// bands. Deliberately text-only: no DOI, edition or page is invented here, because the appendix requires
    /// those to be read off the real copy first.</summary>
    internal static class BandSources
    {
        /// <summary>Used by every seeded <c>LightLux</c> band: the band is inert, and the lx values are
        /// husbandry practice rather than physiology (appendix §5 row 12 — which the report must state).</summary>
        internal static readonly BandCitation HusbandryPracticeLight = new(
            "inert band: the light rule is accumulated light-hours, and the lx values are husbandry practice, "
            + "not physiology (docs/07-appendices/05 §5 row 12)");
    }

    private static Threshold Band(
        MetricCode metric,
        ThresholdPhase phase,
        decimal targetMin,
        decimal targetMax,
        decimal? criticalMin = null,
        decimal? criticalMax = null,
        int dwellWarn = 5,
        int dwellCrit = 2,
        decimal margin = 0.5m,
        BandCitation? source = null) => new()
        {
            Metric = metric,
            Phase = phase,
            TargetMin = targetMin,
            TargetMax = targetMax,
            CriticalMin = criticalMin,
            CriticalMax = criticalMax,
            DwellWarnMinutes = dwellWarn,
            DwellCritMinutes = dwellCrit,
            RecoveryMargin = margin,
            // Only a verified citation replaces the pending stamp. SourceUrl stays null while pending: there is
            // nothing honest to link to for a number nobody has checked yet.
            SourceRef = $"{source?.Reference ?? PendingVerification} — {EngineParametersProvenance}",
            SourceUrl = source?.Url,
        };

    private async Task<int> EnsureProfileAsync(
        string name,
        string scientificName,
        ClimateZone zone,
        decimal photoperiodHours,
        TimeOnly lightsOnLocalTime,
        decimal lightThresholdLux,
        decimal minLightHours,
        string notes,
        IReadOnlyList<Threshold> bands,
        CancellationToken cancellationToken)
    {
        var exists = await db.SpeciesProfiles
            .AnyAsync(p => p.Name == name && p.CreatedByUserId == null, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            return 0;
        }

        var profile = new SpeciesProfile
        {
            Name = name,
            ScientificName = scientificName,
            ClimateZone = zone,
            IsBuiltIn = true,
            PhotoperiodHours = photoperiodHours,
            LightsOnLocalTime = lightsOnLocalTime,
            LightThresholdLux = lightThresholdLux,
            MinLightHoursPerDay = minLightHours,
            Notes = notes,
        };

        foreach (var band in bands)
        {
            profile.Thresholds.Add(band);
        }

        db.SpeciesProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return 1;
    }
}
