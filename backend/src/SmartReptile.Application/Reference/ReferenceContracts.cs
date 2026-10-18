using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Reference;

namespace SmartReptile.Application.Reference;

/// <summary>
/// One time zone as a picker reads it (TASKS 2026-10-14, item 11).
/// </summary>
/// <param name="IanaId">
/// The value to store: <c>Terrarium.TimeZoneId</c> and <c>User.TimeZoneId</c> carry exactly this string (ADR-015).
/// </param>
/// <param name="DisplayName">Human label built from the places the zone covers.</param>
/// <param name="CountryCode">ISO-3166 alpha-2 code, empty for a zone belonging to no country.</param>
/// <param name="UtcOffsetMinutes">Standard-time offset in minutes; negative is west of Greenwich.</param>
/// <param name="UtcOffset">
/// The same offset pre-formatted as <c>UTC+07:00</c>, so neither client has to agree about how to spell one.
/// </param>
public sealed record TimeZoneView(
    string IanaId,
    string DisplayName,
    string CountryCode,
    int UtcOffsetMinutes,
    string UtcOffset);

/// <summary>
/// The reference-data reads a client needs before it can render a form: today, the time-zone catalogue
/// (TASKS 2026-10-14, item 11).
/// </summary>
/// <remarks>
/// A service rather than a bare port call in the endpoint, for the same reason the other groups have one: the
/// projection onto the wire shape is a decision, and this is where the contract lives.
/// </remarks>
public sealed class ReferenceService(ITimeZoneStore zones)
{
    /// <summary>
    /// The time-zone catalogue, ordered west to east. Readable by every authenticated account: a picker is not
    /// privileged data, and both clients need it before they can create anything.
    /// </summary>
    public async Task<IReadOnlyList<TimeZoneView>> ListTimeZonesAsync(CancellationToken cancellationToken)
    {
        var catalogue = await zones.ListAsync(cancellationToken);

        return [.. catalogue.Select(zone => new TimeZoneView(
            zone.IanaId,
            zone.DisplayName,
            zone.CountryCode,
            zone.StandardUtcOffsetMinutes,
            FormatOffset(zone.StandardUtcOffsetMinutes)))];
    }

    /// <summary>
    /// Renders a standard offset the way a keeper reads one: <c>UTC+07:00</c>, <c>UTC-03:30</c>, and <c>UTC</c> for
    /// the zero offset. Invariant, because the label is data and not a localized phrase — a client that prefers
    /// <c>UTC+7</c> can format <c>UtcOffsetMinutes</c> itself.
    /// </summary>
    /// <param name="offsetMinutes">Offset in minutes.</param>
    public static string FormatOffset(int offsetMinutes) =>
        offsetMinutes == 0
            ? "UTC"
            : $"UTC{(offsetMinutes > 0 ? "+" : "-")}{Math.Abs(offsetMinutes) / 60:D2}:{Math.Abs(offsetMinutes) % 60:D2}";

    /// <summary>
    /// The catalogue entry an id names, or null when the catalogue holds no such zone. The create and update paths
    /// refuse a null; nothing else consults it.
    /// </summary>
    /// <param name="zones">The catalogue.</param>
    /// <param name="ianaId">Value as it arrived, possibly with surrounding whitespace or different casing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task<TimeZoneReference?> FindAsync(
        ITimeZoneStore zones,
        string? ianaId,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(ianaId)
            ? Task.FromResult<TimeZoneReference?>(null)
            : zones.FindAsync(ianaId.Trim(), cancellationToken);
}
