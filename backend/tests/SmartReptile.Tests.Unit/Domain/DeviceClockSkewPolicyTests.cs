using FluentAssertions;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Readings;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>Rule V-09's clock-skew signal: what it is about, and how often it may repeat (NFR-10).</summary>
public class DeviceClockSkewPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reads_the_skew_from_the_quality_bit_the_ingest_rules_set()
    {
        DeviceClockSkewPolicy.IsSkewed(QualityFlags.ClockUnsynced).Should().BeTrue();
        DeviceClockSkewPolicy.IsSkewed(QualityFlags.None).Should().BeFalse();
        DeviceClockSkewPolicy.IsSkewed(QualityFlags.ClockUnsynced | QualityFlags.CalibrationApplied)
            .Should().BeTrue("the other bits are about the reading, not about the clock");
    }

    [Fact]
    public void Does_not_read_a_back_fill_as_a_clock_fault() =>
        // V-08's branch deliberately never sets the skew bit: a sample that sat in the ring buffer through an
        // outage is late because of the outage, and labelling the device's clock for it would be a false alarm.
        DeviceClockSkewPolicy.IsSkewed(QualityFlags.Backfilled).Should().BeFalse();

    [Fact]
    public void Signals_the_first_time_and_then_once_an_hour()
    {
        DeviceClockSkewPolicy.IsSignalDue(Now, null).Should().BeTrue();
        DeviceClockSkewPolicy.IsSignalDue(Now, Now - TimeSpan.FromMinutes(1)).Should().BeFalse();
        DeviceClockSkewPolicy.IsSignalDue(Now, Now - TimeSpan.FromMinutes(59)).Should().BeFalse();
        DeviceClockSkewPolicy.IsSignalDue(Now, Now - TimeSpan.FromMinutes(60)).Should().BeTrue();
    }

    [Fact]
    public void Documents_the_hourly_interval() => DeviceClockSkewPolicy.SignalIntervalMinutes.Should().Be(60);
}
