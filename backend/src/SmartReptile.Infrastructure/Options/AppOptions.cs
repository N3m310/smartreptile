using Microsoft.Extensions.Options;

namespace SmartReptile.Infrastructure.Options;

/// <summary>Retention policy (FR-15, ADR-009).</summary>
public sealed class RetentionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Retention";

    /// <summary>Days raw samples and readings are kept before the sweeper purges them.</summary>
    public int RawDays { get; set; } = 90;

    /// <summary>Months hourly rollups are kept.</summary>
    public int RollupMonths { get; set; } = 24;

    /// <summary>Days camera snapshots are kept (FR-17).</summary>
    public int SnapshotDays { get; set; } = 7;

    /// <summary>Maximum rows deleted per transaction by the sweeper, so a purge cannot lock the table.</summary>
    public int PurgeBatchSize { get; set; } = 10_000;
}

/// <summary>System-wide defaults (§03-implementation/01 §5).</summary>
public sealed class DefaultsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Defaults";

    /// <summary>Timezone assigned to new terrariums and used for local-day bucketing when none is set.</summary>
    public string TimeZoneId { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>Default sampling interval pushed to devices.</summary>
    public int SamplingIntervalSec { get; set; } = 60;

    /// <summary>Consecutive missing intervals before the device is treated as silent (FR-07).</summary>
    public int SilentAfterIntervals { get; set; } = 3;
}

/// <summary>Startup behaviour switches (§03-implementation/03 §2).</summary>
public sealed class StartupOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Startup";

    /// <summary>Apply EF Core migrations on start-up. Enabled in Development, an explicit runbook step in release.</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;

    /// <summary>Seed the metric dictionary and the built-in species profiles.</summary>
    public bool SeedReferenceDataOnStartup { get; set; } = true;
}

/// <summary>Ingest behaviour.</summary>
public sealed class IngestOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ingest";

    /// <summary>Allow the HTTPS fallback path in addition to MQTT (FR-06).</summary>
    public bool HttpFallbackEnabled { get; set; } = true;

    /// <summary>Maximum samples accepted in one batch (rule V-01).</summary>
    public int MaxSamplesPerBatch { get; set; } = 120;

    /// <summary>Maximum accepted payload size in kilobytes (rule V-01).</summary>
    public int MaxPayloadKb { get; set; } = 32;

    /// <summary>Back-filled samples older than this many hours are recorded but never notify (BR-06.6/BR-11.8).</summary>
    public int BackfillNotifyCutoffHours { get; set; } = 6;
}

/// <summary>Device onboarding settings (FR-04).</summary>
public sealed class ProvisioningOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Provisioning";

    /// <summary>Legacy shared key accepted by the self-registration path; validated at startup in release.</summary>
    public string SharedKey { get; set; } = string.Empty;

    /// <summary>Claim-code lifetime in minutes (BR-04.1).</summary>
    public int ClaimCodeMinutes { get; set; } = 15;

    /// <summary>Alphabet excludes 0/O/1/I/L to avoid transcription errors (§07-appendices/03 §2.1).</summary>
    public string ClaimCodeAlphabet { get; set; } = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Claim-code length in characters.</summary>
    public int ClaimCodeLength { get; set; } = 8;
}

/// <summary>JWT settings (FR-01).</summary>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Signing key; must be at least 32 characters (validated at start-up).</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Token issuer.</summary>
    public string Issuer { get; set; } = "SmartReptile";

    /// <summary>Token audience.</summary>
    public string Audience { get; set; } = "SmartReptile";

    /// <summary>Access-token lifetime in minutes (BR-01.3).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Security-relevant limits and onboarding protection (FR-01 throttling, FR-04 anti-abuse).</summary>
public sealed class OnboardingProtectionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "OnboardingProtection";

    /// <summary>Maximum self-registration attempts allowed per IP inside the window.</summary>
    public int MaxAttemptsPerIp { get; set; } = 1;

    /// <summary>Length of the attempt window in minutes.</summary>
    public int WindowMinutes { get; set; } = 5;

    /// <summary>Global hourly cap on self-registration, to keep a flood from filling the database.</summary>
    public int MaxAttemptsPerHourGlobal { get; set; } = 20;
}

/// <summary>Placeholder validator used to prove the <c>ValidateOnStart</c> pattern (§03-implementation/01 §5).</summary>
internal sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            failures.Add("Jwt:SigningKey is required.");
        }
        else if (options.SigningKey.Length < 32)
        {
            failures.Add("Jwt:SigningKey must be at least 32 characters.");
        }

        if (options.AccessTokenMinutes is <= 0 or > 1440)
        {
            failures.Add("Jwt:AccessTokenMinutes must be between 1 and 1440.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
