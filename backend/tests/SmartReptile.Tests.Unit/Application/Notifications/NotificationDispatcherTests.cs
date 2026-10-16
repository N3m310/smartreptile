using FluentAssertions;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Notifications;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Tests.Unit.Application.Notifications;

/// <summary>
/// The dispatcher's behaviour (FR-13, roadmap task 3.5): TC-U-37…TC-U-45 driven through the real service with fake
/// channels, so the policy matrix, the retry schedule, the hourly cap, the digest and the escalation ladder are all
/// asserted where they actually run.
/// </summary>
public class NotificationDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 8, 30, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Technician = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TerrariumId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeNotificationStore _store = new();
    private readonly FakeNotificationChannel _push = new(NotificationChannel.Fcm);
    private readonly FakeNotificationChannel _email = new(NotificationChannel.Smtp);
    private readonly NotificationTestClock _clock = new(Now);
    private readonly NotificationDispatcher _dispatcher;

    public NotificationDispatcherTests() => _dispatcher = new NotificationDispatcher(_store, [_push, _email], _clock);

    [Fact]
    [Trait("TestCase", "TC-U-42")]
    public async Task A_first_delivery_writes_the_inbox_row_and_the_push_in_the_users_language()
    {
        _store.Candidates = [Candidate(language: "vi")];

        var outcome = await _dispatcher.SweepAsync(CancellationToken.None);

        outcome.Delivered.Should().Be(3, "the inbox row, the push and the email");
        outcome.Suppressed.Should().Be(0);
        _store.SaveCount.Should().Be(1);

        var inbox = _store.Rows.Should().ContainSingle(row => row.Channel == NotificationChannel.InApp).Subject;
        inbox.Status.Should().Be(NotificationStatus.Sent);
        inbox.IsRead.Should().BeFalse("a new inbox entry is unread until the keeper opens it");
        inbox.AlertId.Should().Be(7);
        inbox.DeepLink.Should().Be("smartreptile://alerts/7");

        // The Vietnamese deck is a satellite assembly, so this asserts more than a string: it proves the resource
        // was resolved for the culture rather than falling back to the neutral deck (ADR-013).
        inbox.Title.Should().Contain("Nhiệt độ");
        inbox.Body.Should().Contain("ngưỡng");

        var push = _push.Delivered.Should().ContainSingle().Subject;
        push.Destination.Should().Be("token-1");
        push.Language.Should().Be("vi");
        push.Title.Should().Be(inbox.Title, "every channel carries the same rendered content");
    }

    [Fact]
    public async Task An_english_account_gets_the_neutral_deck()
    {
        _store.Candidates = [Candidate(language: "en")];

        await _dispatcher.SweepAsync(CancellationToken.None);

        var inbox = _store.Rows.Should().ContainSingle(row => row.Channel == NotificationChannel.InApp).Subject;
        inbox.Title.Should().Contain("Temperature");
        inbox.Body.Should().Contain("band");
    }

    [Fact]
    public async Task A_second_sweep_over_the_same_state_writes_nothing()
    {
        _store.Candidates = [Candidate()];

        var first = await _dispatcher.SweepAsync(CancellationToken.None);
        var second = await _dispatcher.SweepAsync(CancellationToken.None);

        first.Delivered.Should().Be(3);
        second.IsQuiet.Should().BeTrue("the attempt rows the first sweep wrote are the record that says so");
        _store.Rows.Should().HaveCount(3);
        _push.Delivered.Should().ContainSingle();
    }

    [Fact]
    [Trait("TestCase", "TC-U-37")]
    public async Task A_warning_below_the_users_floor_is_recorded_as_suppressed_rather_than_attempted()
    {
        _store.Candidates = [Candidate(severity: AlertSeverity.Warning, minSeverity: AlertSeverity.Critical)];

        var outcome = await _dispatcher.SweepAsync(CancellationToken.None);

        outcome.Suppressed.Should().Be(1);
        outcome.Delivered.Should().Be(0);
        _push.Delivered.Should().BeEmpty();

        var row = _store.Rows.Should().ContainSingle().Subject;
        row.Status.Should().Be(NotificationStatus.Suppressed);
        row.SuppressedReason.Should().Be(NotificationReason.BelowMinSeverity);
    }

    [Fact]
    [Trait("TestCase", "TC-U-39")]
    public async Task A_silenced_metric_is_suppressed_and_the_alert_itself_is_untouched()
    {
        _store.Candidates = [Candidate(silenced: true)];

        await _dispatcher.SweepAsync(CancellationToken.None);

        var row = _store.Rows.Should().ContainSingle().Subject;
        row.SuppressedReason.Should().Be(NotificationReason.SilencedMetric);
        _push.Delivered.Should().BeEmpty("silence removes the telling, not the record");
    }

    [Fact]
    [Trait("TestCase", "TC-U-39")]
    public async Task A_device_in_maintenance_tells_nobody()
    {
        _store.Candidates = [Candidate(maintenance: true)];

        await _dispatcher.SweepAsync(CancellationToken.None);

        _store.Rows.Should().ContainSingle()
            .Which.SuppressedReason.Should().Be(NotificationReason.Maintenance);
    }

    [Fact]
    [Trait("TestCase", "TC-U-38")]
    public async Task Quiet_hours_withhold_a_warning_and_are_bypassed_by_a_critical_alert()
    {
        // 23:30 in the recipient's own zone (UTC+7), not in the server's: the window is judged against their clock,
        // which is why the recipient carries a zone at all.
        _clock.UtcNow = new DateTimeOffset(2026, 10, 16, 16, 30, 0, TimeSpan.Zero);

        _store.Candidates =
        [
            Candidate(zone: "Asia/Ho_Chi_Minh", quietHours: new QuietHours(new TimeOnly(22, 0), new TimeOnly(6, 0))),
        ];

        await _dispatcher.SweepAsync(CancellationToken.None);

        _store.Rows.Should().ContainSingle()
            .Which.SuppressedReason.Should().Be(NotificationReason.QuietHours);

        _store.Rows.Clear();
        _store.Candidates =
        [
            Candidate(
                severity: AlertSeverity.Critical,
                zone: "Asia/Ho_Chi_Minh",
                quietHours: new QuietHours(new TimeOnly(22, 0), new TimeOnly(6, 0))),
        ];

        await _dispatcher.SweepAsync(CancellationToken.None);

        var sent = _store.Rows.Should().Contain(row => row.Channel == NotificationChannel.InApp).Subject;
        sent.Status.Should().Be(NotificationStatus.Sent);
        sent.SuppressedReason.Should().Be(
            NotificationReason.BypassedQuietHours,
            "a sent row keeps the marker that says why it was allowed through");
    }

    [Fact]
    [Trait("TestCase", "TC-U-41")]
    public async Task A_refusing_channel_is_retried_on_the_documented_schedule_and_then_left_alone()
    {
        _push.Behaviour = _ => NotificationSendResult.Failed("503 unavailable");
        _store.Candidates = [Candidate(language: "en")];

        // Attempt 1, immediately.
        await _dispatcher.SweepAsync(CancellationToken.None);
        _store.Rows.Should().ContainSingle(row => row.Channel == NotificationChannel.Fcm)
            .Which.Attempts.Should().Be(1);

        // Too early for the second attempt (5 s have not passed).
        _clock.UtcNow = Now.AddSeconds(4);
        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(1);

        // The second attempt at +5 s, the third at +35 s.
        _clock.UtcNow = Now.AddSeconds(5);
        await _dispatcher.SweepAsync(CancellationToken.None);

        _clock.UtcNow = Now.AddSeconds(35);
        await _dispatcher.SweepAsync(CancellationToken.None);

        _push.Delivered.Should().HaveCount(3);
        _store.Rows.Where(row => row.Channel == NotificationChannel.Fcm)
            .Select(row => row.Attempts)
            .Should().Equal((byte)1, (byte)2, (byte)3);

        _store.Rows.Should().OnlyContain(row =>
            row.Channel != NotificationChannel.Fcm
            || (row.Status == NotificationStatus.Failed && row.LastError == "503 unavailable"));

        // Past +155 s the schedule is over: the alert stays open, but this channel has had its three attempts.
        _clock.UtcNow = Now.AddSeconds(155);
        await _dispatcher.SweepAsync(CancellationToken.None);

        _push.Delivered.Should().HaveCount(3, "the third attempt is the last one BR-13.5 allows");
    }

    [Fact]
    [Trait("TestCase", "TC-U-44")]
    public async Task An_unregistered_push_token_switches_the_channel_off_for_that_account()
    {
        _push.Behaviour = _ => NotificationSendResult.Gone("404 Requested entity was not found. UNREGISTERED");
        _store.Candidates = [Candidate()];

        var outcome = await _dispatcher.SweepAsync(CancellationToken.None);

        outcome.Failed.Should().Be(1);
        outcome.PushTokensRetired.Should().Be(1);
        _store.PushDisabled.Should().Equal(Owner);
        _store.Rows.Should().Contain(row =>
            row.Channel == NotificationChannel.Fcm && row.Status == NotificationStatus.Failed);

        // The inbox row still exists: a dead token must not cost the keeper the record of the alert.
        _store.Rows.Should().Contain(row =>
            row.Channel == NotificationChannel.InApp && row.Status == NotificationStatus.Sent);
    }

    [Fact]
    public async Task An_enabled_channel_with_no_destination_is_recorded_rather_than_silently_skipped()
    {
        _store.Candidates = [Candidate(fcmToken: null, pushEnabled: true)];

        await _dispatcher.SweepAsync(CancellationToken.None);

        _store.Rows.Should().ContainSingle(row => row.Channel == NotificationChannel.Fcm)
            .Which.SuppressedReason.Should().Be(NotificationReason.NoChannel);
    }

    [Fact]
    public async Task A_sent_alert_repeats_only_after_the_hourly_cooldown_and_only_while_it_is_open()
    {
        _store.Candidates = [Candidate()];

        await _dispatcher.SweepAsync(CancellationToken.None);

        _clock.UtcNow = Now.AddMinutes(30);
        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(1, "the cooldown has not elapsed");

        _clock.UtcNow = Now.AddMinutes(61);
        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(2);

        // Once a human has taken ownership the repeats stop, exactly as the escalation ladder does.
        _clock.UtcNow = Now.AddMinutes(122);
        _store.Candidates = [Candidate(acknowledgedAt: Now.AddMinutes(70))];
        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(2);
    }

    [Fact]
    [Trait("TestCase", "TC-U-43")]
    public async Task A_critical_alert_escalates_at_thirty_minutes_and_the_ladder_stops_at_acknowledgement()
    {
        var technician = Recipient(Technician, isOwner: false);
        _store.Candidates = [Candidate(severity: AlertSeverity.Critical, technician: technician)];

        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(1, "step zero is the owner's first push");

        _clock.UtcNow = Now.AddMinutes(31);
        await _dispatcher.SweepAsync(CancellationToken.None);

        // §6's 30-minute step: the owner again, plus the technician who had not been told.
        _push.Delivered.Should().HaveCount(3);
        _push.Delivered.Where(push => push.UserId == Technician).Should().ContainSingle();
        _store.Rows.Should().Contain(row => row.Step == 1 && row.Channel == NotificationChannel.InApp);

        // Acknowledged at 45 minutes: nothing escalates at 60.
        _clock.UtcNow = Now.AddMinutes(61);
        _store.Candidates = [Candidate(
            severity: AlertSeverity.Critical,
            technician: technician,
            acknowledgedAt: Now.AddMinutes(45))];

        await _dispatcher.SweepAsync(CancellationToken.None);
        _push.Delivered.Should().HaveCount(3);
    }

    [Fact]
    [Trait("TestCase", "TC-U-43")]
    public async Task The_hundred_and_twenty_minute_step_is_email_only_because_it_is_the_out_of_band_one()
    {
        _store.Candidates = [Candidate(severity: AlertSeverity.Critical)];

        // Jump straight to the 120-minute step: the ladder sends the next step rather than the two it slept through.
        _store.Rows.Add(new NotificationLog
        {
            AlertId = 7,
            UserId = Owner,
            Channel = NotificationChannel.InApp,
            Step = 0,
            Status = NotificationStatus.Sent,
            Attempts = 1,
            CreatedAt = Now,
        });

        _clock.UtcNow = Now.AddMinutes(121);

        await _dispatcher.SweepAsync(CancellationToken.None);

        _push.Delivered.Should().HaveCount(1);
        _email.Delivered.Should().ContainSingle();
        _store.Rows.Should().Contain(row => row.Step == 1 && row.Channel == NotificationChannel.Smtp);
        _store.Rows.Should().NotContain(row => row.Step == 2 || row.Step == 3,
            "one sweep advances one step");
    }

    [Fact]
    [Trait("TestCase", "TC-U-40")]
    public async Task Past_the_hourly_cap_the_push_is_coalesced_while_the_inbox_entry_still_appears()
    {
        _store.DeliveredThisHour = NotificationCadence.HourlyCap;
        _store.Candidates = [Candidate()];

        var outcome = await _dispatcher.SweepAsync(CancellationToken.None);

        outcome.Suppressed.Should().Be(2, "both the push and the email are coalesced");
        _push.Delivered.Should().BeEmpty();
        _email.Delivered.Should().BeEmpty();

        _store.Rows.Should().Contain(row =>
            row.Channel == NotificationChannel.Fcm
            && row.SuppressedReason == NotificationReason.DigestCoalesced);

        _store.Rows.Should().Contain(row =>
            row.Channel == NotificationChannel.InApp && row.Status == NotificationStatus.Sent,
            "§5 keeps the alert visible in the inbox even when the cap rations the noise");
    }

    [Fact]
    [Trait("TestCase", "TC-U-45")]
    public async Task The_hour_that_coalesced_alerts_gets_exactly_one_digest_at_the_top_of_the_next_hour()
    {
        _store.DigestInputs.Add(new NotificationDigestInput(
            TerrariumId,
            "Linh's gecko box",
            [
                new DigestSource(AlertSeverity.Warning, MetricCode.TempC, 32.1m),
                new DigestSource(AlertSeverity.Critical, MetricCode.TempC, 34.2m),
            ],
            [Recipient(Owner, isOwner: true, language: "en")]));

        _clock.UtcNow = new DateTimeOffset(2026, 10, 16, 9, 0, 5, TimeSpan.Zero);

        var outcome = await _dispatcher.SweepAsync(CancellationToken.None);

        outcome.Digests.Should().Be(1);
        _store.SaveCount.Should().Be(1);

        // One digest, delivered on every channel the owner enabled: the inbox row, the push and the email all carry
        // the same text, and none of them names an alert because the digest summarises several.
        var digestRows = _store.Rows.Where(row => row.AlertId == null).ToArray();

        digestRows.Should().HaveCount(3);
        digestRows.Should().OnlyContain(row => row.CreatedAt == new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));
        digestRows.Should().OnlyContain(row => row.TerrariumId == TerrariumId);

        var digest = digestRows.Should().ContainSingle(row => row.Channel == NotificationChannel.InApp).Subject;
        digest.Status.Should().Be(NotificationStatus.Sent);
        digest.Title.Should().Contain("alerts summary");
        digest.Body.Should().Contain("2 alerts").And.Contain("1 warnings").And.Contain("1 critical");
        digest.Body.Should().Contain("34.2").And.Contain("Temperature");
        _push.Delivered.Should().ContainSingle(push => push.Title == digest.Title);

        // The digest records the top of the hour it covers, so the next sweep in the same hour finds it and sends
        // nothing — which is the whole reason the instant is derived rather than stored in a second table.
        var second = await _dispatcher.SweepAsync(CancellationToken.None);
        second.Digests.Should().Be(0);
        _store.Rows.Count(row => row.AlertId == null).Should().Be(3);
    }

    [Fact]
    public async Task A_resolved_alert_records_a_withheld_recovery_notice_and_stops_there()
    {
        _store.Candidates = [Candidate(resolvedAt: Now.AddMinutes(-1))];

        await _dispatcher.SweepAsync(CancellationToken.None);

        var row = _store.Rows.Should().ContainSingle().Subject;
        row.SuppressedReason.Should().Be(NotificationReason.Preference);
        _push.Delivered.Should().BeEmpty();
    }

    private static NotificationCandidate Candidate(
        AlertSeverity severity = AlertSeverity.Warning,
        AlertSeverity minSeverity = AlertSeverity.Warning,
        bool silenced = false,
        bool maintenance = false,
        QuietHours? quietHours = null,
        string language = "en",
        string zone = "UTC",
        string? fcmToken = "token-1",
        bool pushEnabled = true,
        NotificationRecipient? technician = null,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? resolvedAt = null)
    {
        var recipients = new List<NotificationRecipient>
        {
            Recipient(Owner, isOwner: true, language, minSeverity, fcmToken, quietHours, pushEnabled, zone),
        };

        if (technician is { } extra)
        {
            recipients.Add(extra);
        }

        return new NotificationCandidate(
            7,
            TerrariumId,
            "Linh's gecko box",
            severity,
            acknowledgedAt is null ? AlertState.Open : AlertState.Acknowledged,
            AlertSource.Threshold,
            MetricCode.TempC,
            33.4m,
            34.2m,
            28m,
            32m,
            Now.AddMinutes(-10),
            acknowledgedAt,
            resolvedAt,
            maintenance,
            silenced,
            recipients,
            []);
    }

    private static NotificationRecipient Recipient(
        Guid userId,
        bool isOwner,
        string language = "en",
        AlertSeverity minSeverity = AlertSeverity.Warning,
        string? fcmToken = "token-1",
        QuietHours? quietHours = null,
        bool pushEnabled = true,
        string zone = "UTC") =>
        new(
            userId,
            language,
            zone,
            minSeverity,
            PushEnabled: pushEnabled,
            fcmToken,
            EmailEnabled: true,
            "keeper@example.test",
            quietHours,
            isOwner);
}
