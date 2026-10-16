using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Notifications;

/// <summary>What one sweep did, for the worker to log and count.</summary>
/// <param name="Candidates">Alerts the sweep considered.</param>
/// <param name="Delivered">Attempts a channel accepted.</param>
/// <param name="Failed">Attempts a channel refused (each one is a row, so this counts attempts and not messages).</param>
/// <param name="Suppressed">Rows written because policy withheld a message.</param>
/// <param name="Digests">Hourly digests sent.</param>
/// <param name="PushTokensRetired">Accounts whose push channel was switched off because FCM refused the token.</param>
public sealed record NotificationSweepOutcome(
    int Candidates,
    int Delivered,
    int Failed,
    int Suppressed,
    int Digests,
    int PushTokensRetired)
{
    /// <summary>True when the sweep found nothing to do, which is the common case.</summary>
    public bool IsQuiet => Delivered == 0 && Failed == 0 && Suppressed == 0 && Digests == 0;
}

/// <summary>
/// The notification dispatcher of FR-13 (roadmap task 3.5): decides, for every alert that could owe somebody a
/// message, whether policy allows it, and if so delivers it on every channel that account turned on.
/// </summary>
/// <remarks>
/// <b>Why a sweep and not an event.</b> The design's flowchart starts from an "alert event", but four different
/// writers move an alert — the evaluator (<c>SampleEvaluator</c>), the silence watchdog, the ingest recorder and the
/// lifecycle API — and they live in three layers, so an event bus would mean every one of them learning about
/// notifications. The database is the one place that sees all four, and asking it "which alert owes a message?" is
/// idempotent: a sweep that runs after a crash re-derives the same answer instead of losing the event it missed.
/// The cost is latency, and it is bounded by the sweep interval (15 s), which is far inside the 90 s TC-E2E-03
/// allows. This is the same shape as <c>SilenceWatchdogWorker</c>, and for the same reason.
/// <para>
/// <b>Policy lives in the domain; this class only carries it out.</b> Every decision — suppressed or not, which
/// escalation step, is a retry due, what the digest says — is a pure function of <c>NotificationPolicy</c> and
/// <c>NotificationCadence</c>, which is what makes TC-U-37…45 testable without a clock, a channel or a database.
/// </para>
/// <para>
/// <b>Inbox rows are written for a would-be send even when the hourly cap coalesces the push.</b> §5 asks the
/// eleventh notification of an hour to become a digest entry <em>and</em> the alert to stay visible in the inbox,
/// so the cap is applied per push/email channel and the in-app row is exempt from it. The cap counts delivered
/// push and email messages, not inbox rows: a budget spent by rows nobody can hear would suppress the very
/// notifications it exists to ration.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    INotificationStore store,
    IEnumerable<INotificationChannel> channels,
    IClock clock)
{
    /// <summary>
    /// How far back a resolved alert is still considered. A resolution owes a recovery notice (which v1 withholds
    /// by preference), and the row that records the decision is what makes the pass idempotent — so the window only
    /// has to be long enough to survive a restart, not long enough to be interesting.
    /// </summary>
    public static readonly TimeSpan ResolutionLookback = TimeSpan.FromHours(24);

    private readonly IReadOnlyList<INotificationChannel> _channels = channels.ToArray();

    /// <summary>Runs one pass over what could owe a message, plus the previous hour's digest.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NotificationSweepOutcome> SweepAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var rows = new List<NotificationLog>();

        var candidates = await store
            .ListCandidatesAsync(now - ResolutionLookback, now, cancellationToken)
            .ConfigureAwait(false);

        var delivered = 0;
        var failed = 0;
        var suppressed = 0;
        var retired = 0;
        var deliveredPerHour = new Dictionary<Guid, int>();

        async Task<int> DeliveredThisHourAsync(Guid terrariumId)
        {
            if (deliveredPerHour.TryGetValue(terrariumId, out var known))
            {
                return known;
            }

            var hourStart = NotificationCadence.HourStart(now);

            var count = await store
                .CountDeliveredAsync(terrariumId, hourStart, hourStart + TimeSpan.FromHours(1), cancellationToken)
                .ConfigureAwait(false);

            deliveredPerHour[terrariumId] = count;

            return count;
        }

        foreach (var candidate in candidates)
        {
            // §5's hourly cap is a property of the terrarium, so it is measured once per alert rather than once per
            // channel: two channels of one alert either both fit inside the hour or neither does. The tally is
            // memoised per terrarium because a fleet of alerts in one sweep asks the same question repeatedly.
            var capped = NotificationCadence.CapReached(await DeliveredThisHourAsync(candidate.TerrariumId).ConfigureAwait(false));

            var step = Step(candidate, now);

            foreach (var recipient in StepRecipients(candidate, step))
            {
                // Two verdicts, because the hourly cap is the one cell of §4's matrix that the in-app inbox is
                // exempt from: the cap rations what makes a noise, not the record of it. Every other cell is
                // identical in both, so a withheld inbox row means every channel is withheld.
                var inbox = NotificationPolicy.Judge(Judge(candidate, recipient, now, capped: false));
                var noisy = NotificationPolicy.Judge(Judge(candidate, recipient, now, capped));

                if (!inbox.Send)
                {
                    if (!AlreadyRecorded(candidate, recipient, NotificationChannel.InApp, step, suppressed: true))
                    {
                        rows.Add(For(Suppression(inbox, now), candidate, recipient, null, step, NotificationChannel.InApp));
                        suppressed++;
                    }

                    continue;
                }

                var content = NotificationText.Render(Content(candidate, step, now), recipient.Language);

                foreach (var channel in ChannelsFor(recipient, step))
                {
                    if (channel == NotificationChannel.InApp)
                    {
                        // The inbox row cannot fail and is never repeated: one row per step is the whole record.
                        if (Next(candidate, recipient, channel, step, now) is { } inboxAttempt)
                        {
                            rows.Add(For(
                                NotificationLog.Success(inboxAttempt, now),
                                candidate,
                                recipient,
                                content,
                                step,
                                channel,
                                inbox.Reason));
                            delivered++;
                        }

                        continue;
                    }

                    if (!noisy.Send)
                    {
                        if (!AlreadyRecorded(candidate, recipient, channel, step, suppressed: true))
                        {
                            rows.Add(For(Suppression(noisy, now), candidate, recipient, content, step, channel));
                            suppressed++;
                        }

                        continue;
                    }

                    if (Next(candidate, recipient, channel, step, now) is not { } attempt)
                    {
                        continue;
                    }

                    var destination = Destination(recipient, channel);

                    if (destination is null)
                    {
                        if (!AlreadyRecorded(candidate, recipient, channel, step, suppressed: true))
                        {
                            rows.Add(For(
                                NotificationLog.Suppression(NotificationReason.NoChannel, now),
                                candidate,
                                recipient,
                                content,
                                step,
                                channel));
                            suppressed++;
                        }

                        continue;
                    }

                    var result = await SendAsync(channel, recipient, destination, content, cancellationToken)
                        .ConfigureAwait(false);

                    if (result.Delivered)
                    {
                        rows.Add(For(NotificationLog.Success(attempt, now), candidate, recipient, content, step, channel, noisy.Reason));
                        delivered++;
                    }
                    else
                    {
                        rows.Add(For(NotificationLog.Failure(attempt, result.Error ?? "unknown", now), candidate, recipient, content, step, channel));
                        failed++;
                    }

                    if (result.DestinationRejected)
                    {
                        await store.DisablePushAsync(recipient.UserId, cancellationToken).ConfigureAwait(false);
                        retired++;
                    }
                }
            }
        }

        var digests = await SendDigestsAsync(now, rows, cancellationToken).ConfigureAwait(false);

        if (rows.Count > 0)
        {
            await store.AddAsync(rows, cancellationToken).ConfigureAwait(false);
            await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new NotificationSweepOutcome(candidates.Count, delivered, failed, suppressed, digests, retired);
    }

    /// <summary>
    /// The hourly digest of §5: one message per terrarium that had anything coalesced in the hour that just ended,
    /// sent once, at the top of the next hour.
    /// </summary>
    private async Task<int> SendDigestsAsync(
        DateTimeOffset now,
        List<NotificationLog> rows,
        CancellationToken cancellationToken)
    {
        var hourStart = NotificationCadence.HourStart(now);
        var coveredStart = hourStart - TimeSpan.FromHours(1);

        var inputs = await store
            .ListDigestInputsAsync(coveredStart, hourStart, cancellationToken)
            .ConfigureAwait(false);

        var sent = 0;

        foreach (var input in inputs)
        {
            // The digest's own instant is the top of the hour it covers, so "has this hour been digested" is an
            // equality test rather than a search through a window — which is what keeps the sweep idempotent when
            // it runs twice in the same hour, or restarts halfway through one.
            if (await store.DigestExistsAsync(input.TerrariumId, hourStart, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var summary = NotificationDigest.Summarise(input.Sources);

            if (summary.Total == 0)
            {
                continue;
            }

            foreach (var recipient in input.Recipients.Where(recipient => recipient.IsOwner))
            {
                if (recipient.QuietHours is { } quietHours && IsQuietNow(quietHours, recipient, now))
                {
                    continue;
                }

                var content = NotificationText.RenderDigest(input.TerrariumName, summary, recipient.Language);

                rows.Add(new NotificationLog
                {
                    UserId = recipient.UserId,
                    TerrariumId = input.TerrariumId,
                    Channel = NotificationChannel.InApp,
                    Title = content.Title,
                    Body = content.Body,
                    DeepLink = content.DeepLink,
                    Status = NotificationStatus.Sent,
                    Attempts = 1,
                    CreatedAt = hourStart,
                    SentAt = hourStart,
                    IsRead = true,
                });

                foreach (var channel in ChannelsFor(recipient, step: 0).Where(channel => channel != NotificationChannel.InApp))
                {
                    var destination = Destination(recipient, channel);

                    if (destination is null)
                    {
                        continue;
                    }

                    var result = await SendAsync(
                            channel,
                            recipient,
                            destination,
                            content,
                            cancellationToken)
                        .ConfigureAwait(false);

                    rows.Add(new NotificationLog
                    {
                        UserId = recipient.UserId,
                        TerrariumId = input.TerrariumId,
                        Channel = channel,
                        Title = content.Title,
                        Body = content.Body,
                        DeepLink = content.DeepLink,
                        Status = result.Delivered ? NotificationStatus.Sent : NotificationStatus.Failed,
                        Attempts = 1,
                        LastError = result.Delivered ? null : NotificationLog.Truncate(result.Error ?? "unknown", NotificationLog.LastErrorMaxLength),
                        CreatedAt = hourStart,
                        SentAt = result.Delivered ? hourStart : null,
                        IsRead = true,
                    });
                }

                sent++;
            }
        }

        return sent;
    }

    private async Task<NotificationSendResult> SendAsync(
        NotificationChannel channel,
        NotificationRecipient recipient,
        string destination,
        NotificationContent content,
        CancellationToken cancellationToken)
    {
        var sender = _channels.FirstOrDefault(candidate => candidate.Channel == channel);

        if (sender is null)
        {
            // No adapter registered for a channel the account enabled: a deployment mistake rather than a delivery
            // failure, and it must not read as "the user was told".
            return NotificationSendResult.Failed($"no {channel} channel is configured");
        }

        var delivery = new NotificationDelivery(
            recipient.UserId,
            channel,
            destination,
            content.Title,
            content.Body,
            content.DeepLink,
            recipient.Language);

        try
        {
            return await sender.SendAsync(delivery, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A throwing channel is a refusing channel (TC-U-41): the attempt is recorded and the retry schedule
            // decides whether there is another one.
            return NotificationSendResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Which attempt is owed right now for one (recipient, channel, step): 1 for a first delivery or a repeat, 2–3
    /// for a retry, or null when nothing is due.
    /// </summary>
    private static byte? Next(
        NotificationCandidate candidate,
        NotificationRecipient recipient,
        NotificationChannel channel,
        byte step,
        DateTimeOffset now)
    {
        var existing = candidate
            .Attempts
            .Where(row => row.UserId == recipient.UserId && row.Channel == channel && row.Step == step)
            .OrderByDescending(row => row.CreatedAt)
            .ToArray();

        if (existing.Length == 0)
        {
            return 1;
        }

        var newest = existing[0];

        if (newest.Status == NotificationStatus.Suppressed)
        {
            return null;
        }

        // The inbox cannot fail and repeating it would only duplicate rows: one row per step is the whole record.
        if (channel == NotificationChannel.InApp)
        {
            return null;
        }

        if (newest.Status == NotificationStatus.Sent)
        {
            var stillOpen = candidate.State == AlertState.Open;

            if (!stillOpen || now - newest.CreatedAt < NotificationCadence.RepeatCooldown)
            {
                return null;
            }

            // A repeat is a new notification, not the fourth attempt of the old one.
            return 1;
        }

        if (newest.Status != NotificationStatus.Failed)
        {
            return null;
        }

        if (!NotificationCadence.RetryDue(newest.Attempts, newest.CreatedAt, now))
        {
            return null;
        }

        return (byte)(newest.Attempts + 1);
    }

    private static bool AlreadyRecorded(
        NotificationCandidate candidate,
        NotificationRecipient recipient,
        NotificationChannel channel,
        byte step,
        bool suppressed) =>
        candidate.Attempts.Any(row =>
            row.UserId == recipient.UserId
            && row.Channel == channel
            && row.Step == step
            && (suppressed
                ? row.Status == NotificationStatus.Suppressed
                : row.Status is NotificationStatus.Sent or NotificationStatus.Failed));

    /// <summary>Which escalation step the alert owes, if any (§02-design/05 §6).</summary>
    private static byte Step(NotificationCandidate candidate, DateTimeOffset now)
    {
        var delivered = candidate.Attempts.Count == 0
            ? (byte)0
            : candidate.Attempts.Max(row => row.Step);

        return NotificationCadence.EscalationDue(
            candidate.Severity,
            now - candidate.TriggeredAt,
            candidate.AcknowledgedAt is not null,
            delivered);
    }

    /// <summary>
    /// Who hears about a step. The first delivery and the two later ladder steps belong to the owner; the
    /// 30-minute step is the one §6 adds "a Technician who was not notified" to.
    /// </summary>
    private static IEnumerable<NotificationRecipient> StepRecipients(NotificationCandidate candidate, byte step) =>
        step switch
        {
            1 => candidate.Recipients,
            _ => candidate.Recipients.Where(recipient => recipient.IsOwner),
        };

    /// <summary>
    /// Which channels a recipient's step may use. The 120-minute step is the out-of-band one (§6), so it is email
    /// only: a push that has been ignored twice is not the channel that will find them.
    /// </summary>
    private static IEnumerable<NotificationChannel> ChannelsFor(NotificationRecipient recipient, byte step)
    {
        if (step >= NotificationLog.MaxStep)
        {
            if (recipient.EmailEnabled && recipient.Email is { Length: > 0 })
            {
                yield return NotificationChannel.Smtp;
            }

            yield return NotificationChannel.InApp;

            yield break;
        }

        if (recipient.PushEnabled)
        {
            yield return NotificationChannel.Fcm;
        }

        if (recipient.EmailEnabled)
        {
            yield return NotificationChannel.Smtp;
        }

        yield return NotificationChannel.InApp;
    }

    private static string? Destination(NotificationRecipient recipient, NotificationChannel channel) => channel switch
    {
        NotificationChannel.Fcm => string.IsNullOrWhiteSpace(recipient.FcmToken) ? null : recipient.FcmToken,
        NotificationChannel.Smtp => string.IsNullOrWhiteSpace(recipient.Email) ? null : recipient.Email,
        _ => recipient.UserId.ToString(),
    };

    private static NotificationRequest Judge(
        NotificationCandidate candidate,
        NotificationRecipient recipient,
        DateTimeOffset now,
        bool capped)
    {
        var quiet = recipient.QuietHours is { } quietHours && IsQuietNow(quietHours, recipient, now);

        return new NotificationRequest(
            candidate.Severity,
            recipient.MinSeverity,
            candidate.ResolvedAt is not null,
            candidate.Silenced,
            candidate.Maintenance,
            quiet,
            capped);
    }

    /// <summary>Whether the recipient is inside their quiet hours — judged in their own zone, not the terrarium's.</summary>
    private static bool IsQuietNow(QuietHours quietHours, NotificationRecipient recipient, DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(recipient.TimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);

        return quietHours.Contains(TimeOnly.FromDateTime(local.DateTime));
    }

    private static NotificationContentInput Content(NotificationCandidate candidate, byte step, DateTimeOffset now) =>
        new(
            candidate.AlertId,
            candidate.TerrariumName,
            candidate.Severity,
            candidate.Source,
            candidate.Metric,
            candidate.PeakValue ?? candidate.TriggeringValue,
            candidate.BandMin,
            candidate.BandMax,
            candidate.TriggeredAt,
            now - candidate.TriggeredAt,
            step);

    private static NotificationLog For(
        NotificationLog row,
        NotificationCandidate candidate,
        NotificationRecipient recipient,
        NotificationContent? content,
        byte step,
        NotificationChannel channel,
        string? bypassReason = null)
    {
        row.UserId = recipient.UserId;
        row.AlertId = candidate.AlertId;
        row.TerrariumId = candidate.TerrariumId;
        row.Channel = channel;
        row.Step = step;
        row.IsRead = row.Status != NotificationStatus.Sent || channel != NotificationChannel.InApp;

        if (content is { } rendered)
        {
            row.Title = NotificationLog.Truncate(rendered.Title, NotificationLog.TitleMaxLength);
            row.Body = NotificationLog.Truncate(rendered.Body, NotificationLog.BodyMaxLength);
            row.DeepLink = NotificationLog.Truncate(rendered.DeepLink, NotificationLog.DeepLinkMaxLength);
        }

        if (bypassReason is not null && row.Status == NotificationStatus.Sent)
        {
            row.SuppressedReason = NotificationLog.Truncate(bypassReason, NotificationLog.ReasonMaxLength);
        }

        return row;
    }

    private static NotificationLog Suppression(NotificationVerdict verdict, DateTimeOffset now) =>
        NotificationLog.Suppression(verdict.Reason ?? NotificationReason.NoChannel, now);
}
