namespace SmartReptile.Infrastructure.Options;

/// <summary>
/// The notification layer's deployment settings (FR-13, roadmap task 3.5).
/// </summary>
/// <remarks>
/// Everything here is a deployment decision — an address, a credential, a cadence. Every <em>rule</em> (the hourly
/// cap, the repeat cooldown, the retry backoff, the escalation ladder, quiet hours) is a constant in the domain, so
/// a deployment cannot quietly change what a keeper is promised.
/// </remarks>
public sealed class NotificationOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Notifications";

    /// <summary>
    /// How often the dispatcher looks for work. The cost of the sweep is bounded by the alerts that could owe a
    /// message, and the latency of a push is this interval — 15 s sits far inside the 90 s of TC-E2E-03 while
    /// keeping a two-terrarium demo off the database's critical path.
    /// </summary>
    public int SweepSeconds { get; set; } = 15;

    /// <summary>Firebase Cloud Messaging settings.</summary>
    public FcmOptions Fcm { get; set; } = new();

    /// <summary>SMTP settings. Off by default (§02-design/05 §3): the out-of-band channel is a choice.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>Push settings.</summary>
public sealed class FcmOptions
{
    /// <summary>Whether the push channel is registered at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Path to the service-account JSON. Mounted read-only in compose, exactly as §02-design/05 §3 requires, and
    /// never carried in this file.
    /// </summary>
    public string? ServiceAccountJsonPath { get; set; }
}

/// <summary>Email settings.</summary>
public sealed class SmtpOptions
{
    /// <summary>
    /// Whether the email channel is registered. Off by default: a deployment with no relay must say so rather than
    /// fail a delivery nobody sees. The development compose file turns it on against Mailpit, which is the local
    /// sink TC-I-12 asks for.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Relay host.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>Relay port. Mailpit's default submission port.</summary>
    public int Port { get; set; } = 1025;

    /// <summary>Whether to negotiate STARTTLS. Off for a local sink, on for a real relay.</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Relay username, when it needs one.</summary>
    public string? Username { get; set; }

    /// <summary>Relay password, when it needs one.</summary>
    public string? Password { get; set; }

    /// <summary>Address notifications are sent from.</summary>
    public string FromAddress { get; set; } = "no-reply@smartreptile.local";

    /// <summary>Display name of the sender.</summary>
    public string FromName { get; set; } = "SmartReptile";
}
