using Microsoft.EntityFrameworkCore;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Reference;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// Seeds the metric dictionary, the four built-in species profiles and their bands.
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
    /// <para>The `Arid-cool` profile's two ambient bands joined the set on 2026-10-07 (14 → 16). They are pending
    /// like the rest: appendix §5 row 7 names the band the variant exists for, and the other one is the humidity
    /// band it shares in value with the Arid profile.</para>
    /// </summary>
    public const int BandsAwaitingVerification = 16;

    private const string PendingVerification =
        PendingVerificationMarker + " — typical published husbandry range, see docs/07-appendices/05 §5";

    /// <summary>Dwell time and hysteresis are engineering judgement about sensor noise and alert fatigue, not
    /// biology. Every band says so, so neither the report nor the UI can imply they were sourced (appendix §5
    /// row 13).</summary>
    private const string EngineParametersProvenance =
        "dwell/recovery are team design choices, not literature (docs/07-appendices/05 §5 row 13)";

    /// <summary>One curated catalogue row: the id, the label, the country and the standard offset.</summary>
    /// <param name="IanaId">IANA zone id, stored as the key.</param>
    /// <param name="DisplayName">Label a picker shows.</param>
    /// <param name="CountryCode">ISO-3166 alpha-2 code, empty for a zone belonging to no country.</param>
    /// <param name="StandardUtcOffsetMinutes">Standard-time offset in minutes.</param>
    public sealed record TimeZoneSeed(
        string IanaId,
        string DisplayName,
        string CountryCode,
        int StandardUtcOffsetMinutes);

    /// <summary>
    /// The time-zone catalogue: the IANA zones a terrarium or an account may name, in the order a picker shows them
    /// — west to east, ties broken by the zone id (TASKS 2026-10-14, item 11).
    /// </summary>
    /// <remarks>
    /// <b>Why a curated list rather than every IANA zone.</b> The catalogue is what both clients render as a
    /// dropdown and what the create and update paths validate against, so it has to be a list a human can read,
    /// review and correct — the 600-odd ids of the full database, most of which name an uninhabited island or a
    /// historical alias, are neither. Every offset in it is the zone's <i>standard</i> offset, listed west to east;
    /// the label names the places the zone is used in, the way a keeper would recognise them.
    /// <para>
    /// <b>Offsets are metadata, not arithmetic.</b> Which offset applies at a given instant — including daylight
    /// saving, which this table deliberately does not model — stays the platform database's answer through
    /// <c>SystemClock.ResolveZone</c> (ADR-015). A stale number here mislabels a picker row; it cannot misjudge a
    /// reading.
    /// </para>
    /// <para>
    /// <b>The seeder owns the table.</b> Re-running it inserts what is missing and rewrites any row whose label,
    /// country, offset or position has changed, so editing this list is how the catalogue changes. Rows a
    /// deployment added by hand are left alone — this list only ever speaks for the ids it names.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<TimeZoneSeed> Catalogue { get; } =
        [
            new("Pacific/Honolulu", "Honolulu", "US", -600),
            new("America/Anchorage", "Anchorage", "US", -540),
            new("America/Los_Angeles", "Los Angeles, San Francisco, Seattle", "US", -480),
            new("America/Vancouver", "Vancouver", "CA", -480),
            new("America/Denver", "Denver, Salt Lake City", "US", -420),
            new("America/Edmonton", "Edmonton, Calgary", "CA", -420),
            new("America/Phoenix", "Phoenix, Tucson", "US", -420),
            new("America/Chicago", "Chicago, Dallas, Houston", "US", -360),
            new("America/Mexico_City", "Mexico City, Monterrey", "MX", -360),
            new("America/Bogota", "Bogotá, Quito", "CO", -300),
            new("America/Lima", "Lima", "PE", -300),
            new("America/New_York", "New York, Miami, Washington", "US", -300),
            new("America/Panama", "Panama City", "PA", -300),
            new("America/Toronto", "Toronto, Montreal", "CA", -300),
            new("America/Caracas", "Caracas", "VE", -240),
            new("America/Halifax", "Halifax", "CA", -240),
            new("America/Santiago", "Santiago", "CL", -240),
            new("America/St_Johns", "St. John's", "CA", -210),
            new("America/Argentina/Buenos_Aires", "Buenos Aires", "AR", -180),
            new("America/Sao_Paulo", "São Paulo, Rio de Janeiro", "BR", -180),
            new("Atlantic/Azores", "Azores", "PT", -60),
            new("Africa/Accra", "Accra", "GH", 0),
            new("Africa/Casablanca", "Casablanca", "MA", 0),
            new("Atlantic/Reykjavik", "Reykjavík", "IS", 0),
            new("Europe/Dublin", "Dublin", "IE", 0),
            new("Europe/Lisbon", "Lisbon", "PT", 0),
            new("Europe/London", "London", "GB", 0),
            new("UTC", "UTC", "", 0),
            new("Africa/Algiers", "Algiers", "DZ", 60),
            new("Africa/Lagos", "Lagos", "NG", 60),
            new("Africa/Tunis", "Tunis", "TN", 60),
            new("Europe/Amsterdam", "Amsterdam", "NL", 60),
            new("Europe/Belgrade", "Belgrade, Zagreb", "RS", 60),
            new("Europe/Berlin", "Berlin, Vienna, Prague", "DE", 60),
            new("Europe/Brussels", "Brussels", "BE", 60),
            new("Europe/Budapest", "Budapest", "HU", 60),
            new("Europe/Copenhagen", "Copenhagen", "DK", 60),
            new("Europe/Madrid", "Madrid, Barcelona", "ES", 60),
            new("Europe/Oslo", "Oslo", "NO", 60),
            new("Europe/Paris", "Paris", "FR", 60),
            new("Europe/Prague", "Prague", "CZ", 60),
            new("Europe/Rome", "Rome, Milan", "IT", 60),
            new("Europe/Stockholm", "Stockholm", "SE", 60),
            new("Europe/Vienna", "Vienna", "AT", 60),
            new("Europe/Warsaw", "Warsaw", "PL", 60),
            new("Europe/Zurich", "Zurich", "CH", 60),
            new("Africa/Cairo", "Cairo", "EG", 120),
            new("Africa/Johannesburg", "Johannesburg, Cape Town", "ZA", 120),
            new("Asia/Beirut", "Beirut", "LB", 120),
            new("Asia/Jerusalem", "Jerusalem, Tel Aviv", "IL", 120),
            new("Europe/Athens", "Athens", "GR", 120),
            new("Europe/Bucharest", "Bucharest, Sofia", "RO", 120),
            new("Europe/Helsinki", "Helsinki, Tallinn, Riga", "FI", 120),
            new("Europe/Kyiv", "Kyiv", "UA", 120),
            new("Africa/Addis_Ababa", "Addis Ababa", "ET", 180),
            new("Africa/Nairobi", "Nairobi", "KE", 180),
            new("Asia/Amman", "Amman", "JO", 180),
            new("Asia/Baghdad", "Baghdad", "IQ", 180),
            new("Asia/Damascus", "Damascus", "SY", 180),
            new("Asia/Kuwait", "Kuwait City", "KW", 180),
            new("Asia/Riyadh", "Riyadh", "SA", 180),
            new("Europe/Istanbul", "Istanbul", "TR", 180),
            new("Europe/Minsk", "Minsk", "BY", 180),
            new("Europe/Moscow", "Moscow, Saint Petersburg", "RU", 180),
            new("Asia/Tehran", "Tehran", "IR", 210),
            new("Asia/Baku", "Baku", "AZ", 240),
            new("Asia/Dubai", "Dubai, Abu Dhabi", "AE", 240),
            new("Asia/Muscat", "Muscat", "OM", 240),
            new("Asia/Tbilisi", "Tbilisi", "GE", 240),
            new("Asia/Yerevan", "Yerevan", "AM", 240),
            new("Indian/Mauritius", "Port Louis", "MU", 240),
            new("Indian/Reunion", "Saint-Denis", "RE", 240),
            new("Asia/Kabul", "Kabul", "AF", 270),
            new("Asia/Almaty", "Almaty, Astana", "KZ", 300),
            new("Asia/Ashgabat", "Ashgabat", "TM", 300),
            new("Asia/Karachi", "Karachi, Islamabad", "PK", 300),
            new("Asia/Tashkent", "Tashkent", "UZ", 300),
            new("Indian/Maldives", "Malé", "MV", 300),
            new("Asia/Colombo", "Colombo", "LK", 330),
            new("Asia/Kolkata", "New Delhi, Mumbai", "IN", 330),
            new("Asia/Kathmandu", "Kathmandu", "NP", 345),
            new("Asia/Dhaka", "Dhaka", "BD", 360),
            new("Asia/Yangon", "Yangon", "MM", 390),
            new("Asia/Bangkok", "Bangkok", "TH", 420),
            new("Asia/Ho_Chi_Minh", "Hanoi, Ho Chi Minh City", "VN", 420),
            new("Asia/Jakarta", "Jakarta", "ID", 420),
            new("Asia/Krasnoyarsk", "Krasnoyarsk", "RU", 420),
            new("Asia/Novosibirsk", "Novosibirsk", "RU", 420),
            new("Asia/Phnom_Penh", "Phnom Penh", "KH", 420),
            new("Asia/Vientiane", "Vientiane", "LA", 420),
            new("Asia/Brunei", "Bandar Seri Begawan", "BN", 480),
            new("Asia/Hong_Kong", "Hong Kong", "HK", 480),
            new("Asia/Irkutsk", "Irkutsk", "RU", 480),
            new("Asia/Kuala_Lumpur", "Kuala Lumpur", "MY", 480),
            new("Asia/Manila", "Manila", "PH", 480),
            new("Asia/Shanghai", "Beijing, Shanghai", "CN", 480),
            new("Asia/Singapore", "Singapore", "SG", 480),
            new("Asia/Taipei", "Taipei", "TW", 480),
            new("Asia/Ulaanbaatar", "Ulaanbaatar", "MN", 480),
            new("Australia/Perth", "Perth", "AU", 480),
            new("Asia/Pyongyang", "Pyongyang", "KP", 540),
            new("Asia/Seoul", "Seoul", "KR", 540),
            new("Asia/Tokyo", "Tokyo, Osaka", "JP", 540),
            new("Asia/Yakutsk", "Yakutsk", "RU", 540),
            new("Australia/Adelaide", "Adelaide", "AU", 570),
            new("Australia/Darwin", "Darwin", "AU", 570),
            new("Asia/Vladivostok", "Vladivostok", "RU", 600),
            new("Australia/Brisbane", "Brisbane", "AU", 600),
            new("Australia/Hobart", "Hobart", "AU", 600),
            new("Australia/Melbourne", "Melbourne", "AU", 600),
            new("Australia/Sydney", "Sydney, Canberra", "AU", 600),
            new("Pacific/Guam", "Guam", "GU", 600),
            new("Pacific/Port_Moresby", "Port Moresby", "PG", 600),
            new("Asia/Magadan", "Magadan", "RU", 660),
            new("Pacific/Norfolk", "Norfolk Island", "NF", 660),
            new("Pacific/Noumea", "Nouméa", "NC", 660),
            new("Asia/Kamchatka", "Petropavlovsk-Kamchatsky", "RU", 720),
            new("Pacific/Auckland", "Auckland, Wellington", "NZ", 720),
            new("Pacific/Fiji", "Suva", "FJ", 720),
            new("Pacific/Apia", "Apia", "WS", 780),
            new("Pacific/Tongatapu", "Nuku'alofa", "TO", 780),
            new("Pacific/Kiritimati", "Kiritimati", "KI", 840),
        ];

    /// <summary>How many rows the catalogue holds — asserted by a test so a truncated list is caught.</summary>
    public static int TimeZoneCount => Catalogue.Count;

    /// <summary>
    /// Seeds the time-zone catalogue, and brings it back in step with <see cref="Catalogue"/> when a label, country,
    /// offset or position changed.
    /// </summary>
    /// <remarks>
    /// Update-in-place rather than insert-if-missing, unlike the species profiles: a profile is authored data a
    /// keeper may have edited, so the seeder must not overwrite one, whereas this table is reference data the code
    /// owns outright — leaving a stale offset or label behind would mean the picker and the validation disagree with
    /// the release that shipped them. Rows whose id is not in the list are untouched.
    /// </remarks>
    public async Task<int> SeedTimeZonesAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.TimeZones
            .ToDictionaryAsync(zone => zone.IanaId, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;
        var updated = 0;

        for (var index = 0; index < Catalogue.Count; index++)
        {
            var seed = Catalogue[index];

            if (!existing.TryGetValue(seed.IanaId, out var row))
            {
                db.TimeZones.Add(new TimeZoneReference
                {
                    IanaId = seed.IanaId,
                    DisplayName = seed.DisplayName,
                    CountryCode = seed.CountryCode,
                    StandardUtcOffsetMinutes = seed.StandardUtcOffsetMinutes,
                    SortOrder = index,
                });

                created++;
                continue;
            }

            if (row.DisplayName == seed.DisplayName
                && row.CountryCode == seed.CountryCode
                && row.StandardUtcOffsetMinutes == seed.StandardUtcOffsetMinutes
                && row.SortOrder == index)
            {
                continue;
            }

            row.DisplayName = seed.DisplayName;
            row.CountryCode = seed.CountryCode;
            row.StandardUtcOffsetMinutes = seed.StandardUtcOffsetMinutes;
            row.SortOrder = index;
            updated++;
        }

        if (created > 0 || updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Time-zone catalogue: {Created} created, {Updated} brought back in step, {Total} in the list",
                created,
                updated,
                Catalogue.Count);
        }

        return created;
    }

    /// <summary>
    /// Keeps the metric catalogue in step with <see cref="MetricDictionary"/>, which is what owns the rows.
    /// </summary>
    /// <remarks>
    /// <b>Why this exists when the migration already inserts the seven rows.</b> A metric added to the dictionary in
    /// a later release must appear here without a migration — the same reasoning as the permission catalogue, and the
    /// same safe direction: a row that is missing is inserted, a row whose label drifted is corrected, and nothing is
    /// ever removed (a catalogue row a reading still points at cannot be deleted anyway, and deleting one by hand is a
    /// decision this step must not take back).
    /// </remarks>
    public async Task<int> SeedMetricCatalogueAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.Metrics
            .ToDictionaryAsync(metric => metric.Code, cancellationToken)
            .ConfigureAwait(false);

        var created = 0;
        var updated = 0;
        var order = 0;

        foreach (var definition in MetricDictionary.All)
        {
            order++;

            if (existing.TryGetValue(definition.Code, out var row))
            {
                if (row.ApiKey == definition.ApiKey
                    && row.PayloadKey == definition.PayloadKey
                    && row.DisplayName == definition.DisplayName
                    && row.Unit == definition.Unit
                    && row.Precision == definition.Precision
                    && row.PlausibleMin == definition.PlausibleMin
                    && row.PlausibleMax == definition.PlausibleMax
                    && row.IsCore == definition.IsCore
                    && row.Family == definition.Family
                    && row.SortOrder == order)
                {
                    continue;
                }

                row.ApiKey = definition.ApiKey;
                row.PayloadKey = definition.PayloadKey;
                row.DisplayName = definition.DisplayName;
                row.Unit = definition.Unit;
                row.Precision = definition.Precision;
                row.PlausibleMin = definition.PlausibleMin;
                row.PlausibleMax = definition.PlausibleMax;
                row.IsCore = definition.IsCore;
                row.Family = definition.Family;
                row.SortOrder = order;
                updated++;

                continue;
            }

            db.Metrics.Add(new MetricReference
            {
                Code = definition.Code,
                ApiKey = definition.ApiKey,
                PayloadKey = definition.PayloadKey,
                DisplayName = definition.DisplayName,
                Unit = definition.Unit,
                Precision = definition.Precision,
                PlausibleMin = definition.PlausibleMin,
                PlausibleMax = definition.PlausibleMax,
                IsCore = definition.IsCore,
                Family = definition.Family,
                SortOrder = order,
            });

            created++;
        }

        if (created > 0 || updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Metric catalogue: {Created} created, {Updated} brought back in step, {Total} in the dictionary",
                created,
                updated,
                MetricDictionary.All.Count);
        }

        return created;
    }

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

        created += await EnsureProfileAsync(
            name: "Arid-cool (bearded dragon, ambient)",
            scientificName: "Pogona vitticeps",
            zone: ClimateZone.Arid,
            photoperiodHours: 12m,
            lightsOnLocalTime: new TimeOnly(8, 0),
            lightThresholdLux: 2000m,
            minLightHours: 10m,
            notes: "Cool-side variant of the Arid profile: the same species and the same enclosure, monitored from "
                 + "the cool side rather than the basking spot, which is why the day temperature band is the "
                 + "ambient range (28-33 °C) instead of the basking range. Only the ambient bands ship — there is "
                 + "no SurfaceTempC band, because that probe belongs on the basking rock this variant deliberately "
                 + "does not describe, and no UvIndex band, because the UVB gradient is a property of the basking "
                 + "zone and a cool-side target could never be met. The accumulated light rule stays profile-level "
                 + "(2000 lx for 10 h), exactly as the Arid profile has it.",
            bands:
            [
                Band(MetricCode.TempC, ThresholdPhase.Day, 28m, 33m, 24m, 36m, margin: 1m),
                Band(MetricCode.HumidityPct, ThresholdPhase.Any, 30m, 40m, 20m, 55m, dwellWarn: 15, margin: 3m),
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
        int dwellWarn = 2,
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
