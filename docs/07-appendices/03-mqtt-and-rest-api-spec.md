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
31 symbols, excluding `0 O 1 I L` to avoid transcription errors:
`23456789ABCDEFGHJKMNPQRSTUVWXYZ` → codes are 8 chars, typically displayed as `XXXX-XXXX`.
Entropy ≈ 31⁸ ≈ 8.5 × 10¹¹ (≈ 39.6 bits); TTL 15 min; single-use; regenerated while the device stays in provisioning mode.

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
Errors: `400 registration_invalid` (a missing or malformed `chipId`/`macAddress`), `409 device_already_registered`
(returns the existing `deviceId` and a fresh code only if the device is still unclaimed), `429 rate_limited`.

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
`secret` is returned **once**; it is never retrievable again. Errors: `401 unauthenticated`,
`403 insufficient_role`, `404 claim_code_invalid` (unknown, expired, consumed or superseded — deliberately
indistinguishable), `404 not_found` (unknown **or foreign** terrarium, also indistinguishable so ids cannot be
probed — `BR-02.2`), `409 terrarium_already_bound`.

A revoked device stops occupying its terrarium — `IX_Device_TerrariumId` is filtered to `Status <> 3` — so its
replacement is claimed into the same terrarium without a rebind step.

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

The ACL is enforced at the broker, before a message is stored: a device publishes only on
`telemetry`/`health`/`status`/`events`/`ack` and subscribes only to its own `cmd`, with no wildcard. A refused
**subscription** is answered with a failure code and simply not applied; a refused **publication** is dropped and
that client's session is closed, because a device reaching outside its own prefix is either broken or hostile.
Refusals are counted (`mqtt_refused_subscriptions_total`, `mqtt_refused_publications_total`) and logged with the
reason.

Every payload on every channel is a JSON object carrying `deviceId`. That id must equal the one the transport
authenticated — the topic's — exactly as rule V-04 requires of a batch on the telemetry channel, and a payload that
names another device is refused under `schema_invalid` rather than trusted. Each channel's payload is otherwise the
object below its own heading, plus these two optional members:

| Member | Rule |
|---|---|
| `at` (or `ts`) | ISO-8601 UTC device instant. **Optional**: a node whose clock has not synced still reports, and the server stamps the row with the arrival time instead (NFR-10) |
| `fw` | Firmware version, ≤ 16 chars, denormalised onto the device row the way a batch's `fw` is |

```json
// sr/v1/d/{id}/health — the health object of §3.2, without the samples around it
{"deviceId":"sr-3f9a2c","rssi":-63,"up_s":86400,"heap_kb":142,"bat":null,"src":"mains","fw":"1.0.0","at":"…"}
```
A health field that is absent leaves the device's stored value alone, exactly as it does inside a batch: reporting
only `rssi` is not reporting "no battery".

**Consumed since 2026-10-09** (roadmap task 2.4's "only telemetry is forwarded" gap). `telemetry`, `health`,
`status` and `events` all reach the ingest worker; `ack` does not, because command results are FR-14's and accepting
one would acknowledge a command this build cannot issue. A payload the pipeline cannot use — malformed JSON, a
`status` outside its three words, an event `type` outside the closed vocabulary — is refused under `schema_invalid`
and counted in `ingest_rejected_total`, so a firmware typo is visible rather than becoming a new category.

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
| `seq` | int64 | ✓ | strictly increasing per device; the **first** sample of the batch. The samples that follow continue from there — sample *i* is stored under `seq + i` — because that is what makes `(DeviceId, seq)` the dedupe key (V-04/DI-02). A device that publishes batches of three therefore advances this counter by three, not by one |
| `fw` | string | ✓ | ≤ 16 chars |
| `ts` | string | ✓ | ISO-8601 UTC (`Z`); the instant of `t = 0` |
| `samples[]` | array | ✓ | 1…120 items |
| `samples[].t` | int32 | ✓ | seconds offset from `ts`, strictly increasing, ≤ 86 400 || `samples[].tf` | float | ✓ | filtered air temperature, °C |
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

**Built 2026-10-06** (`Api/Endpoints/IngestEndpoints.cs`). Three things about it are worth knowing before writing
a device against it:

| Situation | Answer | Why that answer |
|---|---|---|
| Batch stored | `202 {accepted, duplicates, rejected: 0, reasons: []}` | The counts are the pipeline's own outcome, not a guess, which is what lets a device **clear its ring buffer** after an outage instead of re-sending until someone looks. The status stays `202` because this contract says so and the firmware is written against it |
| Same batch again | `202 {accepted: 0, duplicates: N}` | Idempotency is `(DeviceId, Sequence)` in the database, not the `Idempotency-Key` header — so a re-delivery is a success and the header is optional. A device that lost the response can simply send the batch again |
| Credential missing, malformed, wrong, or naming a device the payload does not | `401 auth_failed`, **one** body for all four | Two distinguishable answers would tell a caller which public ids exist (BR-02.2). The specific reason (revoked, unbound, secret mismatch) reaches the operator through the log, never the device |
| Payload unparseable, or over the 32 KB limit | `400 schema_invalid` / `400 payload_too_large`, message describing the payload | These describe data the caller sent, so echoing them leaks nothing. A 4xx is the contract with a back-filling device: **do not re-send this payload**, it will fail identically |

`rejected` and `reasons` are always `0` and `[]` today, and not because nothing is ever refused: the validator
decides a batch all-or-nothing, so a refusal leaves through the `4xx` rows above and the `202` body only ever
describes a batch that was stored. The fields stay because the contract has them; a partial-acceptance path is the
only thing that could fill them.

**The per-device rate limit is a fairness rule, not a defence.** Its partition key is the device id from a
caller-supplied header, so a hostile caller can mint a fresh budget per request by varying it. What actually bounds
that caller is that every forged id still costs a parse and a failed device lookup, plus the address-keyed policy
on the auth group. It is per device rather than per address on purpose: two boards behind one home router are two
budgets, and a device coming back from an outage is the one that needs to back-fill.

---

## 4. REST API reference

Legend: `A` = anonymous, `U` = any authenticated role, `T` = Technician or Owner, `O` = Owner only.
All timestamps are ISO-8601 UTC. All list endpoints support `?cursor=&pageSize=` (default 50, max 200) and
return `nextCursor` — as built, `GET /alerts` is the one endpoint that does; `GET /terrariums` says why it does not
(§4.2).

### 4.1 Auth

| Method | Path | Role | Body / params | Success | Errors |
|---|---|---|---|---|---|
| POST | `/auth/register` | A | `{username, email, password}` | `202 {status, recoveryCode}` | `400 registration_invalid`, `409 registration_conflict` |
| POST | `/auth/login` | A | `{username, password}` | `200 {accessToken, refreshToken, expiresIn, user}` | `401 invalid_credentials`, `423 account_locked` |
| POST | `/auth/refresh` | A | `{refreshToken}` | `200 {accessToken, refreshToken}` | `401 token_invalid`, `401 token_reused` |
| POST | `/auth/logout` | U | `{refreshToken}` | `204` | — |
| GET | `/auth/me` | U | — | `200 {user, preferences}` | `401` |
| PATCH | `/auth/me` | U | preferences, timezone, language | `200` | `400 validation_failed` |
| POST | `/auth/change-password` | U | `{currentPassword, newPassword, refreshToken}` | `204` (every **other** session's refresh tokens revoked; the one named by `refreshToken` survives — `ADR-022`) | `400 password_policy_violation`, `401 invalid_credentials`, `429 account_locked` |
| POST | `/auth/recover` | A | `{usernameOrEmail, recoveryCode, newPassword}` | `200 {recoveryCode}` | `400 password_policy_violation`, `401 invalid_recovery_code`, `429 account_locked` |
| POST | `/auth/forgot-password` | A | `{usernameOrEmail}` | `202` (empty body) | `404 identifier_unknown`, `429 rate_limited` |
| POST | `/auth/reset-password` | A | `{usernameOrEmail, resetCode, newPassword}` | `200 {recoveryCode}` | `400 password_policy_violation`, `401 invalid_reset_code`, `429 account_locked` |

**Where a code comes from, and why there are two.** Registration is the only moment the server can hand the keeper
something without a delivery channel, so it returns a **backup recovery code** — 20 characters from the same
31-symbol alphabet as claim codes (§2.1), stored as `SHA-256(code ‖ 16-byte salt)`, shown once and never
retrievable. Recovery with it (`/auth/recover`) consumes the code, issues a replacement and ends every session.

The three server-issued rows are the path for a keeper who no longer has that code. `/auth/forgot-password` mints a
single-use code that expires after `PasswordReset:CodeMinutes` (default 30) and hands it to
`IPasswordResetNotifier`; `/auth/reset-password` spends it, rotates the backup code and ends every session. The
code is stored **unsalted** (SHA-256 of the presented value) for the same reason a refresh token is: the row has to
be findable by the value presented, and the code is high-entropy and short-lived, so there is nothing to brute-force.

`/auth/forgot-password` answers `202` with **no body** when it issued a code, and `404 identifier_unknown` when no
account uses the identifier. That difference is deliberate (`ADR-020`): the non-disclosing version of this endpoint
left a keeper who had mistyped their address waiting for a code that was never generated, and the address they typed
is their own. Login, `recover`, `reset-password` and every terrarium route stay non-disclosing, so the account
enumeration this endpoint now allows is available nowhere else in the API. Both reset paths are throttled exactly
like login (5 per identifier / 20 per address per 15 min) and answer every *code* failure with the single opaque
`invalid_reset_code` / `invalid_recovery_code` — never "no such account", and never a difference between a spent
code, another account's code and a code that never existed. Where the code goes is a deployment decision: the demo
has no mail server, so `LogPasswordResetNotifier` writes it to the server log when `PasswordReset:LogCode` is true
(development only) and otherwise says plainly that it reached nobody (limitation L-02).

**Built so far (2026-10-06):** every row above except `PATCH /auth/me` is implemented. Three cells still describe
the intended shape rather than the built one: `register` answers `202 {status, recoveryCode}` (not `201`), the
session body carries `accessTokenExpiresAtUtc`/`refreshTokenExpiresAtUtc` instead of `expiresIn`, and a lockout is
`429 account_locked` (not `423`) because it is produced by the same throttle that answers `429 rate_limited`. The
registration errors are a `400 registration_invalid` that carries `errors[]` per field, and a taken identifier is the
`409 registration_conflict` the table always asked for (`ADR-020`, built 2026-10-07). `03-implementation/03` §6 is
the as-built table.

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
| GET | `/terrariums/{id}/silences` | T | windows still in force — an addition, §4.5 |
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
Gaps are `null` values with `count: 0` — never interpolated (FR-09 BR-09.5). A bucketed series therefore carries
every bucket in the window, empty ones included. A `raw` series carries one point per sample instead: a sample's own
timestamp is its bucket, so a gap shows up as absent points rather than as nulls.

**Built so far (roadmap 2.8, 3.1, 3.4).** `GET /terrariums`, `POST /terrariums`, `GET /terrariums/{id}`,
`GET /terrariums/{id}/readings/latest`, `GET /terrariums/{id}/readings` and `GET /terrariums/{id}/coverage`; plus,
since 2026-10-09, `PATCH /terrariums/{id}` and `DELETE /terrariums/{id}` — FR-03's update and delete halves — and
`GET /terrariums/{id}/thresholds` — 3.1's read half. Since 2026-10-10 the three silence routes are built too (3.4;
see §4.5), including one this table did not have: `GET /terrariums/{id}/silences`, because the contract asks for a
silence to be "visible on the dashboard" and the client has no other way to learn the id it must cancel. The rest of
this table — threshold write, summaries, exports — is specified and not built; the threshold write half is 3.1's
remainder and summaries are 3.6.

**`GET /terrariums/{id}/thresholds` as built.** Authenticated, not role-gated (`U`), scoped to the caller like
every other route here, and read-only:

```json
{
  "terrariumId": "…",
  "capturedAtUtc": "2026-10-09T14:20:11Z",
  "timeZoneId": "Asia/Ho_Chi_Minh",
  "effectiveThresholds": [
    { "metric": "humidityPct", "unit": "%RH", "phase": "any", "source": "profile",
      "targetMin": 60.0, "targetMax": 80.0, "criticalMin": 40.0, "criticalMax": 95.0,
      "dwellWarnMinutes": 15, "dwellCritMinutes": 2, "recoveryMargin": 3.0 }
  ]
}
```

Three shape rules worth knowing before wiring the editor. **The entry list is the resolved set, not the
dictionary**: a metric with no band for the phase in force is *absent*, which is exactly how the editor tells
"unconfigured" from "configured wrong" — absent is not an error. **`source` is the provenance, `phase` is the
instant**: the same terrarium read at 21:00 local reports `phase: "night"` and the night band, because the
response is a statement about now rather than about configuration. **`capturedAtUtc` and `timeZoneId` are part of
the answer** so a phase-dependent result can be reproduced instead of argued about. Entries are ordered by metric
key (`humidityPct` before `tempC`), matching `readings/latest`, so rows keep their places between reloads.

**`PATCH /terrariums/{id}` requires `If-Match`.** `GET`/`POST`/`PATCH` return the row's SQL Server `rowversion` as
a strong `ETag`, and the update must send it back:

| Situation | Answer |
|---|---|
| `If-Match` matches the stored `rowversion` | `200` with the updated item and the **new** `ETag` |
| `If-Match` is stale, or the row moved between the read and the write | `412 precondition_failed` |
| `If-Match` is absent or unparseable | `428 precondition_required` |
| `If-Match: *` | accepted — existence is enough, and the token is not compared |
| A provided field fails validation | `400 validation_failed` with field-level `errors[]`, **after** the precondition check |

The body is a partial update: an **omitted** member is left unchanged, and `location`/`description` sent as an empty
string clear the field — the one way a JSON body can say "clear it" once it has been bound to a record. The
validation codes are `POST`'s (`terrarium_name_required`, `terrarium_name_too_long`, `terrarium_location_too_long`,
`terrarium_description_too_long`, `terrarium_timezone_invalid`, `species_profile_not_found`). The precondition is
checked before the body because a stale client should be told to reload, not handed a validation report about a
version of the terrarium it is not looking at.

**`DELETE /terrariums/{id}` is a soft delete** — readings, alerts and summaries survive — and it refuses to remove
an enclosure a live device is still reporting into: `409 conflict_device_bound` unless the request carries
`?allowUnboundDevice=true`. The flag's name counts the device, not the terrarium: passing it is the caller's
explicit permission to detach the board, which returns to `Provisioning` with its credentials and owner intact so the
same account can still rotate or revoke it. A *revoked* device has already released the slot (the DI-04 filtered
index excludes it), so it does not make the caller ask twice. `204` on success, `404 not_found` for a missing or
foreign id. Neither route writes an audit row: `BR-18.4` does not name terrarium edits and `02-design/02` §3.18's
closed vocabulary has no `terrarium.*` verb.

`GET /terrariums` answers `{ "items": [ … ] }` and is **not paginated**: the per-account count is small and the
`?cursor=&pageSize=` convention in the legend above is not implemented yet, so no `nextCursor` is returned.

Bucket selection follows BR-09.1, with the width capped so the point budget (NFR-02) cannot be broken by a wide
window or by an unaligned `from`:

| Requested width | `bucket` | Worst case |
|---|---|---|
| ≤ 6 h | `raw` | one point per sample; a series over 720 points degrades to `5min` |
| ≤ 48 h | `5min` | 576 points |
| ≤ 30 d | `hourly` | 720 points |
| > 30 d | — | refused `400 range_too_large` |

`metric` is required and must be a metric-dictionary key, otherwise `400 metric_invalid`. `from` and `to` are
required ISO-8601 instants with `from < to`, otherwise `400 invalid_range`.

`readings/latest` response:

```json
{
  "terrariumId": "6ec11ae9-…",
  "lastSampleAt": "2026-10-06T14:56:57.092Z",
  "device": { "deviceId": "sr-e2e01", "deviceName": "E2E node", "status": "online",
              "firmwareVersion": "0.1.0-test", "lastSeenAt": "2026-10-06T14:56:57.092Z",
              "samplingIntervalSec": 60, "signalStrengthDbm": -61, "batteryPct": 87.5, "uptimeSeconds": 4200 },
  "metrics": [ { "code": "tempC", "value": 35.0, "unit": "°C", "capturedAt": "2026-10-06T14:56:57.092Z",
                 "status": "Critical", "qualityFlags": 0, "target": { "min": 24.0, "max": 28.0 } } ]
}
```

- `status` is one of the `02-design/04` §5 vocabulary values (`InRange`, `OutOfRange`, `Critical`, `Unavailable`,
  `Maintenance`) and is an **instantaneous** judgement of the value against the effective band. Dwell, hysteresis,
  dedupe and alert rows are the evaluator's (FR-11), so an `OutOfRange` reading does **not** imply an alert exists.
- `target` is the band the value was judged against: a per-terrarium override where one exists, otherwise the
  species profile's band for the **phase** the sample fell in (BR-10.3, BR-11.2). It is `null` when the metric has
  no configured band.
- `Unavailable` covers a faulted or implausible sample (quality bits 1 and 2, BR-11.1) *and* a value outside the
  metric's plausible range even when no bit was set; `Maintenance` covers a device in maintenance (BR-12.6).
- A metric with no stored reading is **omitted** rather than sent with a `null` `capturedAt`, because both clients
  parse that field unconditionally.
- `device.status` is derived, not echoed: a device unheard from for three sampling intervals reads `offline`
  (FR-07 BR-07.2), since the silence watchdog is not built yet. `device` is `null` when nothing is claimed.

`coverage` response:

```json
{ "terrariumId": "6ec11ae9-…", "fromUtc": "2026-10-06T13:58:05Z", "toUtc": "2026-10-06T14:58:05Z",
  "samplingIntervalSec": 60, "expectedSamples": 60, "receivedSamples": 3, "coveragePct": 5 }
```

`expectedSamples` is the window divided by the bound device's sampling interval and `coveragePct` is `received` over
`expected`, capped at 100. A terrarium with no bound device answers `409 device_not_bound`: coverage is a statement
about a device, and there is nothing honest to say about `null`.

`POST /terrariums` takes `{name, speciesProfileId, location?, description?, timeZoneId?}` and answers `201` with the
item shape below. It is `Owner`-gated and exists ahead of the rest of FR-03 because every other route needs a
terrarium to point at — `POST /devices/claim` has nothing to bind a board to until one exists. Validation is
`400 validation_failed` with field-level `errors[]` (`terrarium_name_required`, `terrarium_location_too_long`,
`terrarium_description_too_long`, `terrarium_timezone_invalid`, `species_profile_not_found`); `timeZoneId` falls back
to `Defaults:TimeZoneId` when omitted.

The item shape returned by `GET /terrariums` (inside `items`), `POST /terrariums` and `GET /terrariums/{id}`:

```json
{
  "id": "6ec11ae9-…", "name": "E2E gecko box", "speciesProfileId": "…", "speciesName": "Arid (desert)",
  "location": "desk", "description": "end-to-end check", "timeZoneId": "Asia/Ho_Chi_Minh",
  "createdAt": "…", "updatedAt": "…",
  "device": { "…": "the DeviceSummary above, or null when nothing is claimed" },
  "latestSampleAt": "…", "openAlertCount": 0
}
```

`openAlertCount` reads 0 until the evaluator writes alert rows (FR-11). A terrarium that does not exist, is
soft-deleted, or belongs to another account answers `404 not_found` — identically in all three cases, so ids cannot
be probed (BR-02.2).

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

**As built (roadmap 3.4, 2026-10-10).** Every alert route above and all three silence routes are implemented;
notifications, exports and `/audit` are 3.5, 3.7 and the export task. Eight readings this build settles, stated here
because they are contract rather than implementation:

- **`GET /alerts` is the first endpoint to implement the documented paging convention** (`?cursor=&pageSize=`,
  default 50, max 200, `nextCursor`). The cursor is opaque — base64 of `triggeredAt|id` — and the order is
  `TriggeredAt DESC, Id DESC`: newest first by *when the excursion started*, so a back-filled alert appears where it
  happened, not where the system learned of it. The pair is what makes the order total; an identity alone would not
  say which of two alerts that share an instant came first.
- **Filters are the contract's four**, and their vocabulary is the response's: `state` is `Open`, `Acknowledged` or
  `Resolved`, `severity` is `Info`, `Warning` or `Critical` (case-insensitive), `metric` is the REST key (`tempC`),
  and `from`/`to` filter on `TriggeredAt`. An unknown value is a `400` naming the alternatives rather than an empty
  page.
- **The detail carries no snapshot reference**, although the table above lists one: `Alert.ThresholdSnapshotId`
  exists in `07-appendices/02` §3.8's sketch and in no built table, so a field named after it would promise
  something the database cannot keep. The band the alert denormalises is what explains it today, and the reference
  arrives with 3.1's remaining half.
- **The series excerpt is capped at the newest 240 readings** of the episode, oldest first, with `seriesTruncated`
  when the episode was longer — an hour at the default 15-second interval, which is what "excerpt" can mean without
  a cap that depends on the deployment. A device-level alert (silence, sensor fault, clock skew) is not about a
  value and carries an empty series.
- **The resolution note lives on the audit entry, not on the alert.** The contract has `{reason, note?}` while the
  alert row has five fields that would have to grow a sixth; `Alert.Message` is reserved for render-time text
  (`07-appendices/02` §3.8), so the note is written into the `alert.resolved` audit row and read back by the
  timeline. The reason stays a column, because it is machine-readable and the report groups by it.
- **A second acknowledgement is `409 alert_not_open`**, the same answer a resolved alert gives: in both cases the
  request changed nothing, and one code is one thing for a client to handle. Resolution is allowed straight from
  `Open` and deliberately does not fabricate an acknowledgement — the timeline should say what happened.
- **Resolving a threshold alert re-arms its dwell key.** The alert is closed, the `EvaluationState` window for
  `(terrarium, metric, phase)` is cleared, and the alert may only return after the band has been left for the dwell
  again. Without that, a value that never came back into band would re-open the very next sample, and a keeper who
  chose `Accepted` would be told the same thing one interval later. A longer quiet period is what a **silence** is
  for; the two features are deliberately different mechanisms (`02-design/05` §5).
- **The timeline's vocabulary is the hub event's** — `opened`, `acknowledged`, `resolved` — with the actor, the
  reason and the note. Escalation entries are absent until 3.5, exactly as the design arranges them: §02-design/05
  §6 records escalations as `NotificationLog` rows carrying the alert id, and the alert row keeps no escalation
  instant because escalation edits it in place (one row per episode is what DI-01's unique index is for).

**Silences as built.** `POST` answers `200` with the created window rather than `201` with a `Location`, because
there is no read-by-id route to point at; `GET` returns only the windows that are still in force, since an expired
one is history and history is the audit trail's job. `metric` may be omitted, and `null` means **every metric of the
terrarium including its device-level alerts** — a keeper going away for the weekend silences the box, not five
metrics one at a time. The cap is 24 hours and the reason is mandatory, both from §02-design/05 §5, and
`DELETE` is idempotent: a second cancel changes nothing, writes no second audit row and still answers `204`, because
a `DELETE` that failed on a retry would leave a keeper believing a suppression was still in force. A silence
suppresses **notification and not detection** — alerts are still raised, counted, touched and resolved during one,
and the dispatcher of 3.5 reads the same `IsActiveAt`/`Covers` rule that the list endpoint renders.

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
| 400 | `password_policy_violation` | New password failed the policy (see `errors`) |
| 400 | `registration_invalid` | Username, email or password failed validation (see `errors`) |
| 400 | `malformed_request` | The body could not be read as JSON for this endpoint — nothing was processed |
| 401 | `invalid_credentials` | Wrong username/password |
| 401 | `auth_failed` | Device credential missing, malformed, wrong, revoked, unbound, or naming a different device than the payload — **one** answer for all of them, on `/ingest/http` only |
| 401 | `invalid_recovery_code` | Wrong, spent, malformed or account-less backup recovery code — one answer for all of them |
| 401 | `invalid_reset_code` | Wrong, spent, expired, foreign or account-less reset code — one answer for all of them |
| 401 | `token_invalid` / `token_reused` | Expired, consumed, or replayed refresh token (family revoked) |
| 403 | `insufficient_role` | Role not permitted for the action |
| 403 | `builtin_immutable` | Attempt to edit a built-in species profile |
| 404 | `not_found` | Unknown id **or a foreign resource** (deliberate: no id probing) |
| 404 | `identifier_unknown` | No account uses that username or email — `/auth/forgot-password` only, a deliberate disclosure (`ADR-020`) |
| 404 | `claim_code_invalid` | Unknown, expired or consumed claim code |
| 409 | `terrarium_already_bound` / `conflict_device_bound` | Binding conflicts |
| 409 | `registration_conflict` | Username and/or email already has an account (see `errors` for which) |
| 409 | `alert_not_open` | Ack/resolve on a resolved alert |
| 409 | `version_conflict` | Optimistic concurrency (`rowversion` mismatch) |
| 409 | `profile_in_use` | Deleting an assigned profile |
| 413 | `request_too_large` | The transport refused to read the body (an in-app limit such as `[RequestSizeLimit]`). Kestrel answers its own 30 MB default itself, before the application sees the request, so this row is defensive. **Not** the same as `400 payload_too_large`, which is the ingest endpoint's own batch limit |
| 422 | `quality_rejected` | Sample accepted but excluded from evaluation (informational) |
| 423 | `account_locked` | Login throttle active |
| 429 | `rate_limited` (+ `Retry-After`) | Per-endpoint limits (§6) |
| 503 | `dependency_unavailable` | DB or broker down (`/ready` semantics) |

**Rate limits**

| Scope | Limit |
|---|---|
| `/api/v1/auth/*` credential routes (`register`, `login`, `change-password`, `recover`, `forgot-password`, `reset-password`) | 10 / min / IP; 10 failures per username per 15 min, 20 per IP per 15 min (login and both reset paths share the counters) |
| `/auth/me`, `/auth/refresh`, `/auth/logout` (session lifecycle) | 60 / min / IP — a signed-in client reads its profile on load, rotates a 15-minute token and ends a session, so these are housekeeping rather than credential guessing |
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
join**, otherwise the hub would become a cross-tenant leak (`03-implementation/03` §7). A refused join throws a
`HubException` carrying one message for both "not yours" and "does not exist", so the hub cannot be used to find
out whether an id exists (BR-02.2).

**Emitted so far.** `readingAdded` (after a sample commits, since 2.9), `statusChanged` — from the device's own
`status` topic, so the transition is the device's declaration rather than an inference from silence (2026-10-09) —
and, since 2026-10-10, `alertChanged` (roadmap 3.4). What is **not** pushed yet is the `Provisioning → Online`
transition a sample or health message causes: the fan-out boundary for samples carries committed readings, which have
no status in them. `commandChanged` arrives with FR-14.

**`alertChanged` as built.** One event per move, pushed to the terrarium's group **after the commit that wrote it**,
by whichever layer owns that commit: the evaluator's worker, the silence watchdog's worker, the ingest outcome
recorder (the clock-skew entry and the sensor fault) and the lifecycle API itself. `event` is one of `opened`,
`escalated`, `acknowledged`, `resolved` — lower case, because it names a move — while `state`, `severity` and
`metric` keep the REST surface's vocabulary (`Open`/`Acknowledged`/`Resolved`, `Info`/`Warning`/`Critical`, `tempC`),
so one client parsing `GET /alerts` and one handling this event are reading the same words. A touch is deliberately
**not** announced: the documented event reports lifecycle moves, and re-sending the same open alert once per sample
would be noise rather than news. A push that fails is logged and dropped, never retried into a failed request — the
row is committed, so a SignalR outage is a lost push.

**Auth:** the JWT travels in the query string (`/hubs/telemetry?access_token=…`), because a browser cannot set a
header on the WebSocket handshake. It is accepted on `/hubs` only — nowhere else does a token belong in a URL.

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
