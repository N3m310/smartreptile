# Database contents snapshot - SmartReptile

**Taken 2026-10-07 02:52:59 UTC** from the local SQL Server 2022 container
(`127.0.0.1,14330`) through `scripts/dump-database.ps1`. It is a **point-in-time snapshot of a
development database**, not the schema's source of truth - that is `07-appendices/02` - and the
database moves as soon as anything writes to it.

**Secrets are not printed.** Every column matching `hash`, `salt`, `rowversion` or `secret` shows its
length instead of its value (secrets are never recorded, only that they were written). Binary columns
that are not secrets show their byte count, and `NULL` is the SQL `NULL`, which is different from an
empty string.

## Tables and row counts

| Table | Rows |
|---|---|
| `__EFMigrationsHistory` | 5 |
| `Alert` | 0 |
| `AuditLog` | 1 |
| `Device` | 20 |
| `DeviceCredential` | 21 |
| `DeviceHealthSample` | 8 |
| `MetricReading` | 60 |
| `PasswordResetCode` | 6 |
| `RefreshToken` | 48 |
| `SpeciesProfile` | 4 |
| `TelemetrySample` | 15 |
| `Terrarium` | 20 |
| `Threshold` | 19 |
| `ThresholdOverride` | 0 |
| `User` | 25 |
| **total** | **252** |

## Reading the integer columns

The values are the C# enums in `backend/src/SmartReptile.Domain/` - the source of truth - repeated here so
the tables below can be read without opening the code:

| Column | Enum (file) | Values |
|---|---|---|
| `Metric` | `MetricCode` (`Metrics/MetricDictionary.cs`) | 1 TempC, 2 HumidityPct, 3 LightLux, 4 UvIndex, 5 SurfaceTempC, 6 BatteryPct, 7 RssiDbm |
| `Phase` | `ThresholdPhase` (`Thresholds/ThresholdEnums.cs`) | 0 Any, 1 Day, 2 Night |
| `ClimateZone` | `ClimateZone` (`Species/SpeciesProfile.cs`) | 0 Tropical, 1 SemiArid, 2 Arid, 3 Temperate |
| `Role` | `UserRole` (`Identity/User.cs`) | 0 Owner, 1 Technician, 2 Viewer |
| `Severity`, `MinNotifySeverity` | `AlertSeverity` (`Alerts/Alert.cs`) | 0 Info, 1 Warning, 2 Critical |
| `State` | `AlertState` (`Alerts/Alert.cs`) | 0 Open, 1 Acknowledged, 2 Resolved |
| `Status` (`Device`) | `DeviceStatus` (`Devices/Device.cs`) | 0 Provisioning, 1 Online, 2 Offline, 3 Revoked, 4 Maintenance |
| `Protocol` | `DeviceProtocol` (`Devices/Device.cs`) | 0 Mqtt, 1 HttpFallback |
| `Source` (`TelemetrySample`) | `IngestSource` (`Readings/TelemetrySample.cs`) | 0 Mqtt, 1 HttpFallback, 2 Seed |
| `QualityFlags` | bitmask `QualityFlags` (`Readings/QualityFlags.cs`) | 1 SensorFault, 2 Implausible, 4 FirstAfterBoot, 8 Backfilled, 16 ClockUnsynced, 32 CalibrationApplied |

`0` is a clean sample. Only `1` (sensor fault) and `2` (implausible) exclude a reading from evaluation
(`QualityRules.NotEvaluable`), so a flag of `16` means *the device clock was not NTP-synced*: those samples
are still evaluated, which is why the API answers with a status for them instead of `Unavailable`. Note that
`status` is not stored anywhere - it is computed at read time from the effective band, per ADR-005.

## `__EFMigrationsHistory` - 5 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `MigrationId` | `nvarchar(150)` | no | PK |
| `ProductVersion` | `nvarchar(32)` | no |  |

| MigrationId | ProductVersion |
|---|---|
| 20260921091523_InitialSchema | 10.0.2 |
| 20261006151648_AddRecoveryCodes | 10.0.2 |
| 20261006152641_AddPasswordResetCodes | 10.0.2 |
| 20261006170132_AddAuditLog | 10.0.2 |
| 20261007023744_DropTelegramChannel | 10.0.2 |

## `Alert` - 0 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `bigint` | no | PK, identity |
| `TerrariumId` | `uniqueidentifier` | no |  |
| `DeviceId` | `uniqueidentifier` | no |  |
| `Metric` | `int` | yes |  |
| `Severity` | `int` | no |  |
| `Phase` | `int` | no |  |
| `DedupeKey` | `nvarchar(80)` | no |  |
| `State` | `int` | no |  |
| `Source` | `int` | no |  |
| `TriggeringValue` | `decimal` | yes |  |
| `PeakValue` | `decimal` | yes |  |
| `BandMin` | `decimal` | yes |  |
| `BandMax` | `decimal` | yes |  |
| `TriggeredAt` | `datetimeoffset` | no |  |
| `LastObservedAt` | `datetimeoffset` | yes |  |
| `AcknowledgedAt` | `datetimeoffset` | yes |  |
| `ResolvedAt` | `datetimeoffset` | yes |  |
| `AcknowledgedByUserId` | `uniqueidentifier` | yes |  |
| `ResolvedByUserId` | `uniqueidentifier` | yes |  |
| `ResolvedReason` | `int` | yes |  |
| `Message` | `nvarchar(300)` | yes |  |

_No rows._

## `AuditLog` - 1 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `bigint` | no | PK, identity |
| `UserId` | `uniqueidentifier` | yes |  |
| `DeviceId` | `uniqueidentifier` | yes |  |
| `EntityName` | `nvarchar(40)` | no |  |
| `EntityId` | `nvarchar(64)` | no |  |
| `Action` | `nvarchar(40)` | no |  |
| `BeforeJson` | `nvarchar(max)` | yes |  |
| `AfterJson` | `nvarchar(max)` | yes |  |
| `IpAddress` | `nvarchar(45)` | no |  |
| `UserAgent` | `nvarchar(200)` | yes |  |
| `CorrelationId` | `nvarchar(64)` | yes |  |
| `OccurredAt` | `datetimeoffset` | no |  |

| Id | UserId | DeviceId | EntityName | EntityId | Action | BeforeJson | AfterJson | IpAddress | UserAgent | CorrelationId | OccurredAt |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | f9b4280c-b32c-437c-aab4-022b4484eb05 | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | Device | sr-j8m5h2 | device.claimed | `NULL` | {"terrariumId":"7806c641-6cf8-401e-81ed-4363af97d8fe","userId":"f9b4280c-b32c-437c-aab4-022b4484eb05"} | ::1 | curl/8.21.0 | 0HNP425PARA6H:00000001 | 2026-10-07 01:59:45Z |

## `Device` - 20 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `PublicId` | `nvarchar(24)` | no |  |
| `DeviceName` | `nvarchar(60)` | no |  |
| `ChipId` | `nvarchar(32)` | no |  |
| `MacAddress` | `nvarchar(17)` | no |  |
| `FirmwareVersion` | `nvarchar(16)` | no |  |
| `Status` | `int` | no |  |
| `Protocol` | `int` | no |  |
| `TerrariumId` | `uniqueidentifier` | yes |  |
| `UserId` | `uniqueidentifier` | yes |  |
| `ClaimCode` | `nvarchar(8)` | yes |  |
| `ClaimCodeExpiresAt` | `datetimeoffset` | yes |  |
| `ProvisionedAt` | `datetimeoffset` | yes |  |
| `RevokedAt` | `datetimeoffset` | yes |  |
| `LastSeenAt` | `datetimeoffset` | yes |  |
| `SamplingIntervalSec` | `int` | no |  |
| `PublishIntervalSec` | `int` | no |  |
| `CalibrationJson` | `nvarchar(512)` | yes |  |
| `SignalStrengthDbm` | `int` | yes |  |
| `BatteryPct` | `decimal` | yes |  |
| `UptimeSeconds` | `bigint` | yes |  |
| `FreeHeapKb` | `int` | yes |  |
| `CreatedAt` | `datetimeoffset` | no |  |

| Id | PublicId | DeviceName | ChipId | MacAddress | FirmwareVersion | Status | Protocol | TerrariumId | UserId | ClaimCode | ClaimCodeExpiresAt | ProvisionedAt | RevokedAt | LastSeenAt | SamplingIntervalSec | PublishIntervalSec | CalibrationJson | SignalStrengthDbm | BatteryPct | UptimeSeconds | FreeHeapKb | CreatedAt |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| a5724855-29d3-420e-acbd-14896cbada3d | sr-xqef2x | New node | AA1101758811 | AA:11:BB:22:CC:99 | 1.0.0 | 3 | 0 | 378bdd72-a76c-4120-b341-109174f04ff7 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | `NULL` | `NULL` | 2026-10-06 16:58:52Z | 2026-10-06 16:58:59Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-06 16:58:46Z |
| 93a7030c-db1e-4536-90fb-29e9f565ff16 | sr-kcvguk | New node | AA23BB211807 | AA:23:BB:00:00:01 | 1.0.0 | 3 | 0 | 269184b6-2f7e-478d-8d4e-b63138b8fb2f | 5e44c1a7-d921-422a-946c-8842e935974c | `NULL` | `NULL` | 2026-10-03 14:18:12Z | 2026-10-03 14:18:23Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:18:11Z |
| 8dd261a7-b73c-49f6-98bf-2bef7c232ef5 | sr-z3gz3p | New node | AA11BB22CC205745 | AA:11:BB:22:CC:01 | 1.0.0 | 3 | 0 | cfbf0e10-02ec-4358-a38a-8529f9be79f1 | ff4b1624-398c-4ced-887c-4c27e526958c | `NULL` | `NULL` | 2026-10-03 13:57:50Z | 2026-10-03 13:57:51Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 13:57:49Z |
| 90c7641d-bf54-4660-af5c-33e506d2a01f | sr-9g34cu | New node | BB23BB211652 | AA:23:BB:00:00:02 | 1.0.0 | 0 | 0 | 905c36b8-b32b-400e-afa0-01f19878e3f0 | be28d100-34a3-4e89-a81b-fd5c213010f1 | `NULL` | `NULL` | 2026-10-03 14:16:58Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:16:57Z |
| b343b411-8e49-4861-b68d-3eb58a08d2a1 | sr-nakk48 | New node | AA11BB22CC33 | AA:11:BB:22:CC:33 | 1.0.0 | 1 | 0 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 96e47474-0b58-46bc-acc7-440571c3d983 | `NULL` | `NULL` | 2026-10-06 16:45:49Z | `NULL` | 2026-10-06 16:49:07Z | 60 | 60 | `NULL` | -61 | `NULL` | 90000 | 140 | 2026-10-06 16:45:37Z |
| 61f4ac4a-6c50-4148-981e-56ce570e1310 | sr-u8nraq | New node | BB23BB211615 | AA:23:BB:00:00:02 | 1.0.0 | 0 | 0 | 074de6fb-befb-43af-a1ac-2aa0a9b6108b | 1859aa56-74b3-458f-9d6b-c8991ff7b170 | `NULL` | `NULL` | 2026-10-03 14:16:21Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:16:20Z |
| 6ed820af-35b2-41f7-89e3-584f78b4e714 | sr-4x4s8c | New node | AA11BB22CC205516 | AA:11:BB:22:CC:01 | 1.0.0 | 0 | 0 | `NULL` | `NULL` | YAAAHKWW | 2026-10-03 14:10:20Z | `NULL` | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 13:55:20Z |
| 0b502e9d-3b25-41cb-b3bb-671a90a94cac | sr-avuxxb | New node | EE11BB22CC205904 | AA:11:BB:22:CC:05 | 1.0.0 | 0 | 0 | 5d5ab9c6-7424-4c9f-8032-ca2b1f2a5ffc | 126554f9-e194-4afd-8bd0-c156dbcb2bb2 | `NULL` | `NULL` | 2026-10-03 13:59:10Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 13:59:09Z |
| e84cb1b8-33fd-4f28-8a26-677a241bb2fb | sr-j8m5h2 | New node | A0B1C2D3E4F5 | A0:B1:C2:D3:E4:F5 | 1.0.0 | 1 | 0 | 7806c641-6cf8-401e-81ed-4363af97d8fe | f9b4280c-b32c-437c-aab4-022b4484eb05 | `NULL` | `NULL` | 2026-10-07 01:59:45Z | `NULL` | 2026-10-07 02:01:26Z | 60 | 60 | `NULL` | -58 | 87.00 | 4500 | 140 | 2026-10-07 01:59:45Z |
| bf71df86-3f9a-412d-8cc4-72bd0cbdbb57 | sr-e2e01 | E2E node | e2echip000001 | AA:BB:CC:DD:EE:01 | 0.1.0-test | 1 | 0 | 6ec11ae9-e639-4ac4-932c-0e6438246d09 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-06 14:57:57Z | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-06 14:57:57Z |
| 071f64d4-c68c-4daf-aa0d-796981200376 | sr-djkw4n | New node | BB23BB211807 | AA:23:BB:00:00:02 | 1.0.0 | 0 | 0 | f27ba2ef-3f41-43da-8d4d-f3b1e9d8e134 | 5e44c1a7-d921-422a-946c-8842e935974c | `NULL` | `NULL` | 2026-10-03 14:18:12Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:18:12Z |
| aaba76d9-1cf1-45be-ad43-9510a104ef65 | sr-r445u5 | New node | AA23BB212255 | AA:23:BB:00:00:01 | 1.0.0 | 3 | 0 | c8c82578-67e1-41c2-aa24-bc082a27456b | 2224b23e-8473-4fc0-a509-df8fffab8d2c | `NULL` | `NULL` | 2026-10-03 14:22:59Z | 2026-10-03 14:23:10Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:22:59Z |
| 119e6989-4c10-48a6-a88d-96ecdf9bf627 | sr-hm3qbh | New node | EE11BB22CC205745 | AA:11:BB:22:CC:05 | 1.0.0 | 0 | 0 | cfbf0e10-02ec-4358-a38a-8529f9be79f1 | ff4b1624-398c-4ced-887c-4c27e526958c | `NULL` | `NULL` | 2026-10-03 13:57:51Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 13:57:50Z |
| 52198dd7-f1ae-4dfa-9f90-9e3cde0e1e77 | sr-fp9esu | New node | AA23BB211526 | AA:23:BB:00:00:01 | 1.0.0 | 0 | 0 | b8fa7848-d020-4b5d-b102-122083198d6d | e5226158-c6ac-4763-9843-64cc924fa291 | `NULL` | `NULL` | 2026-10-03 14:15:31Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:15:30Z |
| 2e1dd9e3-3559-406e-a67a-a0b5625d09f5 | sr-3mdauz | New node | AA23BB211615 | AA:23:BB:00:00:01 | 1.0.0 | 0 | 0 | 7aa98156-41c1-4f02-aab3-d83dfc03a4be | 1859aa56-74b3-458f-9d6b-c8991ff7b170 | `NULL` | `NULL` | 2026-10-03 14:16:20Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:16:20Z |
| ecb5b54c-2745-4709-93ec-b3812bdeaf3c | sr-88k79v | New node | AA11BB22CC205904 | AA:11:BB:22:CC:01 | 1.0.0 | 3 | 0 | 5d5ab9c6-7424-4c9f-8032-ca2b1f2a5ffc | 126554f9-e194-4afd-8bd0-c156dbcb2bb2 | `NULL` | `NULL` | 2026-10-03 13:59:09Z | 2026-10-03 13:59:10Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 13:59:08Z |
| 2226d3bd-64e1-4b1e-badf-cc7af3a6a20a | sr-2ju643 | New node | BB23BB212255 | AA:23:BB:00:00:02 | 1.0.0 | 0 | 0 | 2ccfb7ff-e8ec-4370-bbd6-e27c94aa0e53 | 2224b23e-8473-4fc0-a509-df8fffab8d2c | `NULL` | `NULL` | 2026-10-03 14:23:00Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:22:59Z |
| e7856904-d0d5-4f14-9ab0-cea97ad483d4 | sr-cb3tuw | New node | AA23BB211652 | AA:23:BB:00:00:01 | 1.0.0 | 3 | 0 | 6de30a0e-8202-4d21-bbbd-2f394dbcde64 | be28d100-34a3-4e89-a81b-fd5c213010f1 | `NULL` | `NULL` | 2026-10-03 14:16:57Z | 2026-10-03 14:17:09Z | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:16:57Z |
| 76e43060-2ab3-4e7e-8ee4-fab118e963b2 | sr-nr3mu2 | New node | BB1333895909 | BB:11:BB:22:CC:99 | 1.0.0 | 1 | 0 | 99079a6a-2639-42fe-8950-15bf7b75ec84 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | `NULL` | `NULL` | 2026-10-06 17:00:02Z | `NULL` | 2026-10-06 17:18:01Z | 60 | 60 | `NULL` | -60 | `NULL` | 1200 | 150 | 2026-10-06 17:00:01Z |
| 9c2b0fca-0c1d-44df-96b1-fbe2c67f8470 | sr-ggxwgb | New node | BB23BB211526 | AA:23:BB:00:00:02 | 1.0.0 | 0 | 0 | baa7ad53-25c3-4233-a05c-f1ca69dd4bed | e5226158-c6ac-4763-9843-64cc924fa291 | `NULL` | `NULL` | 2026-10-03 14:15:31Z | `NULL` | `NULL` | 60 | 60 | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` | 2026-10-03 14:15:31Z |

## `DeviceCredential` - 21 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `DeviceId` | `uniqueidentifier` | no |  |
| `SecretHash` | `varbinary(32)` | no |  |
| `Salt` | `varbinary(16)` | no |  |
| `IssuedAt` | `datetimeoffset` | no |  |
| `ExpiresAt` | `datetimeoffset` | yes |  |
| `RevokedAt` | `datetimeoffset` | yes |  |
| `GraceUntil` | `datetimeoffset` | yes |  |
| `IssuedByUserId` | `uniqueidentifier` | yes |  |

| Id | DeviceId | SecretHash | Salt | IssuedAt | ExpiresAt | RevokedAt | GraceUntil | IssuedByUserId |
|---|---|---|---|---|---|---|---|---|
| 21dcd700-5505-4321-8b21-035eddcf90b8 | 8dd261a7-b73c-49f6-98bf-2bef7c232ef5 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:57:50Z | `NULL` | 2026-10-03 13:57:51Z | `NULL` | ff4b1624-398c-4ced-887c-4c27e526958c |
| 31525d68-1b05-4771-9265-11629d53784e | 8dd261a7-b73c-49f6-98bf-2bef7c232ef5 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:57:50Z | `NULL` | 2026-10-03 13:57:51Z | `NULL` | `NULL` |
| 14e29c83-bf6b-431e-b800-302b0db30805 | aaba76d9-1cf1-45be-ad43-9510a104ef65 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:22:59Z | `NULL` | 2026-10-03 14:23:10Z | `NULL` | `NULL` |
| 22da8794-2f5d-4d13-be1b-34cb0f3a0c54 | 52198dd7-f1ae-4dfa-9f90-9e3cde0e1e77 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:15:31Z | `NULL` | `NULL` | `NULL` | `NULL` |
| b025bfd6-f007-4c7e-a1e9-545cef689c9c | 119e6989-4c10-48a6-a88d-96ecdf9bf627 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:57:51Z | `NULL` | `NULL` | `NULL` | `NULL` |
| ed7b94bd-7f9e-4608-a8a2-5844316e089a | a5724855-29d3-420e-acbd-14896cbada3d | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-06 16:58:52Z | `NULL` | 2026-10-06 16:58:59Z | `NULL` | `NULL` |
| f60169f7-99df-4c90-b026-5a7bb1ffcbaa | 9c2b0fca-0c1d-44df-96b1-fbe2c67f8470 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:15:31Z | `NULL` | `NULL` | `NULL` | `NULL` |
| d4f0cae3-8c17-4137-8063-61c9d844a85e | e7856904-d0d5-4f14-9ab0-cea97ad483d4 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:16:57Z | `NULL` | 2026-10-03 14:17:09Z | `NULL` | `NULL` |
| 652a1a80-af0a-4e31-bac7-6736a37d69f1 | 0b502e9d-3b25-41cb-b3bb-671a90a94cac | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:59:10Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 9d523bae-3b46-490c-b705-7c113bf72bc8 | 2226d3bd-64e1-4b1e-badf-cc7af3a6a20a | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:23:00Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 17299f5a-0258-4b34-b7ce-7d10ee2e22d6 | 93a7030c-db1e-4536-90fb-29e9f565ff16 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:18:12Z | `NULL` | 2026-10-03 14:18:23Z | `NULL` | `NULL` |
| 3fb132ee-1fc5-4ed6-962c-84107cf2ea53 | 071f64d4-c68c-4daf-aa0d-796981200376 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:18:12Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 1bfb5bc9-5cb0-4ad0-93b7-a2e7f2d8cf3d | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-07 01:59:45Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 02e16d87-e70c-4ca7-9b5a-a95c8df1524f | 90c7641d-bf54-4660-af5c-33e506d2a01f | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:16:58Z | `NULL` | `NULL` | `NULL` | `NULL` |
| d1b89779-90e3-4fb0-ac34-adac5dc9b9c8 | 76e43060-2ab3-4e7e-8ee4-fab118e963b2 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-06 17:00:02Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 51c07735-6f12-4a5c-9322-bd705047d496 | 61f4ac4a-6c50-4148-981e-56ce570e1310 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:16:21Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 346359b1-8958-4f0f-bff6-c056511a5ce4 | ecb5b54c-2745-4709-93ec-b3812bdeaf3c | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:59:10Z | `NULL` | 2026-10-03 13:59:10Z | `NULL` | 126554f9-e194-4afd-8bd0-c156dbcb2bb2 |
| bf2ac418-a499-4c05-86a7-c17c09c4cbb2 | 2e1dd9e3-3559-406e-a67a-a0b5625d09f5 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 14:16:20Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 1bb170fe-948b-4c5a-87b5-c37c1aab6484 | a5724855-29d3-420e-acbd-14896cbada3d | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-06 16:58:59Z | `NULL` | 2026-10-06 16:58:59Z | `NULL` | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 |
| 23f80f39-240c-44bc-bfa2-c62dbe452dbc | ecb5b54c-2745-4709-93ec-b3812bdeaf3c | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-03 13:59:09Z | `NULL` | 2026-10-03 13:59:10Z | `NULL` | `NULL` |
| d9d184a5-5b07-458b-8d48-f45d717940e5 | b343b411-8e49-4861-b68d-3eb58a08d2a1 | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 2026-10-06 16:45:49Z | `NULL` | `NULL` | `NULL` | `NULL` |

## `DeviceHealthSample` - 8 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `bigint` | no | PK, identity |
| `DeviceId` | `uniqueidentifier` | no |  |
| `TerrariumId` | `uniqueidentifier` | no |  |
| `RecordedAt` | `datetimeoffset` | no |  |
| `RssiDbm` | `int` | yes |  |
| `UptimeSeconds` | `bigint` | yes |  |
| `FreeHeapKb` | `int` | yes |  |
| `BatteryPct` | `decimal` | yes |  |
| `FirmwareVersion` | `nvarchar(16)` | no |  |
| `QualityFlags` | `int` | no |  |

| Id | DeviceId | TerrariumId | RecordedAt | RssiDbm | UptimeSeconds | FreeHeapKb | BatteryPct | FirmwareVersion | QualityFlags |
|---|---|---|---|---|---|---|---|---|---|
| 20002 | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 2026-10-06 16:44:49Z | -63 | 86400 | 142 | `NULL` | 1.0.0 | 0 |
| 20003 | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 2026-10-06 16:44:49Z | -63 | 86400 | 142 | `NULL` | 1.0.0 | 0 |
| 20004 | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 2026-10-06 16:44:49Z | -63 | 86400 | 142 | `NULL` | 1.0.0 | 0 |
| 20005 | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 2026-10-06 16:48:07Z | -61 | 90000 | 140 | `NULL` | 1.0.0 | 0 |
| 20014 | 76e43060-2ab3-4e7e-8ee4-fab118e963b2 | 99079a6a-2639-42fe-8950-15bf7b75ec84 | 2026-10-06 17:18:01Z | -60 | 1200 | 150 | `NULL` | 1.0.0 | 0 |
| 30002 | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 7806c641-6cf8-401e-81ed-4363af97d8fe | 2026-10-07 01:53:55Z | -61 | 4200 | 142 | 87.50 | 1.0.0 | 0 |
| 30003 | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 7806c641-6cf8-401e-81ed-4363af97d8fe | 2026-10-07 01:59:17Z | -59 | 4400 | 141 | 87.20 | 1.0.0 | 0 |
| 30004 | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 7806c641-6cf8-401e-81ed-4363af97d8fe | 2026-10-07 02:00:46Z | -58 | 4500 | 140 | 87.00 | 1.0.0 | 0 |

## `MetricReading` - 60 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `bigint` | no | PK, identity |
| `SampleId` | `bigint` | no |  |
| `Metric` | `int` | no |  |
| `Value` | `decimal` | no |  |
| `RawValue` | `decimal` | yes |  |

| Id | SampleId | Metric | Value | RawValue |
|---|---|---|---|---|
| 10002 | 40002 | 1 | 26.000 | `NULL` |
| 10003 | 40002 | 2 | 35.000 | `NULL` |
| 10004 | 40003 | 1 | 30.000 | `NULL` |
| 10005 | 40003 | 2 | 45.000 | `NULL` |
| 10006 | 40004 | 1 | 35.000 | `NULL` |
| 10007 | 40004 | 2 | 50.000 | `NULL` |
| 20002 | 50002 | 1 | 28.750 | 28.900 |
| 20003 | 50002 | 2 | 41.200 | 40.800 |
| 20004 | 50002 | 3 | 1820.500 | 1790.000 |
| 20005 | 50002 | 4 | 0.300 | `NULL` |
| 20006 | 50002 | 5 | 31.200 | `NULL` |
| 20007 | 50003 | 1 | 28.900 | `NULL` |
| 20008 | 50003 | 2 | 41.350 | `NULL` |
| 20009 | 50003 | 3 | 1830.000 | `NULL` |
| 20010 | 50003 | 4 | 0.310 | `NULL` |
| 20011 | 50003 | 5 | 31.300 | `NULL` |
| 20012 | 50004 | 1 | 29.100 | `NULL` |
| 20013 | 50004 | 2 | 42.000 | `NULL` |
| 20014 | 50005 | 1 | 29.250 | `NULL` |
| 20015 | 50005 | 2 | 42.150 | `NULL` |
| 20026 | 50014 | 1 | 27.420 | `NULL` |
| 20027 | 50014 | 2 | 55.500 | `NULL` |
| 20028 | 50014 | 3 | 1200.000 | `NULL` |
| 20029 | 50014 | 4 | 0.200 | `NULL` |
| 20030 | 50014 | 5 | 29.900 | `NULL` |
| 20031 | 50015 | 1 | 27.550 | `NULL` |
| 20032 | 50015 | 2 | 55.800 | `NULL` |
| 20033 | 50015 | 3 | 1210.000 | `NULL` |
| 20034 | 50015 | 4 | 0.210 | `NULL` |
| 20035 | 50015 | 5 | 30.000 | `NULL` |
| 30002 | 60002 | 1 | 27.900 | `NULL` |
| 30003 | 60002 | 2 | 44.000 | `NULL` |
| 30004 | 60002 | 3 | 900.000 | `NULL` |
| 30005 | 60002 | 4 | 0.100 | `NULL` |
| 30006 | 60002 | 5 | 29.500 | `NULL` |
| 30007 | 60003 | 1 | 29.400 | `NULL` |
| 30008 | 60003 | 2 | 41.500 | `NULL` |
| 30009 | 60003 | 3 | 1200.000 | `NULL` |
| 30010 | 60003 | 4 | 0.200 | `NULL` |
| 30011 | 60003 | 5 | 30.800 | `NULL` |
| 30012 | 60004 | 1 | 31.200 | `NULL` |
| 30013 | 60004 | 2 | 37.500 | `NULL` |
| 30014 | 60004 | 3 | 1600.000 | `NULL` |
| 30015 | 60004 | 4 | 0.350 | `NULL` |
| 30016 | 60004 | 5 | 32.400 | `NULL` |
| 30017 | 60005 | 1 | 32.600 | `NULL` |
| 30018 | 60005 | 2 | 33.000 | `NULL` |
| 30019 | 60005 | 3 | 1500.000 | `NULL` |
| 30020 | 60005 | 4 | 0.300 | `NULL` |
| 30021 | 60005 | 5 | 33.600 | `NULL` |
| 30022 | 60006 | 1 | 31.800 | `NULL` |
| 30023 | 60006 | 2 | 35.200 | `NULL` |
| 30024 | 60006 | 3 | 1400.000 | `NULL` |
| 30025 | 60006 | 4 | 0.280 | `NULL` |
| 30026 | 60006 | 5 | 32.900 | `NULL` |
| 30027 | 60007 | 1 | 33.400 | `NULL` |
| 30028 | 60007 | 2 | 36.100 | `NULL` |
| 30029 | 60007 | 3 | 1580.000 | `NULL` |
| 30030 | 60007 | 4 | 0.310 | `NULL` |
| 30031 | 60007 | 5 | 41.200 | `NULL` |

## `PasswordResetCode` - 6 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `UserId` | `uniqueidentifier` | no |  |
| `CodeHash` | `varbinary(32)` | no |  |
| `CreatedAt` | `datetimeoffset` | no |  |
| `ExpiresAt` | `datetimeoffset` | no |  |
| `ConsumedAt` | `datetimeoffset` | yes |  |
| `RequestedFromAddress` | `nvarchar(45)` | yes |  |

| Id | UserId | CodeHash | CreatedAt | ExpiresAt | ConsumedAt | RequestedFromAddress |
|---|---|---|---|---|---|---|
| 5f387894-c8c5-4ee2-9cfe-27c389da2c73 | 813e6dee-1d06-4a37-a2e0-38e294881d22 | `<redacted: 32 bytes>` | 2026-10-06 15:54:16Z | 2026-10-06 16:24:16Z | 2026-10-06 15:54:16Z | ::1 |
| d0497a32-af09-46cf-ab96-3d11dffecf5c | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 2026-10-06 15:32:53Z | 2026-10-06 16:02:53Z | 2026-10-06 15:32:59Z | ::1 |
| 1aaa9e5a-4a08-4354-9bcc-5b83457b7157 | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 2026-10-06 15:35:30Z | 2026-10-06 16:05:30Z | 2026-10-06 15:35:31Z | ::1 |
| 03b71379-863c-47c6-ab2e-a69dde70746e | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 2026-10-06 15:35:30Z | 2026-10-06 16:05:30Z | 2026-10-06 15:35:30Z | ::1 |
| 25e6c60f-947a-488b-9905-b9400b6a2f69 | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 2026-10-06 15:37:18Z | 2026-10-06 16:07:18Z | 2026-10-06 15:37:27Z | ::1 |
| 1cf4fe43-a4d2-40f0-81d9-c33852fee53b | 813e6dee-1d06-4a37-a2e0-38e294881d22 | `<redacted: 32 bytes>` | 2026-10-06 15:54:52Z | 2026-10-06 16:24:52Z | `NULL` | ::1 |

## `RefreshToken` - 48 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `UserId` | `uniqueidentifier` | no |  |
| `TokenHash` | `varbinary(32)` | no |  |
| `FamilyId` | `uniqueidentifier` | no |  |
| `IssuedAt` | `datetimeoffset` | no |  |
| `ExpiresAt` | `datetimeoffset` | no |  |
| `ConsumedAt` | `datetimeoffset` | yes |  |
| `RevokedAt` | `datetimeoffset` | yes |  |
| `DeviceInfo` | `nvarchar(200)` | yes |  |

| Id | UserId | TokenHash | FamilyId | IssuedAt | ExpiresAt | ConsumedAt | RevokedAt | DeviceInfo |
|---|---|---|---|---|---|---|---|---|
| ff853ce4-125b-4d97-80f9-02ab5756ce83 | 73c02680-eb43-4b6a-8540-353cd44b4365 | `<redacted: 32 bytes>` | a092b138-0bac-4ad0-9350-c12098cff3e4 | 2026-10-03 13:29:25Z | 2026-11-02 13:29:25Z | `NULL` | 2026-10-03 13:29:25Z | final-verify |
| 399736cd-7cc3-472f-8039-062a96cbee7b | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 2f8e03d5-7536-4e03-a73e-30519b961eb8 | 2026-10-07 02:46:59Z | 2026-11-06 02:46:59Z | `NULL` | `NULL` | `NULL` |
| ca69b8b9-77e4-4e36-814c-0a2f859ae790 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 7c4c6f98-bdc3-40b4-a3e3-1b6630e95387 | 2026-10-07 01:22:58Z | 2026-11-06 01:22:58Z | `NULL` | 2026-10-07 01:23:59Z | `NULL` |
| d61216c6-c65f-402c-8b98-19d5fea06252 | ff4b1624-398c-4ced-887c-4c27e526958c | `<redacted: 32 bytes>` | 27ee8600-1ed2-4cce-85ea-4c0b6f0d1938 | 2026-10-03 13:57:47Z | 2026-11-02 13:57:47Z | `NULL` | `NULL` | `NULL` |
| 9e0034bd-2728-4327-be3a-1a252f40210f | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 83333e2a-e776-492e-9d97-7eb27d7b38a8 | 2026-10-07 01:24:11Z | 2026-11-06 01:24:11Z | `NULL` | `NULL` | `NULL` |
| 2f6bfb40-88aa-4db0-916e-1d4a6842db2d | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 2f8e03d5-7536-4e03-a73e-30519b961eb8 | 2026-10-07 02:00:33Z | 2026-11-06 02:00:33Z | 2026-10-07 02:16:59Z | `NULL` | `NULL` |
| ddda79bf-07c1-4180-9de8-1ecce255a0c9 | be28d100-34a3-4e89-a81b-fd5c213010f1 | `<redacted: 32 bytes>` | 872a4d86-f6d3-4ffc-84e0-14fb4f9333fc | 2026-10-03 14:16:55Z | 2026-11-02 14:16:55Z | `NULL` | `NULL` | `NULL` |
| bcaaaacb-56fb-4d2b-a9ea-20b38e5e8e24 | 5b55ec1b-eb50-4b65-94f7-e26a74fe798d | `<redacted: 32 bytes>` | 6be9a0fa-0012-4f16-b888-af96e0a55235 | 2026-10-03 13:55:18Z | 2026-11-02 13:55:18Z | `NULL` | `NULL` | `NULL` |
| 742c8e9a-759d-4c39-8c57-2160be5e7cb8 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | c236e21f-4981-4d47-84dd-0613839fcf56 | 2026-10-07 01:08:15Z | 2026-11-06 01:08:15Z | 2026-10-07 01:08:38Z | 2026-10-07 01:08:52Z | `NULL` |
| db40d3ba-70cf-4b28-9271-21bedf09e705 | 73c02680-eb43-4b6a-8540-353cd44b4365 | `<redacted: 32 bytes>` | a092b138-0bac-4ad0-9350-c12098cff3e4 | 2026-10-03 13:29:25Z | 2026-11-02 13:29:25Z | 2026-10-03 13:29:25Z | 2026-10-03 13:29:25Z | final-verify |
| edaa10b2-49e6-444b-ad7d-255e5681140b | 96e47474-0b58-46bc-acc7-440571c3d983 | `<redacted: 32 bytes>` | 0e7b9b5b-e4a1-458b-bf80-51458ea643dd | 2026-10-06 16:45:36Z | 2026-11-05 16:45:36Z | `NULL` | `NULL` | `NULL` |
| 827c828c-ebe6-449a-9e0a-2b7a5c6bfec8 | 2224b23e-8473-4fc0-a509-df8fffab8d2c | `<redacted: 32 bytes>` | 2ffec325-9d01-47cb-b6c6-4d74c318f266 | 2026-10-03 14:22:58Z | 2026-11-02 14:22:58Z | `NULL` | `NULL` | `NULL` |
| 143f1123-dd6a-41f8-88bd-2e6c81cbabe9 | 5d0923a7-c353-4715-901d-8e757fb236ea | `<redacted: 32 bytes>` | eb692462-5ab5-43ec-8262-8b543e52662d | 2026-10-06 15:21:17Z | 2026-11-05 15:21:17Z | `NULL` | `NULL` | `NULL` |
| 29ebf955-3e19-4db3-95e6-3340c65b764d | 5d0923a7-c353-4715-901d-8e757fb236ea | `<redacted: 32 bytes>` | e97a0ab6-28f5-420e-83c0-1e292e5869b3 | 2026-10-06 15:02:14Z | 2026-11-05 15:02:14Z | `NULL` | `NULL` | `NULL` |
| 17975c0d-8c86-4575-b9d6-337aec630574 | 51088a02-8ec1-486d-90b2-97250ed1618f | `<redacted: 32 bytes>` | e645a1ef-9444-4613-ab42-7a4b1a3c2fb1 | 2026-10-03 13:59:10Z | 2026-11-02 13:59:10Z | `NULL` | `NULL` | `NULL` |
| 0dc4f841-4dc1-4548-952a-34381616ba9d | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | aa53b3cf-fd21-4e4a-ad9b-52949d49dc0a | 2026-10-07 01:59:45Z | 2026-11-06 01:59:45Z | `NULL` | `NULL` | `NULL` |
| 1ce46cf7-973b-4f9e-a2fe-3633b1e38451 | e5dd665b-9de0-4157-bfcd-b3ee99907009 | `<redacted: 32 bytes>` | 4e48ed0d-5c39-4249-8e92-89318b3b304b | 2026-10-03 13:27:29Z | 2026-11-02 13:27:29Z | `NULL` | `NULL` | `NULL` |
| 3265dcf1-9641-4f44-9928-38abe085198d | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 7bb62467-d447-44f9-b1ff-43813cac81e3 | 2026-10-06 15:37:32Z | 2026-11-05 15:37:32Z | `NULL` | 2026-10-06 15:37:37Z | `NULL` |
| 0e6b9e11-25d2-48d1-a353-3a70634c06dc | 5d0923a7-c353-4715-901d-8e757fb236ea | `<redacted: 32 bytes>` | eb692462-5ab5-43ec-8262-8b543e52662d | 2026-10-06 15:03:09Z | 2026-11-05 15:03:09Z | 2026-10-06 15:21:17Z | `NULL` | `NULL` |
| 8fc5f5c3-b626-4879-9613-3c079f537b9d | 5d0923a7-c353-4715-901d-8e757fb236ea | `<redacted: 32 bytes>` | 3efec8d7-b334-4cac-92a3-bddf8b766c0e | 2026-10-06 14:57:22Z | 2026-11-05 14:57:22Z | `NULL` | `NULL` | `NULL` |
| abcda2c0-9ba3-4514-a237-3c953ae83aff | e5226158-c6ac-4763-9843-64cc924fa291 | `<redacted: 32 bytes>` | c833b5ac-d59f-4377-99d3-58b4ff7c74ee | 2026-10-03 14:15:29Z | 2026-11-02 14:15:29Z | `NULL` | `NULL` | `NULL` |
| 919d1638-a388-45ed-9d08-3cb908dee415 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | c51dffb6-dcf8-496c-bf50-7b45f5b64ad8 | 2026-10-07 01:22:58Z | 2026-11-06 01:22:58Z | `NULL` | 2026-10-07 01:23:59Z | `NULL` |
| 435b31b6-fe61-4536-973f-418402ab31a8 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 63e1bcc9-b744-47df-8f42-91ba802e228a | 2026-10-07 01:09:03Z | 2026-11-06 01:09:03Z | `NULL` | 2026-10-07 01:23:59Z | `NULL` |
| 66700335-238e-47f4-b82c-536bdafdac34 | 5e44c1a7-d921-422a-946c-8842e935974c | `<redacted: 32 bytes>` | dad18108-8bce-4fc5-9c5f-a301695516d6 | 2026-10-03 14:18:10Z | 2026-11-02 14:18:10Z | `NULL` | `NULL` | `NULL` |
| 8c4d83c7-7bcb-467d-afe5-6ae39353121c | 644ae482-bde8-434a-8f82-29ffb6eb7a22 | `<redacted: 32 bytes>` | 177f4995-aef9-46d1-8ac0-dbb0ac2ed067 | 2026-10-03 13:30:54Z | 2026-11-02 13:30:54Z | `NULL` | `NULL` | `NULL` |
| 58cd99e0-9142-43eb-b5f1-75ebd4b9fb20 | 126554f9-e194-4afd-8bd0-c156dbcb2bb2 | `<redacted: 32 bytes>` | 8248761b-cc59-467c-ad58-b17c17f215d0 | 2026-10-03 13:59:06Z | 2026-11-02 13:59:06Z | `NULL` | `NULL` | `NULL` |
| e9d4796f-d16e-4a2b-9db2-7c16287be10b | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | `<redacted: 32 bytes>` | ae1ebb0f-ea32-4f9b-94b2-531da9b2c4a7 | 2026-10-06 16:58:31Z | 2026-11-05 16:58:31Z | `NULL` | `NULL` | `NULL` |
| 86fae50b-6f88-415a-8002-8af5d7742a3a | d0d6c1ba-db7e-477b-85c4-057d7294890b | `<redacted: 32 bytes>` | 93f8fc7d-ce35-4680-be18-addd9812bed5 | 2026-10-03 13:26:04Z | 2026-11-02 13:26:04Z | `NULL` | 2026-10-03 13:26:04Z | verify-script |
| a257a156-e87c-4742-a171-8be90aa7f33e | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | f10e8c3e-f074-4130-8746-93344ffef245 | 2026-10-06 15:32:59Z | 2026-11-05 15:32:59Z | `NULL` | 2026-10-06 15:35:31Z | `NULL` |
| 6cffcfd8-123a-484b-9aee-93b4fbbf4b3a | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 4e42bd0e-ae3f-4a41-8cfb-60dddbe1e35a | 2026-10-06 15:37:00Z | 2026-11-05 15:37:00Z | `NULL` | 2026-10-06 15:37:27Z | `NULL` |
| 500d5a7a-090b-48ce-9eee-9f553e1afebf | e26c12ec-b968-4284-9451-0dbb650ec351 | `<redacted: 32 bytes>` | 8bdf8a89-54b5-4e05-84f0-c4617c1e564d | 2026-10-06 15:23:41Z | 2026-11-05 15:23:41Z | `NULL` | 2026-10-06 15:23:41Z | `NULL` |
| 87ccee40-38a7-499d-a8de-ac6cbb77069b | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 2f8e03d5-7536-4e03-a73e-30519b961eb8 | 2026-10-07 02:16:59Z | 2026-11-06 02:16:59Z | 2026-10-07 02:32:59Z | `NULL` | `NULL` |
| f8df4c4c-e997-4881-8d83-af87ce7ef6a3 | 938a14fe-302e-4cee-878c-0545ecd436ad | `<redacted: 32 bytes>` | 9f2fff03-15e1-4432-b828-4e1e095a724c | 2026-10-03 13:57:51Z | 2026-11-02 13:57:51Z | `NULL` | `NULL` | `NULL` |
| 23198f74-c9ed-4e7e-af09-b3bd2af67bd5 | 813e6dee-1d06-4a37-a2e0-38e294881d22 | `<redacted: 32 bytes>` | 932caac2-9354-415d-bd0a-720818ae173f | 2026-10-06 15:54:17Z | 2026-11-05 15:54:17Z | `NULL` | `NULL` | `NULL` |
| 2ee2698b-054c-47e1-9e2d-be997818d1f4 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | `<redacted: 32 bytes>` | e5046e97-f5e2-4b58-be41-69dd0647cb0a | 2026-10-06 17:17:59Z | 2026-11-05 17:17:59Z | `NULL` | `NULL` | `NULL` |
| eb74aed2-2551-4302-83dd-c364b24be56a | cfddd88d-d97b-4692-a8d5-5a5b31240def | `<redacted: 32 bytes>` | b0e6f59e-63af-4bed-bd6f-b485e787d3bf | 2026-10-03 13:55:18Z | 2026-11-02 13:55:18Z | `NULL` | `NULL` | `NULL` |
| 1664b176-c32c-49c3-ae19-c6b3a1a7728c | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | c236e21f-4981-4d47-84dd-0613839fcf56 | 2026-10-07 01:08:38Z | 2026-11-06 01:08:38Z | `NULL` | 2026-10-07 01:08:52Z | `NULL` |
| 02513137-dac0-49f1-81d0-c77e8ea87e01 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 29935c45-60de-459c-b4e1-76abb4aed20d | 2026-10-07 01:24:08Z | 2026-11-06 01:24:08Z | `NULL` | 2026-10-07 01:24:09Z | `NULL` |
| 529362ce-b86e-46dc-8196-cf4e302ed1ab | 51088a02-8ec1-486d-90b2-97250ed1618f | `<redacted: 32 bytes>` | ad7846c0-13ae-41aa-a767-53e273d9916e | 2026-10-03 13:59:06Z | 2026-11-02 13:59:06Z | `NULL` | `NULL` | `NULL` |
| 53a0680a-148d-4e68-b5e7-d549236e0c1c | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 139dc28f-4abf-4c57-91b7-a24345c648a8 | 2026-10-07 01:59:39Z | 2026-11-06 01:59:39Z | `NULL` | `NULL` | `NULL` |
| 46ced076-030e-41f7-b2bc-d96e22cbabe4 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 5fe7487b-a0eb-4858-b407-8556f221dd41 | 2026-10-07 01:59:56Z | 2026-11-06 01:59:56Z | `NULL` | `NULL` | `NULL` |
| b5598144-944a-4a1d-94ba-d9bb9ba53d09 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 2f8e03d5-7536-4e03-a73e-30519b961eb8 | 2026-10-07 02:32:59Z | 2026-11-06 02:32:59Z | 2026-10-07 02:46:59Z | `NULL` | `NULL` |
| 368306cc-68fe-4e5a-8c51-db1cff37f2c9 | e26c12ec-b968-4284-9451-0dbb650ec351 | `<redacted: 32 bytes>` | 504c45ad-1c7a-4167-b038-3680eca5eeaf | 2026-10-06 15:21:09Z | 2026-11-05 15:21:09Z | `NULL` | 2026-10-06 15:21:53Z | `NULL` |
| 631b2db3-6d8c-43ef-93cf-dbcfd7833854 | d0d6c1ba-db7e-477b-85c4-057d7294890b | `<redacted: 32 bytes>` | 93f8fc7d-ce35-4680-be18-addd9812bed5 | 2026-10-03 13:26:03Z | 2026-11-02 13:26:03Z | 2026-10-03 13:26:04Z | 2026-10-03 13:26:04Z | verify-script |
| ac97b408-dce3-4667-b570-e2488166cc27 | 1859aa56-74b3-458f-9d6b-c8991ff7b170 | `<redacted: 32 bytes>` | 029adfd9-2d3f-44e5-b98b-3f08f43a2168 | 2026-10-03 14:16:18Z | 2026-11-02 14:16:18Z | `NULL` | `NULL` | `NULL` |
| e68acda8-c9a2-43f1-952c-f1937b8868e5 | f9b4280c-b32c-437c-aab4-022b4484eb05 | `<redacted: 32 bytes>` | 39f3408a-428d-4f3f-ab5e-426781100838 | 2026-10-07 01:09:11Z | 2026-11-06 01:09:11Z | `NULL` | 2026-10-07 01:23:59Z | `NULL` |
| bb8d1f5e-5257-460e-b43d-f25b7451ce9a | 938a14fe-302e-4cee-878c-0545ecd436ad | `<redacted: 32 bytes>` | 988683dd-43a1-4713-a7a7-0346dd28cb17 | 2026-10-03 13:57:47Z | 2026-11-02 13:57:47Z | `NULL` | `NULL` | `NULL` |
| be10a0ce-90ff-4dde-84d6-f9985835bb3d | 478341fc-59e3-4a50-b513-75dd2030af6d | `<redacted: 32 bytes>` | 01bb9ad7-53e2-4c84-bba1-ce6d62c5773c | 2026-10-06 15:35:30Z | 2026-11-05 15:35:30Z | `NULL` | 2026-10-06 15:35:31Z | `NULL` |

## `SpeciesProfile` - 4 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `Name` | `nvarchar(80)` | no |  |
| `ScientificName` | `nvarchar(120)` | no |  |
| `ClimateZone` | `int` | no |  |
| `IsBuiltIn` | `bit` | no |  |
| `PhotoperiodHours` | `decimal` | no |  |
| `LightsOnLocalTime` | `time` | no |  |
| `LightThresholdLux` | `decimal` | no |  |
| `MinLightHoursPerDay` | `decimal` | no |  |
| `Notes` | `nvarchar(1000)` | yes |  |
| `CreatedByUserId` | `uniqueidentifier` | yes |  |
| `RowVersion` | `timestamp` | yes |  |
| `CreatedAt` | `datetimeoffset` | no |  |
| `UpdatedAt` | `datetimeoffset` | no |  |

| Id | Name | ScientificName | ClimateZone | IsBuiltIn | PhotoperiodHours | LightsOnLocalTime | LightThresholdLux | MinLightHoursPerDay | Notes | CreatedByUserId | RowVersion | CreatedAt | UpdatedAt |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 8a4659e9-c998-4f3d-a54c-457adac4b8c7 | Arid-cool (bearded dragon, ambient) | Pogona vitticeps | 2 | true | 12.00 | 08:00:00 | 2000.00 | 10.00 | Cool-side variant of the Arid profile: the same species and the same enclosure, monitored from the cool side rather than the basking spot, which is why the day temperature band is the ambient range (28-33 °C) instead of the basking range. Only the ambient bands ship — there is no SurfaceTempC band, because that probe belongs on the basking rock this variant deliberately does not describe, and no UvIndex band, because the UVB gradient is a property of the basking zone and a cool-side target could never be met. The accumulated light rule stays profile-level (2000 lx for 10 h), exactly as the Arid profile has it. | `NULL` | `<redacted: 8 bytes>` | 2026-10-07 01:07:48Z | 2026-10-07 01:07:48Z |
| ae0d3682-877d-4172-8c66-6ba51b576bf4 | Tropical (humid forest) | Correlophus ciliatus | 0 | true | 12.00 | 07:00:00 | 500.00 | 10.00 | Typical ranges for crepuscular humid-forest geckos. Heat, not cold, is the common indoor killer. Humidity is the normal state; a dry spell causes shedding problems. Low-level UVB optional. | `NULL` | `<redacted: 8 bytes>` | 2026-09-21 10:34:21Z | 2026-09-21 10:34:21Z |
| 14609ae0-49b5-4777-bed2-a79165632cf2 | Arid (desert) | Pogona vitticeps | 2 | true | 12.00 | 08:00:00 | 2000.00 | 10.00 | True heliotherm: a hot basking spot is a requirement, and air temperature alone understates its needs. Requires a UVB lamp; without one the UVI band cannot be met and the app reports 'below target' rather than pretending it is fine. | `NULL` | `<redacted: 8 bytes>` | 2026-09-21 10:34:21Z | 2026-09-21 10:34:21Z |
| 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | Leopard gecko (semi-desert) | Eublepharis macularius | 1 | true | 12.00 | 07:00:00 | 1000.00 | 8.00 | Demo species. Thermal-gradient animal: air temperature near the warm side in the low 30s. A 3-5 °C night drop is beneficial, not a fault. The humidity band describes AIR humidity; the humid hide needs 70-80 %RH, which a single sensor cannot represent. | `NULL` | `<redacted: 8 bytes>` | 2026-09-21 10:34:21Z | 2026-09-21 10:34:21Z |

## `TelemetrySample` - 15 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `bigint` | no | PK, identity |
| `TerrariumId` | `uniqueidentifier` | no |  |
| `DeviceId` | `uniqueidentifier` | no |  |
| `RecordedAt` | `datetimeoffset` | no |  |
| `ReceivedAt` | `datetimeoffset` | no |  |
| `Sequence` | `bigint` | no |  |
| `QualityFlags` | `int` | no |  |
| `ClockSkewSeconds` | `int` | yes |  |
| `FirmwareVersion` | `nvarchar(16)` | no |  |
| `Source` | `int` | no |  |

| Id | TerrariumId | DeviceId | RecordedAt | ReceivedAt | Sequence | QualityFlags | ClockSkewSeconds | FirmwareVersion | Source |
|---|---|---|---|---|---|---|---|---|---|
| 40002 | 6ec11ae9-e639-4ac4-932c-0e6438246d09 | bf71df86-3f9a-412d-8cc4-72bd0cbdbb57 | 2026-10-06 14:37:57Z | 2026-10-06 14:57:57Z | 1 | 0 | `NULL` | 0.1.0-test | 0 |
| 40003 | 6ec11ae9-e639-4ac4-932c-0e6438246d09 | bf71df86-3f9a-412d-8cc4-72bd0cbdbb57 | 2026-10-06 14:47:57Z | 2026-10-06 14:57:57Z | 2 | 0 | `NULL` | 0.1.0-test | 0 |
| 40004 | 6ec11ae9-e639-4ac4-932c-0e6438246d09 | bf71df86-3f9a-412d-8cc4-72bd0cbdbb57 | 2026-10-06 14:56:57Z | 2026-10-06 14:57:57Z | 3 | 0 | `NULL` | 0.1.0-test | 0 |
| 50002 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 2026-10-06 16:44:49Z | 2026-10-06 16:45:49Z | 1 | 0 | 61 | 1.0.0 | 1 |
| 50003 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 2026-10-06 16:45:49Z | 2026-10-06 16:45:49Z | 2 | 0 | 1 | 1.0.0 | 1 |
| 50004 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 2026-10-06 16:48:07Z | 2026-10-06 16:49:07Z | 3 | 0 | 61 | 1.0.0 | 1 |
| 50005 | 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | b343b411-8e49-4861-b68d-3eb58a08d2a1 | 2026-10-06 16:49:07Z | 2026-10-06 16:49:07Z | 4 | 0 | 1 | 1.0.0 | 1 |
| 50014 | 99079a6a-2639-42fe-8950-15bf7b75ec84 | 76e43060-2ab3-4e7e-8ee4-fab118e963b2 | 2026-10-06 17:18:01Z | 2026-10-06 17:18:01Z | 1 | 0 | 1 | 1.0.0 | 1 |
| 50015 | 99079a6a-2639-42fe-8950-15bf7b75ec84 | 76e43060-2ab3-4e7e-8ee4-fab118e963b2 | 2026-10-06 17:19:01Z | 2026-10-06 17:18:01Z | 2 | 0 | -59 | 1.0.0 | 1 |
| 60002 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 01:53:55Z | 2026-10-07 01:59:55Z | 1 | 16 | 360 | 1.0.0 | 1 |
| 60003 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 01:54:55Z | 2026-10-07 01:59:55Z | 2 | 16 | 300 | 1.0.0 | 1 |
| 60004 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 01:55:55Z | 2026-10-07 01:59:55Z | 3 | 16 | 240 | 1.0.0 | 1 |
| 60005 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 01:56:55Z | 2026-10-07 01:59:55Z | 4 | 16 | 180 | 1.0.0 | 1 |
| 60006 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 01:57:55Z | 2026-10-07 01:59:55Z | 5 | 16 | 120 | 1.0.0 | 1 |
| 60007 | 7806c641-6cf8-401e-81ed-4363af97d8fe | e84cb1b8-33fd-4f28-8a26-677a241bb2fb | 2026-10-07 02:00:46Z | 2026-10-07 02:01:26Z | 9 | 0 | 40 | 1.0.0 | 1 |

## `Terrarium` - 20 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `UserId` | `uniqueidentifier` | no |  |
| `Name` | `nvarchar(60)` | no |  |
| `SpeciesProfileId` | `uniqueidentifier` | no |  |
| `Location` | `nvarchar(120)` | yes |  |
| `Description` | `nvarchar(1000)` | yes |  |
| `TimeZoneId` | `nvarchar(64)` | no |  |
| `CreatedAt` | `datetimeoffset` | no |  |
| `UpdatedAt` | `datetimeoffset` | no |  |
| `DeletedAt` | `datetimeoffset` | yes |  |

| Id | UserId | Name | SpeciesProfileId | Location | Description | TimeZoneId | CreatedAt | UpdatedAt | DeletedAt |
|---|---|---|---|---|---|---|---|---|---|
| 905c36b8-b32b-400e-afa0-01f19878e3f0 | be28d100-34a3-4e89-a81b-fd5c213010f1 | MQTT probe B | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:16:57Z | 2026-10-03 14:16:57Z | `NULL` |
| 6ec11ae9-e639-4ac4-932c-0e6438246d09 | 5d0923a7-c353-4715-901d-8e757fb236ea | E2E gecko box | 14609ae0-49b5-4777-bed2-a79165632cf2 | desk | end-to-end check | Asia/Ho_Chi_Minh | 2026-10-06 14:57:36Z | 2026-10-06 14:57:36Z | `NULL` |
| 378bdd72-a76c-4120-b341-109174f04ff7 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | M2 audit box | 14609ae0-49b5-4777-bed2-a79165632cf2 | lab | `NULL` | Asia/Ho_Chi_Minh | 2026-10-06 16:58:46Z | 2026-10-06 16:58:46Z | `NULL` |
| b8fa7848-d020-4b5d-b102-122083198d6d | e5226158-c6ac-4763-9843-64cc924fa291 | MQTT probe A | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:15:30Z | 2026-10-03 14:15:30Z | `NULL` |
| 99079a6a-2639-42fe-8950-15bf7b75ec84 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | M2 audit long headers | 14609ae0-49b5-4777-bed2-a79165632cf2 | lab | `NULL` | Asia/Ho_Chi_Minh | 2026-10-06 17:00:01Z | 2026-10-06 17:00:01Z | `NULL` |
| 074de6fb-befb-43af-a1ac-2aa0a9b6108b | 1859aa56-74b3-458f-9d6b-c8991ff7b170 | MQTT probe B | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:16:19Z | 2026-10-03 14:16:19Z | `NULL` |
| 6de30a0e-8202-4d21-bbbd-2f394dbcde64 | be28d100-34a3-4e89-a81b-fd5c213010f1 | MQTT probe A | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:16:56Z | 2026-10-03 14:16:56Z | `NULL` |
| 7806c641-6cf8-401e-81ed-4363af97d8fe | f9b4280c-b32c-437c-aab4-022b4484eb05 | Bearded dragon box | 14609ae0-49b5-4777-bed2-a79165632cf2 | desk | Wired-client verification, 2026-10-07 | Asia/Ho_Chi_Minh | 2026-10-07 01:59:39Z | 2026-10-07 01:59:39Z | `NULL` |
| f8403946-da87-4653-9169-508813f17a99 | e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | M2 audit long headers | 14609ae0-49b5-4777-bed2-a79165632cf2 | lab | `NULL` | Asia/Ho_Chi_Minh | 2026-10-06 16:59:13Z | 2026-10-06 16:59:13Z | `NULL` |
| 4633cec3-5b62-4d65-8075-78808cfa1ca4 | 938a14fe-302e-4cee-878c-0545ecd436ad | Someone else vivarium | 14609ae0-49b5-4777-bed2-a79165632cf2 | Basement | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 13:57:48Z | 2026-10-03 13:57:48Z | `NULL` |
| 79a077db-9eb2-49c9-b7ff-7a8c8eb2d2de | 96e47474-0b58-46bc-acc7-440571c3d983 | M2 fallback box | 14609ae0-49b5-4777-bed2-a79165632cf2 | lab | `NULL` | Asia/Ho_Chi_Minh | 2026-10-06 16:45:36Z | 2026-10-06 16:45:36Z | `NULL` |
| cfbf0e10-02ec-4358-a38a-8529f9be79f1 | ff4b1624-398c-4ced-887c-4c27e526958c | Verification vivarium | 14609ae0-49b5-4777-bed2-a79165632cf2 | Desk | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 13:57:48Z | 2026-10-03 13:57:48Z | `NULL` |
| 081f088c-aabc-4439-a4ae-9a65903ac77b | 51088a02-8ec1-486d-90b2-97250ed1618f | Someone else vivarium | 14609ae0-49b5-4777-bed2-a79165632cf2 | Basement | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 13:59:07Z | 2026-10-03 13:59:07Z | `NULL` |
| 269184b6-2f7e-478d-8d4e-b63138b8fb2f | 5e44c1a7-d921-422a-946c-8842e935974c | MQTT probe A | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:18:11Z | 2026-10-03 14:18:11Z | `NULL` |
| c8c82578-67e1-41c2-aa24-bc082a27456b | 2224b23e-8473-4fc0-a509-df8fffab8d2c | MQTT probe A | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:22:58Z | 2026-10-03 14:22:58Z | `NULL` |
| 5d5ab9c6-7424-4c9f-8032-ca2b1f2a5ffc | 126554f9-e194-4afd-8bd0-c156dbcb2bb2 | Verification vivarium | 14609ae0-49b5-4777-bed2-a79165632cf2 | Desk | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 13:59:07Z | 2026-10-03 13:59:07Z | `NULL` |
| 7aa98156-41c1-4f02-aab3-d83dfc03a4be | 1859aa56-74b3-458f-9d6b-c8991ff7b170 | MQTT probe A | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:16:19Z | 2026-10-03 14:16:19Z | `NULL` |
| 2ccfb7ff-e8ec-4370-bbd6-e27c94aa0e53 | 2224b23e-8473-4fc0-a509-df8fffab8d2c | MQTT probe B | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:22:59Z | 2026-10-03 14:22:59Z | `NULL` |
| baa7ad53-25c3-4233-a05c-f1ca69dd4bed | e5226158-c6ac-4763-9843-64cc924fa291 | MQTT probe B | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:15:30Z | 2026-10-03 14:15:30Z | `NULL` |
| f27ba2ef-3f41-43da-8d4d-f3b1e9d8e134 | 5e44c1a7-d921-422a-946c-8842e935974c | MQTT probe B | 14609ae0-49b5-4777-bed2-a79165632cf2 | Bench | `NULL` | Asia/Ho_Chi_Minh | 2026-10-03 14:18:11Z | 2026-10-03 14:18:11Z | `NULL` |

## `Threshold` - 19 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `SpeciesProfileId` | `uniqueidentifier` | no |  |
| `Metric` | `int` | no |  |
| `Phase` | `int` | no |  |
| `TargetMin` | `decimal` | no |  |
| `TargetMax` | `decimal` | no |  |
| `CriticalMin` | `decimal` | yes |  |
| `CriticalMax` | `decimal` | yes |  |
| `DwellWarnMinutes` | `int` | no |  |
| `DwellCritMinutes` | `int` | no |  |
| `RecoveryMargin` | `decimal` | no |  |
| `Enabled` | `bit` | no |  |
| `SourceRef` | `nvarchar(300)` | yes |  |
| `SourceUrl` | `nvarchar(400)` | yes |  |

| Id | SpeciesProfileId | Metric | Phase | TargetMin | TargetMax | CriticalMin | CriticalMax | DwellWarnMinutes | DwellCritMinutes | RecoveryMargin | Enabled | SourceRef | SourceUrl |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3a201294-a8be-4cb8-9f7d-03d853bd4a5e | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 5 | 1 | 30.000 | 34.000 | 22.000 | 38.000 | 5 | 2 | 1.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 4e7d87e6-f4c8-4193-9e31-0c0d2ef63acb | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 1 | 2 | 22.000 | 27.000 | 18.000 | 31.000 | 10 | 3 | 0.500 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 60585936-c322-4f45-b43f-11a8a270acf0 | 14609ae0-49b5-4777-bed2-a79165632cf2 | 1 | 1 | 38.000 | 42.000 | 30.000 | 45.000 | 5 | 2 | 1.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 54fc606d-d5f7-4116-bfb8-127283c1da26 | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 3 | 1 | 0.000 | 200000.000 | `NULL` | `NULL` | 15 | 15 | 0.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 33060e32-a673-4d46-86f4-1641a8074329 | ae0d3682-877d-4172-8c66-6ba51b576bf4 | 1 | 1 | 24.000 | 28.000 | 18.000 | 31.000 | 5 | 2 | 0.500 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| d6d46f8a-62ad-41a4-983f-1d374df33090 | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 2 | 0 | 30.000 | 40.000 | 20.000 | 60.000 | 15 | 2 | 3.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 0a516d4d-9920-486b-87a5-4494d0c98630 | 14609ae0-49b5-4777-bed2-a79165632cf2 | 2 | 0 | 30.000 | 40.000 | 20.000 | 55.000 | 15 | 2 | 3.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 7b528dd3-116c-460c-9b96-4706e9c07b83 | ae0d3682-877d-4172-8c66-6ba51b576bf4 | 3 | 1 | 0.000 | 200000.000 | `NULL` | `NULL` | 15 | 15 | 0.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 0a5882fb-2430-4daf-98a0-4956cb16216b | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 1 | 1 | 26.000 | 32.000 | 22.000 | 34.500 | 5 | 2 | 0.500 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 1b702029-6af3-4686-b984-5f69285b0c0f | ae0d3682-877d-4172-8c66-6ba51b576bf4 | 4 | 1 | 0.000 | 1.000 | 0.000 | 2.000 | 15 | 2 | 0.100 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| e508df06-75c9-4e84-a3f8-685b940185a4 | ae0d3682-877d-4172-8c66-6ba51b576bf4 | 2 | 0 | 60.000 | 80.000 | 40.000 | 95.000 | 15 | 2 | 3.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| d7ccf7a9-755c-47e0-976b-74c0fb46b6fa | 8a4659e9-c998-4f3d-a54c-457adac4b8c7 | 1 | 1 | 28.000 | 33.000 | 24.000 | 36.000 | 5 | 2 | 1.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 — dwell/recovery are team design choices, not literature (docs/07-appendices/05 §5 row 13) | `NULL` |
| f0f922c8-1c4a-429d-a31d-7e0a74de0bce | 9b0e8f59-5dc0-4299-9026-bfe8eb7ddb59 | 4 | 1 | 0.000 | 1.500 | 0.000 | 2.500 | 15 | 2 | 0.100 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 04d8d243-87bf-412b-b431-84acdc8c9624 | 14609ae0-49b5-4777-bed2-a79165632cf2 | 1 | 2 | 24.000 | 28.000 | 18.000 | 32.000 | 10 | 3 | 1.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| 2091240a-6c83-4811-b922-927ad9cce7d5 | 14609ae0-49b5-4777-bed2-a79165632cf2 | 4 | 1 | 1.000 | 3.500 | 0.000 | 5.000 | 15 | 2 | 0.200 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| c8cc9387-7b83-4a59-9631-99467a4bf04a | 8a4659e9-c998-4f3d-a54c-457adac4b8c7 | 2 | 0 | 30.000 | 40.000 | 20.000 | 55.000 | 15 | 2 | 3.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 — dwell/recovery are team design choices, not literature (docs/07-appendices/05 §5 row 13) | `NULL` |
| 2c888682-4e97-4c6b-97b7-a908aa549d8b | ae0d3682-877d-4172-8c66-6ba51b576bf4 | 1 | 2 | 20.000 | 24.000 | 16.000 | 27.000 | 10 | 3 | 0.500 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| ed6fcbf1-118b-40e8-9996-c0ad544182db | 14609ae0-49b5-4777-bed2-a79165632cf2 | 5 | 1 | 38.000 | 45.000 | 28.000 | 50.000 | 5 | 2 | 1.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |
| bdda227d-23e7-40fb-af7c-f17839936528 | 14609ae0-49b5-4777-bed2-a79165632cf2 | 3 | 1 | 0.000 | 200000.000 | `NULL` | `NULL` | 15 | 15 | 0.000 | true | PENDING VERIFICATION — typical published husbandry range, see docs/07-appendices/05 §5 | https://github.com/your-org/smartreptile/blob/main/docs/07-appendices/05-species-threshold-reference.md |

## `ThresholdOverride` - 0 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `TerrariumId` | `uniqueidentifier` | no |  |
| `Metric` | `int` | no |  |
| `Phase` | `int` | no |  |
| `TargetMin` | `decimal` | no |  |
| `TargetMax` | `decimal` | no |  |
| `CriticalMin` | `decimal` | yes |  |
| `CriticalMax` | `decimal` | yes |  |
| `DwellWarnMinutes` | `int` | no |  |
| `DwellCritMinutes` | `int` | no |  |
| `RecoveryMargin` | `decimal` | no |  |
| `Enabled` | `bit` | no |  |
| `Note` | `nvarchar(300)` | yes |  |
| `CreatedByUserId` | `uniqueidentifier` | yes |  |
| `CreatedAt` | `datetimeoffset` | no |  |

_No rows._

## `User` - 25 row(s)

| Column | Type | Null | Keys |
|---|---|---|---|
| `Id` | `uniqueidentifier` | no | PK |
| `Username` | `nvarchar(32)` | no |  |
| `Email` | `nvarchar(256)` | no |  |
| `PasswordHash` | `varbinary(64)` | no |  |
| `PasswordSalt` | `varbinary(16)` | no |  |
| `PasswordIterations` | `int` | no |  |
| `Role` | `int` | no |  |
| `PreferredLanguage` | `nvarchar(2)` | no |  |
| `TimeZoneId` | `nvarchar(64)` | no |  |
| `QuietHoursStart` | `time` | yes |  |
| `QuietHoursEnd` | `time` | yes |  |
| `MinNotifySeverity` | `int` | no |  |
| `ChannelFcmEnabled` | `bit` | no |  |
| `ChannelEmailEnabled` | `bit` | no |  |
| `FcmToken` | `nvarchar(256)` | yes |  |
| `CreatedAt` | `datetimeoffset` | no |  |
| `LastLoginAt` | `datetimeoffset` | yes |  |
| `DisabledAt` | `datetimeoffset` | yes |  |
| `RecoveryCodeHash` | `varbinary(32)` | yes |  |
| `RecoveryCodeIssuedAt` | `datetimeoffset` | yes |  |
| `RecoveryCodeSalt` | `varbinary(16)` | yes |  |

| Id | Username | Email | PasswordHash | PasswordSalt | PasswordIterations | Role | PreferredLanguage | TimeZoneId | QuietHoursStart | QuietHoursEnd | MinNotifySeverity | ChannelFcmEnabled | ChannelEmailEnabled | FcmToken | CreatedAt | LastLoginAt | DisabledAt | RecoveryCodeHash | RecoveryCodeIssuedAt | RecoveryCodeSalt |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| f9b4280c-b32c-437c-aab4-022b4484eb05 | flowtest1 | flowtest1@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | en | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-07 01:07:49Z | 2026-10-07 02:00:33Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-07 01:07:49Z | `<redacted: 16 bytes>` |
| 938a14fe-302e-4cee-878c-0545ecd436ad | other22205745 | other22-205745@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 1 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:57:47Z | 2026-10-03 13:57:51Z | `NULL` | `NULL` | `NULL` | `NULL` |
| d0d6c1ba-db7e-477b-85c4-057d7294890b | keeper1791033962 | keeper1791033962@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:26:02Z | 2026-10-03 13:26:03Z | `NULL` | `NULL` | `NULL` | `NULL` |
| e26c12ec-b968-4284-9451-0dbb650ec351 | recoverdemo | recoverdemo@example.test | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 15:21:08Z | 2026-10-06 15:23:41Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-06 15:23:41Z | `<redacted: 16 bytes>` |
| 644ae482-bde8-434a-8f82-29ffb6eb7a22 | keeper1791034253 | keeper1791034253@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:30:53Z | 2026-10-03 13:30:54Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 73c02680-eb43-4b6a-8540-353cd44b4365 | keeper1791034163 | keeper1791034163@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:29:23Z | 2026-10-03 13:29:24Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 813e6dee-1d06-4a37-a2e0-38e294881d22 | srfinal6081 | srfinal6081@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 15:54:16Z | 2026-10-06 15:54:17Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-06 15:54:16Z | `<redacted: 16 bytes>` |
| 96e47474-0b58-46bc-acc7-440571c3d983 | m2http993 | m2http993@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 16:45:35Z | 2026-10-06 16:45:36Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-06 16:45:35Z | `<redacted: 16 bytes>` |
| ff4b1624-398c-4ced-887c-4c27e526958c | owner22205745 | owner22-205745@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:57:46Z | 2026-10-03 13:57:46Z | `NULL` | `NULL` | `NULL` | `NULL` |
| cfddd88d-d97b-4692-a8d5-5a5b31240def | other22205516 | other22-205516@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:55:18Z | 2026-10-03 13:55:18Z | `NULL` | `NULL` | `NULL` | `NULL` |
| e0dcf2c8-e46e-4d21-a650-5d027354d5d7 | sraudit1973 | sraudit1973@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 16:58:01Z | 2026-10-06 17:17:59Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-06 16:58:01Z | `<redacted: 16 bytes>` |
| e5226158-c6ac-4763-9843-64cc924fa291 | mqtt23211526 | mqtt23-211526@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 14:15:28Z | 2026-10-03 14:15:29Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 478341fc-59e3-4a50-b513-75dd2030af6d | srreset3593 | srreset3593@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 15:32:47Z | 2026-10-06 15:37:32Z | `NULL` | `<redacted: 32 bytes>` | 2026-10-06 15:37:37Z | `<redacted: 16 bytes>` |
| 9b3ad568-7a2a-49a8-96ac-7e7d1149f9cd | e2erufyiy | e2erufyiy@example.test | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 14:57:10Z | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` |
| bf909113-bf45-474e-8ef0-843d84a7ac16 | verify22204717 | verify22-204717@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:47:17Z | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` |
| 5e44c1a7-d921-422a-946c-8842e935974c | mqtt23211807 | mqtt23-211807@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 14:18:09Z | 2026-10-03 14:18:10Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 5d0923a7-c353-4715-901d-8e757fb236ea | e2envgjvu | e2envgjvu@example.test | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 14:57:22Z | 2026-10-06 15:03:09Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 51088a02-8ec1-486d-90b2-97250ed1618f | other22205904 | other22-205904@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 1 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:59:06Z | 2026-10-03 13:59:10Z | `NULL` | `NULL` | `NULL` | `NULL` |
| e5dd665b-9de0-4157-bfcd-b3ee99907009 | keeper1791034049 | keeper1791034049@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:27:29Z | 2026-10-03 13:27:29Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 126554f9-e194-4afd-8bd0-c156dbcb2bb2 | owner22205904 | owner22-205904@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:59:05Z | 2026-10-03 13:59:05Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 1859aa56-74b3-458f-9d6b-c8991ff7b170 | mqtt23211615 | mqtt23-211615@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 14:16:17Z | 2026-10-03 14:16:18Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 1afdcda1-99bf-43fe-8328-ca40ee651048 | e2erhktvy | e2erhktvy@example.test | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-06 14:57:01Z | `NULL` | `NULL` | `NULL` | `NULL` | `NULL` |
| 2224b23e-8473-4fc0-a509-df8fffab8d2c | mqtt23212255 | mqtt23-212255@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 14:22:57Z | 2026-10-03 14:22:57Z | `NULL` | `NULL` | `NULL` | `NULL` |
| 5b55ec1b-eb50-4b65-94f7-e26a74fe798d | owner22205516 | owner22-205516@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 13:55:17Z | 2026-10-03 13:55:17Z | `NULL` | `NULL` | `NULL` | `NULL` |
| be28d100-34a3-4e89-a81b-fd5c213010f1 | mqtt23211652 | mqtt23-211652@example.com | `<redacted: 32 bytes>` | `<redacted: 16 bytes>` | 210000 | 0 | vi | Asia/Ho_Chi_Minh | `NULL` | `NULL` | 1 | false | false | `NULL` | 2026-10-03 14:16:54Z | 2026-10-03 14:16:55Z | `NULL` | `NULL` | `NULL` | `NULL` |

