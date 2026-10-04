using System.Globalization;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Application.Ingest;

/// <summary>
/// Stage 1 of the ingest pipeline: shape, schema and the timing rules of §02-design/03 §3 (V-01…V-03, V-07…V-09,
/// V-10). Pure — a parsed document and the instant it arrived go in, a batch or the reason there is none comes
/// out, which is what makes the schema rules testable without a broker, a database or a serialiser.
/// </summary>
/// <remarks>
/// The distinction the rules care about, and the one this class implements:
/// <list type="bullet">
/// <item>a <b>batch-level</b> fault (no <c>seq</c>, no <c>ts</c>, too many samples) refuses the whole batch —
/// the device has a bug and must retry with a fixed payload;</item>
/// <item>an <b>unknown metric key</b> drops only that reading and logs it (rule V-10) — otherwise a firmware
/// update that adds a metric would be refused by a backend that is merely older, which is how forward
/// compatibility is lost.</item>
/// </list>
/// It deliberately does <b>not</b> judge plausibility or apply calibration: those are later stages, so a
/// flagged-but-stored sample (rule V-06) can never be confused with a refused batch.
/// </remarks>
public sealed class TelemetryPayloadValidator
{
    /// <summary>Bits 0…5 are defined on <see cref="QualityFlags"/>; anything above is a firmware bug, not data.</summary>
    private const int KnownQualityBits = 0b111111;

    /// <summary>Validates one parsed payload.</summary>
    /// <param name="document">The parsed batch.</param>
    /// <param name="limits">Configured batch limits.</param>
    /// <param name="receivedAt">Server timestamp at arrival; the reference the timing rules measure against.</param>
    /// <param name="source">Transport the batch arrived on, carried through to the stored row.</param>
    public TelemetryValidation Validate(
        TelemetryPayloadDocument document,
        TelemetryValidationLimits limits,
        DateTimeOffset receivedAt,
        IngestSource source)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.DeviceId))
        {
            return TelemetryValidation.Invalid("schema_invalid", "The batch does not name a device.");
        }

        if (document.Sequence is null or <= 0)
        {
            return TelemetryValidation.Invalid("schema_invalid", "The batch has no usable sequence number.");
        }

        if (string.IsNullOrWhiteSpace(document.FirmwareVersion)
            || document.FirmwareVersion.Length > TelemetryIngestRules.MaxFirmwareLength)
        {
            return TelemetryValidation.Invalid("schema_invalid", "The batch reports no usable firmware version.");
        }

        if (!TryParseBatchTimestamp(document.Timestamp, out var batchTimestamp))
        {
            return TelemetryValidation.Invalid("schema_invalid", "The batch timestamp is not an ISO-8601 UTC instant.");
        }

        if (document.Samples is null || document.Samples.Count == 0)
        {
            return TelemetryValidation.Invalid("schema_invalid", "The batch carries no samples.");
        }

        // The batch-level size rule is checked before any per-sample rule, so 121 samples with one bad offset is
        // still reported as the size problem: it is the fault the device has to fix first.
        if (document.Samples.Count > limits.MaxSamplesPerBatch)
        {
            return TelemetryValidation.Invalid(
                "payload_too_large",
                $"The batch carries {document.Samples.Count} samples; the limit is {limits.MaxSamplesPerBatch}.");
        }

        var samples = new List<TelemetrySampleDraft>(document.Samples.Count);
        var droppedKeys = new List<string>();
        var previousOffset = int.MinValue;

        for (var index = 0; index < document.Samples.Count; index++)
        {
            var sample = document.Samples[index];

            if (sample.OffsetSeconds is null)
            {
                return TelemetryValidation.Invalid("schema_invalid", "A sample has no time offset.");
            }

            var offset = sample.OffsetSeconds.Value;

            if (offset <= previousOffset)
            {
                return TelemetryValidation.Invalid(
                    "schema_invalid",
                    "Sample offsets must be strictly increasing within a batch.");
            }

            if (offset > limits.MaxOffsetSeconds)
            {
                return TelemetryValidation.Invalid(
                    "schema_invalid",
                    $"A sample is {offset} s from the batch timestamp; the limit is {limits.MaxOffsetSeconds} s.");
            }

            previousOffset = offset;

            var knownButUnusable = sample.InvalidKeys
                .FirstOrDefault(key => MetricDictionary.TryParsePayloadKey(key, out _));

            if (knownButUnusable is not null)
            {
                return TelemetryValidation.Invalid(
                    "schema_invalid",
                    $"Metric '{knownButUnusable}' was present but did not carry a number.");
            }

            var readings = BuildReadings(sample, droppedKeys);

            if (readings.Count == 0)
            {
                return TelemetryValidation.Invalid(
                    "schema_invalid",
                    "A sample carries no metric this build knows about.");
            }

            var baseFlags = (QualityFlags)((sample.Quality ?? 0) & KnownQualityBits);
            var timing = TelemetryIngestRules.Apply(batchTimestamp.AddSeconds(offset), receivedAt, baseFlags);

            // The wire contract sends the batch's first seq with the samples in order, so the n-th sample of the
            // batch is the n-th sequence the device produced. That is what makes (DeviceId, Sequence) usable as the
            // dedupe key for a whole batch rather than only for its first row (DI-02).
            var sequence = document.Sequence.Value + index;

            samples.Add(new TelemetrySampleDraft(
                sequence,
                timing.RecordedAt,
                timing.ClockSkewSeconds,
                timing.Flags,
                readings));
        }

        var batch = new TelemetryBatch(
            document.DeviceId,
            document.FirmwareVersion,
            batchTimestamp,
            receivedAt,
            samples,
            BuildHealth(document, batchTimestamp),
            source);

        return TelemetryValidation.Valid(batch, droppedKeys);
    }

    /// <summary>
    /// Resolves the wire keys of one sample against the dictionary. Unknown keys are collected rather than
    /// refused (rule V-10); the raw block is matched by the same keys, so a diagnostic value is never mistaken
    /// for a measured one.
    /// </summary>
    private static List<TelemetryReading> BuildReadings(TelemetrySampleDocument sample, List<string> droppedKeys)
    {
        var readings = new List<TelemetryReading>(sample.Metrics.Count);

        foreach (var (key, value) in sample.Metrics)
        {
            if (!MetricDictionary.TryParsePayloadKey(key, out var code))
            {
                droppedKeys.Add(key);
                continue;
            }

            readings.Add(new TelemetryReading(code, value, ResolveRawValue(sample, key)));
        }

        // A raw value whose metric was never measured is noise, not evidence: it is dropped under the same rule.
        foreach (var key in sample.RawMetrics.Keys)
        {
            if (!MetricDictionary.TryParsePayloadKey(key, out _) && !sample.Metrics.ContainsKey(key))
            {
                droppedKeys.Add(key);
            }
        }

        return readings;
    }

    private static decimal? ResolveRawValue(TelemetrySampleDocument sample, string key) =>
        sample.RawMetrics.TryGetValue(key, out var raw) ? raw : null;

    /// <summary>Builds the health report when the batch carried one. A batch without health is normal, not an error.</summary>
    private static DeviceHealthReport? BuildHealth(TelemetryPayloadDocument document, DateTimeOffset batchTimestamp) =>
        document.Health is not { } health
            ? null
            : new DeviceHealthReport(
                batchTimestamp,
                health.RssiDbm,
                health.UptimeSeconds,
                health.FreeHeapKb,
                health.BatteryPct,
                health.PowerSource,
                document.FirmwareVersion);

    /// <summary>
    /// Rule V-03: the timestamp must be an ISO-8601 instant in UTC. A device that sent local time with no zone
    /// would otherwise have its samples placed at an arbitrary offset, and day/night phase — which decides every
    /// threshold — is computed from exactly this value.
    /// </summary>
    private static bool TryParseBatchTimestamp(string? text, out DateTimeOffset timestamp)
    {
        timestamp = default;

        if (string.IsNullOrWhiteSpace(text) || !text.TrimEnd().EndsWith('Z'))
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out timestamp);
    }
}
