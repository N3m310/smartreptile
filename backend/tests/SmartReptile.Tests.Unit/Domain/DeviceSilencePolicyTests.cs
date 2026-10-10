using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>FR-07 silence thresholds (§02-design/03 §4.3, §02-design/05 §2).</summary>
public class DeviceSilencePolicyTests
{
    private const int Interval = 60;
    private const int MissedIntervals = DeviceSilencePolicy.DefaultSilentAfterIntervals;

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(180)]
    public void Calls_nothing_silent_up_to_the_threshold(int silenceSeconds)
    {
        // 180 s is exactly three missed intervals, and the comparison is strictly greater — the same direction the
        // read surface's badge uses, so the two cannot disagree at the boundary.
        var silence = TimeSpan.FromSeconds(silenceSeconds);

        DeviceSilencePolicy.IsSilent(silence, Interval, MissedIntervals).Should().BeFalse();
        DeviceSilencePolicy.SeverityFor(silence, Interval, MissedIntervals).Should().BeNull();
    }

    [Fact]
    public void Warns_one_second_past_the_missed_intervals() =>
        DeviceSilencePolicy.SeverityFor(TimeSpan.FromSeconds(181), Interval, MissedIntervals)
            .Should().Be(AlertSeverity.Warning);

    [Fact]
    public void Goes_critical_at_thirty_minutes_and_not_before()
    {
        DeviceSilencePolicy.SeverityFor(TimeSpan.FromSeconds(1799), Interval, MissedIntervals)
            .Should().Be(AlertSeverity.Warning);

        DeviceSilencePolicy.SeverityFor(TimeSpan.FromMinutes(30), Interval, MissedIntervals)
            .Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public void Scales_the_warning_with_the_devices_own_interval()
    {
        // A five-minute node asks for fifteen minutes before it is silent, and a minute of quiet says nothing.
        DeviceSilencePolicy.SeverityFor(TimeSpan.FromMinutes(2), 300, MissedIntervals).Should().BeNull();
        DeviceSilencePolicy.SeverityFor(TimeSpan.FromMinutes(16), 300, MissedIntervals)
            .Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public void Stays_critical_past_thirty_minutes_whatever_the_interval() =>
        // The critical window is absolute: past half an hour the question is not whether the interval is right.
        DeviceSilencePolicy.SeverityFor(TimeSpan.FromMinutes(31), 300, MissedIntervals)
            .Should().Be(AlertSeverity.Critical);

    [Fact]
    public void Back_dates_the_silence_to_the_instant_the_device_should_have_been_heard_from()
    {
        var lastSeenAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

        DeviceSilencePolicy.SilenceStartedAt(lastSeenAt, Interval, MissedIntervals)
            .Should().Be(lastSeenAt + TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Defaults_match_the_documented_thresholds()
    {
        DeviceSilencePolicy.DefaultSilentAfterIntervals.Should().Be(3, "§02-design/03 §4.3's 3 × interval");
        DeviceSilencePolicy.CriticalAfterMinutes.Should().Be(30, "§02-design/05 §2's DeviceSilent > 30 min");
    }
}
