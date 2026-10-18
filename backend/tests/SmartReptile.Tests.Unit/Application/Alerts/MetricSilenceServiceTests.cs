using System.Text.Json;
using FluentAssertions;
using SmartReptile.Application.Alerts;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;

namespace SmartReptile.Tests.Unit.Application.Alerts;

/// <summary>
/// The silence windows' rules (FR-13, roadmap task 3.4): the value the dispatcher reads, the audit entry every
/// window carries, and the two ways a request is refused.
/// </summary>
public class MetricSilenceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeMetricSilenceStore _store = new();
    private readonly MetricSilenceService _service;

    public MetricSilenceServiceTests() =>
        _service = new MetricSilenceService(_store, new AlertTestClock(Now));

    [Fact]
    public async Task Creating_a_window_returns_it_and_stages_one_audit_row()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            _store.Owner,
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", Now.AddHours(6), "lamp replacement"),
            new AuditActor("203.0.113.7", null, "corr-2"),
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Silence!.Metric.Should().Be("tempC");
        outcome.Silence.Active.Should().BeTrue();
        outcome.Silence.CreatedByUserId.Should().Be(_store.Owner);
        _store.Silences.Should().ContainSingle();
        _store.SaveCount.Should().Be(1);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.MetricSilenced);
        entry.EntityName.Should().Be("MetricSilence");
        entry.EntityId.Should().Be(outcome.Silence.Id.ToString(), "the identity is known before the insert, which is why it is a Guid");
        entry.CorrelationId.Should().Be("corr-2");

        using var after = JsonDocument.Parse(entry.AfterJson!);
        after.RootElement.GetProperty("reason").GetString().Should().Be("lamp replacement");
        after.RootElement.GetProperty("metric").GetString().Should().Be(nameof(MetricCode.TempC));
    }

    [Fact]
    public async Task Omitting_the_metric_silences_every_metric_of_the_terrarium()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            _store.Owner,
            metric: null,
            new CreateSilenceRequest(null, Now.AddHours(12), "away for the weekend"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Silence!.Metric.Should().BeNull();
        _store.Silences.Single().Covers(MetricCode.LightLux).Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_reason_is_refused_with_a_field_and_no_row()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            _store.Owner,
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", Now.AddHours(1), "  "),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("silence_reason_required");
        _store.Silences.Should().BeEmpty();
        _store.AuditEntries.Should().BeEmpty();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task A_window_longer_than_a_day_is_refused_and_names_the_field()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            _store.Owner,
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", Now.AddHours(MetricSilence.MaxWindowHours + 1), "until further notice"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { Field = "untilUtc", Code = "silence_window_too_long" });
    }

    [Fact]
    public async Task A_window_with_no_end_is_refused()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            _store.Owner,
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", null, "forever"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("silence_until_required");
    }

    [Fact]
    public async Task Silence_on_a_terrarium_that_is_not_the_callers_is_not_found()
    {
        var outcome = await _service.CreateAsync(
            _store.TerrariumId,
            Guid.NewGuid(),
            MetricCode.TempC,
            new CreateSilenceRequest("tempC", Now.AddHours(1), "not mine"),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
        _store.Silences.Should().BeEmpty();
    }

    [Fact]
    public async Task Listing_returns_windows_that_are_still_suppressing_only()
    {
        var live = new MetricSilence
        {
            TerrariumId = _store.TerrariumId,
            Reason = "lamp",
            UntilUtc = Now.AddHours(2),
            CreatedAt = Now.AddHours(-1),
        };

        var expired = new MetricSilence
        {
            TerrariumId = _store.TerrariumId,
            Reason = "yesterday",
            UntilUtc = Now.AddHours(-2),
            CreatedAt = Now.AddHours(-3),
        };

        var cancelled = new MetricSilence
        {
            TerrariumId = _store.TerrariumId,
            Reason = "changed my mind",
            UntilUtc = Now.AddHours(3),
            CreatedAt = Now.AddMinutes(-30),
            CancelledAt = Now.AddMinutes(-10),
        };

        _store.Silences.AddRange([live, expired, cancelled]);

        var outcome = await _service.ListAsync(_store.TerrariumId, _store.Owner, CancellationToken.None);

        outcome.Silences.Should().ContainSingle().Which.Id.Should().Be(live.Id);
    }

    [Fact]
    public async Task Cancelling_ends_the_window_early_and_audits_it()
    {
        var silence = new MetricSilence
        {
            TerrariumId = _store.TerrariumId,
            Metric = MetricCode.TempC,
            Reason = "lamp",
            UntilUtc = Now.AddHours(4),
            CreatedAt = Now.AddMinutes(-5),
        };

        _store.Silences.Add(silence);

        var outcome = await _service.CancelAsync(
            _store.TerrariumId,
            silence.Id,
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Silence!.Active.Should().BeFalse();
        silence.CancelledAt.Should().Be(Now);
        _store.SaveCount.Should().Be(1);
        _store.AuditEntries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.SilenceCancelled);
    }

    [Fact]
    public async Task Cancelling_twice_changes_nothing_and_writes_no_second_audit_row()
    {
        var silence = new MetricSilence
        {
            TerrariumId = _store.TerrariumId,
            Reason = "lamp",
            UntilUtc = Now.AddHours(4),
            CreatedAt = Now.AddMinutes(-5),
        };

        _store.Silences.Add(silence);

        await _service.CancelAsync(
            _store.TerrariumId,
            silence.Id,
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        var outcome = await _service.CancelAsync(
            _store.TerrariumId,
            silence.Id,
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue("a DELETE that failed on a retry would leave a keeper believing it is still in force");
        _store.AuditEntries.Should().HaveCount(1);
        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task A_silence_id_that_is_not_this_terrariums_is_not_found()
    {
        var outcome = await _service.CancelAsync(
            _store.TerrariumId,
            Guid.NewGuid(),
            _store.Owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }
}
