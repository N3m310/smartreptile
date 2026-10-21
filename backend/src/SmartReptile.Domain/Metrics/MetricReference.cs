using SmartReptile.Domain.Alerts;

namespace SmartReptile.Domain.Metrics;

/// <summary>
/// One row of the metric catalogue: the reference row every table that names a metric points at (DI-06, ADR-004).
/// </summary>
/// <remarks>
/// <b>Why a table when <see cref="MetricDictionary"/> already defines the same seven metrics.</b> The dictionary is
/// the behaviour — the rules the validator, the evaluator and the wire format follow — and it must stay in code
/// because a metric is only meaningful together with its bounds and its plausibility window. This table is the
/// <i>relational</i> half of the same fact: without it, eight independent <c>int</c> columns named <c>Metric</c>
/// could hold a number that means nothing to any other table, and nothing in the schema would stop it. With it, every
/// one of them is a foreign key, so a reading, a threshold, a silence, an alert and a rollup row cannot disagree
/// about what metric 3 is — and a reader can join to get the name instead of decoding it from C# source.
/// <para>
/// <b>Same pattern as <c>Permission</c> and <c>TimeZone</c>.</b> The rows are written by the migration so a foreign
/// key can be added to a populated database, and a start-up step keeps the labels in step with the dictionary, which
/// is what owns them. A metric added to the dictionary later appears here without a migration.
/// </para>
/// <para>
/// <b>Keyed by <see cref="MetricCode"/>, not by the API key.</b> The enum is already the stored value everywhere, so
/// keying by it means the foreign keys cost nothing and no existing row has to be rewritten. The API key is the
/// natural key — unique, and what a client sends — but making it the primary key would widen every one of those
/// eight indexes from 4 bytes to 40, for a lookup the server already does in memory.
/// </para>
/// </remarks>
public class MetricReference
{
    /// <summary>The metric this row describes. Primary key; the value every other table stores.</summary>
    public MetricCode Code { get; set; }

    /// <summary>Short key used in REST responses and query strings, e.g. <c>tempC</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Key the firmware uses inside <c>samples[]</c> on the telemetry topic, e.g. <c>tf</c>.</summary>
    public string PayloadKey { get; set; } = string.Empty;

    /// <summary>Human-readable name, shown next to the value.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Unit string, e.g. <c>°C</c>. Stored here so a reader of the database does not have to guess it.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Decimal places kept when formatting.</summary>
    public int Precision { get; set; }

    /// <summary>Lowest physically plausible value; outside is flagged, never rejected (rule V-06).</summary>
    public decimal PlausibleMin { get; set; }

    /// <summary>Highest physically plausible value; outside is flagged, never rejected (rule V-06).</summary>
    public decimal PlausibleMax { get; set; }

    /// <summary>Core metrics are required in every species profile.</summary>
    public bool IsCore { get; set; }

    /// <summary>Which alert family an excursion belongs to; the same split the dictionary records.</summary>
    public AlertFamily Family { get; set; }

    /// <summary>Position in the metric picker, kept in dictionary order.</summary>
    public int SortOrder { get; set; }
}
