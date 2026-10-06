using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Application.Terrariums;

/// <summary>
/// The metric status vocabulary shared by the API, the app and the dashboard (`02-design/04` §5). These strings
/// are the wire contract: the Flutter client parses them in <c>core/status.dart</c> and the dashboard in
/// <c>web/legacy/js/i18n.js</c>. Adding a value is an API change, not a client change.
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
/// Create input. Nullable members so a missing field becomes a field-level violation rather than a
/// deserialisation failure, exactly as in the onboarding contracts.
/// </summary>
/// <param name="Name">Display name, required, at most 60 characters.</param>
/// <param name="SpeciesProfileId">Profile whose bands apply until an override is authored (FR-10).</param>
/// <param name="Location">Free-text location label; users are advised not to enter an address.</param>
/// <param name="Description">Keeper notes.</param>
/// <param name="TimeZoneId">IANA/Windows zone id; defaults to the configured system default (ADR-015).</param>
public sealed record CreateTerrariumRequest(
    string? Name,
    Guid SpeciesProfileId,
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
    int OpenAlertCount);

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

    /// <summary>Set when the use case failed.</summary>
    public TerrariumProblem? Problem { get; init; }

    /// <summary>True when no problem was reported.</summary>
    public bool Succeeded => Problem is null;

    /// <summary>A list result.</summary>
    public static TerrariumOutcome Listed(IReadOnlyList<TerrariumSummary> items) => new() { Terrariums = items };

    /// <summary>A single terrarium (get or create).</summary>
    public static TerrariumOutcome Found(TerrariumSummary summary) => new() { Terrarium = summary };

    /// <summary>A latest-readings snapshot.</summary>
    public static TerrariumOutcome Snapshot(LatestReadings readings) => new() { Readings = readings };

    /// <summary>A bucketed series.</summary>
    public static TerrariumOutcome Charted(ReadingSeries series) => new() { Series = series };

    /// <summary>A coverage report.</summary>
    public static TerrariumOutcome Measured(CoverageReport coverage) => new() { Coverage = coverage };

    /// <summary>
    /// A missing or foreign terrarium. Both answer identically so ids cannot be probed (BR-02.2), which is why the
    /// code is <c>not_found</c> and not <c>forbidden</c>.
    /// </summary>
    public static TerrariumOutcome NotFound() =>
        new() { Problem = new TerrariumProblem("not_found", "No such terrarium.") };

    /// <summary>A failure with a stable code and a non-disclosing message.</summary>
    public static TerrariumOutcome Failed(string code, string message) =>
        new() { Problem = new TerrariumProblem(code, message) };

    /// <summary>A failure carrying field-level validation errors.</summary>
    public static TerrariumOutcome Invalid(IReadOnlyList<IdentityViolation> errors, string message = "The request was refused.") =>
        new() { Problem = new TerrariumProblem("validation_failed", message, errors) };
}
