# 03 — MQTT and REST API Specification

Interface contract between the three components. Frozen at M1 (`03-implementation/07`); changes require a
same-PR update to this file (standing rule 2).

Base URL (dev): `http://localhost:8080` · (demo) `https://<host>` · API version prefix: `/api/v1`
Broker (dev): `mqtts://localhost:8883` · plaintext `1883` bound to loopback only and disabled in release.

---

## 1. Authentication

| Caller | Mechanism |
|---|---|
| User (app/dashboard) | `Authorization: Bearer <jwt>` (15 min) + refresh token via `POST /auth/refresh` |
| Device (HTTPS fallback) | `Authorization: Device <deviceId>.<secret>` |
| Device (MQTT) | `username = deviceId`, `password = secret`, TLS required |
| Device (self-register / claim) | self-register is anonymous + rate limited; claim requires a user JWT with role `Owner` |

---

## 2. Provisioning endpoints

### 2.1 Claim-code alphabet
32 symbols, excluding `0 O 1 I L` to avoid transcription errors:
`23456789ABCDEFGHJKMNPQRSTUVWXYZ` → codes are 8 chars, typically displayed as `XXXX-XXXX`.
Entropy ≈ 32⁸ ≈ 1.1 × 10¹²; TTL 15 min; single-use; regenerated while the device stays in provisioning mode.

### 2.2 `POST /api/v1/devices/self-register`
Anonymous. Rate limit: 1 per IP per 5 min, 20 per hour globally (demo setting).

```http
POST /api/v1/devices/self-register
Content-Type: application/json

{ "chipId": "A0B1C2D3E4F5", "macAddress": "A0:B1:C2:D3:E4:F5", "firmwareVersion": "1.0.0" }
```
```http
201 Created
{ "deviceId": "sr-3f9a2c", "claimCode": "K7M2QP4T", "expiresAtUtc": "2026-09-21T08:30:00Z" }
```
Errors: `429 rate_limited`, `409 device_already_registered` (returns the existing `deviceId` and a fresh code
only if the device is still unclaimed).

### 2.3 `POST /api/v1/devices/claim`
Requires `Owner`.

```http
POST /api/v1/devices/claim
Authorization: Bearer <jwt>

{ "claimCode": "K7M2-QP4T", "terrariumId": "6f1c…" }
```
```http
200 OK
{ "deviceId": "sr-3f9a2c", "secret": "MFRGGZDFMZTWQ2LK…", "terrariumId": "6f1c…", "boundAtUtc": "…" }
```
`secret` is returned **once**; it is never retrievable again. Errors: `404 claim_code_invalid` (unknown,
expired, or consumed — deliberately indistinguishable), `409 terrarium_already_bound`, `403 insufficient_role`.

---

## 3. MQTT topics and payloads

### 3.1 Topic scheme

| Topic | Dir. | QoS | Retained | Payload |
|---|---|---|---|---|
| `sr/v1/d/{deviceId}/telemetry` | ↑ | 1 | no | telemetry batch |
| `sr/v1/d/{deviceId}/health` | ↑ | 1 | no | device health (every 5 min) |
| `sr/v1/d/{deviceId}/status` | ↑ | 1 | **yes** | `online` \| `offline` (LWT) \| `maintenance` |
| `sr/v1/d/{deviceId}/events` | ↑ | 1 | no | boot, sensor_fault, buffer_overflow, clock_unsynced, calibrated |
| `sr/v1/d/{deviceId}/cmd` | ↓ | 1 | no | `set_config`, `take_snapshot`, `ping` |
| `sr/v1/d/{deviceId}/ack` | ↑ | 1 | no | command result with `cmdId` |

`{deviceId}` is the short id (`sr-xxxxxx`). Subscriptions use wildcards server-side (`sr/v1/d/+/telemetry`);
the client is never allowed to subscribe to another device's topics (broker ACL = own prefix only).

### 3.2 Telemetry batch (device → broker)

```json
{
  "deviceId": "sr-3f9a2c",
  "seq": 10457,
  "fw": "1.0.0",
  "ts": "2026-09-21T08:15:00Z",
  "samples": [
    { "t": 0,  "tf": 28.75, "rh": 41.20, "lux": 1820.5, "uvi": 0.30, "st": 31.20, "q": 0,
      "raw": { "tf": 28.90, "rh": 40.80, "lux": 1790.0 } },
    { "t": 60, "tf": 28.90, "rh": 41.35, "lux": 1830.0, "uvi": 0.31, "st": 31.30 }
  ],
  "health": { "rssi": -63, "up_s": 86400, "heap_kb": 142, "bat": null, "src": "mains" }
}
```

| Field | Type | Required | Rules |
|---|---|---|---|
| `deviceId` | string | ✓ | must match the authenticated device |
| `seq` | int64 | ✓ | strictly increasing per device; first sample of the batch |
| `fw` | string | ✓ | ≤ 16 chars |
| `ts` | string | ✓ | ISO-8601 UTC (`Z`); the instant of `t = 0` |
| `samples[]` | array | ✓ | 1…120 items |
| `samples[].t` | int32 | ✓ | seconds offset from `ts`, strictly increasing, ≤ 86 400 |
| `samples[].tf` | float | ✓ | filtered air temperature, °C |
| `samples[].rh` | float | ✓ | relative humidity, % |
| `samples[].lux` | float | ○ | illuminance, lx |
| `samples[].uvi` | float | ○ | UV index |
| `samples[].st` | float | ○ | surface temperature, °C (omit the key entirely when absent) |
| `samples[].q` | int | ○ | quality bitmask; omit when 0 |
| `samples[].raw` | object | ○ | diagnostics only, never used for evaluation |
| `health` | object | ○ | rssi, up_s, heap_kb, bat, src (`mains`\|`battery`) |

Batch size limit 32 KB. Unknown metric keys inside `samples[]` cause that key to be dropped (logged as
`unknown_metric`) while the rest of the batch is stored — forward compatibility for future firmware.

### 3.3 Status / LWT

```json
{"deviceId":"sr-3f9a2c","status":"online","fw":"1.0.0","at":"2026-09-21T08:15:00Z"}
```
The device sets its LWT to `status: offline` with `retain = true` at connect time, so an unexpected
disconnect flips the status on the broker without waiting for the 3× interval watchdog.

### 3.4 Events

```json
{"deviceId":"sr-3f9a2c","type":"sensor_fault","metric":"HumidityPct","consecutiveFailures":3,"at":"…"}
{"deviceId":"sr-3f9a2c","type":"buffer_overflow","dropped":17,"oldestDroppedAt":"…","at":"…"}
{"deviceId":"sr-3f9a2c","type":"clock_unsynced","ntpAttempts":5,"at":"…"}
{"deviceId":"sr-3f9a2c","type":"boot","resetReason":"power_on","fw":"1.0.0","at":"…"}
```
Event types: `boot`, `sensor_fault`, `sensor_recovered`, `buffer_overflow`, `clock_unsynced`, `calibrated`.

### 3.5 Commands (server → device) and acks

```json
// sr/v1/d/{id}/cmd
{ "cmdId": "c-8891", "type": "set_config", "samplingIntervalSec": 30, "publishIntervalSec": 60 }

// sr/v1/d/{id}/ack
{ "cmdId": "c-8891", "ok": true, "appliedAt": "2026-09-21T08:20:11Z", "fw": "1.0.0" }
```
Types: `set_config` (intervals), `take_snapshot` (camera nodes only), `ping`. Ack expected within 30 s,
3 attempts, then the command is marked `failed` (FR-16). Unknown command types are acked with
`ok:false, error:"unsupported"` — never ignored silently.

### 3.6 HTTPS fallback

```http
POST /api/v1/ingest/http
Authorization: Device sr-3f9a2c.MFRGGZDFMZTWQ2LK…
Idempotency-Key: sr-3f9a2c:10457
Content-Type: application/json

{ …identical batch body as §3.2… }
```
```http
202 Accepted
{ "accepted": 2, "duplicates": 0, "rejected": 0, "reasons": [] }
```
Rate limit 6 requests/min/device. Same `IngestPipeline` as MQTT, so the only difference observable in the data
is `TelemetrySample.Source`.

---

## 4. REST API reference

Legend: `A` = anonymous, `U` = any authenticated role, `T` = Technician or Owner, `O` = Owner only.
All timestamps are ISO-8601 UTC. All list endpoints support `?cursor=&pageSize=` (default 50, max 200) and
return `nextCursor`.

### 4.1 Auth

| Method | Path | Role | Body / params | Success | Errors |
|---|---|---|---|---|---|
| POST | `/auth/register` | A | `{username, email, password}` | `201 {userId, username}` | `400 validation_failed`, `409 username_taken` / `email_taken` |
| POST | `/auth/login` | A | `{username, password}` | `200 {accessToken, refreshToken, expiresIn, user}` | `401 invalid_credentials`, `423 account_locked` |
| POST | `/auth/refresh` | A | `{refreshToken}` | `200 {accessToken, refreshToken}` | `401 token_invalid`, `401 token_reused` |
| POST | `/auth/logout` | U | `{refreshToken}` | `204` | — |
| GET | `/auth/me` | U | — | `200 {user, preferences}` | `401` |
| PATCH | `/auth/me` | U | preferences, timezone, language | `200` | `400 validation_failed` |
| POST | `/auth/change-password` | U | `{currentPassword, newPassword}` | `204` (all refresh tokens revoked) | `400 weak_password`, `401 invalid_credentials` |

### 4.2 Terrariums, thresholds, readings

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/terrariums` | U | list with device status summary |
| POST | `/terrariums` | O | `{name, speciesProfileId, location?, description?, timeZoneId?}` |
| GET | `/terrariums/{id}` | U (member) | includes `device`, `openAlertCount`, `latestSampleAt` |
| PATCH | `/terrariums/{id}` | O | `If-Match: <rowversion>` for optimistic concurrency |
| DELETE | `/terrariums/{id}` | O | soft delete; requires `?allowUnboundDevice=true` when a device is bound → else `409 conflict_device_bound` |
| GET | `/terrariums/{id}/thresholds` | U | returns `effectiveThresholds[]` with `source` |
| PUT | `/terrariums/{id}/thresholds` | O | upsert overrides; validates band ordering |
| DELETE | `/terrariums/{id}/thresholds/{metricId}` | O | revert one metric to the profile |
| GET | `/terrariums/{id}/readings/latest` | U | one row per metric + `deviceStatus`, `lastSeenAt` |
| GET | `/terrariums/{id}/readings?metric=&from=&to=` | U | bucketed; response echoes `bucket` |
| GET | `/terrariums/{id}/coverage?from=&to=` | U | expected vs received samples |
| GET | `/terrariums/{id}/summaries?from=&to=` | U | daily summaries with coverage |
| POST | `/terrariums/{id}/silences` | T | `{metricId?, untilUtc, reason}` (≤ 24 h) |
| DELETE | `/terrariums/{id}/silences/{silenceId}` | T | cancel early |
| POST | `/terrariums/{id}/exports` | U | `{format, from, to, metricIds?}` → job |

`readings` response shape:

```json
{
  "metric": "tempC", "unit": "°C", "bucket": "5min",
  "fromUtc": "2026-09-14T00:00:00Z", "toUtc": "2026-09-21T00:00:00Z",
  "points": [ { "t": "2026-09-14T00:00:00Z", "min": 27.1, "max": 29.4, "avg": 28.2, "count": 5 }, { "t": "…", "min": null, "max": null, "avg": null, "count": 0 } ],
  "alerts": [ { "id": 812, "severity": "Critical", "from": "…", "to": "…", "metric": "tempC" } ]
}
```
Gaps are `null` values with `count: 0` — never interpolated (FR-09 BR-09.5).

### 4.3 Species profiles

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/species-profiles` | U | built-ins + the user's own |
| POST | `/species-profiles` | O | custom profile; bands validated |
| POST | `/species-profiles/{id}/duplicate` | O | copy a built-in into an editable custom profile |
| PATCH | `/species-profiles/{id}` | O | built-ins return `403 builtin_immutable` |
| DELETE | `/species-profiles/{id}` | O | `409 profile_in_use` if assigned to a terrarium |

### 4.4 Devices

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/devices` | U | fleet list with status, firmware, RSSI, last seen, uptime |
| PATCH | `/devices/{id}` | O | rename, set sampling/publish interval (queues `set_config`) |
| POST | `/devices/{id}/rebind` | O | `{terrariumId}` — unbind + rebind, audited |
| POST | `/devices/{id}/rotate-secret` | O | returns a new secret once; old one valid for the 10-min grace window |
| POST | `/devices/{id}/revoke` | O | immediate disconnect + credential invalidation |
| POST | `/devices/{id}/calibration` | O | `{tempOffsetC?, rhOffsetPct?, luxGain?}` |
| POST | `/devices/{id}/commands` | O | `{type, parameters}` → queued, ack-tracked |
| GET | `/devices/{id}/commands/{cmdId}` | U | `queued` \| `sent` \| `acked` \| `failed` |
| POST | `/devices/{id}/snapshots` | Device | multipart JPEG ≤ 512 KB, ≤ 1 per 30 s (FR-17) |
| GET | `/devices/{id}/snapshots/latest` | U | metadata + short-lived URL |

### 4.5 Alerts, notifications, ops

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/alerts?state=&severity=&metric=&from=&to=` | U | paged, newest first |
| GET | `/alerts/{id}` | U | includes band, snapshot reference, value series excerpt |
| POST | `/alerts/{id}/ack` | T | `409 alert_not_open` if already resolved |
| POST | `/alerts/{id}/resolve` | T | `{reason: Recovered\|FalsePositive\|SensorFault\|Accepted, note?}` |
| GET | `/alerts/{id}/timeline` | U | trigger → escalation → ack → resolve with actors |
| GET | `/notifications?unreadOnly=true` | U | in-app inbox |
| POST | `/notifications/{id}/read`, `/notifications/read-all` | U | |
| GET | `/exports/{jobId}`, `/exports/{jobId}/download?token=` | U | token valid 24 h |
| GET | `/audit?entity=&entityId=&from=&to=` | O | own terrariums only |
| GET | `/health`, `/ready`, `/metrics` | A | no secrets; `/ready` → `503` when a dependency is down |
| GET | `/version` | A | `{api, schema, minFirmware}` |

---

## 5. Error model (RFC 7807 + stable `code`)

```json
{
  "type": "https://smartreptile.example/problems/threshold-ordering-invalid",
  "title": "Threshold band ordering is invalid",
  "status": 400,
  "code": "threshold_ordering_invalid",
  "detail": "TargetMin must be lower than TargetMax.",
  "traceId": "00-4bf92f…-01",
  "errors": { "targetMin": ["must be < targetMax"] }
}
```

| HTTP | Code | Meaning / typical cause |
|---|---|---|
| 400 | `validation_failed` | Field or schema problem (see `errors`) |
| 400 | `threshold_ordering_invalid` / `threshold_critical_invalid` | Band arithmetic violated |
| 400 | `schema_invalid` | Telemetry payload shape wrong |
| 400 | `payload_too_large` | > 32 KB batch or > 120 samples |
| 401 | `invalid_credentials` | Wrong username/password |
| 401 | `token_invalid` / `token_reused` | Expired, consumed, or replayed refresh token (family revoked) |
| 403 | `insufficient_role` | Role not permitted for the action |
| 403 | `builtin_immutable` | Attempt to edit a built-in species profile |
| 404 | `not_found` | Unknown id **or a foreign resource** (deliberate: no id probing) |
| 404 | `claim_code_invalid` | Unknown, expired or consumed claim code |
| 409 | `terrarium_already_bound` / `conflict_device_bound` | Binding conflicts |
| 409 | `alert_not_open` | Ack/resolve on a resolved alert |
| 409 | `version_conflict` | Optimistic concurrency (`rowversion` mismatch) |
| 409 | `profile_in_use` | Deleting an assigned profile |
| 422 | `quality_rejected` | Sample accepted but excluded from evaluation (informational) |
| 423 | `account_locked` | Login throttle active |
| 429 | `rate_limited` (+ `Retry-After`) | Per-endpoint limits (§6) |
| 503 | `dependency_unavailable` | DB or broker down (`/ready` semantics) |

**Rate limits**

| Scope | Limit |
|---|---|
| `/auth/login`, `/auth/refresh` | 10 / min / IP; 5 failures per username per 15 min |
| `/devices/self-register` | 1 / 5 min / IP; 20 / hour global |
| `/ingest/http` | 6 / min / device |
| `/devices/{id}/snapshots` | 1 / 30 s / device |
| `/terrariums/{id}/exports` | 5 / hour / user |
| All authenticated endpoints | 600 / min / user |

---

## 6. Real-time (SignalR, `GET /hubs/telemetry`)

| Event | Payload | Emitted when |
|---|---|---|
| `readingAdded` | `{terrariumId, sampleId, recordedAt, metrics:[{code,value,unit,qualityFlags}]}` | after a sample commits |
| `statusChanged` | `{terrariumId, deviceId, status, lastSeenAt}` | online/offline/maintenance transitions |
| `alertChanged` | `{terrariumId, alertId, state, severity, metric, triggeredAt, resolvedAt?}` | open/escalate/ack/resolve |
| `commandChanged` | `{deviceId, cmdId, status}` | command ack/failure |

Client methods: `JoinTerrarium(terrariumId)`, `LeaveTerrarium(terrariumId)` — **membership is authorised on
join**, otherwise the hub would become a cross-tenant leak (`03-implementation/03` §7).

**Client obligations:** re-subscribe after reconnect, and re-fetch `readings/latest` before resuming the stream
so a reconnected socket never leaves pre-disconnect values on screen (UC-02, `03-implementation/05` §3).

---

## 7. Compatibility and versioning rules

1. Path versioning (`/api/v1`). Breaking changes require `/api/v2`; additive fields do not.
2. Clients must ignore unknown JSON fields.
3. A metric may be added at any time; an existing metric's **meaning or unit never changes silently** — a new
   `Metric.Code` is introduced and the old one is deprecated instead (migration rule 3, `02-design/02` §7).
4. The firmware's minimum supported version is published at `/version` (`minFirmware`); older nodes are flagged
   in the fleet view but still accepted in v1 (no OTA, limitation L-03).
5. `Error.code` values are part of the contract: adding one is safe, changing one is a breaking change and the
   app's message map (`core/result.dart`) must be updated in the same PR.
