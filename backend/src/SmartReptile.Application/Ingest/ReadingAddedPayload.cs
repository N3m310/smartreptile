using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// The <c>readingAdded</c> event of `07-appendices/03` §6: what a subscribed client receives after a sample
/// commits. One event per sample, not per batch, because the contract's payload is a single sample.
/// </summary>
/// <param name="TerrariumId">Terrarium the sample belongs to — the group it was pushed to.</param>
/// <param name="SampleId">Identity assigned by the insert, so a client can request history around it.</param>
/// <param name="RecordedAt">Device timestamp as stored.</param>
/// <param name="Metrics">The sample's values.</param>
public sealed record ReadingAddedPayload(
    Guid TerrariumId,
    long SampleId,
    DateTimeOffset RecordedAt,
    IReadOnlyList<ReadingAddedMetric> Metrics)
{
    /// <summary>Projects one committed sample onto the documented shape.</summary>
    /// <param name="sample">The sample as it was written.</param>
    /// <returns>The event body.</returns>
    public static ReadingAddedPayload From(PersistedSample sample) => new(
        sample.TerrariumId,
        sample.SampleId,
        sample.RecordedAt,
        [.. sample.Readings.Select(reading => ReadingAddedMetric.From(reading, sample.Flags))]);
}

/// <summary>
/// One metric value inside a <c>readingAdded</c> event.
/// </summary>
/// <param name="Code">
/// The metric's <see cref="MetricDefinition.ApiKey"/> — the same key the REST surface uses, deliberately, so one
/// client can key both the live event and a later history request off the same string.
/// </param>
/// <param name="Value">Post-calibration value, in the metric's own unit.</param>
/// <param name="Unit">Unit string from the metric dictionary.</param>
/// <param name="QualityFlags">
/// The sample's quality bitmask. Per-metric in the payload because the contract asks for it there, but the
/// pipeline records quality per sample: calibration offsets are per device, not per metric, so a flag always
/// describes the sample it arrived in.
/// </param>
public sealed record ReadingAddedMetric(string Code, decimal Value, string Unit, int QualityFlags)
{
    /// <summary>Projects one reading of a sample.</summary>
    /// <param name="reading">The stored reading.</param>
    /// <param name="flags">Quality bitmask of the sample that carried it.</param>
    /// <returns>The metric body.</returns>
    public static ReadingAddedMetric From(TelemetryReading reading, QualityFlags flags)
    {
        var definition = MetricDictionary.Get(reading.Code);

        return new ReadingAddedMetric(definition.ApiKey, reading.Value, definition.Unit, (int)flags);
    }
}
