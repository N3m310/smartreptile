using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Species;

/// <summary>
/// One band a client wants on a profile (`07-appendices/03` §4.3). Nullable members so a missing field becomes a
/// field-level violation rather than a deserialisation failure, the rule every write contract here follows;
/// everything optional carries the documented default.
/// </summary>
/// <param name="Metric">Metric key from the dictionary, e.g. <c>tempC</c>; required.</param>
/// <param name="Phase">Phase the band applies in: <c>any</c> (the default when omitted), <c>day</c> or <c>night</c>.</param>
/// <param name="TargetMin">Lower bound of the target band; required.</param>
/// <param name="TargetMax">Upper bound of the target band; required.</param>
/// <param name="CriticalMin">Lower critical bound; omitted derives it from the target band plus the margin.</param>
/// <param name="CriticalMax">Upper critical bound; omitted derives it from the target band plus the margin.</param>
/// <param name="DwellWarnMinutes">Minutes out of band before a Warning; defaults to 5.</param>
/// <param name="DwellCritMinutes">Minutes critically out of band before escalation; defaults to 2.</param>
/// <param name="RecoveryMargin">Distance back inside the band that counts as recovered; defaults to 0.5.</param>
/// <param name="Enabled">False keeps the numbers without judging against them; defaults to true.</param>
/// <param name="SourceRef">Where these numbers came from. Optional for a custom band, and the column the built-in
/// profiles carry a citation in (FR-10, `07-appendices/05` §5); at most 300 characters.</param>
public sealed record SpeciesProfileBandInput(
    string? Metric,
    string? Phase,
    decimal? TargetMin,
    decimal? TargetMax,
    decimal? CriticalMin,
    decimal? CriticalMax,
    int? DwellWarnMinutes,
    int? DwellCritMinutes,
    decimal? RecoveryMargin,
    bool? Enabled,
    string? SourceRef);

/// <summary>Body of <c>POST /species-profiles</c>: a custom profile with its bands (FR-10, BR-10.2).</summary>
/// <param name="Name">Display name; required, at most 80 characters, unique per owner.</param>
/// <param name="ScientificName">Latin name, for the picker's subtitle; at most 120 characters.</param>
/// <param name="ClimateZone">One of <c>Tropical</c>, <c>SemiArid</c>, <c>Arid</c>, <c>Temperate</c>; required,
/// because it is what the plausibility hint is judged against (BR-10.5).</param>
/// <param name="PhotoperiodHours">Length of the photoperiod; defaults to 12, must be inside a day.</param>
/// <param name="LightsOnLocalTime"><c>HH:mm</c> the photoperiod starts; defaults to <c>07:00</c>.</param>
/// <param name="LightThresholdLux">Illuminance above which time counts towards <c>lightHours</c>; defaults to 1000.</param>
/// <param name="MinLightHoursPerDay">Hours of light below which a deficit is signalled; defaults to 8.</param>
/// <param name="Notes">Husbandry summary and caveats; at most 1000 characters.</param>
/// <param name="Bands">The bands the profile ships; at least one, and a metric and phase may appear once each.</param>
public sealed record CreateSpeciesProfileRequest(
    string? Name,
    string? ScientificName,
    string? ClimateZone,
    decimal? PhotoperiodHours,
    string? LightsOnLocalTime,
    decimal? LightThresholdLux,
    decimal? MinLightHoursPerDay,
    string? Notes,
    IReadOnlyList<SpeciesProfileBandInput>? Bands);

/// <summary>
/// Body of <c>PATCH /species-profiles/{id}</c>. Every member is optional and an **omitted** member is left
/// unchanged — the request is a patch, not a replacement — while sending <c>bands</c> replaces the whole set,
/// because merging bands field by field would let a caller edit a band into a state neither it nor the request
/// described. An empty string clears <see cref="ScientificName"/> or <see cref="Notes"/>, since JSON cannot tell
/// "absent" from "null" once the body is bound to this record.
/// </summary>
/// <param name="Name">New display name; omitted leaves the current one.</param>
/// <param name="ScientificName">New Latin name; empty string clears it.</param>
/// <param name="ClimateZone">New climate zone; omitted leaves the current one.</param>
/// <param name="PhotoperiodHours">New photoperiod length; omitted leaves the current one.</param>
/// <param name="LightsOnLocalTime">New <c>HH:mm</c> start; omitted leaves the current one.</param>
/// <param name="LightThresholdLux">New light threshold; omitted leaves the current one.</param>
/// <param name="MinLightHoursPerDay">New minimum light hours; omitted leaves the current one.</param>
/// <param name="Notes">New notes; empty string clears them.</param>
/// <param name="Bands">When present, the profile's complete band set after the change.</param>
public sealed record UpdateSpeciesProfileRequest(
    string? Name,
    string? ScientificName,
    string? ClimateZone,
    decimal? PhotoperiodHours,
    string? LightsOnLocalTime,
    decimal? LightThresholdLux,
    decimal? MinLightHoursPerDay,
    string? Notes,
    IReadOnlyList<SpeciesProfileBandInput>? Bands);

/// <summary>
/// Body of <c>POST /species-profiles/{id}/duplicate</c>. The copy is always a custom profile owned by the caller,
/// which is the point of the route: a built-in is immutable (FR-10 A2), so the only way to tune one is to copy it.
/// </summary>
/// <param name="Name">Name for the copy; omitted derives <c>"&lt;source&gt; (copy)"</c>.</param>
public sealed record DuplicateSpeciesProfileRequest(string? Name);

/// <summary>
/// One band as the API returns it, in the same spelling the thresholds read surface uses: the metric is a
/// dictionary key and the phase is lowercase (BR-10.3's vocabulary, `02-design/04` §5).
/// </summary>
/// <param name="Metric">Metric dictionary key, e.g. <c>tempC</c>.</param>
/// <param name="Unit">Unit the bounds are in.</param>
/// <param name="Phase">Phase the band applies in: <c>any</c> when the metric is not split day/night.</param>
/// <param name="TargetMin">Lower bound of the target band.</param>
/// <param name="TargetMax">Upper bound of the target band.</param>
/// <param name="CriticalMin">Lower critical bound, null when the band relies on the derived margin.</param>
/// <param name="CriticalMax">Upper critical bound, null when the band relies on the derived margin.</param>
/// <param name="DwellWarnMinutes">Minutes out of band before a Warning.</param>
/// <param name="DwellCritMinutes">Minutes critically out of band before escalation.</param>
/// <param name="RecoveryMargin">Hysteresis margin the value must clear to count as recovered.</param>
/// <param name="Enabled">False means the band is stored but skipped by the evaluator.</param>
/// <param name="SourceRef">Where the numbers came from; the built-in profiles carry a citation here.</param>
/// <param name="SourceUrl">Link to the source, when there is one that has been checked.</param>
public sealed record SpeciesProfileBandView(
    string Metric,
    string Unit,
    string Phase,
    decimal TargetMin,
    decimal TargetMax,
    decimal? CriticalMin,
    decimal? CriticalMax,
    int DwellWarnMinutes,
    int DwellCritMinutes,
    decimal RecoveryMargin,
    bool Enabled,
    string? SourceRef,
    string? SourceUrl);

/// <summary>
/// A species profile as the API returns it. The bands travel with it: the set is a handful of rows and the only
/// consumer is the picker and the editor, which need the numbers — a second request per profile would make the
/// picker in `POST /terrariums` a round trip per row.
/// </summary>
/// <param name="Id">Identity, used by the patch, duplicate and delete routes.</param>
/// <param name="Name">Display name.</param>
/// <param name="ScientificName">Latin name, empty string when the profile has none.</param>
/// <param name="ClimateZone">Zone the plausibility hint is judged against (BR-10.5).</param>
/// <param name="IsBuiltIn">True for the seeded profiles, which are read-only (FR-10 A2).</param>
/// <param name="CanEdit">
/// True when this caller may edit the profile: it is one of their own rather than a built-in. The client uses it to
/// decide between showing the edit controls and offering "duplicate it instead", so the rule lives here rather than
/// being re-derived from <paramref name="IsBuiltIn"/> on the wire.
/// </param>
/// <param name="PhotoperiodHours">Length of the photoperiod.</param>
/// <param name="LightsOnLocalTime"><c>HH:mm</c> the photoperiod starts.</param>
/// <param name="LightThresholdLux">Illuminance above which time counts towards <c>lightHours</c>.</param>
/// <param name="MinLightHoursPerDay">Hours of light below which a deficit is signalled.</param>
/// <param name="Notes">Husbandry summary and caveats, null when there are none.</param>
/// <param name="Bands">The profile's bands, ordered by metric then phase.</param>
/// <param name="ImageUrl">
/// Path of the profile's picture, null when it has none. Relative to the API's own origin, and it needs the
/// caller's bearer token like every other read — so a client *fetches* it rather than pointing an <c>img</c> tag
/// at it, which is what <c>web/src/api</c> does. Null is the ordinary state of a profile, not a fault: three of the
/// four built-ins ship without a picture, and the UI shows nothing rather than an empty frame.
/// </param>
/// <param name="ImageUpdatedAt">
/// When the picture was last set. It is the cache-busting value for <see cref="ImageUrl"/> — a client that keeps
/// the bytes should key them on this, because replacing a picture does not change its URL.
/// </param>
public sealed record SpeciesProfileView(
    Guid Id,
    string Name,
    string ScientificName,
    string ClimateZone,
    bool IsBuiltIn,
    bool CanEdit,
    decimal PhotoperiodHours,
    string LightsOnLocalTime,
    decimal LightThresholdLux,
    decimal MinLightHoursPerDay,
    string? Notes,
    IReadOnlyList<SpeciesProfileBandView> Bands,
    string? ImageUrl,
    DateTimeOffset? ImageUpdatedAt);

/// <summary>
/// A picture handed to <c>POST /species-profiles/{id}/image</c>: the bytes, and the media type the caller *claimed*
/// for them.
/// </summary>
/// <remarks>
/// The claim is inspected and then discarded — what the profile records is the type the bytes were sniffed as. A
/// caller's word for what its file is cannot be the reason a browser later renders it as an image, which is the
/// difference between a picture and an uploaded payload with an image's name.
/// </remarks>
/// <param name="Content">The uploaded bytes; read to the end, and bounded by the service rather than by the caller.</param>
/// <param name="DeclaredContentType">The multipart part's own <c>Content-Type</c>, checked against the bytes.</param>
public sealed record ProfileImageUpload(Stream Content, string? DeclaredContentType);

/// <summary>A stored picture, ready to stream: its bytes, its sniffed media type, and when it was set.</summary>
/// <param name="Content">Readable stream the caller disposes.</param>
/// <param name="ContentType">Media type recorded when the bytes were stored.</param>
/// <param name="UpdatedAt">When the picture was last set; served as <c>Last-Modified</c>.</param>
public sealed record SpeciesProfileImage(Stream Content, string ContentType, DateTimeOffset UpdatedAt);

/// <summary>
/// The answer to <c>GET /species-profiles</c>. Wrapped in an object rather than returned as a bare array, so a
/// cursor or a count can join it later without breaking every client — the convention <c>GET /terrariums</c> set.
/// </summary>
/// <param name="Items">Built-ins first by name, then the account's own.</param>
public sealed record SpeciesProfileList(IReadOnlyList<SpeciesProfileView> Items);

/// <summary>
/// An expected failure of a profile use case. Field-level violations reuse <see cref="IdentityViolation"/>, which
/// already means "a named field was refused, with a stable code".
/// </summary>
/// <param name="Code">Stable problem code, e.g. <c>builtin_immutable</c>.</param>
/// <param name="Message">Message safe to show the user.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
public sealed record SpeciesProfileProblem(
    string Code,
    string Message,
    IReadOnlyList<IdentityViolation>? Errors = null);

/// <summary>Outcome of a profile use case. At most one payload is set; a null <see cref="Problem"/> means success.</summary>
public sealed record SpeciesProfileOutcome
{
    /// <summary>Set by list.</summary>
    public IReadOnlyList<SpeciesProfileView>? Profiles { get; init; }

    /// <summary>Set by create, duplicate, patch, and the two picture writes.</summary>
    public SpeciesProfileView? Profile { get; init; }

    /// <summary>Set by the picture read, which answers bytes rather than a view.</summary>
    public SpeciesProfileImage? Image { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public SpeciesProfileProblem? Problem { get; init; }

    /// <summary>True when there is no problem.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A success carrying the profile it produced.</summary>
    /// <param name="profile">The profile to return.</param>
    public static SpeciesProfileOutcome Found(SpeciesProfileView profile) => new() { Profile = profile };

    /// <summary>A success listing what the caller may see.</summary>
    /// <param name="profiles">Built-ins plus the caller's own.</param>
    public static SpeciesProfileOutcome Listed(IReadOnlyList<SpeciesProfileView> profiles) =>
        new() { Profiles = profiles };

    /// <summary>A success that produced nothing to return — the delete route, which answers `204`.</summary>
    public static SpeciesProfileOutcome Deleted() => new();

    /// <summary>A success carrying the stored picture.</summary>
    /// <param name="image">The bytes and how to serve them.</param>
    public static SpeciesProfileOutcome WithImage(SpeciesProfileImage image) => new() { Image = image };

    /// <summary>
    /// The profile is visible to the caller but has no picture. A `404` of its own rather than the plain
    /// <c>not_found</c>, because the two mean different things to a client: one says "nothing to show here", the
    /// other says "no such profile", and only the second is a reason to reload a list.
    /// </summary>
    public static SpeciesProfileOutcome ImageNotSet() =>
        Failed(new SpeciesProfileProblem(
            "image_not_set",
            "This species profile has no picture."));

    /// <summary>A failure the caller caused or should see.</summary>
    /// <param name="problem">What went wrong.</param>
    public static SpeciesProfileOutcome Failed(SpeciesProfileProblem problem) => new() { Problem = problem };

    /// <summary>
    /// The profile does not exist, or belongs to someone else — one answer for both, so the routes cannot be used
    /// to enumerate ids or to discover whose profile a guess is (BR-02.2).
    /// </summary>
    public static SpeciesProfileOutcome NotFound() =>
        Failed(new SpeciesProfileProblem("not_found", "No such species profile."));

    /// <summary>
    /// A built-in profile was the target of a write. `403` rather than `404`: the row exists, the caller can see
    /// it, and what refuses them is that it is not theirs to change — which is the distinction a client needs to
    /// offer "duplicate it instead" (FR-10 A2).
    /// </summary>
    public static SpeciesProfileOutcome BuiltInImmutable() =>
        Failed(new SpeciesProfileProblem(
            "builtin_immutable",
            "A built-in profile cannot be changed. Duplicate it into your own profile instead."));

    /// <summary>A profile a terrarium is assigned to cannot be deleted — the foreign key would refuse it, and a
    /// refusal that says why is worth more than a constraint violation (DI-10).</summary>
    public static SpeciesProfileOutcome InUse() =>
        Failed(new SpeciesProfileProblem(
            "profile_in_use",
            "The profile is assigned to at least one terrarium. Move those terrariums to another profile first."));

    /// <summary>
    /// Another request changed the profile first, so this edit was written against a state that no longer exists.
    /// `409` rather than `500`: nothing is broken, and the client's next step is to reload and try again — the
    /// reason the row carries a <c>RowVersion</c> at all.
    /// </summary>
    public static SpeciesProfileOutcome ProfileConflict() =>
        Failed(new SpeciesProfileProblem(
            "profile_conflict",
            "The profile was changed by someone else. Reload it and try again."));

    /// <summary>A failure of input validation, in the same field-level shape the other write surfaces use.</summary>
    /// <param name="errors">One entry per refused field.</param>
    public static SpeciesProfileOutcome Invalid(IReadOnlyList<IdentityViolation> errors) =>
        Failed(new SpeciesProfileProblem("validation_failed", "The request was refused.", errors));

    /// <summary>The caller's account is not one this database has (the same guard as `TerrariumService.CreateAsync`).</summary>
    public static SpeciesProfileOutcome Unauthenticated() =>
        Failed(new SpeciesProfileProblem(
            "unauthenticated",
            "The access token names an account this server does not have. Sign in again."));
}
