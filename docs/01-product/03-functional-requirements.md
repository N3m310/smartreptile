# 03 — Functional Requirements

**Convention.** `MUST` = required for v1, `SHOULD` = expected but negotiable, `MAY` = optional
stretch. Every requirement has acceptance criteria in Given/When/Then form and, where it
matters, business rules with ids (`BR-xx`). Test coverage is mapped in
`04-quality/04-requirements-traceability-matrix.md`.

**Actors.** `Owner` (account holder, full rights) · `Technician` (operate + acknowledge) ·
`Viewer` (read-only) · `Device` (authenticated ESP32 node) · `Scheduler` (internal background
workers: ingest, evaluator, notifier, rollup, retention).

---

## FR-01 — Account registration, login and session management
**Priority** MUST · **Actors** Owner/Technician/Viewer

The system MUST let a user register with username + email + password, log in, refresh a
session, and log out.

**Rules**
- `BR-01.1` Username: 3–32 chars, `[A-Za-z0-9._-]`, unique (case-insensitive).
- `BR-01.2` Password: ≥ 10 chars, must contain at least one letter and one digit; hashed with
  PBKDF2-HMAC-SHA256 (≥ 210 000 iterations, 16-byte salt) or Argon2id (m=64 MiB, t=3, p=1).
- `BR-01.3` Access token JWT lifetime 15 min; refresh token 30 days, single-use with rotation.
- `BR-01.4` After 5 failed logins for one username within 15 min, that username is throttled for 15 min.

**Acceptance criteria**
- **Given** a valid registration payload, **when** the user submits it, **then** the account is
  created with role `Owner`, a password hash is stored (never the plaintext), and `201` is returned.
- **Given** an existing account, **when** the user logs in with the correct password, **then**
  access + refresh tokens are returned and `LastLoginAt` is updated.
- **Given** an expired access token and a valid refresh token, **when** the app calls
  `POST /auth/refresh`, **then** a new access token and a new refresh token are returned and the
  old refresh token is invalidated.
- **Given** a wrong password, **when** login is attempted, **then** the response is `401` with a
  generic message and no indication of whether the username exists.

---

## FR-02 — Role-based access control on every resource
**Priority** MUST · **Actors** all

All terrarium-scoped data MUST be filtered by ownership, and role MUST gate privileged actions.

**Rules**
- `BR-02.1` Roles: `Owner` (manage terrariums, thresholds, devices, users) ·
  `Technician` (acknowledge/resolve alerts, silence metrics, view everything) ·
  `Viewer` (read-only: dashboard, history, exports).
- `BR-02.2` A user MUST NOT be able to read or write a terrarium they are not a member of,
  even with a valid token. Cross-tenant reads return `404`, not `403`.

**Acceptance criteria**
- **Given** user A and a terrarium owned by user B, **when** A requests
  `GET /terrariums/{B.terrariumId}`, **then** the response is `404`.
- **Given** a `Viewer` token, **when** the user calls `POST /alerts/{id}/ack`, **then** the
  response is `403` with problem code `insufficient_role`.
- **Given** a `Technician` token, **when** the user deletes a terrarium, **then** the response
  is `403`.

---

## FR-03 — Terrarium management
**Priority** MUST · **Actors** Owner

The system MUST support creating, editing, listing and deleting a terrarium record and
assigning exactly one species profile to it.

**Rules**
- `BR-03.1` Fields: `name` (required, ≤ 60 chars), `speciesProfileId` (required),
  `location` (optional), `description`/notes (optional), `timeZoneId` (default `Asia/Ho_Chi_Minh`).
- `BR-03.2` A terrarium with a bound device cannot be deleted until the device is unbound
  (explicit confirmation required, returns `409 conflict_device_bound` otherwise).
- `BR-03.3` Deleting a terrarium cascades: unbind device, soft-delete terrarium, keep alerts and
  summaries for audit until the account is deleted.

**Acceptance criteria**
- **Given** no terrarium exists, **when** the Owner creates "Linh's gecko box" with the leopard
  gecko profile, **then** `201` returns the terrarium with its id and the default thresholds are
  materialised from the profile.
- **Given** a terrarium with a bound device, **when** the Owner deletes it without confirmation,
  **then** the response is `409` and nothing is deleted.
- **Given** an Owner changes `speciesProfileId`, **when** the change is saved, **then** existing
  alerts are left untouched but future evaluations use the new bands, and an audit record is written.

---

## FR-04 — Device provisioning by claim code
**Priority** MUST · **Actors** Device, Owner

The system MUST allow a factory-fresh device to be bound to a terrarium without manual id entry.

**Rules**
- `BR-04.1` A device that has no secret boots into *provisioning mode*, connects to Wi-Fi
  (captive-portal or BLE-assisted configuration), and displays an 8-character claim code
  (uppercase, unambiguous alphabet) with a 15-minute expiry.
- `BR-04.2` `POST /devices/claim { claimCode, terrariumId }` binds the device, returns the device
  secret **exactly once**, and marks the code consumed.
- `BR-04.3` An expired, unknown, or already-consumed code returns `404 claim_code_invalid`
  (indistinguishable to prevent enumeration).
- `BR-04.4` Only `Owner` may claim. A terrarium may hold at most one bound device in v1.
- `BR-04.5` The device persists the secret in NVS and stops showing the claim code.

**Acceptance criteria**
- **Given** a device showing `K7M2-QP4T`, **when** the Owner submits the code for terrarium T,
  **then** the device is bound to T, the secret is returned once, and the fleet list shows status
  `provisioned`.
- **Given** the same code is submitted again, **when** the request is processed, **then** the
  response is `404 claim_code_invalid`.
- **Given** a code created 16 minutes ago, **when** it is submitted, **then** the response is
  `404 claim_code_invalid`.
- **Given** a terrarium already bound to device D1, **when** the Owner claims D2 for it, **then**
  the response is `409 terrarium_already_bound`.

---

## FR-05 — Device authentication and secure transport
**Priority** MUST · **Actors** Device, system

Device traffic MUST be authenticated and encrypted.

**Rules**
- `BR-05.1` MQTT over TLS (`8883`) with a valid broker certificate; username = `deviceId`,
  password = device secret. Plaintext `1883` MUST be disabled on the demo broker.
- `BR-05.2` HTTPS fallback uses `Authorization: Device <deviceId>.<secret>` over TLS 1.2+.
- `BR-05.3` The server stores only `SHA-256(secret || salt)`; comparison is constant-time.
- `BR-05.4` A revoked device MUST be disconnected within 60 s and its subsequent publishes rejected.
- `BR-05.5` Secret rotation (`POST /devices/{id}/rotate-secret`) returns a new secret once and
  invalidates the old one after a 10-minute grace period (so a device can reconnect).

**Acceptance criteria**
- **Given** a provisioned device, **when** it connects with the wrong secret, **then** the broker
  refuses the connection, the server logs an `auth_failed` audit event, and no telemetry is stored.
- **Given** an Owner revokes device D, **when** D publishes 30 s later, **then** the message is not
  persisted and the device record shows status `revoked`.
- **Given** a rotation request, **when** the device reconnects with the old secret 5 minutes later,
  **then** the connection succeeds (grace period) but stores an audit warning.

---

## FR-06 — Telemetry ingest
**Priority** MUST · **Actors** Device, Scheduler

The system MUST accept telemetry batches over MQTT (primary) and HTTPS (fallback) and persist
them reliably.

**Rules**
- `BR-06.1` Accepted metrics: `TempC`, `HumidityPct`, `LightLux`, `UvIndex`, `SurfaceTempC`
  (optional), plus health fields `rssi`, `batteryPct`, `firmwareVersion`, `uptimeS`, `heapKb`.
- `BR-06.2` Payload is JSON with `deviceId`, `seq` (monotonic per device), `fw`, `ts` (ISO-8601 UTC),
  `samples[]`, `health`.
- `BR-06.3` Duplicate detection key: `(deviceId, seq)`. A duplicate MUST be accepted with `200`/
  `ack` and MUST NOT create a second sample row (idempotent).
- `BR-06.4` Plausibility limits: `TempC` −10…60, `HumidityPct` 0…100, `LightLux` 0…200 000,
  `UvIndex` 0…15, `SurfaceTempC` −10…80. Values outside are stored **flagged** (`q & 2`) but
  excluded from threshold evaluation.
- **`BR-06.5`** `RecordedAt` comes from the device (NTP-synced); `ReceivedAt` is assigned by the
  server at ingest. Both are stored; skew > 120 s is recorded as a quality bit.
- `BR-06.6` Out-of-order back-fill is allowed; samples are ordered by `RecordedAt` when queried.
  A back-fill batch MUST NOT trigger retroactive notifications for alerts that would have fired
  more than 6 h ago (they are still recorded).
- `BR-06.7` Server applies per-device calibration offsets before persisting `Value`
  (`RawValue` keeps the sensor output).
- `BR-06.8` Sustained ingest failures MUST NOT lose data: the device buffers (FR-07) and the
  server rejects with `5xx` only when it truly cannot persist.

**Acceptance criteria**
- **Given** a valid MQTT batch, **when** it arrives, **then** one `TelemetrySample` and one
  `MetricReading` per metric are stored, `Device.LastSeenAt` updates, and `device.status` becomes `online`.
- **Given** the identical batch (same `seq`) is published again, **when** it is processed, **then**
  no new rows are created and the outcome is reported as `duplicate`.
- **Given** a sample with `TempC = 85`, **when** it is ingested, **then** it is persisted with
  quality bit `2` set and it does not produce an alert.
- **Given** a 30-minute broker outage followed by back-fill, **when** the buffered batch arrives,
  **then** all samples are stored in chronological order with quality bit `8` and no duplicates exist.

---

## FR-07 — Sensor health, quality flags and offline buffering
**Priority** MUST · **Actors** Device, Scheduler

The system MUST distinguish "the animal is fine" from "the sensor is lying".

**Rules**
- `BR-07.1` Device detects I²C/1-Wire read failure and reports quality bit `1` with the last good
  value omitted; ≥ 3 consecutive failures raises a device-side `SensorFault` event on the
  `events` topic.
- `BR-07.2` If no sample arrives for `3 × samplingInterval`, the system raises a `DeviceSilent`
  alert (`severity: Warning`, then `Critical` after 30 min) instead of metric alerts.
- `BR-07.3` Device keeps a ring buffer of ≥ 12 h at a 60 s interval (≥ 720 samples) in NVS/flash.
- `BR-07.4` Optional per-device calibration offsets (`tempOffsetC`, `rhOffsetPct`, `luxGain`)
  are editable by `Owner` and applied at ingest.
- `BR-07.5` The API MUST expose data coverage: expected vs received samples per interval
  (`GET /terrariums/{id}/coverage`).

**Acceptance criteria**
- **Given** a disconnected device for 4 minutes with a 60 s interval, **when** the evaluator runs,
  **then** a `DeviceSilent` alert is raised within one evaluation cycle and no metric alerts are raised.
- **Given** 3 consecutive I²C failures on the humidity sensor, **when** the device publishes the
  event, **then** the dashboard marks humidity as *unavailable* and no humidity threshold
  evaluation occurs.
- **Given** a 10-hour outage, **when** the device reconnects, **then** all 600 buffered samples are
  delivered across batches and the coverage endpoint reports ≥ 98% for that window.

---

## FR-08 — Live dashboard
**Priority** MUST · **Actors** all

The system MUST show the current state of a terrarium with per-metric in-range status.

**Rules**
- `BR-08.1` Each metric card shows: current filtered value + unit, timestamp, freshness badge,
  target band, status (`InRange` / `OutOfRange` / `Critical` / `NoData`).
- `BR-08.2` Device status: `online` / `offline` / `maintenance`, plus relative last-seen.
- **`BR-08.3`** Values MUST refresh within 5 s of ingest (SignalR push; polling fallback ≤ 30 s).
- `BR-08.4` A terrarium with no data shows an empty state that names the next action
  ("Claim a device to start collecting").
- `BR-08.5` Stale data MUST be visually distinguished (dimmed + "last updated 12 min ago").

**Acceptance criteria**
- **Given** an open dashboard, **when** a sample is ingested, **then** the value, timestamp and
  status update within 5 s without a manual refresh.
- **Given** a device that stopped publishing 10 minutes ago, **when** the dashboard is viewed,
  **then** the header shows `offline · last seen 10 min ago` and metric cards are dimmed.
- **Given** temperature above `CriticalMax`, **when** the dashboard renders, **then** the card is
  in the `Critical` style and shows the exact band that was exceeded.

---

## FR-09 — Historical charts and range queries
**Priority** MUST · **Actors** all

The system MUST serve range queries and chart data for 1 h, 24 h, 7 d, 30 d and custom ranges.

**Rules**
- `BR-09.1` Bucketing: raw ≤ 6 h; 5-minute averages 6–48 h; hourly rollups beyond 48 h
  (bucket size returned in the response so the client can label it).
- `BR-09.2` Response includes `min`, `max`, `avg`, `count` per bucket per metric.
- `BR-09.3` Alert intervals in the range are returned as shaded overlays with severity.
- `BR-09.4` Timestamps are returned in UTC (`timestampUtc`) with the client rendering in the
  terrarium's `timeZoneId`.
- `BR-09.5` Series are gap-aware: intervals with no data are `null`, never interpolated.

**Acceptance criteria**
- **Given** 30 days of data, **when** the user selects 30 d, **then** hourly buckets are returned
  (≤ 720 points per metric) and the chart renders in < 2 s.
- **Given** a 4-hour gap, **when** the chart renders, **then** the line shows a gap rather than a
  straight interpolation across the gap.
- **Given** an alert at 14:05–14:40, **when** the same range is charted, **then** the interval is
  shaded and the alert is listed below the chart.

---

## FR-10 — Species profiles and threshold configuration
**Priority** MUST · **Actors** Owner

The system MUST ship built-in species profiles and allow custom profiles and per-terrarium
overrides.

**Rules**
- `BR-10.1` Seeded profiles: `Tropical (humid forest)`, `SemiArid (semi-desert)`,
  `Arid (desert)`, each with bands for `TempC`, `HumidityPct`, `LightLux`, `UvIndex`, with
  optional day/night variants and a source reference per metric
  (`SourceRef` + `SourceUrl`) — data in `07-appendices/05`.
- `BR-10.2` A custom profile MUST specify `TargetMin < TargetMax` and
  `CriticalMin ≤ TargetMin`, `CriticalMax ≥ TargetMax`, `CriticalMin < CriticalMax`.
- `BR-10.3` A terrarium resolves thresholds in this order: terrarium override → assigned profile →
  system default. The API MUST return `effectiveThresholds` with `source` per metric.
- `BR-10.4` Editing a profile or override MUST be audited (`who`, `when`, `before`, `after`).
- `BR-10.5` The system MUST warn (non-blocking) if a band contradicts the profile's climate zone
  baked-in sanity ranges (e.g. a "desert" profile with `TargetMax < 20 °C`).

**Acceptance criteria**
- **Given** the seeded leopard gecko profile, **when** a terrarium is assigned to it, **then** the
  `effectiveThresholds` endpoint returns 5 metrics with bands and a `source` of `profile`.
- **Given** `TargetMin = 40`, `TargetMax = 30`, **when** the Owner saves, **then** validation fails
  with a field-level error and a `400`.
- **Given** a terrarium override for humidity, **when** the effective thresholds are read, **then**
  that metric reports `source: override` and the others report `source: profile`.

---

## FR-11 — Threshold evaluation engine
**Priority** MUST · **Actors** Scheduler

The system MUST evaluate every ingested sample against the effective thresholds and apply dwell,
hysteresis, dedupe and phase rules. *The engine runs server-side only — the single source of truth.*

**Rules**
- `BR-11.1` Evaluation uses **filtered** values with quality bit `2` (implausible) or `1` (fault) excluded.
- `BR-11.2` Phase selection: `Day` when the local time is inside the photoperiod window, otherwise
  `Night`; metrics with `phase = Any` ignore the split.
- `BR-11.3` Dwell: Warning requires `dwellWarnMinutes` (default 5) consecutive minutes outside the
  target band; Critical requires `dwellCritMinutes` (default 2) outside the critical band.
- **`BR-11.4`** Hysteresis: recovery requires the value back inside the band by `recoveryMargin`
  for 3 consecutive minutes.
- `BR-11.5` Dedupe: at most one open alert per `{terrariumId, metric, severity, phase}`
  (`DedupeKey`). Repeated excursions extend the same alert (`LastObservedAt`, peak value tracked).
- `BR-11.6` Auto-transition `Warning → Critical` when the value crosses the critical band while an
  alert is open; the alert's severity is upgraded and a new notification is sent.
- `BR-11.7` Alerts are recorded even while silenced; silencing suppresses notifications only.
- `BR-11.8` Back-filled samples older than 6 h are evaluated for **reporting** but do not notify.

**Acceptance criteria**
- **Given** a 5-minute dwell and a temperature 2 °C above target for 4 minutes, **when** evaluation
  runs, **then** no alert is created.
- **Given** the same condition persisting to minute 5, **when** the sample arrives, **then** exactly
  one `OutOfRange` warning alert is created with `TriggeredAt` = first out-of-band sample time.
- **Given** an open warning and a value crossing `CriticalMax`, **when** it is evaluated, **then**
  the same alert's severity becomes `Critical` and one new notification is queued.
- **Given** recovery inside the band by the margin for 3 minutes, **when** evaluated, **then** the
  alert becomes `Resolved` with `ResolvedAt` set and a "recovered" notification is queued (configurable).
- **Given** a night phase with a separate night band, **when** the photoperiod window closes,
  **then** the night band is used for the next sample.

---

## FR-12 — Alert lifecycle and history
**Priority** MUST · **Actors** Owner, Technician, Viewer

**Rules**
- `BR-12.1` States: `Open → Acknowledged → Resolved`; `Open → Resolved` (auto or manual) allowed.
  `Resolved` is terminal; a new excursion creates a new alert.
- `BR-12.2` Acknowledge records `AcknowledgedByUserId` + `AcknowledgedAt`.
- `BR-12.3` Manual resolve requires a reason (`resolvedReason`: `Recovered`, `FalsePositive`,
  `SensorFault`, `Accepted`).
- `BR-12.4` Alert list supports filters: severity, metric, state, date range; default sort newest first.
- **`BR-12.5`** `FalsePositive` and `SensorFault` outcomes MUST be retained as labelled data for the
  v2 model (this is the label source described in `07-appendices/06`).
- `BR-12.6` Silence: `POST /terrariums/{id}/silences { metric, untilUtc, reason }`, max 24 h,
  `Owner`/`Technician`, auto-expires, always visible on the dashboard.

**Acceptance criteria**
- **Given** an open alert, **when** a Technician acknowledges it, **then** the state is
  `Acknowledged`, the actor and time are stored, and the badge count for open alerts decreases.
- **Given** an alert auto-resolved at 14:40, **when** the user lists alerts for 14:00–15:00,
  **then** the alert appears with both `TriggeredAt` and `ResolvedAt`.
- **Given** a resolved alert, **when** the same excursion recurs, **then** a new alert row is created
  with a new id (history is never mutated).

---

## FR-13 — Notifications
**Priority** MUST · **Actors** Scheduler, all users

**Rules**
- `BR-13.1` Channels: in-app inbox (always), FCM push (app), Telegram bot (optional per user),
  SMTP email (optional per user).
- `BR-13.2` Per-user preferences: channel enablement, minimum severity (`Warning`/`Critical`),
  quiet hours (default 22:00–06:00 local — *Critical bypasses quiet hours*).
- `BR-13.3` Rate limit: ≤ 10 notifications per terrarium per hour; beyond that, notifications are
  coalesced into one digest and the event is logged.
- `BR-13.4` Every attempt is recorded in `NotificationLog` with status, attempt count and error.
- `BR-13.5` Retry: FCM/Telegram 3 attempts with exponential backoff (5 s, 30 s, 2 min);
  failures never block ingest or evaluation.
- `BR-13.6` Notification content contains: terrarium name, metric, observed value, band, duration
  out of range, and a deep link.

**Acceptance criteria**
- **Given** a Critical alert at 23:30 with quiet hours active, **when** the notifier runs, **then**
  an FCM push is still delivered and the event is logged as `bypassed_quiet_hours`.
- **Given** 25 warnings in one hour, **when** the 11th is produced, **then** no individual
  notification is sent, one digest is sent at the end of the hour, and the inbox still contains 25 entries.
- **Given** a failed Telegram call, **when** the notifier retries, **then** 3 attempts are logged and
  the in-app notification remains available.

---

## FR-14 — Environmental summaries and exposure reporting
**Priority** MUST · **Actors** all, Scheduler

**Rules**
- `BR-14.1` `DailyEnvironmentalSummary` is produced per terrarium per **local** day (terrarium time
  zone) within 5 minutes after local midnight, and recomputed if late data arrives (idempotent upsert).
- `BR-14.2` Fields: min/max/avg per metric, `outOfRangeMinutes` per metric, `lightHours`,
  `alertCount`, `criticalAlertCount`, and the **exposure index** per metric
  (`TempExposureDegC_Hours`, `HumidityExposurePctHours`, `LightDeficitHours`).
- **`BR-14.3`** Exposure index definition (see `03-implementation/06`):
  hot = `Σ max(0, T − TargetMax) · Δt`, cold = `Σ max(0, TargetMin − T) · Δt`, summed over the day
  in degree-hours, using 1-minute linear interpolation between samples.
- `BR-14.4` Weekly view aggregates 7 daily summaries; the report screen shows a compliance
  percentage = 100 × (1 − outOfRangeMinutes / totalMinutesWithData).
- `BR-14.5` Export produces CSV (one row per bucket) or JSON (full summary objects) for a chosen
  range, generated asynchronously with a download link valid for 24 h.
- `BR-14.6` The data-coverage percentage MUST accompany every summary so an incomplete day is
  never presented as a healthy day.

**Acceptance criteria**
- **Given** a full day of data with 40 minutes above `TargetMax` of temperature, **when** the summary
  is read, **then** `outOfRangeMinutes.temperature = 40` and the exposure index reflects the
  magnitude of those minutes (peak-weighted, not just counted).
- **Given** a day with 30% missing data, **when** the summary is displayed, **then** the coverage
  badge shows 70% and the compliance number is marked as low-confidence.
- **Given** an export request for 7 days, **when** the job completes, **then** a CSV with 168 hourly
  rows is downloadable and its row count matches `count` in the response.

---

## FR-15 — Retention and data export
**Priority** MUST · **Actors** Scheduler, Owner

**Rules**
- `BR-15.1` Raw samples/readings retained 90 days, then purged by a nightly sweeper.
- `BR-15.2` Hourly rollups retained 24 months; daily summaries retained until the account is deleted.
- `BR-15.3` Rollups MUST be computed from raw data *before* raw purge, and a rollup must be
  recomputable (idempotent) for the last 48 h to absorb late back-fill.
- `BR-15.4` Owners can export all their data (JSON + CSV zip) and purge a terrarium's history
  explicitly (irreversible, requires typed confirmation).
- `BR-15.5` Purge operations are audited and reported in the response with rows deleted.

**Acceptance criteria**
- **Given** data 91 days old, **when** the sweeper runs, **then** raw rows are deleted, rollups for
  that period still exist, and the deletion count is logged.
- **Given** a 7-day back-fill arriving for a period whose raw data was purged, **when** it is
  ingested, **then** rollups for that period are recomputed and the daily summary is updated.
- **Given** an Owner purges a terrarium's history, **when** confirmed, **then** raw + rollups +
  summaries are removed, alerts are retained for audit, and an audit entry is written.

---

## FR-16 — Device fleet management
**Priority** MUST · **Actors** Owner

**Rules**
- `BR-16.1` Fleet list shows: device id, name, bound terrarium, status
  (`provisioning`/`online`/`offline`/`revoked`), firmware version, last seen, RSSI, uptime.
- `BR-16.2` Actions: rename, rebind to another terrarium (unbind + claim), rotate secret, revoke,
  set calibration offsets, read device configuration (sampling/publish interval).
- `BR-16.3` Downlink configuration is delivered on the next device connection via the `cmd` topic
  with an acknowledgement expectation (`ack` topic, 30 s timeout, retry ×3).
- `BR-16.4` Firmware version mismatch is displayed but OTA is not performed in v1; the minimum
  supported version is configurable and older devices are flagged.

**Acceptance criteria**
- **Given** a device bound to terrarium T, **when** the Owner rebinds it to T2, **then** T shows no
  device, T2 shows the device, a gap is visible in T's coverage, and both changes are audited.
- **Given** the Owner sets `samplingIntervalSec = 30`, **when** the device next connects, **then**
  the config is applied within 60 s and acknowledged, and new samples arrive at 30 s spacing.
- **Given** a device with no ack for 3 attempts, **when** the command times out, **then** the fleet
  view marks the command `failed` with the reason.

---

## FR-17 — Manual camera snapshot *(optional)*
**Priority** MAY · **Actors** Owner, Technician

**Rules**
- `BR-17.1` An optional ESP32-CAM node bound to the same terrarium MAY upload a still JPEG to
  `POST /api/v1/devices/{id}/snapshots` (multipart, ≤ 512 KB), at most one per 30 s.
- `BR-17.2` The dashboard shows the latest snapshot with its capture time and a manual
  "take snapshot" button; **no automatic image analysis exists in v1**.
- `BR-17.3` Snapshots are retained 7 days and are excluded from the v2 dataset unless the team
  extends the consent/notice wording.
- `BR-17.4` If no camera is bound, the UI MUST NOT show an empty camera panel.

**Acceptance criteria**
- **Given** a bound camera node, **when** the user taps "take snapshot", **then** a new image appears
  within 10 s with the correct timestamp.
- **Given** uploads faster than one per 30 s, **when** a second upload arrives, **then** it is
  rejected with `429 snapshot_rate_limited`.
- **Given** a snapshot older than 7 days, **when** the sweeper runs, **then** the file is deleted and
  the metadata row is marked `expired`.

---

## FR-18 — Operational visibility and audit
**Priority** MUST · **Actors** Scheduler, Owner

**Rules**
- `BR-18.1` `/health` (process alive + dependency booleans) and `/ready` (can serve traffic:
  DB + broker reachable) endpoints, unauthenticated but returning no sensitive detail.
- `BR-18.2` `/metrics` exposes counters: `ingest_samples_total`, `ingest_duplicates_total`,
  `ingest_rejected_total`, `alerts_opened_total{severity}`, `notifications_sent_total{channel}`,
  `eval_duration_ms`, `device_last_seen_age_seconds`.
- `BR-18.3` Structured JSON logs with correlation id per ingest batch; log level configurable.
- `BR-18.4` `AuditLog` records: login failures, role changes, device claim/rotate/revoke, threshold
  edits, purge, export generation.
- `BR-18.5` Owners can view the audit log for their terrariums; no API returns another user's audit rows.

**Acceptance criteria**
- **Given** a stopped broker, **when** `/ready` is called, **then** the response is `503` with
  `broker: false` and the app keeps serving read-only requests.
- **Given** a threshold edit, **when** the audit log is filtered by `entity = Threshold`, **then**
  the entry shows actor, timestamp and before/after values.
- **Given** 1 000 ingested samples, **when** `/metrics` is scraped, **then**
  `ingest_samples_total` increased by exactly 1 000.

---

## Requirement index

| Id | Title | Priority | Primary docs |
|---|---|---|---|
| FR-01 | Account registration, login, session | MUST | `02-design/06` |
| FR-02 | Role-based access control | MUST | `02-design/06` |
| FR-03 | Terrarium management | MUST | `02-design/02` |
| FR-04 | Device provisioning by claim code | MUST | `02-design/06` |
| FR-05 | Device authentication and secure transport | MUST | `02-design/06`, `07/03` |
| FR-06 | Telemetry ingest | MUST | `02-design/03` |
| FR-07 | Sensor health, quality flags, offline buffering | MUST | `02-design/03`, `03/04` |
| FR-08 | Live dashboard | MUST | `02-design/04` |
| FR-09 | Historical charts and range queries | MUST | `02-design/03`, `03/03` |
| FR-10 | Species profiles and thresholds | MUST | `03/06`, `07/05` |
| FR-11 | Threshold evaluation engine | MUST | `02-design/03` |
| FR-12 | Alert lifecycle and history | MUST | `02-design/03`, `02-design/05` |
| FR-13 | Notifications | MUST | `02-design/05` |
| FR-14 | Environmental summaries and exposure reporting | MUST | `03/06` |
| FR-15 | Retention and export | MUST | `02-design/02`, `07/02` |
| FR-16 | Device fleet management | MUST | `02-design/02` |
| FR-17 | Manual camera snapshot | MAY | `02-design/04` |
| FR-18 | Operational visibility and audit | MUST | `03/03`, `05/02` |
