using FluentAssertions;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// TC-U-28's and TC-U-29's rule halves: a confirmed sensor fault opens one entry naming the probe and closes when
/// the probe answers again; a skewed batch opens one Info entry that is refreshed rather than repeated (FR-07,
/// NFR-10; roadmap task 3.3).
/// </summary>
public class DeviceSignalRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TerrariumId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly FakeDeviceSignalStore _store = new();
    private readonly DeviceSignalRecorder _recorder;

    private readonly Device _device = new()
    {
        PublicId = "sr-000002",
        DeviceName = "Test node",
        TerrariumId = TerrariumId,
        Status = DeviceStatus.Online,
    };

    public DeviceSignalRecorderTests() => _recorder = new DeviceSignalRecorder(_store);

    // ---- TC-U-28: SensorFault -------------------------------------------------------------------------

    [Fact]
    public async Task A_confirmed_fault_opens_one_warning_that_names_the_probe()
    {
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 3), Now);

        var alert = _store.Alerts.Should().ContainSingle().Subject;
        alert.Source.Should().Be(AlertSource.SensorFault);
        alert.Severity.Should().Be(AlertSeverity.Warning);
        alert.Metric.Should().Be(MetricCode.HumidityPct, "the keeper needs to know which probe to look at");
        alert.State.Should().Be(AlertState.Open);
        alert.TriggeredAt.Should().Be(Now);
        alert.LastObservedAt.Should().Be(Now);
        alert.BandMin.Should().BeNull("nothing was measured, so there is no band to state");
        alert.TriggeringValue.Should().BeNull();
        alert.DedupeKey.Should().Be(Alert.DedupeKeyFor(
            TerrariumId, MetricCode.HumidityPct, AlertSeverity.Warning, ThresholdPhase.Any));
    }

    [Fact]
    public async Task An_early_warning_opens_nothing()
    {
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 2), Now);

        _store.Alerts.Should().BeEmpty("one or two failed reads are not a dead probe");
    }

    [Fact]
    public async Task A_fault_that_names_no_metric_opens_nothing()
    {
        await _recorder.RecordEventAsync(
            _device,
            TerrariumId,
            new DeviceEventDraft(DeviceEventType.SensorFault, null, 4, """{"consecutiveFailures":4}"""),
            Now);

        _store.Alerts.Should().BeEmpty("an event with no probe in it has nothing to mark unavailable");
    }

    [Fact]
    public async Task A_second_fault_on_the_same_probe_does_not_open_a_second_entry()
    {
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 3), Now);
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 9), Now.AddMinutes(5));

        _store.Alerts.Should().ContainSingle("a probe that stays dead is one episode, not one entry per event");
    }

    [Fact]
    public async Task Two_dead_probes_are_two_entries()
    {
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 3), Now);
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.LightLux, 3), Now);

        _store.Alerts.Should().HaveCount(2);
        _store.Alerts.Select(alert => alert.Metric).Should().BeEquivalentTo([MetricCode.HumidityPct, MetricCode.LightLux]);
    }

    [Fact]
    public async Task Recovery_closes_the_entry_at_the_instant_the_device_said_so()
    {
        await _recorder.RecordEventAsync(_device, TerrariumId, Fault(MetricCode.HumidityPct, 3), Now);
        var recoveredAt = Now.AddMinutes(30);

        await _recorder.RecordEventAsync(
            _device,
            TerrariumId,
            new DeviceEventDraft(DeviceEventType.SensorRecovered, MetricCode.HumidityPct, null, "{}"),
            recoveredAt);

        var alert = _store.Alerts.Should().ContainSingle().Subject;
        alert.State.Should().Be(AlertState.Resolved);
        alert.ResolvedAt.Should().Be(recoveredAt);
        alert.ResolvedReason.Should().Be(
            ResolvedReason.Recovered,
            "ResolvedReason.SensorFault is the other direction: a band alert closed because the probe lied");
        alert.LastObservedAt.Should().Be(recoveredAt);
    }

    [Fact]
    public async Task A_recovery_with_no_open_fault_is_a_no_op()
    {
        await _recorder.RecordEventAsync(
            _device,
            TerrariumId,
            new DeviceEventDraft(DeviceEventType.SensorRecovered, MetricCode.HumidityPct, null, "{}"),
            Now);

        _store.Alerts.Should().BeEmpty("a recovery for a probe that never faulted is a fact, not something to raise");
    }

    // ---- TC-U-29: DeviceClockSkew ---------------------------------------------------------------------

    [Fact]
    public async Task A_skewed_batch_opens_one_info_entry()
    {
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now);

        var alert = _store.Alerts.Should().ContainSingle().Subject;
        alert.Source.Should().Be(AlertSource.DeviceClockSkew);
        alert.Severity.Should().Be(AlertSeverity.Info, "samples are stored and flagged; nothing needs doing now");
        alert.Metric.Should().BeNull();
        alert.Phase.Should().Be(ThresholdPhase.Any);
        alert.TriggeredAt.Should().Be(Now);
        alert.LastObservedAt.Should().Be(Now);
    }

    [Fact]
    public async Task A_later_skewed_batch_refreshes_the_entry_instead_of_repeating_it()
    {
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now);
        var later = Now.AddMinutes(10);

        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, later);

        var alert = _store.Alerts.Should().ContainSingle(
            "one entry reads as 'still unsynced'; an hourly row would read as a new problem every hour").Which;
        alert.LastObservedAt.Should().Be(later);
        alert.TriggeredAt.Should().Be(Now, "the episode began when the first skewed sample arrived");
    }

    [Fact]
    public async Task A_new_entry_waits_the_hour_after_the_last_one()
    {
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now);
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: false, Now.AddMinutes(5));

        // Skewed again five minutes later: the last signal is too recent for another one.
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now.AddMinutes(10));
        _store.Alerts.Should().ContainSingle();

        // Two hours after the first signal, a new episode is due.
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now.AddHours(2));
        _store.Alerts.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_trusted_batch_closes_the_entry()
    {
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: true, Now);
        var syncedAt = Now.AddHours(1);

        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: false, syncedAt);

        var alert = _store.Alerts.Should().ContainSingle().Subject;
        alert.State.Should().Be(AlertState.Resolved);
        alert.ResolvedAt.Should().Be(syncedAt);
        alert.ResolvedReason.Should().Be(ResolvedReason.Recovered);
    }

    [Fact]
    public async Task A_trusted_batch_for_a_device_that_has_never_been_skewed_is_a_no_op()
    {
        await _recorder.RecordBatchClockAsync(_device, TerrariumId, skewed: false, Now);

        _store.Alerts.Should().BeEmpty();
    }

    private static DeviceEventDraft Fault(MetricCode metric, int failures) =>
        new(DeviceEventType.SensorFault, metric, failures, $"{{\"consecutiveFailures\":{failures}}}");
}
