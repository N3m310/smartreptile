# 02 — SQL Schema Reference

SQL Server 2022. All timestamps are `datetime2(3)` in **UTC** (ADR-015). Schema is created exclusively by EF
Core migrations (`03-implementation/03` §2). This appendix is the human-readable counterpart of the
`DbContext` configuration and the reference for writing report queries by hand.

Conventions: `Id` = `uniqueidentifier` (Guid) unless stated; `bigint identity` for high-volume tables;
`rowversion` for optimistic concurrency on authored configuration; soft delete (`Terrarium.Status`) only on
`Terrarium`.

---

## 1. Table summary

| # | Table | Purpose | Growth | Retention |
|---|---|---|---|---|
| 1 | `User` | Accounts and notification preferences; the role is a foreign key to `Role` | tiny | account lifetime |
| 2 | `RefreshToken` | Rotating refresh tokens (hashed) | small | 30 days |
| 3 | `Terrarium` | Monitored enclosure; `Status` (`Active`/`Deleted`) is the soft-delete state | tiny | soft delete |
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
| 23 | `PasswordResetCode` | Single-use password-reset codes (hashed) | tiny | 30-minute TTL; consumed, not swept |
| 24 | `DeviceEvent` | Raw device-reported events (boot, sensor fault, buffer overflow, clock, calibration) | low | with the raw-data window (90 days); the sweeper of 5.3 must include it |
| 25 | `TimeZone` | The IANA time-zone catalogue a picker reads and both stored zone ids are validated against (added 2026-10-14, TASKS item 11) | static | seeded; `ReferenceDataSeeder` owns it |
| 26 | `Role` | A named set of permissions; the four the product ships are system roles (added 2026-10-17, role/permission re-framing) | static | seeded / administrator-managed |
| 27 | `Permission` | The permission catalogue, one row per thing the software can be asked to do | static | seeded from `Permissions.All` |
| 28 | `RolePermission` | The grant matrix: this role grants this code | static | seeded, then administrator-managed |

Two columns are concurrency tokens rather than data: `Terrarium.RowVersion` (added 2026-10-09 for FR-03's
`If-Match` round trip) and `EvaluationState.RowVersion`. Both are SQL Server `rowversion` columns, so they are
maintained by the database and cannot be written by an `INSERT`/`UPDATE` statement that names them.

`TimeZone` is keyed by the IANA id itself (`Asia/Ho_Chi_Minh`) rather than by a surrogate, because that string is
what `Terrarium.TimeZoneId` and `User.TimeZoneId` already store (ADR-015) — a surrogate would add a translation step
to every read and let the two columns disagree. The `Status` column on `Terrarium` is the state the model's query
filter reads; `DeletedAt` remains beside it as the record of *when*, and the migration that added the column
back-filled it from that timestamp so no previously deleted enclosure came back to life (TASKS item 6).

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
`(Id, Username nvarchar(32), Email nvarchar(256), PasswordHash varbinary(64), PasswordSalt varbinary(16), PasswordIterations int, RecoveryCodeHash varbinary(32) null, RecoveryCodeSalt varbinary(16) null, RecoveryCodeIssuedAt null, RoleId uniqueidentifier FK, PreferredLanguage varchar(2), TimeZoneId varchar(64), QuietHoursStart time null, QuietHoursEnd time null, MinNotifySeverity tinyint, ChannelsFcm bit, ChannelsEmail bit, FcmToken nvarchar(256) null, CreatedAt, LastLoginAt null, DisabledAt null)`
Unique: `Username` (CI), `Email`. Index: `IX_User_RoleId`.

`RoleId` replaced the `Role` tinyint column on 2026-10-17 (migration `20261016161825_AddRolesAndPermissions`): the
role is a row in `Role` now, and the foreign key is `ON DELETE RESTRICT` so deleting a role cannot silently delete
the accounts holding it — the role API refuses such a delete, and this is the database saying the same thing. The
migration back-filled the column from the old enum (0 `Owner`, 1 `Technician`, 2 `Viewer`, 3 `Admin`, anything else
`Owner`), which is why no account changed hands during the upgrade.

The three `RecoveryCode*` columns are the backup recovery code issued at registration (`03-implementation/07`, the
password-recovery block). They are nullable on purpose: an account created before recovery codes existed has none,
and the API answers a null code exactly like a wrong one rather than growing a special case. `RecoveryCodeHash` is
`SHA-256(code ‖ RecoveryCodeSalt)` — salted, because this code is verified *after* the account is identified,
unlike the lookup-keyed `PasswordResetCode.CodeHash` in §3.13.

### 3.2 `RefreshToken`
`(Id, UserId FK, TokenHash varbinary(32), FamilyId uniqueidentifier, IssuedAt, ExpiresAt, ConsumedAt null, RevokedAt null, DeviceInfo nvarchar(200) null)`
Index: `(UserId, FamilyId)`, unique `(TokenHash)`.

### 3.3 `SpeciesProfile`
`(Id, Name nvarchar(80), ScientificName nvarchar(120), ClimateZone tinyint, IsBuiltIn bit, PhotoperiodHours decimal(4,2), LightsOnLocalTime time, LightThresholdLux int, MinLightHoursPerDay decimal(4,2), Notes nvarchar(1000), ImageStorageKey nvarchar(64) null, ImageContentType nvarchar(100) null, ImageUpdatedAt datetimeoffset null, CreatedByUserId null, RowVersion rowversion, CreatedAt, UpdatedAt)`
Unique: `(Name)` where `CreatedByUserId IS NULL` (built-ins unique); user profiles unique per `(CreatedByUserId, Name)`.

`ImageStorageKey` names a file in the store `Storage:RootPath` points at (migration `20261014010606_AddSpeciesProfileImage`,
2026-10-14) — the picture's bytes are deliberately not in this table, so no catalogue read pays for them.
`ImageContentType` is the media type sniffed from those bytes when they arrived; `ImageUpdatedAt` is when the
picture was last set, replaced or removed and is the cache-busting value the API publishes beside the image URL.

### 3.4 `Threshold` / `ThresholdOverride`
`Threshold`: `(Id, SpeciesProfileId FK, MetricId FK, Phase tinyint /*0 Any, 1 Day, 2 Night*/, TargetMin decimal(9,3), TargetMax, CriticalMin null, CriticalMax null, DwellWarnMinutes smallint, DwellCritMinutes smallint, RecoveryMargin decimal(9,3), SourceRef nvarchar(300), SourceUrl nvarchar(400) null, Enabled bit)`
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

**Which alert family each metric belongs to is a property of the dictionary, not of a row** (2026-10-17):
`MetricDefinition.Family` in `Metrics/MetricDictionary.cs` maps 1, 4 and 5 to `Species` — the animal's thermal and
UV bands — and 2, 3, 6 and 7 to `Enclosure`, the enclosure's own condition and the board measuring it. The dictionary
is the one place the split is written down; `Alert.Family` is filled from it when an episode opens, in the same way
`BandMin`/`BandMax` are denormalised, because a device-level alert has no metric to derive a family from. See
`02-design/05` §2 for the product rule.

### 3.8 `Alert`
| Column | Type | Notes |
|---|---|---|
| `Id` | bigint identity PK | |
| `TerrariumId`, `DeviceId` | FK | |
| `MetricId` | int FK null | null for `DeviceSilent`/`SensorFault` |
| `Family` | tinyint | 0 `Enclosure` (humidity, light, board), 1 `Species` (temperature, UV, basking surface) |
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

**Who writes what (as of 2026-10-09, roadmap 3.2).** The threshold engine writes the row and its first four moves —
`Open` (a new episode, `TriggeredAt` back-dated to the excursion's start), `Touch` (`LastObservedAt` and an outward
`PeakValue`), escalation (`Severity` and therefore `DedupeKey` become Critical on the same row) and `Resolve`
(`State`, `ResolvedAt`, `ResolvedReason = Recovered`). Acknowledgement, manual resolution and the other reasons are
the lifecycle API's (**built 2026-10-10, roadmap 3.4**: `State`, `AcknowledgedAt`, `AcknowledgedByUserId`,
`ResolvedAt`, `ResolvedByUserId` and `ResolvedReason`, each with one audit row and a re-armed dwell key — see
`02-design/02` §4.2), and `Device`-level sources are 3.3's. **`Message` is left null by design**: §02-design/05 §7
composes the notification text from these fields at render time and localises it per user (ADR-013), so an English
sentence here would be a second, unlocalisable copy of a message the clients already know how to build. The
resolution note a keeper sends travels on the audit entry for the same reason — this row has no column for prose, and
the reason it does keep is the machine-readable one the report groups by. `TriggeringValue` is the reading that
started the episode and `PeakValue` the episode's worst reading so far — both filled from the stored `MetricReading`
rows of the dwell window when the alert opens, since the excursion begins before the alert does.

**One row of the table above is a sketch rather than a column: `ThresholdSnapshotId`.** No built table has it and the
`Alert` entity maps no such property — the 2026-10-07 database snapshot lists no such column, and until 3.1's
remaining half creates the snapshot table there is nothing for a foreign key to point at. It is left in the sketch
deliberately, as the shape 3.1 will add, but nothing reads or writes it, and `GET /alerts/{id}` therefore carries the
band the alert denormalised and no snapshot reference (`07-appendices/03` §4.5 says so in the as-built note).


### 3.9 `EvaluationState`
`(TerrariumId, MetricId, Phase, Violation tinyint, FirstOutOfBandAt null, CriticalSinceAt null, ConsecutiveRecoveryTicks int, OpenAlertId bigint null, LastEvaluatedSampleId bigint, LastNotificationAt null, RowVersion rowversion)` — PK `(TerrariumId, MetricId, Phase)`.

**Built 2026-10-09** (roadmap 3.2's scaffolding) exactly as above, with every decision field present and unset: the
queue's consumer writes `LastEvaluatedSampleId` and nothing else, so the rows 3.2 inherits say "nothing decided
yet" rather than a plausible-looking default. The primary key is what makes a second evaluator writing the same key
a database error instead of two of them disagreeing about one excursion, and `RowVersion` is what stops two of them
advancing it at once. The metric is stored as the `MetricCode` value (TempC 1, HumidityPct 2, LightLux 3, …) and the
phase as `ThresholdPhase` (Any 0, Day 1, Night 2).

**Every field is written as of 2026-10-09 (roadmap 3.2)**, by `SampleEvaluator` and the `ThresholdDecision.Decide`
it calls — except `LastNotificationAt`, which stays unwritten until 3.5 has a dispatcher that reads it. Three
properties of this table are worth knowing before touching the engine:

- **`OpenAlertId` is a pointer, not the truth.** An open alert for the key is found by loading the terrarium's
  unresolved alerts, and the pointer is reconciled to what is actually open on each pass. That is what lets a
  human's resolve through the lifecycle API (3.4) and a crash between an alert's insert and this column's commit
  both recover without a second mechanism.
- **`Violation`, `FirstOutOfBandAt` and `CriticalSinceAt` are cleared when the reading comes back inside the target
  band**, whether or not an alert was opened — the dwell window then re-arms instead of accumulating across
  unrelated excursions. The recovery *margin* is a different rule and only gates closing an open alert; it is not a
  column here.
- **`Phase` is part of the key**, so a metric configured day *and* night has two independent excursions. A metric an
  override makes phase-agnostic has one, keyed `Any`.


### 3.10 `NotificationLog`
`(Id bigint identity, UserId uniqueidentifier null, AlertId bigint null, TerrariumId uniqueidentifier null, Channel tinyint /*0 InApp,1 Fcm,2 Smtp*/, Title nvarchar(160), Body nvarchar(500), DeepLink nvarchar(200) null, Status tinyint /*0 Queued,1 Sent,2 Failed,3 Suppressed*/, SuppressedReason nvarchar(40) null, Attempts tinyint, LastError nvarchar(300) null, IsRead bit, CreatedAt datetimeoffset(3), SentAt datetimeoffset(3) null, ReadAt datetimeoffset(3) null, Step tinyint)`
Index: `(UserId, CreatedAt DESC)`, `(AlertId)`, `(TerrariumId, CreatedAt)`.

Created by `20261016143949_AddNotificationLog` (roadmap 3.5, 2026-10-16). Four things to read off the built table
rather than the sketch:

- **The character columns are `nvarchar`** and the instants are `datetimeoffset(3)`, like every other table here
  (`varchar` is this catalogue's shorthand).
- **`Step` is a column the sketch did not have.** §02-design/05 §6 assembles "the full response timeline of one
  incident" out of these rows, and the alert row deliberately keeps no escalation instant (escalation edits the row
  in place, DI-01). Without the step, a 30-minute escalation and an hourly repeat are indistinguishable — both are
  "a second `Sent` row for the same alert" — so neither the timeline nor the ladder could be built out of them.
  `0` is the first delivery, `1`–`3` are the ladder's 30 / 60 / 120 minutes, and a digest carries `0`.
- **There is no foreign key**, to `Alert` or to anything else. The same rule `AuditLog` follows: the record of what a
  keeper was told must not disappear with the alert it was about, so `AlertId` is a plain column and a future
  retention job cannot cascade the delivery history away.
- **`Attempts` counts the row, it does not accumulate.** The table is one row *per attempt* (§1), so this is the
  attempt number this row records — `1`, `2` or `3` — and a suppressed row carries `0` because nothing was
  attempted. The retry schedule is therefore readable from the rows' own `CreatedAt` values rather than from a
  counter that says the retries happened somewhere.

`TerrariumId` is part of an index because both of the questions the dispatcher asks on every sweep are scoped by
terrarium inside a window: the hourly cap's count and the digest's grouping.

### 3.11 `AuditLog`
`(Id bigint identity, UserId uniqueidentifier null, DeviceId uniqueidentifier null, EntityName nvarchar(40), EntityId nvarchar(64), Action nvarchar(40), BeforeJson nvarchar(max) null, AfterJson nvarchar(max) null, IpAddress nvarchar(45), UserAgent nvarchar(200) null, CorrelationId nvarchar(64) null, OccurredAt datetimeoffset(3))`
PK `(Id)`. Index: `(EntityName, EntityId, OccurredAt DESC)`, `(OccurredAt)`.

Created by `20261006170132_AddAuditLog` (the audit-row follow-up to tasks 2.2 and 2.3, 2026-10-06). The table was
specified in `02-design/02` §3.18 from the start but `InitialSchema` never created it, which is why the device
lifecycle ran un-audited for three days. Three differences from the sketch that first appeared here, all read back
from `sys.columns`: the character columns are `nvarchar` like the rest of this schema (the catalogue uses
`varchar` as shorthand throughout); `IpAddress` is **NOT NULL**, because `AuditLog.ForDevice` substitutes the
literal `unknown` when the transport cannot name the caller; and the instant is `datetimeoffset(3)`, because every
other instant in this schema is one. `UserAgent` and `CorrelationId` are **truncated** to their widths rather than
rejected — both arrive as client-supplied headers of unbounded length, and a refused insert would turn a valid
claim into a `500`.

There are no foreign keys, deliberately: the row outlives the entity it names, and a cascade would let deleting a
user erase the record of what that user did.

### 3.12 `ExportJob` and `Snapshot`
`ExportJob` `(Id, UserId, TerrariumId, Format tinyint, RangeStartUtc, RangeEndUtc, MetricIdsJson, Status tinyint, RowCount int, FilePath nvarchar(400), DownloadToken varchar(64) unique, ExpiresAt, ErrorMessage nvarchar(300) null, CreatedAt, CompletedAt null)`.
`Snapshot` `(Id, DeviceId, TerrariumId, CapturedAtUtc, ContentType varchar(32), ByteSize int, Sha256 char(64), StoragePath nvarchar(400), ExpiresAt, Status tinyint)`.

### 3.13 `PasswordResetCode`
`(Id, UserId FK, CodeHash varbinary(32), CreatedAt, ExpiresAt, ConsumedAt null, RequestedFromAddress nvarchar(45) null)`
Unique: `CodeHash`. Index: `(UserId, ExpiresAt)`. Cascade-deleted with the account.

`CodeHash` is the **unsalted** SHA-256 of the presented code, so the row can be found *by* the value presented —
the same reasoning as `RefreshToken.TokenHash`, and deliberately unlike `User.RecoveryCodeHash`, which is salted
because that code is only ever verified *after* the account has already been identified. At most one row per
account is live: issuing a code stamps `ConsumedAt` on any outstanding one, and spending a code stamps it on the
row it used, so a second request replaces the first rather than leaving two ways in.

### 3.14 `DeviceEvent`
| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint identity PK | no | |
| `DeviceId` | uniqueidentifier FK → `Device` (cascade) | no | |
| `TerrariumId` | uniqueidentifier FK → `Terrarium` (set null) | **yes** | copied for history integrity; nullable so unbinding never rewrites or deletes history |
| `Type` | tinyint | no | 0 boot · 1 sensorFault · 2 sensorRecovered · 3 bufferOverflow · 4 clockUnsynced · 5 calibrated |
| `Metric` | tinyint | yes | set for a sensor fault and its recovery |
| `DetailJson` | nvarchar(max) | yes | the event's own fields, verbatim |
| `RecordedAt` | datetimeoffset(3) | no | the device's instant when it supplied one, else arrival |
| `ReceivedAt` | datetimeoffset(3) | no | server-authoritative (NFR-10) |

**Index** `IX_DeviceEvent_DeviceId_RecordedAt` `(DeviceId, RecordedAt DESC)` — "what happened to this board, in
order", which is the query task 3.3's derived signals ask.

**Built 2026-10-09.** The `events` channel is consumed and stored, because the alternative was worse than a gap: a
topic the broker accepts and then drops looks like it worked. The event is kept whole rather than shredded into a
column per type, so a signal rule added in 3.3 can be re-evaluated against history instead of against whatever the
rule was when the message arrived.

### 3.15 `MetricSilence`
| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | uniqueidentifier PK | no | not an identity column: the key is known before the insert, so the audit row that records who asked for the window can name it in the same unit of work |
| `TerrariumId` | uniqueidentifier FK → `Terrarium` (cascade) | no | |
| `Metric` | tinyint | **yes** | null means every metric of the terrarium, device-level alerts included |
| `UntilUtc` | datetimeoffset(3) | no | at most 24 h after `CreatedAt` — a rule, not a constraint (`MetricSilencePolicy`) |
| `Reason` | nvarchar(200) | no | required; the sentence that makes a suppression accountable |
| `CreatedByUserId` | uniqueidentifier | no | who silenced it (no FK: the trail outlives the account) |
| `CreatedAt` | datetimeoffset(3) | no | |
| `CancelledAt` | datetimeoffset(3) | yes | non-null means cancelled early; the row is never deleted |

**Index** `IX_MetricSilence_TerrariumId_UntilUtc` `(TerrariumId, UntilUtc)` — "what is suppressing this terrarium
right now", which the dashboard renders and the dispatcher asks before every notification.

**Built 2026-10-10** by `20261010045253_AddMetricSilences` (roadmap 3.4). The catalogue has listed this table since
the first draft; until now nothing wrote it, so a keeper had no way to tell the system that they already knew about
a condition. Two of the three as-built differences from the sketch are recorded in `02-design/02` §3.15; the third is
that `Metric` is nullable in the built schema although the sketch shows a plain `MetricId` — the route's
`metricId?` is what the reading "every metric" comes from.

---

### 3.16 `Role`, `Permission`, `RolePermission` (the permission matrix)
Added 2026-10-17 by migration `20261016161825_AddRolesAndPermissions`; `02-design/06` §3 is the design.

`Role`: `(Id uniqueidentifier PK, Code nvarchar(40), Name nvarchar(80), Description nvarchar(300), IsSystem bit, Rank int, RowVersion rowversion, CreatedAt, UpdatedAt)`
Unique: `IX_Role_Code` on `Code` (case-insensitive by the database's collation, so `Admin` and `admin` cannot be two
roles).

`Permission`: `(Code nvarchar(60) PK, Group nvarchar(40), Name nvarchar(80), Description nvarchar(300), CrossesAccounts bit, SortOrder int)`

`RolePermission`: `(RoleId uniqueidentifier FK, PermissionCode nvarchar(60) FK)` — the pair *is* the primary key,
because a grant either exists or does not: removing one is deleting its row and the audit entry records who did.
Index: `IX_RolePermission_PermissionCode`, which is the index the last-administrator guard seeks (it counts holders
of `user.manage`).

Three design points are carried by these definitions rather than by code:

- **The ids of the four system roles are literals** (`SystemRoles`: `a0e00000-…-{1,2,3,4}`). Registration assigns
  `Owner` and the start-up seeder creates the administrator, and both would otherwise need a lookup whose answer can
  be absent — an unseeded database, or a deployment with `Startup:SeedReferenceDataOnStartup=false`.
- **`Role.RowVersion`** makes two administrators editing one role's permission set a conflict instead of a silent
  overwrite (the terrarium's own rule, UC-03 A3).
- **`Permission` is written by the seeder and never by a user.** A code the build does not implement cannot be
  granted by inserting a row, which is why the catalogue stays in `Permissions.cs` and only the *mapping* is data.

### 3.17 `Alert.Family`
`Alert` gained `Family int NOT NULL DEFAULT 0` in migration `20261016162814_AddAlertFamily`, back-filled from the
metric the row is about (1, 4 and 5 → `Species`; everything else, including every metric-less alert → `Enclosure`).
Denormalised on purpose, like `BandMin`/`BandMax`: it is a fact about the episode, it is what the alert list filters
and groups by, and a device-level alert has no metric to derive it from.

### 3.18 Seed data for the matrix
The migration inserts it, so a fresh database is complete before the application has run once; `IdentitySeeder`
then reconciles on every start-up and writes nothing unless something is missing. Sixteen `Permission` rows, four
`Role` rows and thirty-nine `RolePermission` rows:

| Role | Rank | Grants |
|---|---|---|
| `Admin` | 0 | all 16 |
| `Owner` | 1 | 11 |
| `Technician` | 2 | 7 |
| `Viewer` | 3 | 5 |

The seeder's asymmetry is the part worth remembering: a missing role is created **with** its shipped grants, and an
existing grant is never re-applied — an administrator who removed a grant has made a decision.

---

## 4. DDL excerpts (the parts that carry invariants)

> **Transcribed from the applied migration** (`20260921091523_InitialSchema`), not from the sketch that first
> appeared here. The sketch named these `FK_Sample_Terrarium`, `IX_Sample_Device_Seq`, `UX_Alert_Open_Dedupe` and
> `UX_Device_Terrarium_Active`; the migration creates none of those names. EF Core derives index and key names by
> convention (`IX_<Table>_<Columns>`, `FK_<Child>_<Parent>_<Column>`) and appends the filter configured in
> `OnModelCreating`, so a name in this appendix has to be copied from the database, never invented. The
> integration tests assert these names, so a rename here without a migration change fails the build.
>
> Read them back with:
> `SELECT i.name, i.filter_definition FROM sys.indexes i WHERE i.has_filter = 1;`

```sql
ALTER TABLE [TelemetrySample] ADD CONSTRAINT [FK_TelemetrySample_Terrarium_TerrariumId]
  FOREIGN KEY ([TerrariumId]) REFERENCES [Terrarium]([Id]) ON DELETE CASCADE;
CREATE UNIQUE INDEX [IX_TelemetrySample_DeviceId_Sequence] ON [TelemetrySample]([DeviceId], [Sequence]);

CREATE INDEX [IX_TelemetrySample_TerrariumId_RecordedAt]
  ON [TelemetrySample]([TerrariumId], [RecordedAt] DESC);

CREATE UNIQUE INDEX [IX_MetricReading_SampleId_Metric] ON [MetricReading]([SampleId], [Metric]);
ALTER TABLE [MetricReading] ADD CONSTRAINT [FK_MetricReading_TelemetrySample_SampleId]
  FOREIGN KEY ([SampleId]) REFERENCES [TelemetrySample]([Id]) ON DELETE CASCADE;

CREATE UNIQUE INDEX [IX_Alert_DedupeKey] ON [Alert]([DedupeKey]) WHERE [State] <> 2;
CREATE UNIQUE INDEX [IX_Device_TerrariumId] ON [Device]([TerrariumId])
  WHERE [TerrariumId] IS NOT NULL AND [Status] <> 3;

ALTER TABLE [Threshold] ADD CONSTRAINT [CK_Threshold_TargetOrder]
  CHECK ([TargetMin] < [TargetMax]);
ALTER TABLE [Threshold] ADD CONSTRAINT [CK_Threshold_CriticalOrder]
  CHECK ([CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax]));
```

From `20261006152641_AddPasswordResetCodes` (account recovery, BR-01.5) — the unique index is what makes "find the
row by the code presented" a seek rather than a scan of every outstanding code, and the pair index is the only
shape the store queries by:

```sql
CREATE UNIQUE INDEX [IX_PasswordResetCode_CodeHash] ON [PasswordResetCode]([CodeHash]);
CREATE INDEX [IX_PasswordResetCode_UserId_ExpiresAt] ON [PasswordResetCode]([UserId], [ExpiresAt]);
ALTER TABLE [PasswordResetCode] ADD CONSTRAINT [FK_PasswordResetCode_User_UserId]
  FOREIGN KEY ([UserId]) REFERENCES [User]([Id]) ON DELETE CASCADE;
```

From `20261009140128_AddTerrariumRowVersionAndDeviceEvaluationTables` (FR-03's update half, plus the tables 3.2 and
3.3 read). The `rowversion` columns are the database's own tokens: an update names the value it read and matches
zero rows if someone else moved it, which is what makes `If-Match` a real precondition rather than a check the API
performs and hopes holds:

```sql
ALTER TABLE [Terrarium] ADD [RowVersion] rowversion NULL;

CREATE TABLE [DeviceEvent] (
  [Id] bigint NOT NULL IDENTITY, [DeviceId] uniqueidentifier NOT NULL, [TerrariumId] uniqueidentifier NULL,
  [Type] int NOT NULL, [Metric] int NULL, [DetailJson] nvarchar(max) NULL,
  [RecordedAt] datetimeoffset(3) NOT NULL, [ReceivedAt] datetimeoffset(3) NOT NULL,
  CONSTRAINT [PK_DeviceEvent] PRIMARY KEY ([Id]),
  CONSTRAINT [FK_DeviceEvent_Device_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [Device]([Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_DeviceEvent_Terrarium_TerrariumId] FOREIGN KEY ([TerrariumId]) REFERENCES [Terrarium]([Id]) ON DELETE SET NULL);
CREATE INDEX [IX_DeviceEvent_DeviceId_RecordedAt] ON [DeviceEvent]([DeviceId], [RecordedAt] DESC);

CREATE TABLE [EvaluationState] (
  [TerrariumId] uniqueidentifier NOT NULL, [Metric] int NOT NULL, [Phase] int NOT NULL,
  [Violation] int NOT NULL, [FirstOutOfBandAt] datetimeoffset(3) NULL, [CriticalSinceAt] datetimeoffset(3) NULL,
  [ConsecutiveRecoveryTicks] int NOT NULL, [OpenAlertId] bigint NULL, [LastEvaluatedSampleId] bigint NOT NULL,
  [LastNotificationAt] datetimeoffset(3) NULL, [RowVersion] rowversion NULL,
  CONSTRAINT [PK_EvaluationState] PRIMARY KEY ([TerrariumId], [Metric], [Phase]),
  CONSTRAINT [FK_EvaluationState_Terrarium_TerrariumId] FOREIGN KEY ([TerrariumId]) REFERENCES [Terrarium]([Id]) ON DELETE CASCADE);
```

From `20261016161825_AddRolesAndPermissions` and `20261016162814_AddAlertFamily` (the role/permission and
alert-family re-framing, 2026-10-17). The names are the ones EF derived; read them back to be sure with
`SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('User');`.

```sql
CREATE TABLE [Role] (
  [Id] uniqueidentifier NOT NULL, [Code] nvarchar(40) NOT NULL, [Name] nvarchar(80) NOT NULL,
  [Description] nvarchar(300) NOT NULL, [IsSystem] bit NOT NULL, [Rank] int NOT NULL,
  [RowVersion] rowversion NULL, [CreatedAt] datetimeoffset NOT NULL, [UpdatedAt] datetimeoffset NOT NULL,
  CONSTRAINT [PK_Role] PRIMARY KEY ([Id]));
CREATE UNIQUE INDEX [IX_Role_Code] ON [Role]([Code]);

CREATE TABLE [Permission] (
  [Code] nvarchar(60) NOT NULL, [Group] nvarchar(40) NOT NULL, [Name] nvarchar(80) NOT NULL,
  [Description] nvarchar(300) NOT NULL, [CrossesAccounts] bit NOT NULL, [SortOrder] int NOT NULL,
  CONSTRAINT [PK_Permission] PRIMARY KEY ([Code]));

CREATE TABLE [RolePermission] (
  [RoleId] uniqueidentifier NOT NULL, [PermissionCode] nvarchar(60) NOT NULL,
  CONSTRAINT [PK_RolePermission] PRIMARY KEY ([RoleId], [PermissionCode]),
  CONSTRAINT [FK_RolePermission_Role_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Role]([Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_RolePermission_Permission_PermissionCode]
    FOREIGN KEY ([PermissionCode]) REFERENCES [Permission]([Code]) ON DELETE CASCADE);
CREATE INDEX [IX_RolePermission_PermissionCode] ON [RolePermission]([PermissionCode]);

ALTER TABLE [User] ADD [RoleId] uniqueidentifier NULL;   -- added nullable, back-filled, then tightened
ALTER TABLE [User] ALTER COLUMN [RoleId] uniqueidentifier NOT NULL;
CREATE INDEX [IX_User_RoleId] ON [User]([RoleId]);
ALTER TABLE [User] ADD CONSTRAINT [FK_User_Role_RoleId]
  FOREIGN KEY ([RoleId]) REFERENCES [Role]([Id]) ON DELETE NO ACTION;   -- RESTRICT: never delete accounts with a role
ALTER TABLE [User] DROP COLUMN [Role];

ALTER TABLE [Alert] ADD [Family] int NOT NULL DEFAULT 0;
```

The `RoleId` column is added nullable, back-filled from the old enum, then tightened — a `NOT NULL` column with a
default would have pointed every existing account at the same role, which is the kind of upgrade that looks like it
worked. The same migration inserts the sixteen permissions, the four roles and their thirty-nine grants (§3.18), and
`Alert.Family` is back-filled from the metric by a `CASE` over the raw codes.

Why these live in the database rather than in application code: an alert could be opened by a worker while a user acknowledges another (race), and a claim endpoint could be
called twice concurrently. Application-level checks would pass a single-threaded test and fail in production;
the filtered unique indexes make the invariant impossible to violate. The API maps the resulting
`DbUpdateException` to `alert_duplicate` / `terrarium_already_bound` instead of returning a 500 — done for
`terrarium_already_bound` on 2026-10-09 (`IProvisioningStore.TrySaveClaimAsync`), and `alert_duplicate` arrives with
3.4. The `PK_EvaluationState` violation is asserted against real SQL Server in `DatabaseInvariantTests`, so "two
evaluators cannot own one key" is a checked claim rather than a comment.

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

Seeded idempotently at start-up (`ReferenceDataSeeder`, keyed on `Code`/`Name`), except the matrix rows of §3.18,
which the migration inserts and `IdentitySeeder` reconciles:

| Seeded | Rows | Notes |
|---|---|---|
| `Permission` | 16 | §3.18; written by the migration, reconciled by `IdentitySeeder` on start-up |
| `Role` | 4 | §3.18; ids fixed in `SystemRoles` |
| `RolePermission` | 39 | §3.18; written only when the role row is created, so an administrator's edits survive a restart |
| `Metric` | 7 | §3.7 |
| `SpeciesProfile` | 3 | Tropical, SemiArid, Arid — with photoperiod + light thresholds |
| `Threshold` | ~3–5 per profile | each row carries `SourceRef` (required for built-ins) |
| Demo account (Development only) | 1 | `demo` / documented password, flagged so it is never seeded in release |

The exact threshold numbers come from `07-appendices/05` and must pass its verification checklist before the
demo.
