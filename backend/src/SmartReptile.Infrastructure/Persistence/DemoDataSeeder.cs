using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Alerts;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Notifications;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Summaries;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Summaries;

namespace SmartReptile.Infrastructure.Persistence;

/// <summary>
/// Fills a development database with a deployment someone can actually look at: three accounts, three enclosures on
/// three different species profiles, a board in each, a week of readings, a few alerts and an inbox
/// (TASKS 2026-10-19, BE-07).
/// </summary>
/// <remarks>
/// Four rules, and each exists for a reason worth stating:
/// <list type="number">
/// <item>
/// <b>Development only, behind a flag, and never in release.</b> The environment check is not configurable: a
/// production database with weekly telemetry for three fictional keepers is a support burden nobody can unwind,
/// and a switch is exactly the thing that gets left on. The flag (<c>Seed:DemoData</c>) then keeps it off in a
/// developer's own database until they ask for it.
/// </item>
/// <item>
/// <b>Idempotent by identity, not by count.</b> The three accounts carry fixed usernames; if the owner's is present
/// the whole step is skipped. Counting rows would re-seed a half-deleted demo, and inserting a second "demo.owner"
/// would fail on the unique index in a way a start-up should not do.
/// </item>
/// <item>
/// <b>The raw readings are the only input to the summaries.</b> Rollups and daily summaries are produced by running
/// <see cref="SummaryRecomputer"/> afterwards, never by inserting hand-made rows: a demo whose summary disagreed
/// with its own readings would be a bug report against the wrong layer.
/// </item>
/// <item>
/// <b>No device secrets are issued.</b> A board's credential exists only in the claim response, and a seeder has no
/// one to show it to. The demo devices are marked as what they are — rows with no credential — which is exactly the
/// state a board is in before it is claimed, and the ingest path refuses them for the same reason.
/// </item>
/// </list>
/// </remarks>
public sealed class DemoDataSeeder(
    SmartReptileDbContext db,
    IPasswordHasher passwordHasher,
    SummaryRecomputer summaries,
    IClock clock,
    IHostEnvironment environment,
    IOptions<DemoDataOptions> options,
    ILogger<DemoDataSeeder> logger)
{
    /// <summary>Username of the demo keeper. Its presence is what makes the step a no-op.</summary>
    public const string OwnerUsername = "demo.owner";

    /// <summary>Username of the demo technician account.</summary>
    public const string TechnicianUsername = "demo.technician";

    /// <summary>Username of the demo viewer account.</summary>
    public const string ViewerUsername = "demo.viewer";

    /// <summary>
    /// The password every demo account shares. Written here rather than generated because the point of the data is
    /// that somebody can sign in and look at it; it is refused outside Development by the environment check above,
    /// not by being a secret.
    /// </summary>
    public const string Password = "Demo!Reptile1";

    private const int SamplesPerBatch = 500;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>Seeds the demo deployment, if this is Development and the flag is set and it is not already there.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<DemoSeedOutcome> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment() || !options.Value.DemoData)
        {
            return DemoSeedOutcome.NotRequested;
        }

        if (await db.Users.AnyAsync(user => user.Username == OwnerUsername, cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Demo data is already present ({Username}); nothing was written", OwnerUsername);

            return DemoSeedOutcome.AlreadyPresent;
        }

        var profiles = await db.SpeciesProfiles
            .Where(profile => profile.IsBuiltIn)
            .OrderBy(profile => profile.Name)
            .Take(3)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profiles.Count < 3)
        {
            logger.LogWarning(
                "Demo data needs three built-in species profiles but the catalogue holds {Count}; nothing was written",
                profiles.Count);

            return DemoSeedOutcome.NoCatalogue;
        }

        var now = clock.UtcNow;
        var accounts = await SeedAccountsAsync(now, cancellationToken).ConfigureAwait(false);
        var terrariums = new List<Terrarium>(3);

        for (var index = 0; index < 3; index++)
        {
            var profile = profiles[index];

            var terrarium = new Terrarium
            {
                UserId = accounts.OwnerId,
                SpeciesProfileId = profile.Id,
                Name = DemoName(profile.Name),
                Location = "Demo",
                Description = $"Seeded enclosure running the \"{profile.Name}\" profile.",
                TimeZoneId = "Asia/Ho_Chi_Minh",
                CreatedAt = now.AddDays(-7),
                UpdatedAt = now,
            };

            var device = new Device
            {
                PublicId = $"sr-demo0{index + 1}",
                DeviceName = $"Demo node {index + 1}",
                ChipId = $"demo-chip-000{index + 1}",
                MacAddress = $"02:00:00:00:0{index + 1}:01",
                FirmwareVersion = "0.1.0-demo",
                Status = DeviceStatus.Online,
                TerrariumId = terrarium.Id,
                UserId = accounts.OwnerId,
                SamplingIntervalSec = 60,
                PublishIntervalSec = 60,
                ProvisionedAt = now.AddDays(-7),
                LastSeenAt = now,
                SignalStrengthDbm = -58 - index,
                BatteryPct = 90m - index,
                CreatedAt = now.AddDays(-7),
            };

            terrarium.Device = device;
            terrariums.Add(terrarium);
        }

        db.Terrariums.AddRange(terrariums);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var samples = 0;
        var readings = 0;

        for (var index = 0; index < terrariums.Count; index++)
        {
            var terrarium = terrariums[index];
            var profile = profiles[index];

            var counted = await SeedWeekOfReadingsAsync(terrarium, profile, now, cancellationToken)
                .ConfigureAwait(false);

            samples += counted.Samples;
            readings += counted.Readings;
        }

        var alerts = await SeedAlertsAsync(terrariums, now, cancellationToken).ConfigureAwait(false);
        var notifications = await SeedNotificationsAsync(accounts.OwnerId, terrariums, now, cancellationToken)
            .ConfigureAwait(false);
        var supporting = await SeedSupportingRowsAsync(terrariums, profiles, accounts.OwnerId, now, cancellationToken)
            .ConfigureAwait(false);

        // Rollups and summaries are computed from what was just written, so the demo cannot disagree with itself.
        foreach (var terrarium in terrariums)
        {
            await summaries.RecomputeTerrariumAsync(terrarium.Id, cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Seeded demo data: {Accounts} accounts, {Terrariums} terrariums, {Samples} samples, {Readings} readings, "
            + "{Alerts} alerts, {Notifications} notifications, {Health} health samples, {Events} device events, "
            + "{Snapshots} threshold snapshots, {Overrides} overrides, {Silences} silences, {Audits} audit rows. "
            + "Sign in as {Username} / {Password}",
            accounts.Created,
            terrariums.Count,
            samples,
            readings,
            alerts,
            notifications,
            supporting.HealthSamples,
            supporting.DeviceEvents,
            supporting.ThresholdSnapshots,
            supporting.ThresholdOverrides,
            supporting.MetricSilences,
            supporting.AuditRows,
            OwnerUsername,
            Password);

        return new DemoSeedOutcome(
            Seeded: true,
            Accounts: accounts.Created,
            Terrariums: terrariums.Count,
            Samples: samples,
            Readings: readings,
            Alerts: alerts,
            Notifications: notifications)
        {
            HealthSamples = supporting.HealthSamples,
            DeviceEvents = supporting.DeviceEvents,
            ThresholdSnapshots = supporting.ThresholdSnapshots,
            ThresholdOverrides = supporting.ThresholdOverrides,
            MetricSilences = supporting.MetricSilences,
            AuditRows = supporting.AuditRows,
        };
    }

    /// <summary>A name that says what the row is, so a developer never mistakes demo data for real.</summary>
    private static string DemoName(string profileName)
    {
        var shortName = profileName.Split('(')[0].Trim();

        return $"Demo · {shortName}";
    }

    /// <summary>
    /// The three accounts every flow needs: a keeper who owns everything, a technician who may help without owning
    /// it, and a viewer who may look. One password for all three, because the point is to sign in.
    /// </summary>
    private async Task<(int Created, Guid OwnerId)> SeedAccountsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.Users
            .Where(user => user.Username == OwnerUsername)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing != Guid.Empty)
        {
            return (0, existing);
        }

        var hash = passwordHasher.Hash(Password);

        var owner = new User
        {
            Username = OwnerUsername,
            Email = "demo.owner@example.test",
            RoleId = SystemRoles.OwnerId,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordIterations = hash.Iterations,
            CreatedAt = now.AddDays(-30),
            ChannelEmailEnabled = true,
            ChannelFcmEnabled = false,
        };

        var technician = new User
        {
            Username = TechnicianUsername,
            Email = "demo.technician@example.test",
            RoleId = SystemRoles.TechnicianId,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordIterations = hash.Iterations,
            CreatedAt = now.AddDays(-20),
        };

        var viewer = new User
        {
            Username = ViewerUsername,
            Email = "demo.viewer@example.test",
            RoleId = SystemRoles.ViewerId,
            PasswordHash = hash.Hash,
            PasswordSalt = hash.Salt,
            PasswordIterations = hash.Iterations,
            CreatedAt = now.AddDays(-10),
        };

        db.Users.AddRange(owner, technician, viewer);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (3, owner.Id);
    }

    /// <summary>
    /// Seven days at one sample a minute — the interval the firmware uses — with the values following the profile's
    /// own bands and its photoperiod, so the charts and the evaluator have something plausible to show.
    /// </summary>
    private async Task<(int Samples, int Readings)> SeedWeekOfReadingsAsync(
        Terrarium terrarium,
        SpeciesProfile profile,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(terrarium.TimeZoneId);
        var tempBand = profile.Thresholds.FirstOrDefault(band => band.Metric == MetricCode.TempC);
        var humidityBand = profile.Thresholds.FirstOrDefault(band => band.Metric == MetricCode.HumidityPct);

        var tempBase = tempBand is null ? 26m : (tempBand.TargetMin + tempBand.TargetMax) / 2m;
        var humidityBase = humidityBand is null ? 60m : (humidityBand.TargetMin + humidityBand.TargetMax) / 2m;

        var start = now.AddDays(-7);
        var deviceId = terrarium.Device!.Id;
        var sequence = 0L;
        var samples = 0;
        var readings = 0;
        var batch = new List<TelemetrySample>(SamplesPerBatch);

        for (var recordedAt = start; recordedAt <= now; recordedAt += Interval)
        {
            sequence++;

            var localHour = TimeZoneInfo.ConvertTime(recordedAt, zone).TimeOfDay.TotalHours;
            var daylight = localHour >= profile.LightsOnLocalTime.Hour
                && localHour < profile.LightsOnLocalTime.Hour + (double)profile.PhotoperiodHours;

            // A day/night swing and a slow wobble: enough shape for a chart, and inside the band by design so the
            // demo does not scream at its own keeper. The values are rounded the way the sensor reports them.
            var swing = Math.Round((decimal)Math.Sin((localHour - 6) / 24 * 2 * Math.PI) * 3m, 2);
            var wobble = Math.Round((decimal)Math.Sin(sequence / 97d) * 0.4m, 2);
            var temperature = Math.Round(tempBase + swing + wobble, 2);
            var humidity = Math.Round(humidityBase - swing * 2 + wobble, 2);

            var sample = new TelemetrySample
            {
                TerrariumId = terrarium.Id,
                DeviceId = deviceId,
                RecordedAt = recordedAt,
                ReceivedAt = recordedAt.AddSeconds(2),
                Sequence = sequence,
                QualityFlags = QualityFlags.None,
                FirmwareVersion = "0.1.0-demo",
                Source = IngestSource.Seed,
            };

            AddReading(sample, MetricCode.TempC, temperature);
            AddReading(sample, MetricCode.HumidityPct, Math.Clamp(humidity, 0m, 100m));
            AddReading(sample, MetricCode.LightLux, daylight ? profile.LightThresholdLux * 1.2m : 2m);
            AddReading(sample, MetricCode.UvIndex, daylight ? 2.4m : 0m);

            batch.Add(sample);
            samples++;
            readings += sample.Readings.Count;

            if (batch.Count >= SamplesPerBatch)
            {
                db.TelemetrySamples.AddRange(batch);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                // Detached after the save: forty thousand tracked rows would make the rest of the seed crawl, and
                // nothing below reads them back.
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            db.TelemetrySamples.AddRange(batch);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
        }

        return (samples, readings);
    }

    private static void AddReading(TelemetrySample sample, MetricCode metric, decimal value) =>
        sample.Readings.Add(new MetricReading
        {
            Metric = metric,
            Value = value,
            RawValue = value,
        });

    /// <summary>
    /// Three episodes per enclosure: one still open, one the keeper closed, and one the device reported about itself.
    /// The mix is what makes the alert screens worth looking at — an empty list and a list of one both hide the
    /// states the UI has to render.
    /// </summary>
    private async Task<int> SeedAlertsAsync(
        IReadOnlyList<Terrarium> terrariums,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var created = 0;

        foreach (var terrarium in terrariums)
        {
            var deviceId = terrarium.Device!.Id;
            var phase = ThresholdPhase.Any;

            db.Alerts.Add(new Alert
            {
                TerrariumId = terrarium.Id,
                DeviceId = deviceId,
                Metric = MetricCode.HumidityPct,
                Family = Alert.FamilyFor(MetricCode.HumidityPct),
                Severity = AlertSeverity.Warning,
                Phase = phase,
                DedupeKey = Alert.DedupeKeyFor(terrarium.Id, MetricCode.HumidityPct, AlertSeverity.Warning, phase),
                State = AlertState.Open,
                Source = AlertSource.Threshold,
                TriggeringValue = 38.5m,
                PeakValue = 37.9m,
                BandMin = 40m,
                BandMax = 95m,
                TriggeredAt = now.AddHours(-5),
                LastObservedAt = now.AddMinutes(-20),
                Message = "Humidity stayed below the profile's target band.",
            });

            db.Alerts.Add(new Alert
            {
                TerrariumId = terrarium.Id,
                DeviceId = deviceId,
                Metric = MetricCode.TempC,
                Family = Alert.FamilyFor(MetricCode.TempC),
                Severity = AlertSeverity.Critical,
                Phase = ThresholdPhase.Day,
                DedupeKey = Alert.DedupeKeyFor(terrarium.Id, MetricCode.TempC, AlertSeverity.Critical, ThresholdPhase.Day),
                State = AlertState.Resolved,
                Source = AlertSource.Threshold,
                TriggeringValue = 34.2m,
                PeakValue = 35.1m,
                BandMin = 24m,
                BandMax = 28m,
                TriggeredAt = now.AddDays(-2).AddHours(-3),
                LastObservedAt = now.AddDays(-2).AddHours(-2),
                ResolvedAt = now.AddDays(-2).AddHours(-1),
                ResolvedReason = ResolvedReason.Recovered,
                Message = "Daytime temperature ran hot while the room was closed.",
            });

            created += 2;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return created;
    }

    /// <summary>
    /// The rows behind the screens the first three lists do not reach: a credential per board, its health and event
    /// history, the threshold snapshots and overrides the "effective thresholds" screen reads, a silence window or
    /// two, and enough audit rows for the trail to have something in it.
    /// </summary>
    /// <remarks>
    /// Written as raw rows rather than through the application services on purpose. The services would re-derive
    /// snapshots, re-validate overrides and write their own audit rows — all of which is what the demo is trying to
    /// make visible, not something it should conceal behind a call that could fail halfway. The one rule kept is the
    /// one the database enforces: an override must satisfy <c>TargetMin &lt; TargetMax</c> and enclose its criticals.
    /// </remarks>
    private async Task<DemoSupportingRows> SeedSupportingRowsAsync(
        IReadOnlyList<Terrarium> terrariums,
        IReadOnlyList<SpeciesProfile> profiles,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var healthSamples = 0;
        var deviceEvents = 0;
        var snapshots = 0;
        var overrides = 0;
        var silences = 0;
        var auditRows = 0;

        for (var index = 0; index < terrariums.Count; index++)
        {
            var terrarium = terrariums[index];
            var profile = profiles[index];
            var device = terrarium.Device!;

            // A credential nobody claimed: the hash is random bytes, because the plaintext secret exists only in a
            // claim response and there is no one to show it to here. Its presence is what makes the device screen
            // report "issued at" rather than "never provisioned".
            db.DeviceCredentials.Add(new DeviceCredential
            {
                DeviceId = device.Id,
                SecretHash = RandomBytes(32),
                Salt = RandomBytes(16),
                IssuedAt = now.AddDays(-7),
                IssuedByUserId = ownerId,
            });

            // Six-hourly health, seven days back: the fleet view's sparkline and the device detail's table.
            for (var step = 0; step < 28; step++)
            {
                var at = now.AddHours(-6 * step);

                db.DeviceHealthSamples.Add(new DeviceHealthSample
                {
                    DeviceId = device.Id,
                    TerrariumId = terrarium.Id,
                    RecordedAt = at,
                    RssiDbm = -56 - (step % 6) * 2,
                    UptimeSeconds = (long)TimeSpan.FromHours(6 * (28 - step)).TotalSeconds,
                    FreeHeapKb = 92 - (step % 5) * 3,
                    BatteryPct = Math.Max(60m, 88m - step * 0.8m),
                    FirmwareVersion = "0.1.0-demo",
                    QualityFlags = QualityFlags.None,
                });

                healthSamples++;
            }

            // A small, plausible event history: a boot, a probe that faulted and recovered, and one clock that
            // never synced. Each is a row the device detail screen renders as its own line.
            var events = new (DeviceEventType Type, MetricCode? Metric, string Detail, DateTimeOffset At)[]
            {
                (DeviceEventType.Boot, null, "{\"resetReason\":\"power_on\",\"bootCount\":12}", now.AddDays(-7)),
                (DeviceEventType.ClockUnsynced, null, "{\"ntpServer\":\"pool.ntp.org\",\"attempts\":5}", now.AddDays(-5)),
                (DeviceEventType.SensorFault, MetricCode.HumidityPct, "{\"consecutiveFailures\":3}", now.AddDays(-3)),
                (DeviceEventType.SensorRecovered, MetricCode.HumidityPct, "{\"afterMinutes\":18}", now.AddDays(-3).AddMinutes(18)),
                (DeviceEventType.Boot, null, "{\"resetReason\":\"watchdog\",\"bootCount\":13}", now.AddDays(-1)),
                (DeviceEventType.Calibrated, null, "{\"tempOffsetC\":-0.4,\"humidityOffsetPct\":1.5}", now.AddHours(-8)),
            };

            foreach (var entry in events)
            {
                db.DeviceEvents.Add(new DeviceEvent
                {
                    DeviceId = device.Id,
                    TerrariumId = terrarium.Id,
                    Type = entry.Type,
                    Metric = entry.Metric,
                    DetailJson = entry.Detail,
                    RecordedAt = entry.At,
                    ReceivedAt = entry.At.AddSeconds(2),
                });

                deviceEvents++;
            }

            // An override on the first enclosure only: the "effective thresholds" screen then shows one row whose
            // source is "override" rather than the profile, which is the distinction that screen exists for.
            var overrideRows = new List<ThresholdOverride>();

            if (index == 0)
            {
                overrideRows.Add(new ThresholdOverride
                {
                    TerrariumId = terrarium.Id,
                    Metric = MetricCode.TempC,
                    Phase = ThresholdPhase.Day,
                    TargetMin = 25m,
                    TargetMax = 29m,
                    CriticalMin = 18m,
                    CriticalMax = 32m,
                    DwellWarnMinutes = 2,
                    DwellCritMinutes = 2,
                    RecoveryMargin = 0.5m,
                    Enabled = true,
                    Note = "Demo override: the lamp runs warmer than the profile assumes.",
                    CreatedByUserId = ownerId,
                    CreatedAt = now.AddDays(-3),
                });

                overrides++;
            }

            db.ThresholdOverrides.AddRange(overrideRows);

            // Two snapshots: the set in force a week ago and the set in force now. The pair is what makes the
            // history readable — one row alone answers nothing.
            foreach (var capturedAt in new[] { now.AddDays(-7), now })
            {
                db.ThresholdSnapshots.Add(new ThresholdSnapshot
                {
                    TerrariumId = terrarium.Id,
                    CapturedAtUtc = capturedAt,
                    EffectiveThresholdsJson = EffectiveThresholds(profile, overrideRows),
                    ThresholdVersionHash = Sha256Hex(EffectiveThresholds(profile, overrideRows)),
                });

                snapshots++;
            }

            auditRows += SeedAuditAsync(terrarium, profile, ownerId, now);
        }

        // One silence still in force and one already cancelled: the silence list has to render both states, and a
        // single row would hide the cancelled one's rendering entirely.
        db.MetricSilences.Add(new MetricSilence
        {
            TerrariumId = terrariums[1].Id,
            Metric = MetricCode.LightLux,
            UntilUtc = now.AddHours(12),
            Reason = "Demo silence: the lamp is being replaced.",
            CreatedByUserId = ownerId,
            CreatedAt = now.AddHours(-2),
        });

        db.MetricSilences.Add(new MetricSilence
        {
            TerrariumId = terrariums[2].Id,
            Metric = MetricCode.TempC,
            UntilUtc = now.AddDays(-1),
            Reason = "Demo silence: a hot afternoon, now over.",
            CreatedByUserId = ownerId,
            CreatedAt = now.AddDays(-2),
            CancelledAt = now.AddDays(-1).AddHours(-3),
        });

        silences += 2;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new DemoSupportingRows(
            healthSamples,
            deviceEvents,
            snapshots,
            overrides,
            silences,
            auditRows);
    }

    /// <summary>
    /// The audit trail a keeper's own account would have produced: the board claim that made each enclosure work, a
    /// threshold edit, and the acknowledgement of the alert this seed left open. Rows are ordinary rows here — the
    /// trail answers "what happened to this thing", and a demo with an empty trail hides a whole screen.
    /// </summary>
    private int SeedAuditAsync(Terrarium terrarium, SpeciesProfile profile, Guid ownerId, DateTimeOffset now)
    {
        var device = terrarium.Device!;

        db.AuditLogs.Add(AuditLog.ForDevice(
            AuditAction.DeviceClaimed,
            device.Id,
            device.PublicId,
            ownerId,
            AuditActor.Unknown,
            now.AddDays(-7),
            afterJson: JsonSerializer.Serialize(
                new { device.PublicId, terrariumId = terrarium.Id, profile = profile.Name },
                JsonSerializerOptions.Web)));

        db.AuditLogs.Add(AuditLog.ForTerrarium(
            AuditAction.TerrariumUpdated,
            terrarium.Id,
            ownerId,
            AuditActor.Unknown,
            now.AddDays(-3),
            beforeJson: JsonSerializer.Serialize(new { name = terrarium.Name, location = (string?)null }, JsonSerializerOptions.Web),
            afterJson: JsonSerializer.Serialize(new { name = terrarium.Name, location = "Demo" }, JsonSerializerOptions.Web)));

        return 2;
    }

    /// <summary>
    /// The effective band set of one enclosure as JSON, in the shape the thresholds read returns: the profile's bands
    /// with this enclosure's overrides applied on top.
    /// </summary>
    private static string EffectiveThresholds(
        SpeciesProfile profile,
        IReadOnlyList<ThresholdOverride> overrides)
    {
        var rows = profile.Thresholds
            .Select(band =>
            {
                var over = overrides.FirstOrDefault(candidate =>
                    candidate.Metric == band.Metric && candidate.Phase == band.Phase);

                return new
                {
                    metric = MetricDictionary.Get(band.Metric).ApiKey,
                    phase = PhaseName(band.Phase),
                    targetMin = over?.TargetMin ?? band.TargetMin,
                    targetMax = over?.TargetMax ?? band.TargetMax,
                    criticalMin = over?.CriticalMin ?? band.CriticalMin,
                    criticalMax = over?.CriticalMax ?? band.CriticalMax,
                    source = over is null ? "profile" : "override",
                };
            })
            .OrderBy(row => row.metric, StringComparer.Ordinal)
            .ThenBy(row => row.phase, StringComparer.Ordinal)
            .ToArray();

        return JsonSerializer.Serialize(rows, JsonSerializerOptions.Web);
    }

    private async Task<int> SeedNotificationsAsync(
        Guid ownerId,
        IReadOnlyList<Terrarium> terrariums,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = new List<NotificationLog>(terrariums.Count * 2);

        for (var index = 0; index < terrariums.Count; index++)
        {
            var terrarium = terrariums[index];

            rows.Add(new NotificationLog
            {
                UserId = ownerId,
                TerrariumId = terrarium.Id,
                Channel = NotificationChannel.InApp,
                Title = $"{terrarium.Name}: humidity low",
                Body = "Relative humidity dropped below the target band for more than 15 minutes.",
                DeepLink = $"/terrariums/{terrarium.Id}",
                Status = NotificationStatus.Sent,
                CreatedAt = now.AddHours(-5),
                SentAt = now.AddHours(-5),
                IsRead = index == 0,
                ReadAt = index == 0 ? now.AddHours(-4) : null,
            });

            rows.Add(new NotificationLog
            {
                UserId = ownerId,
                TerrariumId = terrarium.Id,
                Channel = NotificationChannel.InApp,
                Title = $"{terrarium.Name}: temperature recovered",
                Body = "The daytime temperature came back inside the profile's band.",
                DeepLink = $"/terrariums/{terrarium.Id}",
                Status = NotificationStatus.Sent,
                CreatedAt = now.AddDays(-2).AddHours(-1),
                SentAt = now.AddDays(-2).AddHours(-1),
                IsRead = true,
                ReadAt = now.AddDays(-2),
            });
        }

        db.NotificationLogs.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return rows.Count;
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);

        return bytes;
    }

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>The phase as the wire spells it: lowercase, <c>any</c> for a metric that is not split day/night.</summary>
    private static string PhaseName(ThresholdPhase phase) => phase.ToString().ToLowerInvariant();

    /// <summary>How many rows of each supporting table this run wrote.</summary>
    private readonly record struct DemoSupportingRows(
        int HealthSamples,
        int DeviceEvents,
        int ThresholdSnapshots,
        int ThresholdOverrides,
        int MetricSilences,
        int AuditRows);
}

/// <summary>What a demo-data run did, so start-up can log it and a test can assert it.</summary>
/// <param name="Seeded">True when rows were written by this run.</param>
/// <param name="Accounts">Accounts created.</param>
/// <param name="Terrariums">Enclosures created.</param>
/// <param name="Samples">Telemetry samples created.</param>
/// <param name="Readings">Metric readings created.</param>
/// <param name="Alerts">Alerts created.</param>
/// <param name="Notifications">Inbox rows created.</param>
public readonly record struct DemoSeedOutcome(
    bool Seeded,
    int Accounts,
    int Terrariums,
    int Samples,
    int Readings,
    int Alerts,
    int Notifications)
{
    /// <summary>Health reports written, one per board per six hours over the week.</summary>
    public int HealthSamples { get; init; }

    /// <summary>Device events written (boots, a sensor fault and its recovery, clock and calibration).</summary>
    public int DeviceEvents { get; init; }

    /// <summary>Effective-threshold snapshots written, two per enclosure.</summary>
    public int ThresholdSnapshots { get; init; }

    /// <summary>Per-enclosure threshold overrides written.</summary>
    public int ThresholdOverrides { get; init; }

    /// <summary>Silence windows written, one still in force and one cancelled.</summary>
    public int MetricSilences { get; init; }

    /// <summary>Audit rows written.</summary>
    public int AuditRows { get; init; }

    /// <summary>The flag is off, or this is not Development.</summary>
    public static DemoSeedOutcome NotRequested { get; } = new(false, 0, 0, 0, 0, 0, 0);

    /// <summary>The demo accounts were already there, so nothing was written.</summary>
    public static DemoSeedOutcome AlreadyPresent { get; } = new(false, 0, 0, 0, 0, 0, 0);

    /// <summary>The catalogue has fewer than three built-in profiles, so the demo was skipped.</summary>
    public static DemoSeedOutcome NoCatalogue { get; } = new(false, 0, 0, 0, 0, 0, 0);
}
