using FluentAssertions;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// TC-U-27's lifecycle and the edges around it: silence warns, escalates on the same row, and closes when the
/// device speaks again (FR-07; §02-design/03 §4.3, roadmap task 3.3).
/// </summary>
public class DeviceSilenceMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TerrariumId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TestClock _clock = new() { UtcNow = Now };
    private readonly FakeDeviceSilenceStore _store = new();
    private readonly DeviceSilenceMonitor _monitor;

    public DeviceSilenceMonitorTests() =>
        _monitor = new DeviceSilenceMonitor(
            _store,
            _clock,
            new DeviceSilenceSettings(DeviceSilencePolicy.DefaultSilentAfterIntervals));

    // ---- TC-U-27: Warning → Critical on one row → Resolved -------------------------------------------

    [Fact]
    public async Task Opens_one_warning_and_moves_the_device_offline_after_three_missed_intervals()
    {
        var device = Watch(minutesSinceLastSeen: 4).Device;

        var outcome = await _monitor.SweepAsync();

        outcome.Should().Be(new DeviceSilenceOutcome(1, MarkedOffline: 1, AlertsOpened: 1));
        device.Status.Should().Be(DeviceStatus.Offline, "the state machine's Online → Offline edge is silence");

        var alert = _store.Added.Should().ContainSingle().Subject;
        alert.Source.Should().Be(AlertSource.DeviceSilent);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.State.Should().Be(AlertState.Open);
        alert.Metric.Should().BeNull("silence is not a metric misbehaving");
        alert.Phase.Should().Be(ThresholdPhase.Any);
        alert.TriggeringValue.Should().BeNull("silence is the absence of a reading, so there is no value to point at");
        alert.BandMin.Should().BeNull();
        alert.BandMax.Should().BeNull();
        alert.TriggeredAt.Should().Be(
            Now.AddMinutes(-4) + DeviceSilencePolicy.SilenceThreshold(60, 3),
            "the alert is dated from the instant the device should have been heard from, not from the sweep");
        alert.DedupeKey.Should().Be(Alert.DedupeKeyFor(TerrariumId, null, AlertSeverity.Warning, ThresholdPhase.Any));
    }

    [Fact]
    public async Task Escalates_the_open_alert_in_place_once_silence_passes_thirty_minutes()
    {
        var (device, open) = Watch(minutesSinceLastSeen: 31, AlertSeverity.Warning, Now.AddMinutes(-31));

        var outcome = await _monitor.SweepAsync();

        outcome.AlertsEscalated.Should().Be(1);
        outcome.AlertsOpened.Should().Be(0);
        _store.Added.Should().BeEmpty("an escalation changes the open row rather than opening a second episode");

        open!.Severity.Should().Be(AlertSeverity.Critical);
        open.State.Should().Be(AlertState.Open);
        open.TriggeredAt.Should().Be(Now.AddMinutes(-31), "the outage began when it began");
        open.DedupeKey.Should().Be(Alert.DedupeKeyFor(TerrariumId, null, AlertSeverity.Critical, ThresholdPhase.Any));
        device.Status.Should().Be(DeviceStatus.Offline);
    }

    [Fact]
    public async Task Opens_straight_into_critical_when_the_first_sweep_already_finds_half_an_hour_of_silence()
    {
        Watch(minutesSinceLastSeen: 45);

        var outcome = await _monitor.SweepAsync();

        outcome.Should().Be(new DeviceSilenceOutcome(1, MarkedOffline: 1, AlertsOpened: 1, CriticalAlertsOpened: 1));
        _store.Added.Single().Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public async Task Resolves_the_alert_when_the_device_speaks_again()
    {
        var (device, open) = Watch(minutesSinceLastSeen: 0, AlertSeverity.Critical, Now.AddMinutes(-40));

        var outcome = await _monitor.SweepAsync();

        outcome.AlertsResolved.Should().Be(1);
        outcome.AlertsOpened.Should().Be(0);
        outcome.MarkedOffline.Should().Be(0, "coming back is ingest's transition to make, not the watchdog's");

        open!.State.Should().Be(AlertState.Resolved);
        open.ResolvedReason.Should().Be(ResolvedReason.Recovered);
        open.ResolvedAt.Should().Be(device.LastSeenAt);
        open.LastObservedAt.Should().Be(device.LastSeenAt, "the returning sample is the last reading this alert saw");
    }

    // ---- the edges ------------------------------------------------------------------------------------

    [Fact]
    public async Task Leaves_a_device_that_is_still_reporting_alone()
    {
        var device = Watch(minutesSinceLastSeen: 2).Device;

        var outcome = await _monitor.SweepAsync();

        outcome.Watched.Should().Be(1);
        outcome.Should().Be(new DeviceSilenceOutcome(1));
        _store.Added.Should().BeEmpty();
        device.Status.Should().Be(DeviceStatus.Online);
    }

    [Fact]
    public async Task Judges_a_device_against_its_own_interval()
    {
        // Ten minutes of quiet on a five-minute node is two missed intervals, not silence — the rule is relative to
        // the device, and the alert must not fire for a configuration the keeper chose.
        var device = Watch(minutesSinceLastSeen: 10, samplingIntervalSec: 300).Device;

        var outcome = await _monitor.SweepAsync();

        outcome.AlertsOpened.Should().Be(0);
        device.Status.Should().Be(DeviceStatus.Online);
    }

    [Fact]
    public async Task Does_not_judge_a_device_that_is_being_serviced()
    {
        var (device, open) = Watch(
            minutesSinceLastSeen: 45,
            AlertSeverity.Warning,
            Now.AddMinutes(-45),
            status: DeviceStatus.Maintenance);

        var outcome = await _monitor.SweepAsync();

        outcome.Watched.Should().Be(0, "maintenance exists so routine servicing does not create false alerts");
        outcome.AlertsEscalated.Should().Be(0);
        _store.Added.Should().BeEmpty();
        device.Status.Should().Be(DeviceStatus.Maintenance);
        open!.State.Should().Be(AlertState.Open, "leaving maintenance is the owner's decision, so nothing is closed here");
        open.Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public async Task Does_not_count_a_device_that_was_already_offline()
    {
        var device = Watch(minutesSinceLastSeen: 10, status: DeviceStatus.Offline).Device;

        var outcome = await _monitor.SweepAsync();

        outcome.MarkedOffline.Should().Be(0, "an earlier sweep or the device's own LWT already moved it");
        outcome.AlertsOpened.Should().Be(1, "offline and alerted are different questions");
        device.Status.Should().Be(DeviceStatus.Offline);
    }

    [Fact]
    public async Task Commits_the_sweep_even_when_it_decided_nothing()
    {
        Watch(minutesSinceLastSeen: 0);

        await _monitor.SweepAsync();

        _store.SaveCount.Should().Be(1);
    }

    /// <summary>Watches one device, optionally with a silence alert already open for it.</summary>
    private (Device Device, Alert? Open) Watch(
        int minutesSinceLastSeen,
        AlertSeverity? openSeverity = null,
        DateTimeOffset? openTriggeredAt = null,
        int samplingIntervalSec = 60,
        DeviceStatus status = DeviceStatus.Online)
    {
        var device = new Device
        {
            PublicId = "sr-000001",
            DeviceName = "Test node",
            TerrariumId = TerrariumId,
            Status = status,
            SamplingIntervalSec = samplingIntervalSec,
            LastSeenAt = Now.AddMinutes(-minutesSinceLastSeen),
        };

        Alert? open = null;

        if (openSeverity is { } severity)
        {
            open = new Alert
            {
                TerrariumId = TerrariumId,
                DeviceId = device.Id,
                Metric = null,
                Phase = ThresholdPhase.Any,
                Severity = severity,
                State = AlertState.Open,
                Source = AlertSource.DeviceSilent,
                DedupeKey = Alert.DedupeKeyFor(TerrariumId, null, severity, ThresholdPhase.Any),
                TriggeredAt = openTriggeredAt ?? device.LastSeenAt!.Value,
            };
        }

        _store.Watch(device, open);

        return (device, open);
    }
}
