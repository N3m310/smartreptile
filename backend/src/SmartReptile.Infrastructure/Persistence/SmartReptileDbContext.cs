using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Evaluation;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Reference;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Summaries;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// The application database context. Schema is created exclusively by EF Core migrations (§03-implementation/03 §2),
/// and the invariants that must survive concurrency are enforced by the database itself (§07-appendices/02 §4).
/// </summary>
public class SmartReptileDbContext(DbContextOptions<SmartReptileDbContext> options) : DbContext(options)
{
    /// <summary>User accounts.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Rotating refresh tokens.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Single-use password-reset codes (BR-01.5).</summary>
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    /// <summary>Monitored enclosures.</summary>
    public DbSet<Terrarium> Terrariums => Set<Terrarium>();

    /// <summary>Species profiles (built-in and user-created).</summary>
    public DbSet<SpeciesProfile> SpeciesProfiles => Set<SpeciesProfile>();

    /// <summary>Bands belonging to species profiles.</summary>
    public DbSet<Threshold> Thresholds => Set<Threshold>();

    /// <summary>Per-terrarium band overrides.</summary>
    public DbSet<ThresholdOverride> ThresholdOverrides => Set<ThresholdOverride>();

    /// <summary>Effective bands captured whenever a terrarium's overrides change (§02-design/02 §3.13).</summary>
    public DbSet<ThresholdSnapshot> ThresholdSnapshots => Set<ThresholdSnapshot>();

    /// <summary>Sensor nodes.</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Device credentials (hashed secrets).</summary>
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();

    /// <summary>Device health reports.</summary>
    public DbSet<DeviceHealthSample> DeviceHealthSamples => Set<DeviceHealthSample>();

    /// <summary>Device-reported events (boot, sensor faults, buffer overflows, clock, calibration).</summary>
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();

    /// <summary>Telemetry samples.</summary>
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();

    /// <summary>Metric values inside samples.</summary>
    public DbSet<MetricReading> MetricReadings => Set<MetricReading>();

    /// <summary>Recomputable per-metric UTC-hour statistics.</summary>
    public DbSet<TelemetryHourlyRollup> TelemetryHourlyRollups => Set<TelemetryHourlyRollup>();

    /// <summary>Computed environmental statistics for one terrarium-local day.</summary>
    public DbSet<DailyEnvironmentalSummary> DailyEnvironmentalSummaries => Set<DailyEnvironmentalSummary>();

    /// <summary>Alerts and their lifecycle.</summary>
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>Time-boxed silence windows (FR-13).</summary>
    public DbSet<MetricSilence> MetricSilences => Set<MetricSilence>();

    /// <summary>Roles and the permission matrix (FR-18).</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>The permission catalogue, seeded from <c>Permissions.All</c>.</summary>
    public DbSet<Permission> PermissionCatalog => Set<Permission>();

    /// <summary>Which role grants which permission — the matrix itself.</summary>
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>Delivery records: one row per dispatch attempt, which is also the in-app inbox (FR-12/FR-13).</summary>
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    /// <summary>Dwell/hysteresis state of the threshold evaluator, one row per (terrarium, metric, phase).</summary>
    public DbSet<EvaluationState> EvaluationStates => Set<EvaluationState>();

    /// <summary>Audit trail (FR-18).</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>The time-zone catalogue a picker reads and the write paths validate against.</summary>
    public DbSet<TimeZoneReference> TimeZones => Set<TimeZoneReference>();

    /// <summary>
    /// The metric catalogue every table that names a metric points at (DI-06). Rows are code-owned, written by the
    /// migration and kept in step with <c>MetricDictionary</c> at start-up.
    /// </summary>
    public DbSet<MetricReference> Metrics => Set<MetricReference>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureIdentity(modelBuilder);
        ConfigureTerrariums(modelBuilder);
        ConfigureSpecies(modelBuilder);
        ConfigureDevices(modelBuilder);
        ConfigureTelemetry(modelBuilder);
        ConfigureSummaries(modelBuilder);
        ConfigureAlerts(modelBuilder);
        ConfigureSilences(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureEvaluation(modelBuilder);
        ConfigureAuditing(modelBuilder);
        ConfigureReferenceData(modelBuilder);
    }

    /// <summary>
    /// The reference tables: data the application reads to fill a picker and validates a body against, owned by the
    /// seeder rather than by a user (TASKS 2026-10-14, item 11).
    /// </summary>
    private static void ConfigureReferenceData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TimeZoneReference>(entity =>
        {
            // The IANA id is the key: it is the standard's own identifier, and Terrarium.TimeZoneId stores exactly
            // this string, so a surrogate key would only add a translation step (ADR-015).
            entity.HasKey(zone => zone.IanaId);
            entity.Property(zone => zone.IanaId).HasMaxLength(64);
            entity.Property(zone => zone.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(zone => zone.CountryCode).HasMaxLength(2).IsRequired();

            // "The picker's list, in order" is the only query this table serves.
            entity.HasIndex(zone => zone.SortOrder);

            entity.ToTable("TimeZone");
        });

        // The metric catalogue (DI-06). Same shape as the permission catalogue: the code defines the rows, the
        // table makes them referenceable, and the eight columns below stop being bare numbers.
        modelBuilder.Entity<MetricReference>(entity =>
        {
            // Keyed by the code the other tables already store, so no row has to be rewritten and the foreign key
            // stays four bytes wide.
            entity.HasKey(metric => metric.Code);
            entity.Property(metric => metric.Code).ValueGeneratedNever();
            entity.Property(metric => metric.ApiKey).HasMaxLength(24).IsRequired();
            entity.Property(metric => metric.PayloadKey).HasMaxLength(8).IsRequired();
            entity.Property(metric => metric.DisplayName).HasMaxLength(60).IsRequired();
            entity.Property(metric => metric.Unit).HasMaxLength(8).IsRequired();
            entity.Property(metric => metric.PlausibleMin).HasPrecision(9, 3);
            entity.Property(metric => metric.PlausibleMax).HasPrecision(9, 3);

            // The natural key a client sends (`?metric=tempC`), and the picker's order.
            entity.HasIndex(metric => metric.ApiKey).IsUnique();
            entity.HasIndex(metric => metric.SortOrder);

            entity.ToTable("Metric");
        });
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        // Roles and the permission matrix (FR-18). Configured before User because the account's foreign key points
        // at it, and the two tables below it are pure join/catalogue rows.
        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Code).HasMaxLength(Role.CodeMaxLength).IsRequired();
            entity.Property(r => r.Name).HasMaxLength(Role.NameMaxLength).IsRequired();
            entity.Property(r => r.Description).HasMaxLength(Role.DescriptionMaxLength).IsRequired();

            // Two editors must not silently overwrite each other's permission set — the terrarium's own rule.
            entity.Property(r => r.RowVersion).IsRowVersion();

            // Case-insensitive by the database's collation, like the username, so `Admin` and `admin` cannot be two
            // roles a deployment accidentally creates.
            entity.HasIndex(r => r.Code).IsUnique();

            entity.ToTable("Role");
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(p => p.Code);
            entity.Property(p => p.Code).HasMaxLength(Permission.CodeMaxLength);
            entity.Property(p => p.Group).HasMaxLength(Permission.GroupMaxLength).IsRequired();
            entity.Property(p => p.Name).HasMaxLength(Permission.NameMaxLength).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(Permission.DescriptionMaxLength).IsRequired();
            entity.ToTable("Permission");
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            // The pair is the identity: a grant either exists or does not, so there is no surrogate key and no
            // duplicate row to reconcile.
            entity.HasKey(rp => new { rp.RoleId, rp.PermissionCode });
            entity.Property(rp => rp.PermissionCode).HasMaxLength(Permission.CodeMaxLength);

            entity.HasOne(rp => rp.Role).WithMany(r => r.Permissions)
                .HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(rp => rp.Permission).WithMany(p => p.Roles)
                .HasForeignKey(rp => rp.PermissionCode).OnDelete(DeleteBehavior.Cascade);

            // "Who holds this permission" is the query the last-administrator guard asks before every role change.
            entity.HasIndex(rp => rp.PermissionCode);

            entity.ToTable("RolePermission");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasMaxLength(32).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PasswordSalt).HasMaxLength(16).IsRequired();

            // Optional on purpose: an account created before recovery codes existed has none, and the API answers
            // a null code exactly like a wrong one rather than growing a special case (BR-01.5).
            entity.Property(u => u.RecoveryCodeHash).HasMaxLength(32);
            entity.Property(u => u.RecoveryCodeSalt).HasMaxLength(16);
            entity.Property(u => u.PreferredLanguage).HasMaxLength(2).IsRequired();
            entity.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(u => u.FcmToken).HasMaxLength(256);

            // Username uniqueness is case-insensitive (BR-01.1).
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

            // Restrict, not cascade: deleting a role must not silently delete the accounts holding it. The role
            // API refuses such a delete, and this is the database saying the same thing in case something else
            // ever tries.
            entity.HasOne(u => u.Role).WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(u => u.RoleId);

            entity.ToTable("User");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).HasMaxLength(32).IsRequired();
            entity.Property(t => t.DeviceInfo).HasMaxLength(200);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => new { t.UserId, t.FamilyId });
            entity.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("RefreshToken");
        });

        modelBuilder.Entity<PasswordResetCode>(entity =>
        {
            entity.HasKey(c => c.Id);

            // SHA-256 of the presented code. Unsalted, so the row can be found by this column — see the type for
            // why that is safe here.
            entity.Property(c => c.CodeHash).HasMaxLength(32).IsRequired();
            entity.Property(c => c.RequestedFromAddress).HasMaxLength(45);
            entity.HasIndex(c => c.CodeHash).IsUnique();

            // Covers "this account's live codes", the only shape the store queries by.
            entity.HasIndex(c => new { c.UserId, c.ExpiresAt });
            entity.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("PasswordResetCode");
        });
    }

    private static void ConfigureTerrariums(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Terrarium>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(60).IsRequired();
            entity.Property(t => t.Location).HasMaxLength(120);
            entity.Property(t => t.Description).HasMaxLength(1000);
            entity.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired();

            // FR-03: the update half is an ETag/If-Match round trip, so the row carries the concurrency token the
            // database maintains rather than a hand-rolled version column a writer could forget to bump.
            entity.Property(t => t.RowVersion).IsRowVersion();

            // Soft delete (DI-10): the query filter keeps deleted terrariums out of normal reads while
            // their alerts and summaries survive for audit. The state column is what the filter reads, so a
            // writer that only stamped DeletedAt cannot leave a "deleted" row visible.
            entity.HasQueryFilter(t => t.Status != TerrariumStatus.Deleted);

            // "This account's live enclosures", which is every list query: the owner leads, the state narrows.
            entity.HasIndex(t => new { t.UserId, t.Status });
            entity.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);

            // Optional since 2026-10-25: an enclosure may exist before its species does. The foreign key stays
            // restricted so a profile that an enclosure still names cannot be deleted out from under it.
            entity.HasOne(t => t.SpeciesProfile).WithMany()
                .HasForeignKey(t => t.SpeciesProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable("Terrarium");
        });
    }

    private static void ConfigureSpecies(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SpeciesProfile>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).HasMaxLength(80).IsRequired();
            entity.Property(p => p.ScientificName).HasMaxLength(120);
            entity.Property(p => p.Notes).HasMaxLength(1000);

            // The picture itself lives in the file store; the row keeps the key and the sniffed media type, so the
            // two columns describe the file rather than being it (FR-10).
            entity.Property(p => p.ImageStorageKey).HasMaxLength(64);
            entity.Property(p => p.ImageContentType).HasMaxLength(100);
            entity.Property(p => p.ImageUpdatedAt);
            entity.Property(p => p.PhotoperiodHours).HasPrecision(4, 2);
            entity.Property(p => p.LightThresholdLux).HasPrecision(9, 2);
            entity.Property(p => p.MinLightHoursPerDay).HasPrecision(4, 2);

            // Optimistic concurrency for authored configuration (UC-03 A3).
            entity.Property(p => p.RowVersion).IsRowVersion();

            entity.HasIndex(p => p.Name);

            // A user cannot create two custom profiles with the same name; seeded names are unique too.
            entity.HasIndex(p => new { p.CreatedByUserId, p.Name }).IsUnique();

            entity.ToTable("SpeciesProfile");
        });

        modelBuilder.Entity<Threshold>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TargetMin).HasPrecision(9, 3);
            entity.Property(t => t.TargetMax).HasPrecision(9, 3);
            entity.Property(t => t.CriticalMin).HasPrecision(9, 3);
            entity.Property(t => t.CriticalMax).HasPrecision(9, 3);
            entity.Property(t => t.RecoveryMargin).HasPrecision(9, 3);
            entity.Property(t => t.SourceRef).HasMaxLength(300);
            entity.Property(t => t.SourceUrl).HasMaxLength(400);

            entity.HasIndex(t => new { t.SpeciesProfileId, t.Metric, t.Phase }).IsUnique();
            entity.HasOne(t => t.SpeciesProfile).WithMany(p => p.Thresholds)
                .HasForeignKey(t => t.SpeciesProfileId).OnDelete(DeleteBehavior.Cascade);

            // DI-06: the metric is a row of the catalogue, not a bare number, so a band cannot name metric 8 by typo.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(t => t.Metric).OnDelete(DeleteBehavior.Restrict);

            // DI-05: band ordering is a database rule as well as a domain rule (§03-implementation/06 §2).
            entity.ToTable("Threshold", table =>
            {
                table.HasCheckConstraint(
                    "CK_Threshold_TargetOrder",
                    "[TargetMin] < [TargetMax]");
                table.HasCheckConstraint(
                    "CK_Threshold_CriticalOrder",
                    "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
                table.HasCheckConstraint(
                    "CK_Threshold_DwellOrder",
                    "[DwellCritMinutes] <= [DwellWarnMinutes] AND [DwellWarnMinutes] > 0");
            });
        });

        modelBuilder.Entity<ThresholdOverride>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.TargetMin).HasPrecision(9, 3);
            entity.Property(o => o.TargetMax).HasPrecision(9, 3);
            entity.Property(o => o.CriticalMin).HasPrecision(9, 3);
            entity.Property(o => o.CriticalMax).HasPrecision(9, 3);
            entity.Property(o => o.RecoveryMargin).HasPrecision(9, 3);
            entity.Property(o => o.Note).HasMaxLength(300);

            entity.HasIndex(o => new { o.TerrariumId, o.Metric, o.Phase }).IsUnique();
            entity.HasOne<Terrarium>().WithMany(t => t.ThresholdOverrides)
                .HasForeignKey(o => o.TerrariumId).OnDelete(DeleteBehavior.Cascade);

            // DI-06: an override points at a catalogue row like the profile band it replaces.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(o => o.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("ThresholdOverride", table =>
            {
                table.HasCheckConstraint("CK_ThresholdOverride_TargetOrder", "[TargetMin] < [TargetMax]");
                table.HasCheckConstraint(
                    "CK_ThresholdOverride_CriticalOrder",
                    "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
            });
        });

        modelBuilder.Entity<ThresholdSnapshot>(entity =>
        {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.EffectiveThresholdsJson).IsRequired();

            // SHA-256 as lower-case hex: fixed width, so char(64) and not nvarchar(64).
            entity.Property(snapshot => snapshot.ThresholdVersionHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .IsUnicode(false)
                .IsRequired();

            // "the snapshot in force at an instant" is the only query: newest for one terrarium, so the index leads
            // with the terrarium and orders by time descending.
            entity.HasIndex(snapshot => new { snapshot.TerrariumId, snapshot.CapturedAtUtc })
                .IsDescending(false, true);

            entity.HasOne<Terrarium>().WithMany()
                .HasForeignKey(snapshot => snapshot.TerrariumId).OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("ThresholdSnapshot");
        });
    }

    private static void ConfigureDevices(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.PublicId).HasMaxLength(24).IsRequired();
            entity.Property(d => d.DeviceName).HasMaxLength(60);
            entity.Property(d => d.ChipId).HasMaxLength(32).IsRequired();
            entity.Property(d => d.MacAddress).HasMaxLength(17).IsRequired();
            entity.Property(d => d.FirmwareVersion).HasMaxLength(16);
            entity.Property(d => d.ClaimCode).HasMaxLength(8);
            entity.Property(d => d.CalibrationJson).HasMaxLength(512);
            entity.Property(d => d.BatteryPct).HasPrecision(5, 2);

            entity.HasIndex(d => d.PublicId).IsUnique();
            entity.HasIndex(d => d.ChipId).IsUnique();
            entity.HasIndex(d => d.LastSeenAt);

            // DI-04: a terrarium holds at most one active device. Enforced here rather than in a service so
            // two concurrent claim requests cannot both win.
            entity.HasIndex(d => d.TerrariumId)
                .IsUnique()
                .HasFilter("[TerrariumId] IS NOT NULL AND [Status] <> 3");

            entity.HasOne(d => d.Terrarium).WithOne(t => t.Device)
                .HasForeignKey<Device>(d => d.TerrariumId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.SetNull);

            entity.ToTable("Device");
        });

        modelBuilder.Entity<DeviceCredential>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.SecretHash).HasMaxLength(32).IsRequired();
            entity.Property(c => c.Salt).HasMaxLength(16).IsRequired();
            entity.HasIndex(c => new { c.DeviceId, c.RevokedAt });
            entity.HasOne(c => c.Device).WithMany(d => d.Credentials)
                .HasForeignKey(c => c.DeviceId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("DeviceCredential");
        });

        modelBuilder.Entity<DeviceHealthSample>(entity =>
        {
            entity.HasKey(h => h.Id);
            entity.Property(h => h.FirmwareVersion).HasMaxLength(16);
            entity.Property(h => h.BatteryPct).HasPrecision(5, 2);
            entity.HasIndex(h => new { h.DeviceId, h.RecordedAt });
            entity.HasOne<Device>().WithMany().HasForeignKey(h => h.DeviceId).OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("DeviceHealthSample");
        });

        modelBuilder.Entity<DeviceEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.DetailJson).HasColumnType("nvarchar(max)");
            entity.Property(e => e.RecordedAt).HasPrecision(3);
            entity.Property(e => e.ReceivedAt).HasPrecision(3);

            // "What happened to this board, in order" is the query task 3.3 asks of this table.
            entity.HasIndex(e => new { e.DeviceId, e.RecordedAt }).IsDescending(false, true);

            entity.HasOne<Device>().WithMany().HasForeignKey(e => e.DeviceId).OnDelete(DeleteBehavior.Cascade);

            // Nullable and set-null: an event is a property of the board, and unbinding it must not delete history.
            entity.HasOne<Terrarium>().WithMany()
                .HasForeignKey(e => e.TerrariumId).OnDelete(DeleteBehavior.SetNull);

            // DI-06: an event that names a metric (a sensor fault, say) names a catalogue row; device-level events
            // carry null.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(e => e.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("DeviceEvent");
        });
    }

    private static void ConfigureTelemetry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetrySample>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedOnAdd();
            entity.Property(s => s.FirmwareVersion).HasMaxLength(16);
            entity.Property(s => s.RecordedAt).HasPrecision(3);
            entity.Property(s => s.ReceivedAt).HasPrecision(3);

            // DI-02: the ingest idempotency guarantee. At-least-once MQTT delivery plus this index means
            // retries cannot create a second row (TC-I-02).
            entity.HasIndex(s => new { s.DeviceId, s.Sequence }).IsUnique();

            // FR-08/FR-09: latest-value lookup and range scans.
            entity.HasIndex(s => new { s.TerrariumId, s.RecordedAt }).IsDescending(false, true);

            // FR-15: the retention sweeper deletes by time.
            entity.HasIndex(s => s.RecordedAt);

            entity.HasOne<Device>().WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Terrarium>().WithMany().HasForeignKey(s => s.TerrariumId).OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("TelemetrySample");
        });

        modelBuilder.Entity<MetricReading>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedOnAdd();
            entity.Property(r => r.Value).HasPrecision(9, 3);
            entity.Property(r => r.RawValue).HasPrecision(9, 3);

            // DI-03: one reading per (sample, metric).
            entity.HasIndex(r => new { r.SampleId, r.Metric }).IsUnique();
            entity.HasIndex(r => new { r.Metric, r.SampleId });

            entity.HasOne(r => r.Sample).WithMany(s => s.Readings)
                .HasForeignKey(r => r.SampleId).OnDelete(DeleteBehavior.Cascade);

            // DI-06: what was 1..7 as a bare number is now a row of `Metric`, so a stored reading always names a
            // metric the catalogue knows. Restrict, not cascade: a catalogue row in use must not be deletable.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(r => r.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("MetricReading");
        });
    }

    private static void ConfigureSummaries(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryHourlyRollup>(entity =>
        {
            entity.HasKey(rollup => rollup.Id);
            entity.Property(rollup => rollup.Id).ValueGeneratedOnAdd();
            entity.Property(rollup => rollup.HourStartUtc).HasPrecision(3);
            entity.Property(rollup => rollup.MinValue).HasPrecision(9, 3);
            entity.Property(rollup => rollup.MaxValue).HasPrecision(9, 3);
            entity.Property(rollup => rollup.AvgValue).HasPrecision(9, 3);
            entity.Property(rollup => rollup.ExposureValue).HasPrecision(18, 6);
            entity.Property(rollup => rollup.ComputedAt).HasPrecision(3);

            entity.HasIndex(rollup => new { rollup.TerrariumId, rollup.Metric, rollup.HourStartUtc }).IsUnique();
            entity.HasOne<Terrarium>().WithMany()
                .HasForeignKey(rollup => rollup.TerrariumId).OnDelete(DeleteBehavior.Cascade);
            // SQL Server rejects a second cascade path from Terrarium through ThresholdSnapshot to this table.
            entity.HasOne<ThresholdSnapshot>().WithMany()
                .HasForeignKey(rollup => rollup.ThresholdSnapshotId).OnDelete(DeleteBehavior.NoAction);

            // DI-06: the rollup's metric is the catalogue's, so a summary cannot be keyed by an unknown number.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(rollup => rollup.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("TelemetryHourlyRollup");
        });

        modelBuilder.Entity<DailyEnvironmentalSummary>(entity =>
        {
            entity.HasKey(summary => summary.Id);
            entity.Property(summary => summary.Id).ValueGeneratedOnAdd();
            entity.Property(summary => summary.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(summary => summary.CoveragePct).HasPrecision(6, 2);
            entity.Property(summary => summary.TempMin).HasPrecision(9, 3);
            entity.Property(summary => summary.TempMax).HasPrecision(9, 3);
            entity.Property(summary => summary.TempAvg).HasPrecision(9, 3);
            entity.Property(summary => summary.HumidityMin).HasPrecision(9, 3);
            entity.Property(summary => summary.HumidityMax).HasPrecision(9, 3);
            entity.Property(summary => summary.HumidityAvg).HasPrecision(9, 3);
            entity.Property(summary => summary.LuxAvg).HasPrecision(12, 3);
            entity.Property(summary => summary.LightHours).HasPrecision(8, 3);
            entity.Property(summary => summary.UvMax).HasPrecision(9, 3);
            entity.Property(summary => summary.OutOfRangeMinutesJson).IsRequired();
            entity.Property(summary => summary.TempExposureDegCHours).HasPrecision(12, 4);
            entity.Property(summary => summary.HumidityDryExposurePctHours).HasPrecision(12, 4);
            entity.Property(summary => summary.HumidityWetExposurePctHours).HasPrecision(12, 4);
            entity.Property(summary => summary.LightDeficitHours).HasPrecision(8, 3);
            entity.Property(summary => summary.ComputedAt).HasPrecision(3);

            entity.HasIndex(summary => new { summary.TerrariumId, summary.LocalDate }).IsUnique();
            entity.HasOne<Terrarium>().WithMany()
                .HasForeignKey(summary => summary.TerrariumId).OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("DailyEnvironmentalSummary");
        });
    }

    private static void ConfigureAlerts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedOnAdd();
            entity.Property(a => a.DedupeKey).HasMaxLength(80).IsRequired();
            entity.Property(a => a.Message).HasMaxLength(300);
            entity.Property(a => a.TriggeringValue).HasPrecision(9, 3);
            entity.Property(a => a.PeakValue).HasPrecision(9, 3);
            entity.Property(a => a.BandMin).HasPrecision(9, 3);
            entity.Property(a => a.BandMax).HasPrecision(9, 3);
            entity.Property(a => a.TriggeredAt).HasPrecision(3);
            entity.Property(a => a.LastObservedAt).HasPrecision(3);
            entity.Property(a => a.AcknowledgedAt).HasPrecision(3);
            entity.Property(a => a.ResolvedAt).HasPrecision(3);

            // DI-01: at most one OPEN alert per dedupe key, enforced by the database (TC-I-07).
            entity.HasIndex(a => a.DedupeKey)
                .IsUnique()
                .HasFilter("[State] <> 2");

            entity.HasIndex(a => new { a.TerrariumId, a.State, a.TriggeredAt }).IsDescending(false, false, true);
            entity.HasIndex(a => a.TriggeredAt);

            entity.HasOne<Terrarium>().WithMany().HasForeignKey(a => a.TerrariumId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Device>().WithMany().HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);

            // DI-06: a metric alert names a catalogue row; a device-level alert carries no metric at all, which is
            // why the column is nullable and the foreign key accepts null.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(a => a.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(a => a.Duration);

            entity.ToTable("Alert");
        });
    }

    /// <summary>
    /// The silence windows of FR-13 (§07-appendices/02 §3.15). Created by 3.4, which is also the only writer: the
    /// dispatcher of 3.5 reads them, and nothing deletes them — a window that expired is history, and history is
    /// what the audit trail is for.
    /// </summary>
    private static void ConfigureSilences(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetricSilence>(entity =>
        {
            entity.HasKey(silence => silence.Id);
            entity.Property(silence => silence.Reason).HasMaxLength(MetricSilence.ReasonMaxLength).IsRequired();
            entity.Property(silence => silence.UntilUtc).HasPrecision(3);
            entity.Property(silence => silence.CreatedAt).HasPrecision(3);
            entity.Property(silence => silence.CancelledAt).HasPrecision(3);

            // "What is suppressing this terrarium right now" is the query the dashboard renders and the dispatcher
            // asks before every notification, so the window's end is part of the index.
            entity.HasIndex(silence => new { silence.TerrariumId, silence.UntilUtc });

            entity.HasOne<Terrarium>().WithMany().HasForeignKey(silence => silence.TerrariumId)
                .OnDelete(DeleteBehavior.Cascade);

            // DI-06: null means "every metric", a value must be one the catalogue holds.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(silence => silence.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("MetricSilence");
        });
    }

    /// <summary>
    /// The delivery records of FR-13 (§07-appendices/02 §3.10), which are also the in-app inbox of FR-12.
    /// </summary>
    /// <remarks>
    /// Three index choices, each answering a question the dispatcher asks on every sweep: the inbox reads
    /// <c>(UserId, CreatedAt DESC)</c>, the response timeline of §6 reads <c>(AlertId)</c>, and the hourly cap and the
    /// digest both read a terrarium's rows inside a window, which is what <c>(TerrariumId, CreatedAt)</c> is for.
    /// <para>
    /// <b>No foreign key to <c>Alert</c>.</b> The audit rule this schema follows elsewhere applies here too: the
    /// record of what a keeper was told must not disappear with the alert it was about, and a cascade would let a
    /// future retention job erase the delivery history of an incident. <c>AlertId</c> is therefore a plain column,
    /// like <c>AuditLog</c>'s own references.
    /// </para>
    /// </remarks>
    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).ValueGeneratedOnAdd();
            entity.Property(row => row.Title).HasMaxLength(NotificationLog.TitleMaxLength).IsRequired();
            entity.Property(row => row.Body).HasMaxLength(NotificationLog.BodyMaxLength).IsRequired();
            entity.Property(row => row.DeepLink).HasMaxLength(NotificationLog.DeepLinkMaxLength);
            entity.Property(row => row.SuppressedReason).HasMaxLength(NotificationLog.ReasonMaxLength);
            entity.Property(row => row.LastError).HasMaxLength(NotificationLog.LastErrorMaxLength);
            entity.Property(row => row.CreatedAt).HasPrecision(3);
            entity.Property(row => row.SentAt).HasPrecision(3);
            entity.Property(row => row.ReadAt).HasPrecision(3);

            entity.HasIndex(row => new { row.UserId, row.CreatedAt }).IsDescending(false, true);
            entity.HasIndex(row => row.AlertId);
            entity.HasIndex(row => new { row.TerrariumId, row.CreatedAt });

            entity.ToTable("NotificationLog");
        });
    }

    /// <summary>
    /// The evaluator's state table (§07-appendices/02 §3.9). The key is the (terrarium, metric, phase) triple
    /// itself: one state row per key is the invariant, and making it the primary key means a bug that tries to
    /// write a second row fails at the database rather than at review time.
    /// </summary>
    private static void ConfigureEvaluation(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvaluationState>(entity =>
        {
            entity.HasKey(state => new { state.TerrariumId, state.Metric, state.Phase });

            entity.Property(state => state.FirstOutOfBandAt).HasPrecision(3);
            entity.Property(state => state.CriticalSinceAt).HasPrecision(3);
            entity.Property(state => state.LastNotificationAt).HasPrecision(3);
            entity.Property(state => state.LastEvaluatedAtUtc).HasPrecision(3);

            // Two evaluators must not advance one key at the same time.
            entity.Property(state => state.RowVersion).IsRowVersion();

            entity.HasOne<Terrarium>().WithMany()
                .HasForeignKey(state => state.TerrariumId).OnDelete(DeleteBehavior.Cascade);

            // DI-06: the evaluator's key names a catalogue metric, so its state cannot outlive the metric's meaning.
            entity.HasOne<MetricReference>().WithMany()
                .HasForeignKey(state => state.Metric).OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("EvaluationState");
        });
    }

    private static void ConfigureAuditing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedOnAdd();
            entity.Property(a => a.EntityName).HasMaxLength(40).IsRequired();
            entity.Property(a => a.EntityId).HasMaxLength(64).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(40).IsRequired();
            entity.Property(a => a.IpAddress).HasMaxLength(AuditLog.IpAddressMaxLength).IsRequired();

            // Both are client-supplied headers, so the widths match the truncation in AuditLog.ForDevice rather
            // than letting an over-long header fail the insert.
            entity.Property(a => a.UserAgent).HasMaxLength(AuditLog.UserAgentMaxLength);
            entity.Property(a => a.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength);
            entity.Property(a => a.OccurredAt).HasPrecision(3);

            // "What happened to this thing, in order" is the query the trail exists to answer.
            entity.HasIndex(a => new { a.EntityName, a.EntityId, a.OccurredAt }).IsDescending(false, false, true);
            entity.HasIndex(a => a.OccurredAt);

            // No foreign keys: an audit row outlives the entity it names, and a cascade would let deleting a user
            // erase the record of what that user did.
            entity.ToTable("AuditLog");
        });
    }
}
