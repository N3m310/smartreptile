namespace SmartReptile.Domain.Reference;

/// <summary>
/// One entry of the time-zone catalogue: an IANA zone the terrarium editor offers, and the metadata a picker needs
/// to render it (FR-03, ADR-015 — TASKS 2026-10-14, item 11).
/// </summary>
/// <remarks>
/// <b>The table is named <c>TimeZone</c> and the type is not.</b> <c>System.TimeZone</c> is imported into every file
/// through implicit usings, so a type with that exact name would be ambiguous wherever both namespaces are in scope
/// — a compile error in half the solution for a cosmetic gain. The columns, the route and the documentation all say
/// <c>TimeZone</c>, because that is what the row is.
/// <para>
/// <b>Keyed by the IANA id.</b> The id <i>is</i> the standard's identifier — <c>Asia/Ho_Chi_Minh</c>, not a surrogate
/// that has to be joined back — and <c>Terrarium.TimeZoneId</c> already stores exactly this string (ADR-015). A
/// surrogate key would add a translation step to every read and let the two columns disagree.
/// </para>
/// <para>
/// <b>The offset is standard time, not "the offset right now".</b> A zone that observes daylight saving has two
/// offsets, and the one that belongs in reference data is the one the zone is defined by. Which offset applies at a
/// given instant is the platform time-zone database's business — <c>SystemClock.ResolveZone</c> asks it at read time
/// and degrades to UTC when the id is unknown — so this column is for grouping and display, never for arithmetic.
/// </para>
/// </remarks>
public class TimeZoneReference
{
    /// <summary>IANA zone id, e.g. <c>Asia/Ho_Chi_Minh</c>. The primary key.</summary>
    public string IanaId { get; set; } = string.Empty;

    /// <summary>
    /// Human label for a picker, built from the places the zone covers ("Hanoi, Bangkok, Jakarta"). Place names
    /// rather than a phrase like "Indochina Time", because the label ships once and both clients render it as-is.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// ISO-3166 alpha-2 code of the zone's primary country ("VN"), or empty for a zone that belongs to no country —
    /// only <c>UTC</c> in this catalogue.
    /// </summary>
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// Standard-time offset from UTC in minutes, e.g. <c>420</c> for UTC+07:00. Negative is west of Greenwich.
    /// </summary>
    public int StandardUtcOffsetMinutes { get; set; }

    /// <summary>
    /// Position in the curated list, ordered west to east. Stored rather than derived because it is a product
    /// decision — a picker that reads the way a keeper reads a map — and because ordering by the offset alone leaves
    /// the zones that share one in whatever order the database happens to return.
    /// </summary>
    public int SortOrder { get; set; }
}
