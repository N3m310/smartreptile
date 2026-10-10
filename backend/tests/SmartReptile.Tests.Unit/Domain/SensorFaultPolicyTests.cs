using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>FR-07 / BR-07.1's sensor-fault threshold (§02-design/03 §4.3).</summary>
public class SensorFaultPolicyTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(30)]
    public void Confirms_a_fault_at_or_past_the_devices_own_threshold(int consecutiveFailures) =>
        SensorFaultPolicy.IsConfirmed(consecutiveFailures).Should().BeTrue();

    [Fact]
    public void Takes_an_event_with_no_count_at_its_word() =>
        // The device only raises this event after its own three failures (BR-07.1), so a payload that omits the
        // count is still the device saying "this probe has failed".
        SensorFaultPolicy.IsConfirmed(null).Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Refuses_an_early_warning(int consecutiveFailures) =>
        // A firmware reporting its first failed read must not have a working metric marked unavailable.
        SensorFaultPolicy.IsConfirmed(consecutiveFailures).Should().BeFalse();

    [Fact]
    public void Defaults_match_the_documented_rule()
    {
        SensorFaultPolicy.MinConsecutiveFailures.Should().Be(3, "BR-07.1's three consecutive failures");
        SensorFaultPolicy.Severity.Should().Be(
            AlertSeverity.Warning,
            "§02-design/05 §1 reserves Info for entries that ask nothing of the keeper");
    }
}
