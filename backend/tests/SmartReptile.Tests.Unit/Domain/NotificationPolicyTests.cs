using FluentAssertions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The decision matrix of §02-design/05 §4 and the cadence arithmetic of §5–§6 — TC-U-37…TC-U-45 and TC-U-41's
/// retry schedule. These are the tests that make "why was I not told" answerable: every cell of the matrix is one
/// case here, and the reasons are the strings the report groups by.
/// </summary>
public class NotificationPolicyTests
{
    private static readonly TimeOnly QuietStart = new(22, 0);
    private static readonly TimeOnly QuietEnd = new(6, 0);

    // ---- §4's matrix ---------------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-U-37")]
    public void A_warning_below_the_user_minimum_is_suppressed_with_its_own_reason()
    {
        var verdict = NotificationPolicy.Judge(Request(AlertSeverity.Warning, MinSeverity: AlertSeverity.Critical));

        verdict.Send.Should().BeFalse();
        verdict.Reason.Should().Be(NotificationReason.BelowMinSeverity);
    }

    [Fact]
    [Trait("TestCase", "TC-U-38")]
    public void A_warning_inside_quiet_hours_is_suppressed_but_a_critical_one_is_not()
    {
        var warning = NotificationPolicy.Judge(Request(AlertSeverity.Warning, quietHours: true));

        warning.Send.Should().BeFalse();
        warning.Reason.Should().Be(NotificationReason.QuietHours);

        var critical = NotificationPolicy.Judge(Request(AlertSeverity.Critical, quietHours: true));

        critical.Send.Should().BeTrue();
        critical.Reason.Should().Be(NotificationReason.BypassedQuietHours);
    }

    [Fact]
    [Trait("TestCase", "TC-U-39")]
    public void A_silenced_metric_and_a_maintenance_device_each_suppress_with_their_own_reason()
    {
        NotificationPolicy.Judge(Request(AlertSeverity.Critical, silenced: true)).Reason
            .Should().Be(NotificationReason.SilencedMetric);

        NotificationPolicy.Judge(Request(AlertSeverity.Critical, maintenance: true)).Reason
            .Should().Be(NotificationReason.Maintenance);
    }

    [Fact]
    public void The_more_specific_decision_wins_when_two_of_them_apply()
    {
        // A silence a human set is more specific than the severity threshold or a quiet window, so the record says
        // silenced_metric rather than below_min_severity — the order of §4's flowchart is contract, not detail.
        var verdict = NotificationPolicy.Judge(Request(
            AlertSeverity.Warning,
            MinSeverity: AlertSeverity.Critical,
            silenced: true,
            quietHours: true));

        verdict.Reason.Should().Be(NotificationReason.SilencedMetric);
    }

    [Fact]
    public void A_resolution_is_withheld_by_preference_and_stops_there()
    {
        // v1 has no recovery-notice opt-in, so every resolution takes this branch; the point of asserting it is that
        // the record says "preference" rather than pretending the alert was delivered and ignored.
        var verdict = NotificationPolicy.Judge(Request(AlertSeverity.Critical, resolved: true));

        verdict.Send.Should().BeFalse();
        verdict.Reason.Should().Be(NotificationReason.Preference);
    }

    [Fact]
    [Trait("TestCase", "TC-U-40")]
    public void An_alert_over_the_hourly_cap_becomes_a_digest_entry()
    {
        NotificationCadence.CapReached(9).Should().BeFalse();
        NotificationCadence.CapReached(10).Should().BeTrue();

        var verdict = NotificationPolicy.Judge(Request(AlertSeverity.Warning, coalesced: true));

        verdict.Send.Should().BeFalse();
        verdict.Reason.Should().Be(NotificationReason.DigestCoalesced);
    }

    [Fact]
    public void An_ordinary_alert_is_sent_with_no_reason()
    {
        var verdict = NotificationPolicy.Judge(Request(AlertSeverity.Warning));

        verdict.Send.Should().BeTrue();
        verdict.Reason.Should().BeNull();
    }

    // ---- quiet hours ---------------------------------------------------------------------------------

    [Fact]
    public void A_quiet_window_that_crosses_midnight_covers_both_ends_of_the_night()
    {
        var quiet = new QuietHours(QuietStart, QuietEnd);

        quiet.Contains(new TimeOnly(23, 30)).Should().BeTrue();
        quiet.Contains(new TimeOnly(2, 0)).Should().BeTrue();
        quiet.Contains(new TimeOnly(22, 0)).Should().BeTrue("the start is inclusive");
        quiet.Contains(new TimeOnly(6, 0)).Should().BeFalse("the end is exclusive");
        quiet.Contains(new TimeOnly(12, 0)).Should().BeFalse();
    }

    [Fact]
    public void A_quiet_window_inside_one_day_is_a_plain_interval()
    {
        var quiet = new QuietHours(new TimeOnly(13, 0), new TimeOnly(15, 0));

        quiet.Contains(new TimeOnly(14, 0)).Should().BeTrue();
        quiet.Contains(new TimeOnly(2, 0)).Should().BeFalse();
    }

    [Fact]
    public void A_pair_whose_ends_are_equal_is_no_window_at_all()
    {
        // Reading 22:00–22:00 as "never notify" would turn a typo into a silent alarm system.
        QuietHours.From(new TimeOnly(22, 0), new TimeOnly(22, 0)).Should().BeNull();
        QuietHours.From(null, new TimeOnly(6, 0)).Should().BeNull();
        QuietHours.From(new TimeOnly(22, 0), null).Should().BeNull();
        QuietHours.From(new TimeOnly(22, 0), new TimeOnly(6, 0)).Should().NotBeNull();
    }

    // ---- §5's repeat cooldown and §6's ladder ----------------------------------------------------------

    [Fact]
    public void The_hourly_cooldown_is_the_documented_sixty_minutes()
    {
        NotificationCadence.RepeatCooldown.Should().Be(TimeSpan.FromMinutes(60));
        NotificationCadence.HourlyCap.Should().Be(10);
    }

    [Fact]
    [Trait("TestCase", "TC-U-43")]
    public void Escalation_walks_the_ladder_once_and_stops_at_acknowledgement()
    {
        // A critical alert opened at t: no step is due before 30 minutes, then 1, 2 and 3 in order.
        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(29), acknowledged: false, stepDelivered: 0)
            .Should().Be(0);

        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(30), acknowledged: false, stepDelivered: 0)
            .Should().Be(1);

        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(60), acknowledged: false, stepDelivered: 1)
            .Should().Be(2);

        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(120), acknowledged: false, stepDelivered: 2)
            .Should().Be(3);

        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(240), acknowledged: false, stepDelivered: 3)
            .Should().Be(0, "the ladder ends at 120 minutes");

        // An acknowledged alert escalates no further, which is what TC-U-43 asserts about an ack at 45 minutes.
        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(60), acknowledged: true, stepDelivered: 1)
            .Should().Be(0);

        // Warnings never escalate.
        NotificationCadence.EscalationDue(
            AlertSeverity.Warning, TimeSpan.FromHours(5), acknowledged: false, stepDelivered: 0)
            .Should().Be(0);
    }

    [Fact]
    public void A_dispatcher_that_missed_two_steps_sends_the_next_one_and_not_the_three_it_missed()
    {
        NotificationCadence.EscalationDue(
            AlertSeverity.Critical, TimeSpan.FromMinutes(125), acknowledged: false, stepDelivered: 0)
            .Should().Be(1, "the ladder advances one step at a time rather than replaying the hours it slept through");
    }

    // ---- BR-13.5's retry schedule ---------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-U-41")]
    public void The_retry_schedule_is_t_plus_5s_35s_and_155s()
    {
        var first = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

        NotificationCadence.AttemptScheduledAt(first, 1).Should().Be(first);
        NotificationCadence.AttemptScheduledAt(first, 2).Should().Be(first.AddSeconds(5));
        NotificationCadence.AttemptScheduledAt(first, 3).Should().Be(first.AddSeconds(35));
        NotificationCadence.ScheduleEndsAt(first).Should().Be(first.AddSeconds(155));
        NotificationCadence.MaxAttempts.Should().Be(3);
    }

    [Fact]
    [Trait("TestCase", "TC-U-41")]
    public void A_retry_is_due_only_after_its_backoff_and_never_past_the_third_attempt()
    {
        var first = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

        NotificationCadence.RetryDue(1, first, first.AddSeconds(4)).Should().BeFalse();
        NotificationCadence.RetryDue(1, first, first.AddSeconds(5)).Should().BeTrue();

        // The second attempt happened at +5 s, so its own 30-second backoff puts the third at +35 s.
        NotificationCadence.RetryDue(2, first.AddSeconds(5), first.AddSeconds(34)).Should().BeFalse();
        NotificationCadence.RetryDue(2, first.AddSeconds(5), first.AddSeconds(35)).Should().BeTrue();
        NotificationCadence.RetryDue(3, first.AddSeconds(35), first.AddHours(1)).Should().BeFalse("the third attempt is the last");
        NotificationCadence.RetryDue(0, first, first.AddHours(1)).Should().BeFalse("nothing was attempted yet");
    }

    // ---- §5's digest -----------------------------------------------------------------------------------

    [Fact]
    [Trait("TestCase", "TC-U-45")]
    public void The_digest_counts_the_hours_alerts_and_names_the_worst_of_them()
    {
        var sources = new List<DigestSource>
        {
            new(AlertSeverity.Warning, MetricCode.TempC, 33.4m),
            new(AlertSeverity.Warning, MetricCode.TempC, 32.1m),
            new(AlertSeverity.Warning, MetricCode.TempC, 31.9m),
            new(AlertSeverity.Warning, MetricCode.TempC, 30.5m),
            new(AlertSeverity.Warning, MetricCode.TempC, 30.2m),
            new(AlertSeverity.Warning, MetricCode.TempC, 30.1m),
            new(AlertSeverity.Warning, MetricCode.HumidityPct, 22m),
            new(AlertSeverity.Critical, MetricCode.TempC, 34.2m),
        };

        var summary = NotificationDigest.Summarise(sources);

        summary.Total.Should().Be(8);
        summary.Warnings.Should().Be(7);
        summary.Criticals.Should().Be(1);
        summary.WorstMetric.Should().Be(MetricCode.TempC, "the hour was mostly about temperature");
        summary.PeakValue.Should().Be(34.2m);
    }

    [Fact]
    public void An_hour_with_nothing_coalesced_has_no_digest_to_send()
    {
        var summary = NotificationDigest.Summarise([]);

        summary.Total.Should().Be(0);
        summary.WorstMetric.Should().BeNull();
        summary.PeakValue.Should().BeNull();
    }

    [Fact]
    public void A_device_level_alert_contributes_to_the_count_without_inventing_a_metric()
    {
        var summary = NotificationDigest.Summarise([
            new DigestSource(AlertSeverity.Critical, null, null),
            new DigestSource(AlertSeverity.Warning, null, null),
        ]);

        summary.Total.Should().Be(2);
        summary.WorstMetric.Should().BeNull();
        summary.PeakValue.Should().BeNull();
    }

    [Fact]
    public void The_hour_start_is_the_hour_the_instant_falls_in()
    {
        var instant = new DateTimeOffset(2026, 10, 16, 8, 42, 17, TimeSpan.Zero);

        NotificationCadence.HourStart(instant).Should().Be(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
    }

    // ---- the row factories ----------------------------------------------------------------------------

    [Fact]
    public void A_truncated_row_keeps_the_column_width_rather_than_failing_the_insert()
    {
        var long1 = new string('x', NotificationLog.LastErrorMaxLength + 20);

        var row = NotificationLog.Failure(2, long1, DateTimeOffset.UnixEpoch);

        row.Attempts.Should().Be(2);
        row.Status.Should().Be(NotificationStatus.Failed);
        row.LastError!.Length.Should().Be(NotificationLog.LastErrorMaxLength);
        row.SentAt.Should().BeNull();
    }

    private static NotificationRequest Request(
        AlertSeverity severity,
        AlertSeverity MinSeverity = AlertSeverity.Warning,
        bool resolved = false,
        bool silenced = false,
        bool maintenance = false,
        bool quietHours = false,
        bool coalesced = false) =>
        new(severity, MinSeverity, resolved, silenced, maintenance, quietHours, coalesced);
}
