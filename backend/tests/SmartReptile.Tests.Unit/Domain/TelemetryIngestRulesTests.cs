using FluentAssertions;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The ingest timing rules of §02-design/03 §3 (V-07…V-09): when a sample's own timestamp may be trusted, and how
/// lateness is told apart from a wrong clock.
/// </summary>
public class TelemetryIngestRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 8, 15, 0, TimeSpan.Zero);

    [Fact]
    public void GivenASampleArrivingOnTime_ThenNothingIsFlagged()
    {
        var timing = TelemetryIngestRules.Apply(Now, Now, QualityFlags.None);

        timing.RecordedAt.Should().Be(Now);
        timing.ClockSkewSeconds.Should().Be(0);
        timing.Flags.Should().Be(QualityFlags.None);
    }

    [Fact]
    public void GivenASampleAFewSecondsOut_ThenSkewIsRecordedButNotFlagged()
    {
        // Rule V-09's threshold is what separates "the clock ticks a bit differently" from "the device never
        // synced"; flagging seconds would flag every healthy node.
        var timing = TelemetryIngestRules.Apply(Now.AddSeconds(-30), Now, QualityFlags.None);

        timing.ClockSkewSeconds.Should().Be(30);
        timing.Flags.Should().Be(QualityFlags.None);
    }

    [Fact]
    public void GivenASampleFromTheFuture_ThenItIsClampedAndFlaggedButTheSkewIsStillRecorded()
    {
        var timing = TelemetryIngestRules.Apply(Now.AddHours(3), Now, QualityFlags.None);

        timing.RecordedAt.Should().Be(Now, "a wrong device clock must not be able to reorder history");
        timing.ClockSkewSeconds.Should().Be(-10_800, "hiding the observed skew would hide the fault");
        timing.Flags.Should().HaveFlag(QualityFlags.ClockUnsynced);
    }

    [Fact]
    public void GivenASampleJustInsideTheFutureTolerance_ThenItIsKeptAsSent()
    {
        var recorded = Now.AddSeconds(TelemetryIngestRules.MaxFutureSeconds);

        var timing = TelemetryIngestRules.Apply(recorded, Now, QualityFlags.None);

        timing.RecordedAt.Should().Be(recorded);
    }

    [Fact]
    public void GivenASampleBeyondTheBackfillCutoff_ThenItIsBackfillAndNotAClockFault()
    {
        // The two rules describe different situations and must not both fire: a sample that sat in the ring buffer
        // through an outage has a perfectly good clock.
        var timing = TelemetryIngestRules.Apply(Now.AddHours(-14), Now, QualityFlags.None);

        timing.Flags.Should().HaveFlag(QualityFlags.Backfilled);
        timing.Flags.Should().NotHaveFlag(QualityFlags.ClockUnsynced);
    }

    [Fact]
    public void GivenASampleJustInsideTheBackfillCutoff_ThenItIsTreatedAsAStaleClockNotBackfill()
    {
        var timing = TelemetryIngestRules.Apply(Now.AddHours(-1), Now, QualityFlags.None);

        timing.Flags.Should().HaveFlag(QualityFlags.ClockUnsynced);
        timing.Flags.Should().NotHaveFlag(QualityFlags.Backfilled);
    }

    [Fact]
    public void GivenFirmwareQualityBits_ThenTheySurviveTheTimingRules()
    {
        var timing = TelemetryIngestRules.Apply(Now, Now, QualityFlags.FirstAfterBoot);

        timing.Flags.Should().Be(QualityFlags.FirstAfterBoot);
    }

    [Fact]
    public void GivenAPayloadBeyondTheDocumentedLimits_ThenTheRulesCarryThoseLimits()
    {
        TelemetryIngestRules.MaxSamplesPerBatch.Should().Be(120);
        TelemetryIngestRules.MaxPayloadKb.Should().Be(32);
        TelemetryIngestRules.MaxSampleOffsetSeconds.Should().Be(86_400);
        TelemetryIngestRules.MaxClockSkewSeconds.Should().Be(120);
        TelemetryIngestRules.BackfillAfterHours.Should().Be(12);
    }
}
