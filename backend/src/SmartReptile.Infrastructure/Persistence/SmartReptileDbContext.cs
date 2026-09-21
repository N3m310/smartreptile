using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;

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

    /// <summary>Monitored enclosures.</summary>
    public DbSet<Terrarium> Terrariums => Set<Terrarium>();

    /// <summary>Species profiles (built-in and user-created).</summary>
    public DbSet<SpeciesProfile> SpeciesProfiles => Set<SpeciesProfile>();

    /// <summary>Bands belonging to species profiles.</summary>
    public DbSet<Threshold> Thresholds => Set<Threshold>();

    /// <summary>Per-terrarium band overrides.</summary>
    public DbSet<ThresholdOverride> ThresholdOverrides => Set<ThresholdOverride>();

    /// <summary>Sensor nodes.</summary>
    public DbSet<Device> Devices => Set<Device>();

    /// <summary>Device credentials (hashed secrets).</summary>
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();

    /// <summary>Device health reports.</summary>
    public DbSet<DeviceHealthSample> DeviceHealthSamples => Set<DeviceHealthSample>();

    /// <summary>Telemetry samples.</summary>
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();

    /// <summary>Metric values inside samples.</summary>
    public DbSet<MetricReading> MetricReadings => Set<MetricReading>();

    /// <summary>Alerts and their lifecycle.</summary>
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureIdentity(modelBuilder);
        ConfigureTerrariums(modelBuilder);
        ConfigureSpecies(modelBuilder);
        ConfigureDevices(modelBuilder);
        ConfigureTelemetry(modelBuilder);
        ConfigureAlerts(modelBuilder);
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasMaxLength(32).IsRequired();
            entity.Property(u => u.Email).HasMaxLength(256).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(64).IsRequired();
            entity.Property(u => u.PasswordSalt).HasMaxLength(16).IsRequired();
            entity.Property(u => u.PreferredLanguage).HasMaxLength(2).IsRequired();
            entity.Property(u => u.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(u => u.TelegramChatId).HasMaxLength(32);
            entity.Property(u => u.FcmToken).HasMaxLength(256);

            // Username uniqueness is case-insensitive (BR-01.1).
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

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

            // Soft delete (DI-10): the query filter keeps deleted terrariums out of normal reads while
            // their alerts and summaries survive for audit.
            entity.HasQueryFilter(t => t.DeletedAt == null);

            entity.HasIndex(t => t.UserId);
            entity.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
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

            entity.ToTable("ThresholdOverride", table =>
            {
                table.HasCheckConstraint("CK_ThresholdOverride_TargetOrder", "[TargetMin] < [TargetMax]");
                table.HasCheckConstraint(
                    "CK_ThresholdOverride_CriticalOrder",
                    "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
            });
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

            entity.ToTable("MetricReading");
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

            entity.Ignore(a => a.Duration);

            entity.ToTable("Alert");
        });
    }
}
