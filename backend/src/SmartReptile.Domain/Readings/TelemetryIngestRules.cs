namespace SmartReptile.Domain.Readings;

/// <summary>
/// The ingest limits and timing rules of §02-design/03 §3 (V-01…V-09) that do not depend on a transport, a
/// database or a clock. Kept in the domain because the same numbers appear in the firmware, the API and the
/// design table, and a disagreement between them is how a batch that a device considers legal gets refused.
/// </summary>
public static class TelemetryIngestRules
{
    /// <summary>Rule V-01: most samples one batch may carry (one per minute for two hours).</summary>
    public const int MaxSamplesPerBatch = 120;

    /// <summary>Rule V-01: largest accepted payload, in kilobytes.</summary>
    public const int MaxPayloadKb = 32;

    /// <summary>Rule V-02/V-03 context: the offset of the last sample from <c>ts</c> cannot exceed a day.</summary>
    public const int MaxSampleOffsetSeconds = 86_400;

    /// <summary>Rule V-03 context: firmware version strings are at most 16 characters.</summary>
    public const int MaxFirmwareLength = 16;

    /// <summary>Rule V-09: skew above this many seconds is worth recording; it also sets
    /// <see cref="QualityFlags.ClockUnsynced"/>, because a device that is two minutes out has not synced.</summary>
    public const int MaxClockSkewSeconds = 120;

    /// <summary>
    /// Rule V-07: a <c>RecordedAt</c> further than this into the future is not trusted. The sample is still
    /// accepted — it is real measurement — but its timestamp is clamped to <c>ReceivedAt</c> so a device with a
    /// wrong clock cannot reorder history or land itself in the wrong day/night phase.
    /// </summary>
    public const int MaxFutureSeconds = 300;

    /// <summary>
    /// Rule V-08: a sample older than this arrived from the device's ring buffer after an outage. It is stored as
    /// back-fill and carries <see cref="QualityFlags.Backfilled"/>, which downstream code reads as "report it,
    /// never notify on it" (BR-11.8).
    /// </summary>
    public const int BackfillAfterHours = 12;

    /// <summary>
    /// The timing decisions for one sample (rules V-07…V-09). Pure, so the arithmetic that decides whether a
    /// sample is back-fill or clock-skewed is testable without a device or a database.
    /// </summary>
    /// <param name="RecordedAt">The timestamp actually stored — clamped when the device clock ran ahead.</param>
    /// <param name="ClockSkewSeconds">
    /// <c>ReceivedAt − RecordedAt</c> as observed, <b>before</b> any clamping. Kept unclamped on purpose: the
    /// clamped value would read as zero skew and hide the very fault the column exists to surface.
    /// </param>
    /// <param name="Flags">Timing flags to merge into the sample's quality bitmask.</param>
    public readonly record struct SampleTiming(DateTimeOffset RecordedAt, int ClockSkewSeconds, QualityFlags Flags);

    /// <summary>Applies the timing rules to one sample.</summary>
    public static SampleTiming Apply(DateTimeOffset recordedAt, DateTimeOffset receivedAt, QualityFlags baseFlags)
    {
        // Positive means the sample is late (the device clock is behind); negative means it is from the future.
        // The sign follows the column's own definition, ClockSkewSeconds = ReceivedAt − RecordedAt.
        var lateness = receivedAt - recordedAt;
        var skewSeconds = (int)Math.Round(lateness.TotalSeconds, MidpointRounding.AwayFromZero);
        var flags = baseFlags;

        // V-07: a timestamp from the future is clamped rather than refused, so a device with a wrong clock cannot
        // reorder history or land itself in the wrong day/night phase. The observed skew is still recorded — that
        // value is the evidence a calibration review needs.
        var stored = lateness < -TimeSpan.FromSeconds(MaxFutureSeconds) ? receivedAt : recordedAt;

        if (receivedAt - stored > TimeSpan.FromHours(BackfillAfterHours))
        {
            // V-08: the sample is late because it sat in the device's ring buffer through an outage. Its clock is
            // fine, so this must not also be reported as clock skew — otherwise every honest back-fill would be
            // labelled a device fault.
            flags |= QualityFlags.Backfilled;
        }
        else if (Math.Abs(lateness.TotalSeconds) > MaxClockSkewSeconds)
        {
            // V-09: the device has not synced. Either direction counts: a clock two minutes out cannot be used for
            // ordering or phase, which is what the flag tells every downstream consumer.
            flags |= QualityFlags.ClockUnsynced;
        }

        return new SampleTiming(stored, skewSeconds, flags);
    }
}
