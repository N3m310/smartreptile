using System.Globalization;
using System.Resources;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Notifications;

/// <summary>One rendered message: what every channel carries, and where it points.</summary>
/// <param name="Title">Rendered title.</param>
/// <param name="Body">Rendered body.</param>
/// <param name="DeepLink">Client deep link.</param>
public readonly record struct NotificationContent(string Title, string Body, string DeepLink);

/// <summary>Everything the content contract of §02-design/05 §7 renders from.</summary>
/// <param name="AlertId">Alert the message is about (the deep link carries it).</param>
/// <param name="TerrariumName">Terrarium display name.</param>
/// <param name="Severity">Severity the message carries.</param>
/// <param name="Source">What produced the alert, which selects the sentence.</param>
/// <param name="Metric">Metric, or null for a device-level alert.</param>
/// <param name="Value">Triggering or peak value, when there is one.</param>
/// <param name="BandMin">Target band lower bound.</param>
/// <param name="BandMax">Target band upper bound.</param>
/// <param name="TriggeredAt">Start of the episode, used by the sentences that name an instant rather than a value.</param>
/// <param name="Duration">How long the episode has lasted.</param>
/// <param name="Step">Escalation step; 0 is the first delivery.</param>
public readonly record struct NotificationContentInput(
    long AlertId,
    string TerrariumName,
    AlertSeverity Severity,
    AlertSource Source,
    MetricCode? Metric,
    decimal? Value,
    decimal? BandMin,
    decimal? BandMax,
    DateTimeOffset TriggeredAt,
    TimeSpan Duration,
    byte Step);

/// <summary>
/// The notification content contract (§02-design/05 §7) and its localisation (ADR-013).
/// </summary>
/// <remarks>
/// <b>Why resources rather than a switch.</b> The text is user-facing, so it belongs to the user's language and not
/// to the database — the same rule ADR-013 states for every other string the product shows. Two decks ship
/// (neutral and <c>vi</c>), a recipient whose language has no deck falls back to the neutral one, and the test that
/// guards this asserts the two decks carry the same keys, because a missing key would silently ship English to a
/// Vietnamese keeper.
/// <para>
/// <b>What the sentences deliberately do not say.</b> §7 forbids jargon in user-facing text, so "dwell" and
/// "hysteresis" appear nowhere. And the Critical sentence names the <em>value</em> rather than the critical bound:
/// the alert row denormalises the target band it was judged against, not the critical one, so a bound in this
/// sentence would have to be read from the thresholds of *today* — which may have moved since the alert fired.
/// Repeating a value the alert actually recorded is honest; inventing a bound it never saw is not.
/// </para>
/// </remarks>
public static class NotificationText
{
    /// <summary>Deep-link scheme the app registers (BR-13.6): <c>smartreptile://alerts/{id}</c>.</summary>
    public const string DeepLinkScheme = "smartreptile";

    private static readonly ResourceManager Strings = new(
        "SmartReptile.Application.Resources.NotificationStrings",
        typeof(NotificationText).Assembly);

    /// <summary>Renders one alert message for one language.</summary>
    /// <param name="input">What to render.</param>
    /// <param name="language">Recipient's preferred language.</param>
    public static NotificationContent Render(NotificationContentInput input, string language)
    {
        var culture = CultureOf(language);
        var metricName = MetricName(input.Metric, culture);
        var minutes = (int)Math.Round(input.Duration.TotalMinutes, MidpointRounding.AwayFromZero);

        var title = input.Metric is null
            ? input.Source switch
            {
                AlertSource.DeviceSilent => Get("AlertTitleSilent", culture, input.TerrariumName),
                AlertSource.SensorFault => Get("AlertTitleSensorFault", culture, input.TerrariumName, metricName),
                AlertSource.DeviceClockSkew => Get("AlertTitleClockSkew", culture, input.TerrariumName),
                AlertSource.Gradient => Get("AlertTitleGradient", culture, input.TerrariumName),
                AlertSource.LightDeficit => Get("AlertTitleLightDeficit", culture, input.TerrariumName),
                _ => Get("AlertTitle", culture, input.TerrariumName, metricName, Direction(input, culture)),
            }
            : Get("AlertTitle", culture, input.TerrariumName, metricName, Direction(input, culture));

        var body = Body(input, culture, metricName, minutes);

        if (input.Step > 0)
        {
            body = Get("EscalationBody", culture, body, minutes);
        }

        return new NotificationContent(title, body, $"{DeepLinkScheme}://alerts/{input.AlertId}");
    }

    /// <summary>Renders the hourly digest of §5.</summary>
    /// <param name="terrariumName">Terrarium the digest is about.</param>
    /// <param name="summary">What was coalesced.</param>
    /// <param name="language">Recipient's preferred language.</param>
    public static NotificationContent RenderDigest(
        string terrariumName,
        DigestSummary summary,
        string language)
    {
        var culture = CultureOf(language);
        var title = Get("DigestTitle", culture, terrariumName);

        string body;

        if (summary.WorstMetric is { } metric && summary.PeakValue is { } peak)
        {
            var unit = MetricDictionary.Get(metric).Unit;
            body = Get(
                "DigestBody",
                culture,
                summary.Total.ToString(culture),
                summary.Warnings.ToString(culture),
                summary.Criticals.ToString(culture),
                MetricName(metric, culture),
                $"{Format(peak, metric, culture)} {unit}");
        }
        else
        {
            body = Get(
                "DigestBodyNoPeak",
                culture,
                summary.Total.ToString(culture),
                summary.Warnings.ToString(culture),
                summary.Criticals.ToString(culture));
        }

        return new NotificationContent(
            NotificationLog.Truncate(title, NotificationLog.TitleMaxLength),
            NotificationLog.Truncate(body, NotificationLog.BodyMaxLength),
            $"{DeepLinkScheme}://terrariums");
    }

    /// <summary>The localised display name of a metric.</summary>
    /// <param name="metric">Metric, or null.</param>
    /// <param name="language">Recipient's preferred language.</param>
    public static string MetricName(MetricCode? metric, string language) =>
        MetricName(metric, CultureOf(language));

    /// <summary>Every resource key this class reads. Used by the test that keeps the two decks in step.</summary>
    public static IReadOnlyCollection<string> Keys { get; } =
    [
        "AlertTitle", "DirectionHigh", "DirectionLow", "AlertBody", "AlertBodyNoBand", "AlertBodyCritical",
        "AlertTitleSilent", "AlertTitleSensorFault", "AlertTitleClockSkew", "AlertTitleGradient",
        "AlertTitleLightDeficit", "AlertBodySilent", "AlertBodySkew", "EscalationBody", "DigestTitle",
        "DigestBody", "DigestBodyNoPeak",
    ];

    private static string Body(
        NotificationContentInput input,
        CultureInfo culture,
        string metricName,
        int minutes)
    {
        var value = input.Metric is { } metric ? Format(input.Value, metric, culture) : string.Empty;
        var unit = input.Metric is { } measured ? MetricDictionary.Get(measured).Unit : string.Empty;

        string body = input.Source switch
        {
            AlertSource.DeviceSilent => Get(
                "AlertBodySilent",
                culture,
                input.TriggeredAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC",
                minutes.ToString(culture)),

            AlertSource.DeviceClockSkew => Get(
                "AlertBodySkew",
                culture,
                minutes.ToString(culture)),

            _ when input.BandMin is { } min && input.BandMax is { } max => Get(
                "AlertBody",
                culture,
                value,
                unit,
                Format(min, input.Metric!.Value, culture),
                Format(max, input.Metric!.Value, culture),
                minutes.ToString(culture)),

            _ => Get("AlertBodyNoBand", culture, minutes.ToString(culture)),
        };

        if (input.Severity == AlertSeverity.Critical && input.Metric is not null)
        {
            body = $"{body} {Get("AlertBodyCritical", culture, value, unit)}";
        }

        return NotificationLog.Truncate(body, NotificationLog.BodyMaxLength);
    }

    private static string Direction(NotificationContentInput input, CultureInfo culture)
    {
        var high = input.BandMax is { } max && input.Value is { } value
            ? value >= max
            : input.Value is null || input.BandMin is not { } min || input.Value >= min;

        return Get(high ? "DirectionHigh" : "DirectionLow", culture);
    }

    private static string Format(decimal? value, MetricCode metric, CultureInfo culture) =>
        value is { } number
            ? number.ToString($"F{MetricDictionary.Get(metric).Precision}", culture)
            : "-";

    private static string MetricName(MetricCode? metric, CultureInfo culture) =>
        metric is { } code
            ? Get($"Metric_{MetricDictionary.Get(code).ApiKey}", culture)
            : string.Empty;

    private static CultureInfo CultureOf(string language) =>
        string.IsNullOrWhiteSpace(language)
            ? CultureInfo.InvariantCulture
            : CultureInfo.GetCultureInfo(language);

    private static string Get(string key, CultureInfo culture, params object?[] arguments)
    {
        var template = Strings.GetString(key, culture) ?? key;

        return arguments.Length == 0 ? template : string.Format(culture, template, arguments);
    }
}
