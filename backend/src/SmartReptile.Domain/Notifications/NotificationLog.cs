namespace SmartReptile.Domain.Notifications;

/// <summary>Delivery channels of FR-13 (§02-design/05 §3).</summary>
/// <remarks>
/// Values are persisted as integers, so a member is only ever appended. <see cref="InApp"/> is not "a channel like
/// the others": it is the inbox row, and it is the only one that cannot fail — which is why the demo falls back to
/// it (§02-design/05 §8).
/// </remarks>
public enum NotificationChannel
{
    /// <summary>The in-app inbox (S6). Always on.</summary>
    InApp = 0,

    /// <summary>Firebase Cloud Messaging push. Android only in v1.</summary>
    Fcm = 1,

    /// <summary>SMTP email: the out-of-band channel, off by default.</summary>
    Smtp = 2,
}

/// <summary>What happened to one dispatch attempt.</summary>
public enum NotificationStatus
{
    /// <summary>Written and not yet attempted.</summary>
    Queued = 0,

    /// <summary>The channel accepted it (<c>SentAt</c> is set).</summary>
    Sent = 1,

    /// <summary>The attempt threw, or the channel refused it (<c>LastError</c> says what).</summary>
    Failed = 2,

    /// <summary>Policy decided not to send it (<c>SuppressedReason</c> says why).</summary>
    Suppressed = 3,
}

/// <summary>
/// The suppression and bypass vocabulary of §02-design/05 §4. Stored as text rather than an enum because
/// <c>SuppressedReason</c> is 40 characters of report material — the whole point of keeping it is that the
/// suppression table in the report can be read without a decoder ring.
/// </summary>
public static class NotificationReason
{
    /// <summary>The metric is inside an active silence window.</summary>
    public const string SilencedMetric = "silenced_metric";

    /// <summary>The device is in <c>Maintenance</c>: it records everything and tells nobody.</summary>
    public const string Maintenance = "maintenance";

    /// <summary>The alert's severity is below the user's <c>MinNotifySeverity</c>.</summary>
    public const string BelowMinSeverity = "below_min_severity";

    /// <summary>Inside the user's quiet hours, and the alert was only a Warning.</summary>
    public const string QuietHours = "quiet_hours";

    /// <summary>The hourly cap was already reached; this entry is counted into the hour's digest instead.</summary>
    public const string DigestCoalesced = "digest_coalesced";

    /// <summary>A recovery notice and no opt-in. v1 has no such preference, so every resolution takes this path.</summary>
    public const string Preference = "preference";

    /// <summary>The channel is enabled but has no destination (a push with no token, an email with no address).</summary>
    public const string NoChannel = "no_channel";

    /// <summary>
    /// Not a suppression: the marker a <b>sent</b> row carries when a Critical alert passed through quiet hours
    /// (§02-design/05 §4's "Log bypassed_quiet_hours, continue", asserted by TC-U-38).
    /// </summary>
    public const string BypassedQuietHours = "bypassed_quiet_hours";
}

/// <summary>
/// One dispatch attempt (§02-design/05 §3, <c>07-appendices/02</c> §3.10) — the delivery record of FR-13 and the
/// in-app inbox of FR-12 at once.
/// </summary>
/// <remarks>
/// <b>One row per attempt, not one row per notification.</b> <c>07-appendices/02</c> §1 calls the table "one
/// dispatch attempt per row", and TC-I-12 asserts "rows per attempt", so a retried push leaves three rows behind
/// rather than one row that grows an attempt counter. That is what makes the retry schedule auditable: each row's
/// <see cref="CreatedAt"/> is the instant of its own attempt, so the report can show the 5 s / 30 s / 2 min
/// spacing rather than a final count that says the retries happened somewhere.
/// <para>
/// <b><see cref="Step"/> is not in the §3.10 sketch, and it is here on purpose.</b> §02-design/05 §6 asks for
/// escalation rows that "show the full response timeline of one incident", and the alert row deliberately keeps no
/// escalation instant — escalation edits the row in place. Without the step, a 30-minute escalation row and an
/// hourly repeat row are indistinguishable (both are a second <c>Sent</c> row for the same alert), so a timeline
/// could not be assembled from them and the ladder could not be advanced exactly once. The column is the smallest
/// thing that makes the design's own sentence possible.
/// </para>
/// <para>
/// A <see cref="NotificationStatus.Suppressed"/> row carries the same title and body a sent one would. That is
/// deliberate: the suppression table is only useful if it says what was withheld, and re-rendering the content on
/// read would make the record depend on the thresholds and preferences of the day it is read rather than the day it
/// happened. (The dispatcher suppresses before it renders, so a suppressed row's text is rendered for the record —
/// see the as-built note in <c>03-implementation/03</c>.)
/// </para>
/// </remarks>
public class NotificationLog
{
    /// <summary>Column width of <see cref="Title"/>.</summary>
    public const int TitleMaxLength = 160;

    /// <summary>Column width of <see cref="Body"/>.</summary>
    public const int BodyMaxLength = 500;

    /// <summary>Column width of <see cref="DeepLink"/>.</summary>
    public const int DeepLinkMaxLength = 200;

    /// <summary>Column width of <see cref="SuppressedReason"/>.</summary>
    public const int ReasonMaxLength = 40;

    /// <summary>Column width of <see cref="LastError"/>. A message, not a stack trace.</summary>
    public const int LastErrorMaxLength = 300;

    /// <summary>Highest escalation step (§02-design/05 §6's 30 / 60 / 120 minutes).</summary>
    public const byte MaxStep = 3;

    /// <summary>Surrogate key.</summary>
    public long Id { get; set; }

    /// <summary>Recipient, or null for a row that addresses nobody (a diagnostic entry).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Alert this records, or null for a digest entry, which summarises several.</summary>
    public long? AlertId { get; set; }

    /// <summary>Terrarium it is about; null only for account-level notices, which v1 does not send.</summary>
    public Guid? TerrariumId { get; set; }

    /// <summary>Channel this row records.</summary>
    public NotificationChannel Channel { get; set; }

    /// <summary>Rendered title, in the recipient's language.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Rendered body, in the recipient's language.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Client deep link (<c>smartreptile://alerts/{id}</c>), or null for a row with no destination.</summary>
    public string? DeepLink { get; set; }

    /// <summary>What happened.</summary>
    public NotificationStatus Status { get; set; }

    /// <summary>Why it was suppressed, or the bypass marker a sent row carries. Null on an ordinary attempt.</summary>
    public string? SuppressedReason { get; set; }

    /// <summary>
    /// Which attempt this row records: 1 is the first try, 2 and 3 are the retries of BR-13.5. A suppressed row
    /// has 0 attempts, because nothing was attempted.
    /// </summary>
    public byte Attempts { get; set; }

    /// <summary>Channel error of this attempt, truncated to the column.</summary>
    public string? LastError { get; set; }

    /// <summary>Read state of the inbox row.</summary>
    public bool IsRead { get; set; }

    /// <summary>When the attempt was made (and when the inbox row appears).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the channel accepted it; null when it did not.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the recipient opened it.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// Escalation step (§02-design/05 §6): 0 is the first delivery of the alert, 1–3 are the 30 / 60 / 120 minute
    /// steps. A digest row carries 0.
    /// </summary>
    public byte Step { get; set; }

    /// <summary>True when this row is what the user sees in the inbox.</summary>
    public bool IsInboxRow => Channel == NotificationChannel.InApp;

    /// <summary>An attempt the channel refused.</summary>
    /// <param name="attempt">Attempt number, 1-based.</param>
    /// <param name="error">Channel error text.</param>
    /// <param name="atUtc">Instant of the attempt.</param>
    public static NotificationLog Failure(byte attempt, string error, DateTimeOffset atUtc) => new()
    {
        Status = NotificationStatus.Failed,
        Attempts = attempt,
        LastError = Truncate(error, LastErrorMaxLength),
        CreatedAt = atUtc,
    };

    /// <summary>An attempt the channel accepted.</summary>
    /// <param name="attempt">Attempt number, 1-based.</param>
    /// <param name="atUtc">Instant the channel accepted it.</param>
    public static NotificationLog Success(byte attempt, DateTimeOffset atUtc) => new()
    {
        Status = NotificationStatus.Sent,
        Attempts = attempt,
        CreatedAt = atUtc,
        SentAt = atUtc,
    };

    /// <summary>A row policy refused to send, recorded so the report can count what was withheld.</summary>
    /// <param name="reason">One of the <see cref="NotificationReason"/> values.</param>
    /// <param name="atUtc">Instant of the decision.</param>
    public static NotificationLog Suppression(string reason, DateTimeOffset atUtc) => new()
    {
        Status = NotificationStatus.Suppressed,
        SuppressedReason = Truncate(reason, ReasonMaxLength),
        CreatedAt = atUtc,
    };

    /// <summary>Truncates to a column width — never rejects, because a rejected insert would lose the record.</summary>
    /// <param name="value">Text as the channel or the policy produced it.</param>
    /// <param name="maxLength">Column width.</param>
    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
