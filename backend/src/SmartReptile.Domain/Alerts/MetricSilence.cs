using SmartReptile.Domain.Metrics;

namespace SmartReptile.Domain.Alerts;

/// <summary>
/// A time-boxed, accountable suppression of one terrarium's alerts (FR-13, §02-design/05 §5, roadmap task 3.4).
/// </summary>
/// <remarks>
/// A row says "for this window, these alerts are expected, and here is who decided that and why". It is the only
/// way to tell the system to stop telling a keeper something they already know, which is why the window is capped
/// (<see cref="MaxWindowHours"/>) and the reason is mandatory: an unbounded, unexplained silence is
/// indistinguishable from a broken notifier. Nothing here hides history — alerts are still raised, stored and
/// counted; what a silence removes is the notification (§02-design/05 §4's <c>silenced_metric</c> suppression).
/// <para>
/// <see cref="Metric"/> is nullable, and null means <b>every metric</b> of the terrarium including its device-level
/// sources: a keeper going away for the weekend silences the box, not five metrics one at a time. Narrowing it to
/// a metric is the precise form and the common case.
/// </para>
/// </remarks>
public class MetricSilence
{
    /// <summary>Longest window a single silence may cover (§02-design/05 §5: "≤ 24 h").</summary>
    public const int MaxWindowHours = 24;

    /// <summary>Column width of <see cref="Reason"/>. Short by design: it is an explanation, not a note.</summary>
    public const int ReasonMaxLength = 200;

    /// <summary>
    /// Surrogate key; named in the cancel route and on the dashboard.
    /// </summary>
    /// <remarks>
    /// A <see cref="Guid"/> rather than an identity column, unlike its neighbours, for one reason: the key is known
    /// before the insert, so the audit row that records who asked for this window can name it in the same unit of
    /// work. An identity would make the trail either wrong (id 0) or a second commit, and a silence without its
    /// audit row in the same transaction is exactly the gap the audit rule forbids.
    /// </remarks>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Terrarium whose alerts are suppressed.</summary>
    public Guid TerrariumId { get; set; }

    /// <summary>Metric suppressed, or null for every metric of the terrarium.</summary>
    public MetricCode? Metric { get; set; }

    /// <summary>Instant the suppression ends.</summary>
    public DateTimeOffset UntilUtc { get; set; }

    /// <summary>Why the keeper silenced it.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Who silenced it.</summary>
    public Guid CreatedByUserId { get; set; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it was cancelled early, if it was.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>True while the row still suppresses something.</summary>
    /// <param name="instant">Instant to judge at.</param>
    public bool IsActiveAt(DateTimeOffset instant) => CancelledAt is null && UntilUtc > instant;

    /// <summary>True when this silence covers <paramref name="metric"/>.</summary>
    /// <param name="metric">Metric an alert is about.</param>
    public bool Covers(MetricCode metric) => Metric is null || Metric == metric;
}
