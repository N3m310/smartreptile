using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The acknowledgement and resolution rules of §02-design/02 §4.2 (roadmap task 3.4, TC-I-07's transitions).
/// </summary>
public class AlertLifecycleTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_open_alert_may_be_acknowledged_and_records_who_and_when()
    {
        var alert = Open();

        AlertLifecycle.Acknowledge(alert, Actor, Now).Should().BeTrue();

        alert.State.Should().Be(AlertState.Acknowledged);
        alert.AcknowledgedAt.Should().Be(Now);
        alert.AcknowledgedByUserId.Should().Be(Actor);
    }

    [Fact]
    public void A_second_acknowledgement_is_refused_and_changes_nothing()
    {
        var alert = Open();
        AlertLifecycle.Acknowledge(alert, Actor, Now);
        var firstInstant = alert.AcknowledgedAt;
        var second = Guid.NewGuid();

        AlertLifecycle.Acknowledge(alert, second, Now.AddMinutes(5)).Should().BeFalse();

        alert.AcknowledgedAt.Should().Be(firstInstant, "the first acknowledgement is the one that happened");
        alert.AcknowledgedByUserId.Should().Be(Actor);
    }

    [Fact]
    public void A_resolved_alert_may_not_be_acknowledged()
    {
        var alert = Open();
        AlertLifecycle.Resolve(alert, Actor, ResolvedReason.Recovered, Now);

        AlertLifecycle.Acknowledge(alert, Actor, Now.AddMinutes(1)).Should().BeFalse();
        alert.AcknowledgedAt.Should().BeNull("a resolve is not an acknowledgement, and this one was not acknowledged");
    }

    [Fact]
    public void An_open_alert_may_be_resolved_without_being_acknowledged_first()
    {
        var alert = Open();

        AlertLifecycle.Resolve(alert, Actor, ResolvedReason.FalsePositive, Now).Should().BeTrue();

        alert.State.Should().Be(AlertState.Resolved);
        alert.ResolvedAt.Should().Be(Now);
        alert.ResolvedReason.Should().Be(ResolvedReason.FalsePositive);
        alert.ResolvedByUserId.Should().Be(Actor);
        alert.AcknowledgedAt.Should().BeNull("the timeline should say what happened, and no acknowledgement happened");
    }

    [Fact]
    public void An_acknowledged_alert_keeps_its_acknowledgement_when_it_is_resolved()
    {
        var alert = Open();
        AlertLifecycle.Acknowledge(alert, Actor, Now);
        var acknowledgedAt = alert.AcknowledgedAt;

        AlertLifecycle.Resolve(alert, Actor, ResolvedReason.Accepted, Now.AddMinutes(2)).Should().BeTrue();

        alert.AcknowledgedAt.Should().Be(acknowledgedAt);
        alert.ResolvedAt.Should().Be(Now.AddMinutes(2));
        alert.Duration.Should().Be(TimeSpan.FromMinutes(11), "duration runs from the trigger, not from the acknowledgement");
    }

    [Fact]
    public void Resolution_is_terminal_and_keeps_the_first_reason()
    {
        var alert = Open();
        AlertLifecycle.Resolve(alert, Actor, ResolvedReason.Recovered, Now);
        var other = Guid.NewGuid();

        AlertLifecycle.Resolve(alert, other, ResolvedReason.SensorFault, Now.AddMinutes(30)).Should().BeFalse();

        alert.ResolvedReason.Should().Be(ResolvedReason.Recovered);
        alert.ResolvedByUserId.Should().Be(Actor);
        alert.ResolvedAt.Should().Be(Now);
    }

    [Fact]
    public void A_threshold_alert_re_arms_its_dwell_key_and_a_device_level_one_has_none()
    {
        var threshold = Open();

        AlertLifecycle.RearmsItsKey(threshold).Should().BeTrue();

        var silent = Open();
        silent.Source = AlertSource.DeviceSilent;
        silent.Metric = null;

        AlertLifecycle.RearmsItsKey(silent).Should().BeFalse();

        var fault = Open();
        fault.Source = AlertSource.SensorFault;

        AlertLifecycle.RearmsItsKey(fault).Should().BeFalse(
            "the sensor-fault rule keeps its own state on the device's event stream, not in a dwell key");
    }

    private static Alert Open() => new()
    {
        Id = 1,
        TerrariumId = Guid.NewGuid(),
        DeviceId = Guid.NewGuid(),
        Metric = MetricCode.TempC,
        Severity = AlertSeverity.Warning,
        Source = AlertSource.Threshold,
        State = AlertState.Open,
        TriggeredAt = Now.AddMinutes(-9),
    };
}
