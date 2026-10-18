using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;

namespace SmartReptile.Application.Abstractions;

/// <summary>
/// One recipient of one alert, with everything policy needs: their preferences, their zone and their quiet hours.
/// </summary>
/// <param name="UserId">The account.</param>
/// <param name="Language">Preferred language (<c>vi</c> or <c>en</c>, ADR-013).</param>
/// <param name="TimeZoneId">Zone quiet hours are evaluated in.</param>
/// <param name="MinSeverity">Lowest severity this account wants to hear about (BR-13.2).</param>
/// <param name="PushEnabled">FCM channel switched on.</param>
/// <param name="FcmToken">Push destination, or null when none is registered.</param>
/// <param name="EmailEnabled">Email channel switched on.</param>
/// <param name="Email">Email destination, or null when the account has none.</param>
/// <param name="QuietHours">The account's quiet window, or null when it has none.</param>
/// <param name="IsOwner">
/// True for the terrarium's owner — the recipient of the first delivery, of every escalation step, and of the
/// hourly digest. A technician holds the role but not the enclosure, which is exactly why §6's ladder names them
/// separately at the 30-minute step.
/// </param>
public sealed record NotificationRecipient(
    Guid UserId,
    string Language,
    string TimeZoneId,
    AlertSeverity MinSeverity,
    bool PushEnabled,
    string? FcmToken,
    bool EmailEnabled,
    string? Email,
    QuietHours? QuietHours,
    bool IsOwner);

/// <summary>One delivery row that already exists for an alert, as the dispatcher needs to see it.</summary>
/// <param name="UserId">Recipient the row belongs to.</param>
/// <param name="Channel">Channel it records.</param>
/// <param name="Step">Escalation step it records.</param>
/// <param name="Attempts">Attempt number this row is, 0 for a suppressed row.</param>
/// <param name="Status">What happened.</param>
/// <param name="CreatedAt">Instant of the attempt.</param>
public sealed record NotificationAttempt(
    Guid UserId,
    NotificationChannel Channel,
    byte Step,
    byte Attempts,
    NotificationStatus Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// An alert that might owe somebody a message, with the state the decision needs — assembled by the store so the
/// dispatcher makes one pass over the database per sweep rather than one query per question.
/// </summary>
/// <param name="AlertId">Alert identity, carried into every row the sweep writes.</param>
/// <param name="TerrariumId">Terrarium the alert belongs to.</param>
/// <param name="TerrariumName">Its display name, for the rendered title.</param>
/// <param name="Severity">Current severity.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="Source">What produced the alert.</param>
/// <param name="Metric">Metric it is about, or null for a device-level alert.</param>
/// <param name="TriggeringValue">Value that triggered it, when it had one.</param>
/// <param name="PeakValue">Peak value of the episode.</param>
/// <param name="BandMin">Band lower bound in force.</param>
/// <param name="BandMax">Band upper bound in force.</param>
/// <param name="TriggeredAt">Start of the episode (back-dated by the engine).</param>
/// <param name="AcknowledgedAt">When a human took ownership, if they did.</param>
/// <param name="ResolvedAt">When it ended, if it did.</param>
/// <param name="Maintenance">True when the device is in <c>Maintenance</c>.</param>
/// <param name="Silenced">True when an active silence window covers this alert.</param>
/// <param name="Recipients">Who may be told, already resolved.</param>
/// <param name="Attempts">Every delivery row this alert already has.</param>
public sealed record NotificationCandidate(
    long AlertId,
    Guid TerrariumId,
    string TerrariumName,
    AlertSeverity Severity,
    AlertState State,
    AlertSource Source,
    MetricCode? Metric,
    decimal? TriggeringValue,
    decimal? PeakValue,
    decimal? BandMin,
    decimal? BandMax,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    bool Maintenance,
    bool Silenced,
    IReadOnlyList<NotificationRecipient> Recipients,
    IReadOnlyList<NotificationAttempt> Attempts);

/// <summary>A whole terrarium's digest input for one hour: what was coalesced, and who is told.</summary>
/// <param name="TerrariumId">Terrarium the digest is about.</param>
/// <param name="TerrariumName">Its display name.</param>
/// <param name="Sources">The alerts the cap withheld during the hour.</param>
/// <param name="Recipients">Who may be told.</param>
public sealed record NotificationDigestInput(
    Guid TerrariumId,
    string TerrariumName,
    IReadOnlyList<DigestSource> Sources,
    IReadOnlyList<NotificationRecipient> Recipients);

/// <summary>
/// The dispatcher's port (FR-13, roadmap task 3.5).
/// </summary>
/// <remarks>
/// Three groups of methods, and the split is the design's: <b>candidates</b> are the sweep's question ("what could
/// owe somebody a message?"), <b>tallies and digests</b> are §5's hourly cap, and <b>inbox reads</b> are FR-12's
/// S6. The dispatcher never issues a query of its own, so the whole policy is testable against a fake store with
/// no SQL Server in sight — which is what TC-U-37…45 are.
/// </remarks>
public interface INotificationStore
{
    /// <summary>
    /// Alerts that could owe a message: those still open or acknowledged, plus those resolved inside the window
    /// (whose recovery notice is the only thing a resolution owes).
    /// </summary>
    /// <param name="fromUtc">Start of the resolution window.</param>
    /// <param name="nowUtc">Instant the sweep is judged at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationCandidate>> ListCandidatesAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    /// <summary>Notifications actually delivered to a terrarium inside the window, for §5's hourly cap.</summary>
    /// <param name="terrariumId">Terrarium to count.</param>
    /// <param name="fromUtc">Window start (inclusive).</param>
    /// <param name="toUtc">Window end (exclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountDeliveredAsync(
        Guid terrariumId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>Every terrarium with coalesced entries in the hour, and what they were.</summary>
    /// <param name="fromUtc">Hour start (inclusive).</param>
    /// <param name="toUtc">Hour end (exclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationDigestInput>> ListDigestInputsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    /// <summary>True when the digest covering an hour has already been sent.</summary>
    /// <param name="terrariumId">Terrarium the digest covers.</param>
    /// <param name="coveredHourEndUtc">The instant the digest records as its own (the top of the next hour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> DigestExistsAsync(Guid terrariumId, DateTimeOffset coveredHourEndUtc, CancellationToken cancellationToken);

    /// <summary>Stages the rows the sweep decided on. The caller commits.</summary>
    /// <param name="rows">Rows to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(IReadOnlyList<NotificationLog> rows, CancellationToken cancellationToken);

    /// <summary>Commits what <see cref="AddAsync"/> staged, and any channel switch the sweep turned off.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>The caller's inbox, newest first.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="unreadOnly">True to return only what has not been opened.</param>
    /// <param name="limit">Maximum rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationLog>> ListInboxAsync(
        Guid userId,
        bool unreadOnly,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// The caller's own inbox row, or null — the scoping rule every route shares: an id that belongs to somebody
    /// else answers exactly as an id that does not exist (BR-02.2).
    /// </summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="notificationId">Row the caller named.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NotificationLog?> FindInboxRowAsync(Guid userId, long notificationId, CancellationToken cancellationToken);

    /// <summary>Marks one row, or every unread row, as read.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="notificationId">Row to mark, or null for "all".</param>
    /// <param name="atUtc">Instant to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many rows changed.</returns>
    Task<int> MarkReadAsync(
        Guid userId,
        long? notificationId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>Unread rows the caller has, for the badge.</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Turns a push token off for an account, because the push service said it is no longer registered (TC-U-44).
    /// </summary>
    /// <param name="userId">Account whose token was refused.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DisablePushAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The delivery rows of one alert, oldest first — the response timeline of §6.</summary>
    /// <param name="alertId">Alert whose deliveries are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<NotificationLog>> ListForAlertAsync(long alertId, CancellationToken cancellationToken);
}
