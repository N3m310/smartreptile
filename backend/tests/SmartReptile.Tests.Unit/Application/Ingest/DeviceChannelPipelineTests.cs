using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Infrastructure.Ingest;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// The health, status and events channels — the consumer side of the broker forwarding described by
/// `07-appendices/03` §3.1/§3.3/§3.4 and roadmap task 2.4's "only telemetry is forwarded" gap.
/// </summary>
/// <remarks>
/// The real <see cref="JsonDeviceChannelParser"/> is used rather than a stub, so each case states a payload a
/// device could actually send and the assertion covers the parse and the rule together. That is deliberate: the
/// two are separate types because they are separate concerns, not because either is expected to be wrong.
/// </remarks>
public class DeviceChannelPipelineTests
{
    private static readonly DateTimeOffset ArrivedAt = new(2026, 10, 9, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeTelemetryStore _store = new();
    private readonly TestClock _clock = new() { UtcNow = ArrivedAt };
    private readonly DeviceChannelPipeline _pipeline;
    private readonly Device _device;
    private readonly Guid _terrariumId = Guid.NewGuid();

    public DeviceChannelPipelineTests()
    {
        var store = _store;
        var updater = new DeviceStateUpdater(store);

        _pipeline = new DeviceChannelPipeline(new JsonDeviceChannelParser(), store, updater);

        _device = new Device
        {
            PublicId = "sr-3f9a2c",
            DeviceName = "Test node",
            ChipId = "A0B1C2D3E4F5",
            MacAddress = "A0:B1:C2:D3:E4:F5",
            Status = DeviceStatus.Online,
            TerrariumId = _terrariumId,
        };

        _store.Devices.Add(_device);
    }

    private static TelemetryEnvelope Envelope(string json, DeviceChannel channel) =>
        new("sr-3f9a2c", System.Text.Encoding.UTF8.GetBytes(json), ArrivedAt, IngestSource.Mqtt, Channel: channel);

    // ---- status -------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_moves_a_running_device_out_into_offline_and_reports_the_transition()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"offline","fw":"1.0.0","at":"2026-10-09T09:29:00Z"}""",
                DeviceChannel.Status),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        _device.Status.Should().Be(DeviceStatus.Offline);

        outcome.StatusChanged.Should().NotBeNull();
        outcome.StatusChanged!.Status.Should().Be("offline");
        outcome.StatusChanged.TerrariumId.Should().Be(_terrariumId);
        outcome.StatusChanged.LastSeenAt.Should().Be(ArrivedAt);
        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Status_that_changes_nothing_reports_no_transition()
    {
        _device.Status = DeviceStatus.Online;

        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"online"}""", DeviceChannel.Status),
            CancellationToken.None);

        // A publisher that repeats "online" every minute must not make a client re-render a badge every minute.
        outcome.Succeeded.Should().BeTrue();
        outcome.StatusChanged.Should().BeNull();
    }

    [Fact]
    public async Task Status_offline_does_not_overwrite_maintenance()
    {
        _device.Status = DeviceStatus.Maintenance;

        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"offline"}""", DeviceChannel.Status),
            CancellationToken.None);

        // Maintenance is the owner's decision, so a device's own LWT may not undo it (BR-12.6).
        outcome.Succeeded.Should().BeTrue();
        _device.Status.Should().Be(DeviceStatus.Maintenance);
        outcome.StatusChanged.Should().BeNull();
    }

    [Fact]
    public async Task Status_refuses_a_word_that_is_not_in_the_vocabulary()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"sleeping"}""", DeviceChannel.Status),
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("schema_invalid");
        _device.Status.Should().Be(DeviceStatus.Online);
        _store.SaveCount.Should().Be(0);
    }

    // ---- health -------------------------------------------------------------------------------------

    [Fact]
    public async Task Health_denormalises_the_fleet_figures_and_stages_a_row()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope(
                """{"deviceId":"sr-3f9a2c","rssi":-63,"up_s":86400,"heap_kb":142,"bat":87.5,"src":"mains","fw":"1.2.0"}""",
                DeviceChannel.Health),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        _device.SignalStrengthDbm.Should().Be(-63);
        _device.UptimeSeconds.Should().Be(86400);
        _device.FreeHeapKb.Should().Be(142);
        _device.BatteryPct.Should().Be(87.5m);
        _device.FirmwareVersion.Should().Be("1.2.0");

        var health = _store.Health.Should().ContainSingle().Subject;
        health.TerrariumId.Should().Be(_terrariumId);
        health.RssiDbm.Should().Be(-63);

        // No timestamp in the payload, so arrival is the instant — a node with an unsynced clock still reports.
        health.RecordedAt.Should().Be(ArrivedAt);
    }

    [Fact]
    public async Task Health_keeps_the_previous_value_of_a_field_the_device_did_not_report()
    {
        _device.BatteryPct = 90m;

        await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","rssi":-70}""", DeviceChannel.Health),
            CancellationToken.None);

        // Reporting only RSSI is not reporting "no battery".
        _device.BatteryPct.Should().Be(90m);
        _device.SignalStrengthDbm.Should().Be(-70);
    }

    [Fact]
    public async Task Health_moves_a_offline_device_back_online()
    {
        _device.Status = DeviceStatus.Offline;

        await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","rssi":-60}""", DeviceChannel.Health),
            CancellationToken.None);

        _device.Status.Should().Be(DeviceStatus.Online);
    }

    // ---- events -------------------------------------------------------------------------------------

    [Fact]
    public async Task Event_is_stored_verbatim_with_its_metric()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope(
                """{"deviceId":"sr-3f9a2c","type":"sensor_fault","metric":"humidityPct","consecutiveFailures":3,"at":"2026-10-09T09:28:00Z"}""",
                DeviceChannel.Events),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();

        var stored = _store.Events.Should().ContainSingle().Subject;
        stored.DeviceId.Should().Be(_device.Id);
        stored.TerrariumId.Should().Be(_terrariumId);
        stored.Type.Should().Be(DeviceEventType.SensorFault);
        stored.Metric.Should().Be(MetricCode.HumidityPct);
        stored.RecordedAt.Should().Be(new DateTimeOffset(2026, 10, 9, 9, 28, 0, TimeSpan.Zero));

        // The event's own fields are kept whole, so a signal rule can be changed and re-run against history.
        stored.DetailJson.Should().Contain("consecutiveFailures");
    }

    [Fact]
    public async Task Event_refuses_an_unknown_type_rather_than_inventing_a_category()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","type":"melted"}""", DeviceChannel.Events),
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("schema_invalid");
        _store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Event_with_a_metric_the_dictionary_does_not_know_is_still_stored()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","type":"sensor_fault","metric":"pressureHpa"}""", DeviceChannel.Events),
            CancellationToken.None);

        // The event happened; only the metric link is lost, and the raw payload keeps it for a later dictionary.
        outcome.Succeeded.Should().BeTrue();
        _store.Events.Should().ContainSingle().Which.Metric.Should().BeNull();
    }

    // ---- the gate every channel shares ---------------------------------------------------------------

    [Fact]
    public async Task A_payload_naming_another_device_is_refused()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-999999","status":"online"}""", DeviceChannel.Status),
            CancellationToken.None);

        // Rule V-04 applies to every channel: a payload may not name a device the transport did not authenticate.
        outcome.Problem!.Code.Should().Be("schema_invalid");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task An_unbound_device_is_refused_with_the_same_code_as_a_revoked_one()
    {
        _device.TerrariumId = null;

        var unbound = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"online"}""", DeviceChannel.Status),
            CancellationToken.None);

        _device.TerrariumId = _terrariumId;
        _device.Status = DeviceStatus.Revoked;

        var revoked = await _pipeline.ProcessAsync(
            Envelope("""{"deviceId":"sr-3f9a2c","status":"online"}""", DeviceChannel.Status),
            CancellationToken.None);

        unbound.Problem!.Code.Should().Be("auth_failed");
        revoked.Problem!.Code.Should().Be("auth_failed");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Malformed_bytes_are_refused_without_throwing()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("not json at all", DeviceChannel.Health),
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("schema_invalid");
    }

    [Fact]
    public async Task A_json_body_that_is_not_an_object_is_refused()
    {
        var outcome = await _pipeline.ProcessAsync(
            Envelope("""["status","online"]""", DeviceChannel.Status),
            CancellationToken.None);

        // Valid JSON, wrong kind: the same answer the telemetry parser gives, so a firmware bug reads the same way
        // on every topic.
        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("schema_invalid");
    }
}
