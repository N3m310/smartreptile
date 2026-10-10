using System.Text.Json;
using FluentAssertions;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Tests.Unit.Application.Alerts;

/// <summary>
/// The alert lifecycle API's rules (FR-12, roadmap task 3.4): scoping, the two transitions, the audit entry each one
/// writes, the dwell key a resolution re-arms, and the push that must never fail a request.
/// </summary>
public class AlertServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeAlertStore _store = new();
    private readonly RecordingAlertBroadcaster _broadcaster = new();
    private readonly AlertTestClock _clock = new(Now);
    private readonly AlertService _service;

    public AlertServiceTests() => _service = new AlertService(_store, _broadcaster, _clock);

    [Fact]
    public async Task Listing_asks_for_one_row_more_than_the_page_it_will_show()
    {
        Add(1);
        Add(2);
        Add(3);

        var outcome = await _service.ListAsync(_store.Owner, new AlertQuery(PageSize: 2), CancellationToken.None);

        _store.LastListTake.Should().Be(3, "the extra row is how 'there is more' is known without a count query");
        outcome.Page!.Items.Should().HaveCount(2);
        outcome.Page.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task The_last_page_has_no_cursor()
    {
        Add(1);
        Add(2);

        var outcome = await _service.ListAsync(_store.Owner, new AlertQuery(PageSize: 2), CancellationToken.None);

        outcome.Page!.Items.Should().HaveCount(2);
        outcome.Page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task A_cursor_this_api_did_not_issue_is_refused_as_a_bad_request()
    {
        var outcome = await _service.ListAsync(
            _store.Owner,
            new AlertQuery(Cursor: "not-a-cursor"),
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("invalid_cursor");
    }

    [Fact]
    public async Task A_window_that_ends_before_it_starts_is_refused()
    {
        var outcome = await _service.ListAsync(
            _store.Owner,
            new AlertQuery(FromUtc: Now, ToUtc: Now.AddHours(-1)),
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("invalid_range");
    }

    [Fact]
    public async Task The_detail_carries_an_excerpt_and_says_when_it_truncated()
    {
        var alert = Add(812);
        _store.Series.AddRange(Enumerable.Range(0, 300).Select(index => new AlertSeriesPoint(
            Now.AddMinutes(index - 300),
            20m + (index / 10m))));

        var outcome = await _service.GetAsync(alert.Id, _store.Owner, CancellationToken.None);

        outcome.Alert!.SeriesTruncated.Should().BeTrue();
        outcome.Alert.Series.Should().HaveCount(240, "the excerpt is capped, and it says so");
        outcome.Alert.Series.Should().BeInAscendingOrder(point => point.At);
    }

    [Fact]
    public async Task A_device_level_alert_has_no_series_to_carry()
    {
        var alert = Add(812, metric: null, source: AlertSource.DeviceSilent);
        _store.Series.Add(new AlertSeriesPoint(Now, 20m));

        var outcome = await _service.GetAsync(alert.Id, _store.Owner, CancellationToken.None);

        outcome.Alert!.Series.Should().BeEmpty();
        outcome.Alert.SeriesTruncated.Should().BeFalse();
        outcome.Alert.Alert.Metric.Should().BeNull();
        outcome.Alert.Alert.Source.Should().Be(nameof(AlertSource.DeviceSilent));
    }

    [Fact]
    public async Task An_alert_of_somebody_else_is_answered_as_if_it_did_not_exist()
    {
        var alert = Add(812);

        var read = await _service.GetAsync(alert.Id, Guid.NewGuid(), CancellationToken.None);
        var acknowledged = await _service.AcknowledgeAsync(
            alert.Id,
            Guid.NewGuid(),
            AuditActor.Unknown,
            CancellationToken.None);

        read.Problem!.Code.Should().Be("not_found");
        acknowledged.Problem!.Code.Should().Be("not_found");
        _store.AuditEntries.Should().BeEmpty();
        _broadcaster.Alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task Acknowledging_records_who_and_when_and_stages_one_audit_row()
    {
        var alert = Add(812);

        var outcome = await _service.AcknowledgeAsync(
            alert.Id,
            _store.Owner,
            new AuditActor("203.0.113.7", "RegressionTests/1.0", "corr-1"),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Changed!.State.Should().Be(nameof(AlertState.Acknowledged));
        outcome.Changed.AcknowledgedAt.Should().Be(Now);
        _store.SaveCount.Should().Be(1);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.AlertAcknowledged);
        entry.EntityName.Should().Be("Alert");
        entry.EntityId.Should().Be(alert.Id.ToString());
        entry.UserId.Should().Be(_store.Owner);
        entry.DeviceId.Should().Be(alert.DeviceId);
        entry.IpAddress.Should().Be("203.0.113.7");
        entry.OccurredAt.Should().Be(Now);

        _broadcaster.Alerts.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            AlertId = alert.Id,
            Event = AlertEvent.Acknowledged,
            State = nameof(AlertState.Acknowledged),
            Metric = "tempC",
        });
    }

    [Fact]
    public async Task A_second_acknowledgement_is_a_conflict_and_changes_nothing()
    {
        var alert = Add(812);
        await _service.AcknowledgeAsync(alert.Id, _store.Owner, AuditActor.Unknown, CancellationToken.None);

        var outcome = await _service.AcknowledgeAsync(
            alert.Id,
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("alert_not_open");
        _store.AuditEntries.Should().HaveCount(1, "the refused attempt wrote no second entry");
        _store.SaveCount.Should().Be(1);
        _broadcaster.Alerts.Should().HaveCount(1);
    }

    [Fact]
    public async Task Resolving_re_arms_the_dwell_key_so_the_alert_cannot_come_straight_back()
    {
        var alert = Add(812);

        _store.States.Add(new EvaluationState
        {
            TerrariumId = alert.TerrariumId,
            Metric = MetricCode.TempC,
            Phase = ThresholdPhase.Any,
            Violation = ViolationKind.Hot,
            FirstOutOfBandAt = Now.AddMinutes(-12),
            CriticalSinceAt = Now.AddMinutes(-6),
            OpenAlertId = alert.Id,
            LastEvaluatedSampleId = 4711,
        });

        var outcome = await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("Accepted", "keeper knows about the lamp"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Changed!.State.Should().Be(nameof(AlertState.Resolved));
        outcome.Changed.ResolvedReason.Should().Be(nameof(ResolvedReason.Accepted));

        var state = _store.States.Single();
        state.Violation.Should().Be(ViolationKind.None);
        state.FirstOutOfBandAt.Should().BeNull("a re-armed key measures its dwell from the next excursion");
        state.CriticalSinceAt.Should().BeNull();
        state.OpenAlertId.Should().BeNull();
        state.LastEvaluatedSampleId.Should().Be(4711, "the watermark is untouched: those samples were judged already");
    }

    [Fact]
    public async Task Resolving_a_device_level_alert_re_arms_nothing()
    {
        var alert = Add(812, metric: null, source: AlertSource.DeviceSilent);

        var outcome = await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("Accepted", null),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        _store.StateLookups.Should().Be(0, "a device-level alert names no dwell key to look up");
    }

    [Fact]
    public async Task Resolving_keeps_the_reason_and_the_note_on_the_audit_row()
    {
        var alert = Add(812);

        await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("FalsePositive", "  lid was open during cleaning  "),
            AuditActor.Unknown,
            CancellationToken.None);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.AlertResolved);

        using var after = JsonDocument.Parse(entry.AfterJson!);
        after.RootElement.GetProperty("reason").GetString().Should().Be(nameof(ResolvedReason.FalsePositive));
        after.RootElement.GetProperty("note").GetString().Should().Be("lid was open during cleaning");

        _broadcaster.Alerts.Should().ContainSingle().Which.Event.Should().Be(AlertEvent.Resolved);
    }

    [Fact]
    public async Task An_unknown_reason_is_refused_before_the_alert_is_even_looked_up()
    {
        var alert = Add(812);

        var outcome = await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("BecauseICan", null),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("reason");
        alert.State.Should().Be(AlertState.Open);
        _store.AuditEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_note_longer_than_the_limit_is_refused()
    {
        var alert = Add(812);

        var outcome = await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("Accepted", new string('n', 501)),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("note");
        alert.State.Should().Be(AlertState.Open);
    }

    [Fact]
    public async Task Resolving_an_already_resolved_alert_is_a_conflict()
    {
        var alert = Add(812);
        await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("Recovered", null),
            AuditActor.Unknown,
            CancellationToken.None);

        var outcome = await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("SensorFault", null),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("alert_not_open");
        alert.ResolvedReason.Should().Be(ResolvedReason.Recovered);
        _store.AuditEntries.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_failed_push_does_not_turn_a_stored_change_into_a_failed_request()
    {
        var alert = Add(812);
        _broadcaster.Failure = new InvalidOperationException("hub closed");

        var outcome = await _service.AcknowledgeAsync(
            alert.Id,
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        alert.State.Should().Be(AlertState.Acknowledged);
        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task The_timeline_reports_the_trigger_the_acknowledgement_and_the_resolution()
    {
        var alert = Add(812);
        await _service.AcknowledgeAsync(alert.Id, _store.Owner, AuditActor.Unknown, CancellationToken.None);
        _clock.UtcNow = Now.AddMinutes(7);
        await _service.ResolveAsync(
            alert.Id,
            _store.Owner,
            new ResolveAlertRequest("SensorFault", "probe replaced"),
            AuditActor.Unknown,
            CancellationToken.None);

        var outcome = await _service.TimelineAsync(alert.Id, _store.Owner, CancellationToken.None);
        var timeline = outcome.Timeline!;

        timeline.Select(entry => entry.Event)
            .Should().Equal(AlertEvent.Opened, AlertEvent.Acknowledged, AlertEvent.Resolved);
        timeline[1].ActorUserId.Should().Be(_store.Owner);
        timeline[2].Reason.Should().Be(nameof(ResolvedReason.SensorFault));
        timeline[2].Note.Should().Be("probe replaced");
        timeline[2].At.Should().Be(Now.AddMinutes(7));
    }

    [Fact]
    public async Task An_open_alert_has_only_the_trigger_on_its_timeline()
    {
        var alert = Add(812);

        var outcome = await _service.TimelineAsync(alert.Id, _store.Owner, CancellationToken.None);

        outcome.Timeline.Should().ContainSingle()
            .Which.Event.Should().Be(AlertEvent.Opened, "with no note to read and no escalation row yet");
    }

    /// <summary>Puts one alert in the store and returns the row, so a test can assert what changed on it.</summary>
    private Alert Add(
        long id,
        MetricCode? metric = MetricCode.TempC,
        AlertSource source = AlertSource.Threshold)
    {
        var alert = new Alert
        {
            Id = id,
            TerrariumId = Guid.NewGuid(),
            DeviceId = Guid.NewGuid(),
            Metric = metric,
            Severity = AlertSeverity.Warning,
            Source = source,
            Phase = ThresholdPhase.Any,
            State = AlertState.Open,
            TriggeredAt = Now.AddMinutes(-9),
            LastObservedAt = Now.AddMinutes(-1),
            BandMin = 24m,
            BandMax = 32m,
        };

        _store.Alerts.Add(new AlertRecord(alert, "sr-3f9a2c"));

        return alert;
    }
}
