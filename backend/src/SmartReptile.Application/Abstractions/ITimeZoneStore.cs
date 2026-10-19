using SmartReptile.Domain.Reference;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Persistence port for the reference data a client needs to fill a picker (TASKS 2026-10-14, item 11).
/// </summary>
/// <remarks>
/// It exists because the zone id stopped being a free-text field: the create and update paths now have to ask
/// whether the id a caller sent is one of the catalogue's, and <c>GET /api/v1/time-zones</c> has to hand the list to
/// a picker. One port for both, because a validation that consulted one source and a picker that offered another
/// would refuse exactly the values it had just suggested.
/// </remarks>
public interface ITimeZoneStore
{
    /// <summary>The catalogue, ordered for display (west to east).</summary>
    Task<IReadOnlyList<TimeZoneReference>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// One catalogue entry by id, or null when the catalogue holds no such zone. Case-insensitive: the ids travel
    /// through query strings and forms a human may have typed, and an IANA id is case-sensitive only by convention.
    /// </summary>
    /// <returns>
    /// The row rather than a boolean, because the caller needs the stored spelling back: a body that said
    /// <c>asia/ho_chi_minh</c> is accepted and stored as <c>Asia/Ho_Chi_Minh</c>.
    /// </returns>
    Task<TimeZoneReference?> FindAsync(string ianaId, CancellationToken cancellationToken);
}
