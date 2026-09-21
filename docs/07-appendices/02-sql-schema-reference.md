# 02 — SQL Schema Reference

SQL Server 2022. All timestamps are `datetime2(3)` in **UTC** (ADR-015). Schema is created exclusively by EF
Core migrations (`03-implementation/03` §2). This appendix is the human-readable counterpart of the
`DbContext` configuration and the reference for writing report queries by hand.

Conventions: `Id` = `uniqueidentifier` (Guid) unless stated; `bigint identity` for high-volume tables;
`rowversion` for optimistic concurrency on authored configuration; soft delete (`DeletedAt`) only on
`Terrarium`.

---

## 1. Table summary

| # | Table | Purpose | Growth | Retention |
|---|---|---|---|---|
| 1 | `User` | Accounts, roles, notification preferences | tiny | account lifetime |
| 2 | `RefreshToken` | Rotating refresh tokens (hashed) | small | 30 days |
| 3 | `Terrarium` | Monitored enclosure | tiny | soft delete |
| 4 | `SpeciesProfile` | Bands + photoperiod + sources | tiny | account lifetime |
| 5 | `Threshold` | Band per (profile, metric, phase) | small | account lifetime |
| 6 | `ThresholdOverride` | Per-terrarium band override | small | account lifetime |
| 7 | `ThresholdSnapshot` | Effective bands at a point in time | small | indefinite (explains history) |
| 8 | `Device` | Node, claim state, config, calibration | tiny | account lifetime |
| 9 | `DeviceCredential` | Hashed device secrets + rotation history | small | indefinite (audit) |
| 10 | `Metric` | Metric dictionary (code, unit, precision) | static | seeded |
| 11 | `TelemetrySample` | One device report | **high** | 90 days |
| 12 | `MetricReading` | One value per metric per sample | **high** | 90 days |
| 13 | `DeviceHealthSample` | RSSI, uptime, heap, battery | low | 90 days |
| 14 | `TelemetryHourlyRollup` | min/max/avg/count, out-of-range, exposure | low | 24 months |
| 15 | `DailyEnvironmentalSummary` | Daily stats, exposure index, coverage | very low | indefinite |
| 16 | `Alert` | Alert lifecycle with band snapshot reference | low | indefinite |
| 17 | `MetricSilence` | Time-boxed notification suppression | tiny | 90 days after expiry |
| 18 | `NotificationLog` | One dispatch attempt per row | small | 90 days |
| 19 | `EvaluationState` | Dwell/hysteresis state per (terrarium, metric, phase) | tiny | current only |
| 20 | `AuditLog` | Who changed what, when | small | 24 months |
| 21 | `ExportJob` | Async export + download token | tiny | file 24 h, row 90 days |
| 22 | `Snapshot` | Camera still (optional) | medium | 7 days |

---

## 2. Core telemetry tables

### 2.1 `TelemetrySample`
| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint identity PK | no | |
| `TerrariumId` | uniqueidentifier FK → `Terrarium` | no | copied at ingest so rebinding never rewrites history |
| `DeviceId` | uniqueidentifier FK → `Device` | no | |
| `RecordedAt` | datetime2(3) | no | device clock, UTC |
| `ReceivedAt` | datetime2(3) | no | default `SYSUTCDATETIME()`; server-authoritative |
| `Sequence` | bigint | no | device monotonic counter; dedupe key part |
| `QualityFlags` | smallint | no | 0 OK · 1 sensorFault · 2 implausible · 4 firstAfterBoot · 8 backfilled · 16 clockUnsynced · 32 calibrationApplied |
| `ClockSkewSeconds` | int | yes | `ReceivedAt − RecordedAt`; null when the device reported no NTP sync |
| `FirmwareVersion` | varchar(16) | no | as reported by the device |
| `Source` | tinyint | no | 0 Mqtt · 1 HttpFallback · 2 Seed/Demo |

**Indexes**
| Name | Definition | Serves |
|---|---|---|
| `IX_Sample_Device_Seq` (unique) | `(DeviceId, Sequence)` | idempotency (DI-02) |
| `IX_Sample_Terrarium_Recorded` | `(TerrariumId, RecordedAt DESC) INCLUDE (Id)` | latest + ranges (FR-08/09) |
| `IX_Sample_Recorded` | `(RecordedAt)` | retention sweep (FR-15) |

### 2.2 `MetricReading`
| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint identity PK | no | |
| `SampleId` | bigint FK → `TelemetrySample` (cascade) | no | |
| `MetricId` | int FK → `Metric` | no | |
| `Value` | decimal(9,3) | no | post-calibration, used for evaluation |
| `RawValue` | decimal(9,3) | yes | sensor output, diagnostics only |

**Indexes:** `UX_Reading_Sample_Metric` unique `(SampleId, MetricId)` (DI-03); `IX_Reading_Metric` `(MetricId, SampleId)`.

### 2.3 `DeviceHealthSample`
`(Id bigint, DeviceId, TerrariumId, RecordedAt, RssiDbm smallint, UptimeSeconds int, FreeHeapKb int, BatteryPct decimal(5,2) null, PowerSource tinyint, FirmwareVersion varchar(16))`
with `IX_Health_Device_Recorded (DeviceId, RecordedAt DESC)`.

### 2.4 `TelemetryHourlyRollup`
| Column | Type | Notes |
|---|---|---|
| `Id` | bigint identity PK | |
| `TerrariumId`, `MetricId` | FK | |
| `HourStartUtc` | datetime2(3) | truncated to the hour |
| `MinValue`, `MaxValue`, `AvgValue` | decimal(9,3) | `AvgValue` is time-weighted (see `03-implementation/06` §5.5) |
| `SampleCount` | int | |
| `OutOfRangeMinutes` | smallint | against the thresholds in force |
| `ExposureValue` | decimal(9,3) | degree-hours / %-hours / lux-deficit-hours depending on the metric |
| `ThresholdSnapshotId` | uniqueidentifier FK, null | explains which bands were used |
| `ComputedAt` | datetime2(3) | recomputation marker |

**Index:** `UX_Rollup_Terrarium_Metric_Hour` unique `(TerrariumId, MetricId, HourStartUtc)`; `IX_Rollup_Hour` `(HourStartUtc)` for the sweeper.

### 2.5 `DailyEnvironmentalSummary`
| Column | Type | Notes |
|---|---|---|
| `Id` | uniqueidentifier PK | |
| `TerrariumId` | FK | |
| `LocalDate` | date | local date per `TimeZoneId` |
| `TimeZoneId` | varchar(64) | the timezone used at compute time |
| `CoveragePct` | decimal(5,2) | samplesReceived / expected |
| `IsLowConfidence` | bit | `CoveragePct < 80` |
| `TempMin`, `TempMax`, `TempAvg` | decimal(5,2) | °C |
| `HumidityMin`, `HumidityMax`, `HumidityAvg` | decimal(5,2) | %RH |
| `LuxAvg` | decimal(9,2) | lx |
| `UvMax` | decimal(4,2) | UVI |
| `LightHours` | decimal(4,2) | hours above the profile threshold |
| `LightDeficitHours` | decimal(4,2) | `max(0, required − actual)` |
| `TempExposureDegCHours` | decimal(7,3) | hot + cold, see `03-implementation/06` §5.1 |
| `HumidityDryHours` / `HumidityWetHours` | decimal(7,3) | separated on purpose |
| `OutOfRangeMinutesJson` | nvarchar(400) | `{"tempC":40,"humidityPct":15}` |
| `AlertCount` / `CriticalAlertCount` | int / int | |
| `ComputedAt` | datetime2(3) | |

**Index:** `UX_Summary_Terrarium_LocalDate` unique `(TerrariumId, LocalDate)`.

---

## 3. Configuration and identity tables

### 3.1 `User`
`(Id, Username nvarchar(32), Email nvarchar(256), PasswordHash varbinary(64), PasswordSalt varbinary(16), PasswordIterations int, Role tinyint, PreferredLanguage varchar(2), TimeZoneId varchar(64), QuietHoursStart time null, QuietHoursEnd time null, MinNotifySeverity tinyint, ChannelsFcm bit, ChannelsTelegram bit, ChannelsEmail bit, TelegramChatId nvarchar(32) null, FcmToken nvarchar(256) null, CreatedAt, LastLoginAt null, DisabledAt null)`
Unique: `Username` (CI), `Email`.

### 3.2 `RefreshToken`
`(Id, UserId FK, TokenHash varbinary(32), FamilyId uniqueidentifier, IssuedAt, ExpiresAt, ConsumedAt null, RevokedAt null, DeviceInfo nvarchar(200) null)`
Index: `(UserId, FamilyId)`, unique `(TokenHash)`.

### 3.3 `SpeciesProfile`
`(Id, Name nvarchar(80), ScientificName nvarchar(120), ClimateZone tinyint, IsBuiltIn bit, PhotoperiodHours decimal(4,2), LightsOnLocalTime time, LightThresholdLux int, MinLightHoursPerDay decimal(4,2), Notes nvarchar(1000), CreatedByUserId null, RowVersion rowversion, CreatedAt, UpdatedAt)`
Unique: `(Name)` where `CreatedByUserId IS NULL` (built-ins unique); user profiles unique per `(CreatedByUserId, Name)`.

### 3.4 `Threshold` / `ThresholdOverride`
`Threshold`: `(Id, SpeciesProfileId FK, MetricId FK, Phase tinyint /*0 Any, 1 Day, 2 Night*/, TargetMin decimal(9,3), TargetMax, CriticalMin null, CriticalMax null, DwellWarnMinutes smallint, DwellCritMinutes smallint, RecoveryMargin decimal(9,3), SourceRef nvarchar(200), SourceUrl nvarchar(400) null, Enabled bit)`
Unique: `(SpeciesProfileId, MetricId, Phase)`.
Check constraints: `TargetMin < TargetMax`; `CriticalMin <= TargetMin AND CriticalMax >= TargetMax`.

`ThresholdOverride`: same columns with `TerrariumId` instead of `SpeciesProfileId`; unique `(TerrariumId, MetricId, Phase)`.

`ThresholdSnapshot`: `(Id, TerrariumId, CapturedAtUtc, EffectiveThresholdsJson nvarchar(max), ThresholdVersionHash char(64))`.

### 3.5 `Device`
`(Id, DeviceId varchar(24) unique, DeviceName nvarchar(60), ChipId varchar(32) unique, MacAddress varchar(17), FirmwareVersion varchar(16), Status tinyint /*0 Provisioning,1 Online,2 Offline,3 Revoked,4 Maintenance*/, Protocol tinyint, TerrariumId uniqueidentifier null FK, UserId null FK, ClaimCode varchar(8) null, ClaimCodeExpiresAt null, ProvisionedAt null, RevokedAt null, LastSeenAt null, SamplingIntervalSec smallint, PublishIntervalSec smallint, CalibrationJson nvarchar(512) null, SignalStrengthDbm smallint null, BatteryPct decimal(5,2) null, UptimeSeconds int null, FreeHeapKb int null)`
Indexes: unique filtered `(TerrariumId) WHERE TerrariumId IS NOT NULL AND Status <> 3` (**DI-04**: one active device per terrarium); `IX_Device_LastSeen` `(LastSeenAt)` for the silence watchdog.

### 3.6 `DeviceCredential`
`(Id, DeviceId FK, SecretHash varbinary(32), Salt varbinary(16), IssuedAt, ExpiresAt null, RevokedAt null, GraceUntil null, IssuedByUserId null)`
Index: `(DeviceId, RevokedAt)`.

### 3.7 `Metric` (seeded dictionary)
| Id | Code | DisplayName | Unit | Precision | PlausibleMin | PlausibleMax | IsCore |
|---|---|---|---|---|---|---|---|
| 1 | `TempC` | Air temperature | °C | 2 | −10 | 60 | 1 |
| 2 | `HumidityPct` | Relative humidity | %RH | 2 | 0 | 100 | 1 |
| 3 | `LightLux` | Illuminance | lx | 1 | 0 | 200 000 | 1 |
| 4 | `UvIndex` | UV index | UVI | 2 | 0 | 15 | 1 |
| 5 | `SurfaceTempC` | Surface temperature | °C | 2 | −10 | 80 | 0 |
| 6 | `BatteryPct` | Battery | % | 1 | 0 | 100 | 0 |
| 7 | `RssiDbm` | Wi-Fi signal | dBm | 0 | −120 | 0 | 0 |

### 3.8 `Alert`
| Column | Type | Notes |
|---|---|---|
| `Id` | bigint identity PK | |
| `TerrariumId`, `DeviceId` | FK | |
| `MetricId` | int FK null | null for `DeviceSilent`/`SensorFault` |
| `Severity` | tinyint | 1 Warning, 2 Critical |
| `Phase` | tinyint | evaluated phase |
| `DedupeKey` | varchar(80) | `{terrariumId}:{metric}:{severity}:{phase}` |
| `State` | tinyint | 0 Open, 1 Acknowledged, 2 Resolved |
| `TriggeringValue`, `PeakValue` | decimal(9,3) null | peak is the max (hot) or min (cold) during the episode |
| `BandMin`, `BandMax` | decimal(9,3) null | band in force (denormalised for the alert card) |
| `Source` | tinyint | 0 Threshold, 1 DeviceSilent, 2 SensorFault, 3 ClockSkew |
| `TriggeredAt`, `LastObservedAt`, `AcknowledgedAt`, `ResolvedAt` | datetime2(3) | |
| `AcknowledgedByUserId`, `ResolvedByUserId` | FK null | |
| `ResolvedReason` | tinyint null | 0 Recovered, 1 FalsePositive, 2 SensorFault, 3 Accepted |
| `ThresholdSnapshotId` | FK null | |
| `Message` | nvarchar(300) | structured fallback text; UI renders from fields |

Indexes: `IX_Alert_Terrarium_State_Triggered` `(TerrariumId, State, TriggeredAt DESC)`; **unique filtered** `UX_Alert_Open_Dedupe` `(DedupeKey) WHERE State <> 2` (**DI-01**); `IX_Alert_Triggered` `(TriggeredAt)` for summaries.

### 3.9 `EvaluationState`
`(TerrariumId, MetricId, Phase, Violation tinyint, FirstOutOfBandAt null, CriticalSinceAt null, ConsecutiveRecoveryTicks int, OpenAlertId bigint null, LastEvaluatedSampleId bigint, LastNotificationAt null, RowVersion rowversion)` — PK `(TerrariumId, MetricId, Phase)`.

### 3.10 `NotificationLog`
`(Id bigint, UserId FK null, AlertId bigint null, TerrariumId null, Channel tinyint /*0 InApp,1 Fcm,2 Telegram,3 Smtp*/, Title nvarchar(160), Body nvarchar(500), DeepLink nvarchar(200), Status tinyint /*0 Queued,1 Sent,2 Failed,3 Suppressed*/, SuppressedReason varchar(40) null, Attempts tinyint, LastError nvarchar(300) null, IsRead bit, CreatedAt, SentAt null, ReadAt null)`
Index: `(UserId, CreatedAt DESC)`, `(AlertId)`.

### 3.11 `AuditLog`
`(Id bigint, UserId null, DeviceId null, EntityName varchar(40), EntityId nvarchar(64), Action varchar(40), BeforeJson nvarchar(max) null, AfterJson nvarchar(max) null, IpAddress varchar(45) null, UserAgent nvarchar(200) null, CorrelationId varchar(64) null, OccurredAt datetime2(3))`
Index: `(EntityName, EntityId, OccurredAt DESC)`, `(OccurredAt)`.

### 3.12 `ExportJob` and `Snapshot`
`ExportJob` `(Id, UserId, TerrariumId, Format tinyint, RangeStartUtc, RangeEndUtc, MetricIdsJson, Status tinyint, RowCount int, FilePath nvarchar(400), DownloadToken varchar(64) unique, ExpiresAt, ErrorMessage nvarchar(300) null, CreatedAt, CompletedAt null)`.
`Snapshot` `(Id, DeviceId, TerrariumId, CapturedAtUtc, ContentType varchar(32), ByteSize int, Sha256 char(64), StoragePath nvarchar(400), ExpiresAt, Status tinyint)`.

---

## 4. DDL excerpts (the parts that carry invariants)

```sql
ALTER TABLE [TelemetrySample]
  ADD CONSTRAINT [FK_Sample_Terrarium] FOREIGN KEY ([TerrariumId]) REFERENCES [Terrarium]([Id]);
CREATE UNIQUE INDEX [IX_Sample_Device_Seq] ON [TelemetrySample]([DeviceId], [Sequence]);

CREATE INDEX [IX_Sample_Terrarium_Recorded]
  ON [TelemetrySample]([TerrariumId], [RecordedAt] DESC) INCLUDE ([Id]);

CREATE UNIQUE INDEX [IX_Reading_Sample_Metric] ON [MetricReading]([SampleId], [MetricId]);
ALTER TABLE [MetricReading] ADD CONSTRAINT [FK_Reading_Sample]
  FOREIGN KEY ([SampleId]) REFERENCES [TelemetrySample]([Id]) ON DELETE CASCADE;

CREATE UNIQUE INDEX [UX_Alert_Open_Dedupe] ON [Alert]([DedupeKey]) WHERE [State] <> 2;
CREATE UNIQUE INDEX [UX_Device_Terrarium_Active] ON [Device]([TerrariumId])
  WHERE [TerrariumId] IS NOT NULL AND [Status] <> 3;

ALTER TABLE [Threshold] ADD CONSTRAINT [CK_Threshold_TargetOrder]
  CHECK ([TargetMin] < [TargetMax]);
ALTER TABLE [Threshold] ADD CONSTRAINT [CK_Threshold_CriticalOrder]
  CHECK ([CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax]));
```

Why these five live in the database rather than in application code:
an alert could be opened by a worker while a user acknowledges another (race), and a claim endpoint could be
called twice concurrently. Application-level checks would pass a single-threaded test and fail in production;
the filtered unique indexes make the invariant impossible to violate. The API maps the resulting
`DbUpdateException` to `alert_duplicate` / `terrarium_already_bound` instead of returning a 500.

---

## 5. Report queries (copy-paste ready)

```sql
-- 1. Daily compliance for the last 7 days (report §R11)
SELECT s.LocalDate, s.CoveragePct, s.IsLowConfidence,
       s.TempMin, s.TempMax, s.TempExposureDegCHours,
       s.LightHours, s.AlertCount, s.CriticalAlertCount
FROM DailyEnvironmentalSummary s
JOIN Terrarium t ON t.Id = s.TerrariumId
WHERE t.Name = N'Linh''s gecko box'
  AND s.LocalDate >= DATEADD(day, -7, CAST(SYSUTCDATETIME() AS date))
ORDER BY s.LocalDate;

-- 2. The worst excursions (peak + duration) — used as a report figure
SELECT a.TriggeredAt, a.ResolvedAt,
       DATEDIFF(minute, a.TriggeredAt, a.ResolvedAt) AS DurationMinutes,
       m.Code AS Metric, a.Severity, a.TriggeredValue = a.TriggeringValue,
       a.PeakValue, a.BandMin, a.BandMax, a.ResolvedReason
FROM Alert a LEFT JOIN Metric m ON m.Id = a.MetricId
WHERE a.TerrariumId = @TerrariumId
ORDER BY a.PeakValue DESC, DurationMinutes DESC;

-- 3. Data coverage per hour for the soak report
SELECT DATEADD(hour, DATEDIFF(hour, 0, s.RecordedAt), 0) AS HourUtc, COUNT(*) AS Samples
FROM TelemetrySample s
WHERE s.TerrariumId = @TerrariumId AND s.RecordedAt >= @FromUtc
GROUP BY DATEADD(hour, DATEDIFF(hour, 0, s.RecordedAt), 0)
ORDER BY HourUtc;

-- 4. Storage measurement for NFR-11 (raw telemetry footprint)
SELECT OBJECT_NAME(p.object_id) AS ObjectName,
       SUM(p.rows) AS [Rows],
       SUM(a.total_pages) * 8 / 1024.0 AS TotalMB
FROM sys.partitions p
JOIN sys.allocation_units a ON a.container_id = p.partition_id
WHERE p.object_id IN (OBJECT_ID('TelemetrySample'), OBJECT_ID('MetricReading'))
GROUP BY p.object_id;

-- 5. Duplicate detector (must always return zero rows)
SELECT DeviceId, Sequence, COUNT(*) AS Copies
FROM TelemetrySample GROUP BY DeviceId, Sequence HAVING COUNT(*) > 1;
```

---

## 6. Storage estimate

| Table | Row size (approx.) | Rows/day (1 device, 60 s) | 90 days |
|---|---|---|---|
| `TelemetrySample` | ~120 B | 1 440 | ~15.5 MB |
| `MetricReading` | ~60 B × 4 metrics | 5 760 | ~31 MB |
| `DeviceHealthSample` | ~60 B | 288 | ~1.5 MB |
| Indexes | ~15% of above | — | ~7 MB |
| **Raw total** | | | **≈ 55 MB** |
| `TelemetryHourlyRollup` | ~90 B × 4 | 96 | negligible (< 1 MB) |
| `DailyEnvironmentalSummary` | ~1.5 KB | 1 | ~0.5 MB/year |
| `Alert` + `NotificationLog` | ~1 KB | 2–3 | negligible |

The raw total sits close to the NFR-11 budget of 60 MB/90 days, which is why `RetentionSweeperWorker` is
load-bearing. **The measured value from `TC-I-14`/`TC-U-49` replaces this estimate in the report** — the table
above is the design prediction, and any difference is reported rather than quietly reconciled.

## 7. Seed data

Seeded idempotently at start-up (`ReferenceDataSeeder`, keyed on `Code`/`Name`):

| Seeded | Rows | Notes |
|---|---|---|
| `Metric` | 7 | §3.7 |
| `SpeciesProfile` | 3 | Tropical, SemiArid, Arid — with photoperiod + light thresholds |
| `Threshold` | ~3–5 per profile | each row carries `SourceRef` (required for built-ins) |
| Demo account (Development only) | 1 | `demo` / documented password, flagged so it is never seeded in release |

The exact threshold numbers come from `07-appendices/05` and must pass its verification checklist before the
demo.
