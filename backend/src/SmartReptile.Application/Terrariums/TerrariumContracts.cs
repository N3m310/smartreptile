using System.Text.Json.Serialization;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Terrariums;

/// <summary>
/// The metric status vocabulary shared by the API, the app and the web client (`02-design/04` §5). These strings
/// are the wire contract: the Flutter client parses them in <c>core/status.dart</c> and the web client in
/// <c>web/src/lib/status.ts</c>. Adding a value is an API change, not a client change.
/// </summary>
public static class ReadingStatus
{
    /// <summary>Inside the effective target band.</summary>
    public const string InRange = "InRange";

    /// <summary>Outside the target band but inside the critical band.</summary>
    public const string OutOfRange = "OutOfRange";

    /// <summary>Outside the critical band.</summary>
    public const string Critical = "Critical";

    /// <summary>No usable value.</summary>
    public const string NoData = "NoData";

    /// <summary>Sensor fault or implausible value: the reading is stored but not evaluable (BR-11.1).</summary>
    public const string Unavailable = "Unavailable";

    /// <summary>Device in maintenance: alerts are recorded but not notified (BR-12.6).</summary>
    public const string Maintenance = "Maintenance";
}

/// <summary>Wire spelling of <see cref="DeviceStatus"/>, which the clients compare against lowercase words.</summary>
public static class DeviceStatusNames
{
    /// <summary>The stored status as the clients expect it: <c>online</c>, <c>offline</c>, …</summary>
    public static string Of(DeviceStatus status) => status switch
    {
        DeviceStatus.Online => "online",
        DeviceStatus.Offline => "offline",
        DeviceStatus.Maintenance => "maintenance",
        DeviceStatus.Revoked => "revoked",
        _ => "provisioning",
    };
}

/// <summary>
/// Wire spelling of the two threshold vocabularies, lower case so both clients compare against literals the way
/// they already do for <see cref="ReadingStatus"/> and <see cref="DeviceStatusNames"/>. <c>source</c> is the
/// vocabulary `07-appendices/03` §4.2 documents: <c>override | profile | default</c>.
/// </summary>
public static class ThresholdNames
{
    /// <summary>Which layer a band came from.</summary>
    public static string Source(ThresholdSource source) => source switch
    {
        ThresholdSource.Override => "override",
        ThresholdSource.Profile => "profile",
        _ => "default",
    };

    /// <summary>Which phase a band applies in.</summary>
    public static string Phase(ThresholdPhase phase) => phase switch
    {
        ThresholdPhase.Day => "day",
        ThresholdPhase.Night => "night",
        _ => "any",
    };

    /// <summary>
    /// Parses a phase from a request. An absent value means <see cref="ThresholdPhase.Any"/> — a band that is not
    /// split day/night is the default a client gets when it says nothing — while a value that is present but not in
    /// the vocabulary is a refusal rather than a silent fallback. Case-insensitive, like the metric keys.
    /// </summary>
    public static bool TryParsePhase(string? value, out ThresholdPhase phase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            phase = ThresholdPhase.Any;
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "any":
                phase = ThresholdPhase.Any;
                return true;
            case "day":
                phase = ThresholdPhase.Day;
                return true;
            case "night":
                phase = ThresholdPhase.Night;
                return true;
            default:
                phase = ThresholdPhase.Any;
                return false;
        }
    }
}

/// <summary>
/// One metric's effective band, with everything the editor's preview table needs and the provenance that answers
/// "why is my limit 32 and not 30?" (BR-10.3).
/// </summary>
/// <param name="Metric">Metric dictionary key, the same string the readings surface uses.</param>
/// <param name="Unit">Unit the bounds are in.</param>
/// <param name="Phase">Phase the band was resolved in: <c>any</c> when the metric is not split day/night.</param>
/// <param name="Source">Layer the band came from: <c>override</c> or <c>profile</c>.</param>
/// <param name="TargetMin">Lower bound of the target band.</param>
/// <param name="TargetMax">Upper bound of the target band.</param>
/// <param name="CriticalMin">Lower bound of the critical band, null when the band relies on the derived margin.</param>
/// <param name="CriticalMax">Upper bound of the critical band, null when the band relies on the derived margin.</param>
/// <param name="DwellWarnMinutes">Minutes out of band before a Warning.</param>
/// <param name="DwellCritMinutes">Minutes critically out of band before escalation.</param>
/// <param name="RecoveryMargin">Hysteresis margin the value must clear to count as recovered.</param>
public sealed record EffectiveThresholdView(
    string Metric,
    string Unit,
    string Phase,
    string Source,
    decimal TargetMin,
    decimal TargetMax,
    decimal? CriticalMin,
    decimal? CriticalMax,
    int DwellWarnMinutes,
    int DwellCritMinutes,
    decimal RecoveryMargin);

/// <summary>
/// The answer to <c>GET /terrariums/{id}/thresholds</c> (FR-10, BR-10.3 — roadmap 3.1's read half).
/// </summary>
/// <param name="TerrariumId">Terrarium the bands belong to.</param>
/// <param name="CapturedAtUtc">Instant the resolution was made at, so a phase-dependent answer is dated.</param>
/// <param name="TimeZoneId">Zone the day/night split was computed in (ADR-015).</param>
/// <param name="EffectiveThresholds">
/// One entry per metric that resolves to a band right now. A metric nothing configures for the current phase is
/// <b>absent</b> rather than sent with null bounds — the same convention the latest-readings surface uses for a
/// metric with no reading, and the editor unions this with the metric dictionary to show an unconfigured row.
/// </param>
public sealed record TerrariumThresholds(
    Guid TerrariumId,
    DateTimeOffset CapturedAtUtc,
    string TimeZoneId,
    IReadOnlyList<EffectiveThresholdView> EffectiveThresholds);

/// <summary>
/// One band a client wants to write for a terrarium (UC-03, roadmap 3.1's write half). Nullable members so a
/// missing field becomes a field-level violation rather than a deserialisation failure, the same rule the
/// create/update contracts follow; everything optional carries the documented default.
/// </summary>
/// <param name="Metric">Metric key from the dictionary, e.g. <c>tempC</c>; required.</param>
/// <param name="Phase">Phase the band applies in: <c>any</c> (the default when omitted), <c>day</c> or <c>night</c>.</param>
/// <param name="TargetMin">Lower bound of the target band; required.</param>
/// <param name="TargetMax">Upper bound of the target band; required.</param>
/// <param name="CriticalMin">Lower critical bound; omitted derives it from the target band.</param>
/// <param name="CriticalMax">Upper critical bound; omitted derives it from the target band.</param>
/// <param name="DwellWarnMinutes">Minutes out of band before a Warning; defaults to 5.</param>
/// <param name="DwellCritMinutes">Minutes critically out of band before escalation; defaults to 2.</param>
/// <param name="RecoveryMargin">Distance back inside the band that counts as recovered; defaults to 0.5.</param>
/// <param name="Note">Why this terrarium differs from the profile; at most 300 characters.</param>
/// <param name="Enabled">False disables the override without deleting it, so the profile band applies; defaults to true.</param>
public sealed record ThresholdOverrideInput(
    string? Metric,
    string? Phase,
    decimal? TargetMin,
    decimal? TargetMax,
    decimal? CriticalMin,
    decimal? CriticalMax,
    int? DwellWarnMinutes,
    int? DwellCritMinutes,
    decimal? RecoveryMargin,
    string? Note,
    bool? Enabled);

/// <summary>
/// The body of <c>PUT /terrariums/{id}/thresholds</c> (UC-03). Each entry upserts the override for its
/// (metric, phase); a metric the body does not mention is left alone, and an empty list is a valid no-op.
/// </summary>
/// <param name="Overrides">Bands to upsert. A metric and phase may appear at most once.</param>
public sealed record UpdateThresholdsRequest(IReadOnlyList<ThresholdOverrideInput>? Overrides);

/// <summary>
/// Create input. Nullable members so a missing field becomes a field-level violation rather than a
/// deserialisation failure, exactly as in the onboarding contracts.
/// </summary>
/// <param name="Name">Display name, required, at most 60 characters.</param>
/// <param name="SpeciesProfileId">Profile whose bands apply until an override is authored (FR-10).</param>
/// <param name="Location">Free-text location label; users are advised not to enter an address.</param>
/// <param name="Description">Keeper notes.</param>
/// <param name="TimeZoneId">IANA/Windows zone id; defaults to the configured system default (ADR-015).</param>
/// <param name="DeviceId">
/// Optional public id of a board to bind in the same request (<c>sr-3f9a2c</c>, TASKS 2026-10-14, item 2). Omitted
/// or null is exactly the behaviour that existed before: create the enclosure, claim a board afterwards.
/// </param>
/// <param name="MacAddress">
/// Optional MAC address of a board to bind, for the caller who has the board in hand but no public id to hand
/// (item 8's lower-trust path). Supplying both this and <paramref name="DeviceId"/> is refused rather than guessed.
/// </param>
public sealed record CreateTerrariumRequest(
    string? Name,
    Guid SpeciesProfileId,
    string? Location,
    string? Description,
    string? TimeZoneId,
    string? DeviceId = null,
    string? MacAddress = null);

/// <summary>
/// Update input (FR-03's update half). Every member is optional and an **omitted** member is left unchanged — the
/// request is a patch, not a replacement. To clear the two free-text fields send an empty string, because JSON
/// cannot distinguish "absent" from "null" once the body is bound to this record.
/// </summary>
/// <param name="Name">New display name; omitted leaves the current one.</param>
/// <param name="SpeciesProfileId">New profile whose bands apply; omitted leaves the current one.</param>
/// <param name="Location">New location label; empty string clears it.</param>
/// <param name="Description">New keeper notes; empty string clears them.</param>
/// <param name="TimeZoneId">New zone for local-day bucketing; omitted leaves the current one.</param>
public sealed record UpdateTerrariumRequest(
    string? Name,
    Guid? SpeciesProfileId,
    string? Location,
    string? Description,
    string? TimeZoneId);

/// <summary>The sensor node behind a terrarium, as the dashboards header shows it.</summary>
/// <param name="DeviceId">Public id (<c>sr-3f9a2c</c>), never the surrogate key.</param>
/// <param name="DeviceName">Owner-chosen display name.</param>
/// <param name="Status">Derived online/offline state (see <see cref="DeviceStatusNames"/>).</param>
/// <param name="FirmwareVersion">Last reported firmware, null when the board has never reported.</param>
/// <param name="LastSeenAt">When the server last heard from the board.</param>
/// <param name="SamplingIntervalSec">Expected report interval; the clients derive staleness from it (BR-07.2).</param>
/// <param name="SignalStrengthDbm">Last reported Wi-Fi signal strength.</param>
/// <param name="BatteryPct">Last reported battery percentage, if the node is battery powered.</param>
/// <param name="UptimeSeconds">Last reported uptime.</param>
public sealed record DeviceSummary(
    string DeviceId,
    string DeviceName,
    string Status,
    string? FirmwareVersion,
    DateTimeOffset? LastSeenAt,
    int SamplingIntervalSec,
    int? SignalStrengthDbm,
    decimal? BatteryPct,
    long? UptimeSeconds);

/// <summary>Target band of one metric, so a card can show the limits it is judged against (BR-10.3).</summary>
/// <param name="Min">Lower bound of the target band.</param>
/// <param name="Max">Upper bound of the target band.</param>
public sealed record MetricBand(decimal Min, decimal Max);

/// <summary>One metric in a latest-readings response.</summary>
/// <param name="Code">Metric dictionary key, e.g. <c>tempC</c>.</param>
/// <param name="Value">Post-calibration value in the metric's own unit.</param>
/// <param name="Unit">Unit string from the metric dictionary.</param>
/// <param name="CapturedAt">Device timestamp of the sample — never omitted, so a value is always dated.</param>
/// <param name="Status">One of the <see cref="ReadingStatus"/> values.</param>
/// <param name="QualityFlags">Quality bitmask of the sample it came from (see the backend <c>QualityFlags</c>).</param>
/// <param name="Target">Effective band, or null when no band is configured for the metric.</param>
public sealed record LatestMetric(
    string Code,
    decimal Value,
    string Unit,
    DateTimeOffset CapturedAt,
    string Status,
    int QualityFlags,
    MetricBand? Target);

/// <summary>
/// The answer to <c>readings/latest</c>: the newest reading per metric plus the device state behind them
/// (§07-appendices/03 §4.2). Shaped for both clients, which read <c>metrics[]</c> and <c>device</c> by name.
/// </summary>
/// <param name="TerrariumId">Terrarium the values belong to.</param>
/// <param name="LastSampleAt">Newest sample in the terrarium, null before the first report.</param>
/// <param name="Device">Bound device, null when none is claimed.</param>
/// <param name="Metrics">One entry per metric that has ever reported; metrics with no reading are omitted rather
/// than sent with a null timestamp, because both clients parse <c>capturedAt</c> unconditionally.</param>
public sealed record LatestReadings(
    Guid TerrariumId,
    DateTimeOffset? LastSampleAt,
    DeviceSummary? Device,
    IReadOnlyList<LatestMetric> Metrics);

/// <summary>A terrarium as the list and detail endpoints return it.</summary>
/// <param name="Id">Surrogate key, the value clients put in the URL.</param>
/// <param name="Name">Display name.</param>
/// <param name="SpeciesProfileId">Assigned profile.</param>
/// <param name="SpeciesName">Profile name, resolved for display so the client needs no second call.</param>
/// <param name="Location">Free-text location label.</param>
/// <param name="Description">Keeper notes.</param>
/// <param name="TimeZoneId">Zone used for local-day bucketing and phase selection.</param>
/// <param name="CreatedAt">Creation timestamp.</param>
/// <param name="UpdatedAt">Last modification timestamp.</param>
/// <param name="Device">Bound device summary, null when none is claimed.</param>
/// <param name="LatestSampleAt">Newest sample, null before the first report.</param>
/// <param name="OpenAlertCount">Alerts not yet resolved; 0 until the evaluator exists (M3).</param>
public sealed record TerrariumSummary(
    Guid Id,
    string Name,
    Guid SpeciesProfileId,
    string SpeciesName,
    string? Location,
    string? Description,
    string TimeZoneId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DeviceSummary? Device,
    DateTimeOffset? LatestSampleAt,
    int OpenAlertCount)
{
    /// <summary>
    /// The concurrency token, base64, repeated from the <c>ETag</c> response header so an update can be authored
    /// by a client that cannot read response headers — Swagger UI is the one that matters, because it renders no
    /// <c>If-Match</c> box of its own and the route was therefore impossible to call from it (FR-03).
    /// </summary>
    /// <remarks>
    /// Set on the single-terrarium answers (<c>GET {id}</c>, <c>POST</c>, <c>PATCH</c>) and absent from list items,
    /// which are read-only and carry no token to send back. The header stays the authoritative HTTP form; this is
    /// the same value, so a client may use either and must not treat the two as different versions.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; init; }

    /// <summary>
    /// The account that owns the terrarium, present only on the answers an administrator receives
    /// (TASKS 2026-10-14, item 5).
    /// </summary>
    /// <remarks>
    /// Absent for every other caller, including a keeper's own list: ownership is not a field a client acts on,
    /// and sending it everywhere would invite one to try. It is set when the caller is an administrator, whose
    /// list and detail answers can name an account they did not ask for — and who needs to know whose enclosure
    /// they are looking at before editing it.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? OwnerUserId { get; init; }

    /// <summary>
    /// The secret of a board bound by the same <c>POST /terrariums</c> that created this enclosure, present only on
    /// that one response (TASKS 2026-10-14, item 2).
    /// </summary>
    /// <remarks>
    /// Binding a board at creation is the same act as claiming it — the board needs its credential to publish, and
    /// the alternative would be a board that can neither report nor be claimed again — so this is
    /// <c>POST /devices/claim</c>'s one-time secret, delivered on the route that performed the claim. It is never
    /// retrievable afterwards: only its hash is stored, and a rotation is the only way to get a new one. Absent from
    /// every other answer, including a create that named no device.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceSecret { get; init; }
}

/// <summary>One bucket of a chart series. Null bounds with <see cref="Count"/> 0 are a gap (BR-09.5).</summary>
/// <param name="T">Bucket start, UTC.</param>
/// <param name="Min">Lowest value in the bucket, null when the bucket is a gap.</param>
/// <param name="Max">Highest value in the bucket, null when the bucket is a gap.</param>
/// <param name="Avg">Mean value in the bucket, null when the bucket is a gap.</param>
/// <param name="Count">Samples behind the bucket; 0 marks a gap that was never interpolated.</param>
public sealed record ReadingPoint(DateTimeOffset T, decimal? Min, decimal? Max, decimal? Avg, int Count);

/// <summary>An alert shown as a shaded overlay on a chart (BR-09.3). Empty until the evaluator exists (M3).</summary>
/// <param name="Id">Alert surrogate key.</param>
/// <param name="Severity">Alert severity name.</param>
/// <param name="From">Start of the episode.</param>
/// <param name="To">End of the episode.</param>
/// <param name="Metric">Metric the episode concerned, null for device-level alerts.</param>
public sealed record AlertInterval(long Id, string Severity, DateTimeOffset From, DateTimeOffset To, string? Metric);

/// <summary>A bucketed chart series (§07-appendices/03 §4.2).</summary>
/// <param name="Metric">Metric key the series is for.</param>
/// <param name="Unit">Unit of the values.</param>
/// <param name="Bucket">Bucket size, echoed so the client can label the axis.</param>
/// <param name="FromUtc">Window start, UTC.</param>
/// <param name="ToUtc">Window end, UTC.</param>
/// <param name="Points">Bucket-aligned points, gaps included as nulls.</param>
/// <param name="Alerts">Alert overlays inside the window.</param>
public sealed record ReadingSeries(
    string Metric,
    string Unit,
    string Bucket,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<ReadingPoint> Points,
    IReadOnlyList<AlertInterval> Alerts);

/// <summary>Expected versus received samples for a window (FR-07, TC-I-08).</summary>
/// <param name="TerrariumId">Terrarium the window belongs to.</param>
/// <param name="FromUtc">Window start, UTC.</param>
/// <param name="ToUtc">Window end, UTC.</param>
/// <param name="SamplingIntervalSec">Interval the expectation was derived from.</param>
/// <param name="ExpectedSamples">Samples the interval implies for the window.</param>
/// <param name="ReceivedSamples">Samples actually stored in the window.</param>
/// <param name="CoveragePct">Received as a percentage of expected, capped at 100.</param>
public sealed record CoverageReport(
    Guid TerrariumId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int SamplingIntervalSec,
    int ExpectedSamples,
    int ReceivedSamples,
    decimal CoveragePct);

/// <summary>
/// An expected failure. Field-level violations reuse <see cref="IdentityViolation"/>, which already means
/// "a named field was refused, with a stable code".
/// </summary>
/// <param name="Code">Stable problem code, e.g. <c>not_found</c>.</param>
/// <param name="Message">Message safe to show the user — never reveals whether a foreign terrarium exists.</param>
/// <param name="Errors">Field-level violations, when the failure was input validation.</param>
public sealed record TerrariumProblem(
    string Code,
    string Message,
    IReadOnlyList<IdentityViolation>? Errors = null);

/// <summary>
/// Outcome of a terrarium use case. At most one payload is set; a null <see cref="Problem"/> means success.
/// Deliberately one type rather than five: every read here can fail the same two ways (not found, bad range), and
/// five return types would make the endpoints five slightly different shapes.
/// </summary>
public sealed record TerrariumOutcome
{
    /// <summary>Set by list.</summary>
    public IReadOnlyList<TerrariumSummary>? Terrariums { get; init; }

    /// <summary>Set by get and create.</summary>
    public TerrariumSummary? Terrarium { get; init; }

    /// <summary>Set by readings/latest.</summary>
    public LatestReadings? Readings { get; init; }

    /// <summary>Set by readings (range).</summary>
    public ReadingSeries? Series { get; init; }

    /// <summary>Set by coverage.</summary>
    public CoverageReport? Coverage { get; init; }

    /// <summary>Set by the effective-thresholds read (roadmap 3.1).</summary>
    public TerrariumThresholds? Thresholds { get; init; }

    /// <summary>Set when the use case failed.</summary>
    public TerrariumProblem? Problem { get; init; }

    /// <summary>
    /// Concurrency token of the terrarium the outcome describes, when it has one. The endpoint publishes it as the
    /// <c>ETag</c> response header a client returns as <c>If-Match</c> on the next update (FR-03), and repeats it
    /// as the item's <see cref="TerrariumSummary.RowVersion"/> so the value is reachable from the body too.
    /// </summary>
    public byte[]? RowVersion { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A list result.</summary>
    public static TerrariumOutcome Listed(IReadOnlyList<TerrariumSummary> items) => new() { Terrariums = items };

    /// <summary>
    /// A single terrarium (get, create or update), with the concurrency token when there is one.
    /// </summary>
    /// <param name="summary">The terrarium as the wire shape describes it.</param>
    /// <param name="rowVersion">Concurrency token, for the answers that publish one.</param>
    /// <param name="deviceSecret">
    /// The one-time secret of a board bound by the same create request, or null when no board was named.
    /// </param>
    public static TerrariumOutcome Found(
        TerrariumSummary summary,
        byte[]? rowVersion = null,
        string? deviceSecret = null) =>
        new()
        {
            Terrarium = deviceSecret is null ? summary : summary with { DeviceSecret = deviceSecret },
            RowVersion = rowVersion,
        };

    /// <summary>A latest-readings snapshot.</summary>
    public static TerrariumOutcome Snapshot(LatestReadings readings) => new() { Readings = readings };

    /// <summary>A bucketed series.</summary>
    public static TerrariumOutcome Charted(ReadingSeries series) => new() { Series = series };

    /// <summary>A coverage report.</summary>
    public static TerrariumOutcome Measured(CoverageReport coverage) => new() { Coverage = coverage };

    /// <summary>An effective-thresholds snapshot.</summary>
    public static TerrariumOutcome Resolved(TerrariumThresholds thresholds) => new() { Thresholds = thresholds };

    /// <summary>
    /// A missing or foreign terrarium. Both answer identically so ids cannot be probed (BR-02.2), which is why the
    /// code is <c>not_found</c> and not <c>forbidden</c>.
    /// </summary>
    public static TerrariumOutcome NotFound() =>
        new() { Problem = new TerrariumProblem("not_found", "No such terrarium.") };

    /// <summary>
    /// The row moved on since the caller read it, so its <c>If-Match</c> is stale (FR-03). Refused rather than
    /// merged: silently overwriting the other edit is the failure the concurrency token exists to prevent.
    /// </summary>
    /// <remarks>
    /// The message names where the replacement comes from, because a client that sent a token has already done the
    /// read and the only useful next step is another one: "reload it" alone leaves a caller who read the
    /// <c>ETag</c> header from a proxy that strips it, or from Swagger UI which shows no header at all, with no
    /// second way to obtain the value.
    /// </remarks>
    public static TerrariumOutcome PreconditionFailed() =>
        new()
        {
            Problem = new TerrariumProblem(
                "precondition_failed",
                "This terrarium changed since it was read, so the ETag that was sent is stale. Read it again — "
                + "GET /api/v1/terrariums/{id} returns the current value as its ETag header and as the rowVersion "
                + "field of the body — and resend the update with that value in If-Match."),
        };

    /// <summary>A device is still bound and the caller did not ask for it to be unbound (FR-03, DI-04).</summary>
    public static TerrariumOutcome DeviceBound() =>
        new()
        {
            Problem = new TerrariumProblem(
                "conflict_device_bound",
                "A device is bound to this terrarium. Revoke it first, or repeat the request with "
                + "allowUnboundDevice=true to unbind and delete it."),
        };

    /// <summary>
    /// A soft delete that succeeded; the response carries no body.
    /// </summary>
    public static TerrariumOutcome Deleted() => new();

    /// <summary>
    /// The terrarium exists but belongs to another account (FR-03). Distinct from <c>not_found</c> so a keeper can
    /// tell a mistyped id from an enclosure that is not theirs — the narrowing of BR-02.2 recorded in the decision
    /// log and in `02-design/06` §3 (TASKS 2026-10-14, item 3).
    /// </summary>
    public static TerrariumOutcome Forbidden() =>
        new()
        {
            Problem = new TerrariumProblem(
                "forbidden_terrarium",
                "This terrarium belongs to another account."),
        };

    /// <summary>
    /// The terrarium was soft-deleted (FR-03, BR-03.3). Its readings, alerts and summaries are still stored, so
    /// answering <c>not_found</c> would say the opposite of the truth; <c>410 Gone</c> says the resource existed and
    /// was removed (TASKS 2026-10-14, item 6).
    /// </summary>
    public static TerrariumOutcome Gone() =>
        new()
        {
            Problem = new TerrariumProblem(
                "terrarium_deleted",
                "This terrarium was deleted. Its readings and alert history are kept, but the enclosure itself is "
                + "gone."),
        };

    /// <summary>A failure with a stable code and a non-disclosing message.</summary>
    public static TerrariumOutcome Failed(string code, string message) =>
        new() { Problem = new TerrariumProblem(code, message) };

    /// <summary>A failure carrying field-level validation errors.</summary>
    public static TerrariumOutcome Invalid(IReadOnlyList<IdentityViolation> errors, string message = "The request was refused.") =>
        new() { Problem = new TerrariumProblem("validation_failed", message, errors) };
}
