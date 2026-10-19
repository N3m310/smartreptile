using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;

namespace SmartReptile.Application.Signals;

/// <summary>
/// What the light-deficit sweep needs from storage: the terrariums it can judge, the one alert that may be open for
/// each, and the day's light readings.
/// </summary>
/// <remarks>
/// A port of its own rather than a method on <c>ITerrariumStore</c>, for the same reason the evaluator has one: the
/// sweep must not be able to reach the write paths a request owns, and the terrarium read surface must not grow a
/// method that exists for one background rule.
/// </remarks>
public interface ILightDeficitStore
{
    /// <summary>Every active terrarium with a bound device and a species profile, with the values the rule needs.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<LightDeficitCandidate>> FindCandidatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The light-deficit alert currently open for a terrarium, or null. One per terrarium: the rule reports "today
    /// fell short", and a second open row would say the same thing twice.
    /// </summary>
    /// <param name="terrariumId">Terrarium to look at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Alert?> FindOpenAlertAsync(Guid terrariumId, CancellationToken cancellationToken);

    /// <summary>The <c>LightLux</c> readings inside a window, oldest first.</summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="fromUtc">Window start, inclusive.</param>
    /// <param name="toUtc">Window end, exclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<LightReadingPoint>> FindLightReadingsAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Stages a new alert.</summary>
    /// <param name="alert">Alert to write.</param>
    void AddAlert(Alert alert);

    /// <summary>Commits everything the sweep staged.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>One terrarium the light-deficit rule can judge, with the profile values it is judged against.</summary>
/// <param name="TerrariumId">Terrarium being judged.</param>
/// <param name="DeviceId">Device whose light sensor produced the day's readings.</param>
/// <param name="TimeZoneId">Zone that decides when the local day closed.</param>
/// <param name="LightThresholdLux">Illuminance at or above which a minute counts as lit.</param>
/// <param name="MinLightHoursPerDay">The profile's requirement for a full day.</param>
public sealed record LightDeficitCandidate(
    Guid TerrariumId,
    Guid DeviceId,
    string TimeZoneId,
    decimal LightThresholdLux,
    decimal MinLightHoursPerDay);

/// <summary>One stored <c>LightLux</c> reading.</summary>
/// <param name="AtUtc">Instant the device recorded it.</param>
/// <param name="Value">Illuminance in lux.</param>
public sealed record LightReadingPoint(DateTimeOffset AtUtc, decimal Value);

/// <summary>What one sweep did, so the worker can log and count it without re-reading the database.</summary>
/// <param name="Terrariums">Terrariums judged this pass.</param>
/// <param name="Opened">Deficit alerts opened.</param>
/// <param name="Resolved">Deficit alerts closed, by recovery or by the day rolling over.</param>
/// <param name="Touched">Open alerts whose last-observed instant moved.</param>
public sealed record LightDeficitOutcome(int Terrariums, int Opened, int Resolved, int Touched)
{
    /// <summary>The lifecycle moves to push after the sweep commits.</summary>
    public IReadOnlyList<AlertChange> AlertChanges { get; init; } = [];
}
