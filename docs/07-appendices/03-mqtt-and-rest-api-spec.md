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
| Device (self-register / claim) | self-register is anonymous + rate limited; claim requires a user JWT holding `device.provision` (§4.4) |

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
Requires the `device.provision` permission (§4.4).

```http
POST /api/v1/devices/claim
Authorization: Bearer <jwt>

{ "claimCode": "K7M2-QP4T", "terrariumId": "6f1c…", "deviceName": "Node bể gecko" }
```
```http
200 OK
{ "deviceId": "sr-3f9a2c", "secret": "MFRGGZDFMZTWQ2LK…", "terrariumId": "6f1c…", "boundAtUtc": "…" }
```
`secret` is returned **once**; it is never retrievable again. `deviceName` is optional (2026-10-23): omitted keeps
the label the board already carries, an empty string restores the default `New node`, and anything longer than 60
characters is refused with `400 claim_invalid` / `errors[0].code = device_name_too_long` before anything is written.
Errors: `401 unauthenticated`,
`403 insufficient_role`, `404 claim_code_invalid` (unknown, expired, consumed or superseded — deliberately
indistinguishable), `404 not_found` (unknown **or foreign** terrarium, also indistinguishable so ids cannot be
probed — `BR-02.2`), `409 terrarium_already_bound`.

A revoked device stops occupying its terrarium — `IX_Device_TerrariumId` is filtered to `Status <> 3` — so its
replacement is claimed into the same terrarium without a rebind step.

### 2.4 `POST /api/v1/devices/claim-by-mac` (2026-10-14, TASKS item 8)

Requires the `device.provision` permission (§4.4). The **lower-trust** pairing path: a claim code proves the caller has the board in front of them —
it is shown on its screen and lives for minutes — while a MAC is printed on the case, broadcast on every network the
board joins and guessable in bulk. It exists so a keeper holding a board whose screen has failed is not stuck, and it
is confined to a board that has already self-registered and has no owner yet.

```http
POST /api/v1/devices/claim-by-mac
Authorization: ******

{ "macAddress": "A0-B1-C2-D3-E4-F5", "terrariumId": "6f1c…", "deviceName": "Node bể gecko" }
```

The address is normalised before it is looked up — `AA:BB:CC:DD:EE:FF`, `aa-bb-cc-dd-ee-ff`, `AABB.CCDD.EEFF` and
bare hex all name the same board — and the comparison against the stored form is case-insensitive.

**The address must also be one a board can own (2026-10-23).** An ESP32 reports its station address through
`WiFi.macAddress()`: 48 bits, **unicast** (the I/G bit — the low bit of the first octet — is clear), never the
broadcast address and never all zeros. The same rule is applied everywhere a MAC enters the system —
`self-register` (which also stores the canonical spelling), `claim-by-mac` and the `macAddress` field of
`POST /terrariums` — and a value that breaks it answers `400 registration_invalid` with
`errors[0].code = mac_invalid`; the message names the reason:

| Value | Why it is refused |
|---|---|
| not twelve hex digits | `Malformed` — a truncated address could match a different board instead of none |
| `FF:FF:FF:FF:FF:FF` | `Broadcast` — every station on the segment, so no station |
| `00:00:00:00:00:00` | `AllZero` — what a chip reports before Wi-Fi starts, not an identity |
| `01:…`, `33:…` (odd first octet) | `Multicast` — a group address, delivered to many stations |

The reply is
`claim`'s body, because the act is the same: `{deviceId, secret, terrariumId, boundAtUtc}` with the secret returned
once. Other errors: `404 device_not_found` — **one** answer for an unknown address, a board already bound elsewhere
and a revoked board, so the route cannot be used to test whether an address belongs to this fleet — `404 not_found`
for an unknown or foreign terrarium, `409 terrarium_already_bound`. The audit row is `device.claimed` with
`"method":"mac_address"`; a code claim carries `"method":"claim_code"`, so the trail can tell the two apart.

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

Legend: `A` = anonymous, `U` = an authenticated account with no permission beyond its token (the
`AuthorizationPolicies.Authenticated` policy, §4.1), and every other cell names the **permission code** the route
requires — `terrarium.read`, `terrarium.write`, `device.read`, `device.write`, `device.provision`, `alert.read`,
`alert.write`, `notification.read`, `audit.read`, `species.read`, `species.write`, `user.read`, `user.manage`, plus the
two **cross-account** codes `data.read.any` and `data.write.any` (§4.8). A code is a row of the permission catalogue,
not a role: the product ships four roles — `Admin`, `Owner`, `Technician`, `Viewer` — each a named set of these codes,
and a deployment may re-band them through the console, so this file names the capability the route needs and
`02-design/06` §3 is the catalogue (its §3.1, the matrix).
All timestamps are ISO-8601 UTC. All list endpoints support `?cursor=&pageSize=` (default 50, max 200) and
return `nextCursor` — as built, `GET /alerts` is the one endpoint that does; `GET /terrariums` says why it does not
(§4.2).

### 4.1 Auth

| Method | Path | Role | Body / params | Success | Errors |
|---|---|---|---|---|---|
| POST | `/auth/register` | A | `{username, email, password}` | `202 {status, recoveryCode}` | `400 registration_invalid`, `409 registration_conflict` |
| POST | `/auth/login` | A | `{usernameOrEmail, password}` | `200 {accessToken, refreshToken, expiresIn, user}` | `401 invalid_credentials`, `423 account_locked` |
| POST | `/auth/refresh` | A | `{refreshToken}` | `200 {accessToken, refreshToken}` | `401 token_invalid`, `401 token_reused` |
| POST | `/auth/logout` | U | `{refreshToken}` | `204` | — |
| GET | `/auth/me` | U | — | `200 {user, preferences}` | `401` |
| PATCH | `/auth/me` | U | preferences, timezone, language | `200` | `400 validation_failed` |
| DELETE | `/auth/me` | U | — | `204` (account disabled, every session ended) | `401` |
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

**Profile read and edit (2026-10-14, TASKS item 9a).** `PATCH /auth/me` and `DELETE /auth/me` are built; the profile
the two `GET`/`PATCH` answers carry is `{id, username, email, role, roleId, permissions, preferredLanguage, timeZoneId,
quietHoursStart, quietHoursEnd, minNotifySeverity, channelFcmEnabled, channelEmailEnabled, hasFcmToken}`. `PATCH` is a patch: an
omitted member keeps its value. `preferredLanguage` is `vi` or `en` (ADR-013), `timeZoneId` must come from
`GET /api/v1/time-zones` and is stored in that catalogue's spelling, and quiet hours are a **pair** of `HH:mm` values
that may cross midnight — one end alone is `400 profile_invalid` with `errors[0].code = quiet_hours_incomplete`,
because half a window says nothing about when notifications are suppressed; an empty pair clears it. `fcmToken` is
accepted and cleared with an empty string, and is never echoed back: the response carries `hasFcmToken` instead,
since the value is a delivery address the client already owns. Username, email and password are deliberately absent
— the first two are the account's identifiers, with their own uniqueness and recovery consequences, and the password
has its own route because it re-proves the old one. `DELETE /auth/me` **disables** the account and ends every session
it holds, so a later sign-in attempt is refused; the row stays, because the terrariums, alerts, readings and audit
rows that name it do — a purge is a retention decision with a policy attached, and item 12 keeps it out of v1.

**Roles became permissions (2026-10-17).** `POST /auth/login` is unchanged in what it asks for — the password — and the
session body, `GET /auth/me` and `PATCH /auth/me` now answer `roleId` and `permissions` alongside `role`: the codes the
account's role currently grants, in catalogue order. The access token carries one `perm` claim per code, plus `role`
**for display only** — nothing authorises on it — and `ver` = `2`. A client gates its screens on the codes
(`hasPermission('terrarium.write')`), never on a role name, because a deployment may re-band a role without a release
(`02-design/06` §3). **A role change, a password reset and a delete end the account's refresh tokens**, so the next
refresh fails rather than silently keeping the old rights; an edit to a role's *permission set* does not end them, and
takes effect within one 15-minute access-token lifetime.

### 4.2 Terrariums, thresholds, readings

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/terrariums` | `terrarium.read` | list with device status summary; `?speciesProfileId=` narrows it to one profile's enclosures |
| POST | `/terrariums` | `terrarium.write` | `{name, speciesProfileId?, location?, description?, timeZoneId?, deviceId?, macAddress?}`; the created item carries `rowVersion`, and `deviceSecret` when a board was bound. `speciesProfileId` is **optional** (2026-10-25): omitted or null creates an enclosure with no species, which stores and shows readings but has no bands to be judged against. `data.write.any` creates an **unowned** enclosure (`ownerUserId`/`ownerEmail` null) — assign it on `PATCH` |
| GET | `/terrariums/{id}` | `terrarium.read` (member) | includes `device`, `openAlertCount`, `latestSampleAt`, `rowVersion` |
| PATCH | `/terrariums/{id}` | `terrarium.write` | `If-Match: <rowversion>` for optimistic concurrency, required; the item answers the new `rowVersion`. `{ownerUserId}` reassigns the enclosure and is **administrator-only** (`data.write.any`), audited as `terrarium.owner_changed`; the answer then names the owner (with `ownerEmail`). `{speciesProfileId}` changes the profile, and the all-zero GUID `00000000-0000-0000-0000-000000000000` detaches it — `null`/omitted still means "leave it" (2026-10-25) |
| DELETE | `/terrariums/{id}` | `terrarium.write` | soft delete; requires `?allowUnboundDevice=true` when a device is bound → else `409 conflict_device_bound` |
| GET | `/terrariums/{id}/thresholds` | `terrarium.read` | returns `effectiveThresholds[]` with `source` |
| PUT | `/terrariums/{id}/thresholds` | `terrarium.write` | upsert overrides; validates band ordering |
| DELETE | `/terrariums/{id}/thresholds/{metricId}` | `terrarium.write` | revert one metric to the profile |
| GET | `/terrariums/{id}/readings/latest` | `terrarium.read` | one row per metric + `deviceStatus`, `lastSeenAt` |
| GET | `/terrariums/{id}/readings?metric=&from=&to=` | `terrarium.read` | bucketed; response echoes `bucket` |
| GET | `/terrariums/{id}/coverage?from=&to=` | `terrarium.read` | expected vs received samples |
| GET | `/terrariums/{id}/summaries?from=&to=` | `terrarium.read` | daily summaries with coverage — **not built (roadmap 3.6)** |
| POST | `/terrariums/{id}/silences` | `silence.write` | `{metricId?, untilUtc, reason}` (≤ 24 h) |
| GET | `/terrariums/{id}/silences` | `alert.read` | windows still in force — an addition, §4.5 |
| DELETE | `/terrariums/{id}/silences/{silenceId}` | `silence.write` | cancel early |
| POST | `/terrariums/{id}/exports` | `terrarium.read` | `{format, from, to, metricIds?}` → job |

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
silence to be "visible on the dashboard" and the client has no other way to learn the id it must cancel. And since
2026-10-13 the threshold write half is built — `PUT /terrariums/{id}/thresholds` and
`DELETE /terrariums/{id}/thresholds/{metric}`, described below — which closes 3.1. The rest of this table —
`GET /terrariums/{id}/summaries` and `POST /terrariums/{id}/exports` — is specified and not built; the summaries are
3.6 and the export task is M4's.

**Ownership is assignable (2026-10-23).** An account holding `data.write.any` creates an enclosure **without an
owner**: it is the administrator's blank slate, appears in no keeper's list, and answers `404` on the owner-scoped
routes until it is assigned. `PATCH /terrariums/{id}` with `{"ownerUserId": "<guid>"}` gives it to an account, and is
refused for everyone else (`errors[0].code = owner_change_forbidden`; an unknown account is `account_not_found`).
The bound board follows the enclosure: it stays in it — the readings and the DI-04 slot are untouched — and its
`Device.UserId` becomes the new account, so the new keeper's fleet lists it. Recorded as `terrarium.owner_changed`
with the previous owner in `beforeJson`, and that row replaces the `terrarium.updated` row an administrator's edit
would otherwise write — one request, one entry.

**`GET /terrariums/{id}/thresholds` as built.** `terrarium.read` only, scoped to the caller like
every other route here:

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

**`PUT`/`DELETE` overrides as built (3.1's write half, 2026-10-13).** Both require `terrarium.write` — the policy `POST`,
`PATCH` and `DELETE` on the terrarium already use — and both answer with the **resulting effective set**, in exactly
the shape `GET` returns, so an editor re-renders from one payload rather than two:

```json
PUT /terrariums/{id}/thresholds
{ "overrides": [ { "metric": "tempC", "phase": "any", "targetMin": 28, "targetMax": 30,
                   "criticalMin": 22, "criticalMax": 34.5, "dwellWarnMinutes": 5, "dwellCritMinutes": 2,
                   "recoveryMargin": 0.5, "note": "summer", "enabled": true } ] }
```

`metric` and both target bounds are required; `phase` defaults to `any`, `criticalMin`/`criticalMax` to the derived
margin, and the three engine parameters to the engine's own defaults. The body is a **patch over the override set**:
a metric it does not mention keeps its override, so `{"overrides":[]}` is a valid no-op. Every entry is validated
before anything is written, so one incoherent band changes nothing at all, and the answer is `400 validation_failed`
with one entry per problem addressed by position (`overrides[1].targetMin`) carrying the stable codes
`metric_invalid`, `threshold_phase_invalid`, `threshold_target_required`, `threshold_note_too_long`,
`threshold_duplicate`, or the band rules' own (`threshold_ordering_invalid`, `threshold_critical_invalid`,
`threshold_dwell_invalid`, `threshold_recovery_margin_invalid`, `threshold_implausible_band`). A band identical to
the stored one writes nothing and records no history.

`DELETE /terrariums/{id}/thresholds/{metric}` removes **every** override the terrarium holds for the metric,
whichever phase — "revert this metric" is the metric, not the phase the caller happened to be looking at. It is
idempotent (a metric carrying no override is not an error) and refuses only an unknown key, which is a client
mistake rather than a no-op.

Both write a `ThresholdSnapshot` of the set now in force, in the same unit of work as the change. That is what makes
`TC-U-26` hold: an alert **already open** keeps its denormalised `BandMin`/`BandMax` for the record while the next
sample is judged against the new band, and the normal recovery path — never a special "threshold changed" outcome —
is what closes it if the new band puts the reading back in range (UC-03 A4). Neither route takes an `If-Match`:
§4.2 lists the precondition for `PATCH /terrariums/{id}` alone.

**`PATCH /terrariums/{id}` requires `If-Match`.** `GET`/`POST`/`PATCH` return the row's SQL Server `rowversion` as
a strong `ETag` **and, base64, as the item's `rowVersion` field**, and the update must send one of the two back —
they are the same value, so a client uses whichever it can read. The field exists for clients that cannot read
response headers at all: Swagger UI shows no response headers and, before the parameter was declared, no `If-Match`
box either, which left the route readable but not updatable from the one surface the demo is driven from. List items
have no `rowVersion`: a list is a read, and a token there would invite an update from a stale copy.

| Situation | Answer |
|---|---|
| `If-Match` matches the stored `rowversion` | `200` with the updated item, the **new** `ETag` and the new `rowVersion` |
| `If-Match` is stale, or the row moved between the read and the write | `412 precondition_failed` |
| `If-Match` is absent or unparseable | `428 precondition_required` |
| `If-Match: *` | accepted — existence is enough, and the token is not compared |
| A provided field fails validation | `400 validation_failed` with field-level `errors[]`, **after** the precondition check |

The body is a partial update: an **omitted** member is left unchanged, and `location`/`description` sent as an empty
string clear the field — the one way a JSON body can say "clear it" once it has been bound to a record. The
validation codes are `POST`'s (`terrarium_name_required`, `terrarium_name_too_long`, `terrarium_location_too_long`,
`terrarium_description_too_long`, `terrarium_timezone_invalid`, `species_profile_not_found`). The precondition is
checked before the body because a stale client should be told to reload, not handed a validation report about a
version of the terrarium it is not looking at. Both refusals say where the replacement comes from — `GET
/terrariums/{id}`, naming the `ETag` header and the `rowVersion` field — because a caller who has already sent a
token has done the read once and "reload it" alone is not a next step it can take from a surface that shows no
headers (2026-10-14).

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

**Ownership, deletion and the administrator (2026-10-14, TASKS items 3, 5 and 6).** Three readings that changed on
this date, stated here because they are contract rather than implementation:

- **A foreign terrarium is `403 forbidden_terrarium`**, not `404`. A keeper who mistyped an id and a keeper looking
  at somebody else's enclosure need different next steps, and the row is not disclosed either way. **This is a
  deliberate narrowing of `BR-02.2` to the terrarium routes**: alerts, silences, species profiles and devices still
  answer `404 not_found` for another account's resource, which is what keeps their ids unprobeable. The
  inconsistency is recorded in `05-release/03` §5 rather than left to be discovered.
- **A soft-deleted terrarium is `410 terrarium_deleted`**, not `404`. Its readings, alerts and summaries are still
  stored, so answering "no such thing" would say the opposite of the truth.
- **The case is decided by a `Status` column, not by a null timestamp.** `Terrarium.Status` (`Active`/`Deleted`) is
  what the model's query filter reads; `DeletedAt` remains as the detail of *when*, and the migration back-fills the
  new column from it, so an enclosure deleted before this change does not come back to life.
- **An administrator sees and acts on any account's terrariums**, on every route in this table including the reads
  and both threshold writes. Since 2026-10-17 that is expressed as permissions rather than a composite policy: the
  routes name `terrarium.read`/`terrarium.write` like every other caller's, and the *service* widens the answer for
  an account holding `data.read.any`/`data.write.any`
  (`TerrariumService.IsMemberAsync(..., mayReachAnyAccount)`). An administrator's list, detail **and update**
  answers carry `ownerUserId` **and** `ownerEmail` — the id is what the client keys on, the address is what it can
  show, because an id alone does not say whose enclosure it is (2026-10-23). The update answer carries them because
  it is the reply to the assignment itself: without them the screen that just gave the enclosure away would read
  "belongs to nobody" until it was read again (2026-10-24). An ordinary caller's answers never carry either. A
  cross-account `PATCH` or `DELETE` writes an audit row (`terrarium.updated`, `terrarium.deleted`) with the
  administrator as the actor and the owner in the payload — the ordinary owner edit stays unaudited, as it was.

**Creating an enclosure with a board in one request (2026-10-14, TASKS item 2).** `POST /terrariums` accepts
`deviceId` (the board's public id) or `macAddress` (item 8's spellings), and leaving both out is exactly the
behaviour that existed before. Naming one **is a claim**: the board has to be claimable (registered, unbound, not
revoked) and the response carries the one-time `deviceSecret`, exactly as `POST /devices/claim` does — the
alternative would be a board that can neither publish nor be claimed again, because the enclosure it now belongs to
already holds a device. Both selectors at once is refused rather than resolved by precedence. The board is resolved
**before** anything is written, so a request naming a board that cannot be used creates no enclosure at all, and the
enclosure, the binding, the credential and the audit row commit in one unit of work. The field errors are
`400 validation_failed` with `device_not_found`, `device_already_bound`, `device_selector_ambiguous` or
`mac_invalid`, each naming `deviceId` or `macAddress` — a 400 rather than a 404 because the board is a field of the
body the caller sent, exactly as an unknown `speciesProfileId` already is.

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

`POST /terrariums` takes `{name, speciesProfileId?, location?, description?, timeZoneId?}` and answers `201` with the
item shape below. It requires `terrarium.write` and exists ahead of the rest of FR-03 because every other route needs a
terrarium to point at — `POST /devices/claim` has nothing to bind a board to until one exists. Validation is
`400 validation_failed` with field-level `errors[]` (`terrarium_name_required`, `terrarium_location_too_long`,
`terrarium_description_too_long`, `terrarium_timezone_invalid`, `species_profile_not_found`); `timeZoneId` falls back
to `Defaults:TimeZoneId` when omitted. `speciesProfileId` is optional since 2026-10-25 — an enclosure may be set up
before its species is chosen, and `species_profile_not_found` is answered only for an id that was actually sent.

**An enclosure with no species (2026-10-25).** `speciesProfileId` and `speciesName` are null together on every answer
whose enclosure has none, and the enclosure is otherwise ordinary: it stores and returns its readings, it can hold a
board, and `GET /terrariums/{id}/thresholds` answers an empty `effectiveThresholds[]`. The consequence is stated
rather than implied: with no profile bands and no overrides there is nothing to judge, so no temperature, humidity or
UV alert opens for it until a profile is assigned. On `PATCH`, an omitted or null `speciesProfileId` leaves the
assignment alone; the all-zero GUID detaches it, which is this contract's spelling of "empty" for a member that is not
a string.

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
| GET | `/species-profiles` | `species.read` | built-ins + the user's own |
| GET | `/species-profiles/{id}` | `species.read` | one profile, bands included; a foreign id is `404 not_found` |
| POST | `/species-profiles` | `species.write` | custom profile; bands validated |
| POST | `/species-profiles/{id}/duplicate` | `species.write` | copy a built-in into an editable custom profile |
| PATCH | `/species-profiles/{id}` | `species.write` | built-ins return `403 builtin_immutable`; an administrator (`data.write.any`) may edit them and other accounts' profiles; a racing edit is `409 profile_conflict` |
| DELETE | `/species-profiles/{id}` | `species.write` | `409 profile_in_use` if assigned to a terrarium; an administrator may delete a built-in |
| POST | `/species-profiles/{id}/image` | `species.write` | `multipart/form-data`, one part named `file`; PNG/JPEG/WebP ≤ 2 MB; replaces any picture already there |
| GET | `/species-profiles/{id}/image` | `species.read` | the picture's bytes; needs the bearer token, so a client fetches it rather than linking it |
| DELETE | `/species-profiles/{id}/image` | `species.write` | idempotent; answers the profile, not `204` |

**As built (roadmap 3.9, 2026-10-13; the picture added 2026-10-14; the read-by-id route added 2026-10-20).** All nine routes are implemented. What follows is contract rather than
implementation, because a client written against either reading would break:

- **Reads name `species.read` and the writes `species.write`** — the catalogue is shared data, so it is the
  administrator's role, which is seeded with `species.write`, that authors it, and a keeper is refused
  (`403 insufficient_role`) rather than rewriting what every terrarium is judged against. This is the one place a
  keeper's write code does not reach the catalogue: it was `Owner`-gated when the routes were first built, and
  `02-design/06` §3 records the change.

- **The list carries the bands**, and is unpaged. It is four built-ins plus whatever the account authored, and the
  picker in `POST /terrariums` needs all of them at once.
- **`GET /species-profiles/{id}` answers one profile in the same item shape the writes return**, bands included, so
  a detail page renders from one payload. The item carries `canEdit`: true for a profile of the caller's own, false
  for a built-in, which is edited by duplicating it — and true for an administrator throughout, who edits any
  profile in place. The client picks between the edit controls and the "duplicate it instead" affordance without
  re-deriving the rule from `isBuiltIn`. A profile of another account is
  `404 not_found`, indistinguishable from a made-up id (BR-02.2), exactly like the list never returning it.
- **Create and duplicate answer `200` with the profile, not `201` with a `Location`** — a client renders what it just
  made from one payload, and both writes have replied in the shape the reads use since they were built. The
  read-by-id route added later would have allowed a `Location`; the answer stays `200` so a client written against
  the original reading does not change. A duplicate is named `"<source> (copy)"` when the body names nothing, and carries
  its source's `sourceRef`, because a copy whose numbers lost their citation would be a second, quieter way of
  shipping an unsourced range.
- **`PATCH` replaces the band set when `bands` is sent.** An omitted member is left alone and an empty string clears
  a text field; a sent `bands` is the complete set afterwards, never a merge — half a band cannot be validated.
- **The three refusals are distinguished**: `403 builtin_immutable` (the row is visible and immutable — the client
  can offer to duplicate it), `404 not_found` (a foreign profile, indistinguishable from a made-up id, BR-02.2), and
  `409 profile_in_use`. Soft-deleted terrariums count towards "in use": their rows still hold the foreign key, so
  ignoring them would turn a documented `409` into a constraint violation.
- **An administrator edits the catalogue in place** (2026-10-20). A caller holding `data.write.any` may patch or
  delete a built-in and any account's profile without duplicating first; the change is audited with before and after
  like every other, ownership does not move, and every band rule still applies. A caller who only holds
  `species.write` keeps the two refusals above. `canEdit` on the item reports which case the caller is in, so a
  client shows the edit controls or "duplicate it instead" without re-deriving the rule.
- **A racing edit is `409 profile_conflict`** (2026-10-20). The profile row carries a `RowVersion`; when another
  request committed first, the writer answers `409 profile_conflict` rather than `500`, and the editor reloads.
- **`sourceUrl` stays reserved** for a verified citation: a custom band may carry `sourceRef`, and nothing this build
  writes sets a URL.
- **`BR-10.5`'s climate-zone warning is the client's**, not this API's: `climateZone` travels on every profile so a
  client can judge a band against the zone's ranges in `07-appendices/05` §4. The server refuses only what BR-10.2
  and the metric dictionary's plausible range make impossible.

**The picture, as built 2026-10-14.** A profile may carry one picture, and it is the only part of a profile that is
not JSON, so it has routes of its own. Four readings are contract:

- **What is stored is what the bytes turned out to be.** The part's own `Content-Type` is checked and then
  discarded: the file is stored under the media type sniffed from its signature (PNG, JPEG or WebP), because a
  caller's claim about its own file cannot be the reason a browser later treats it as an image. A *specific*
  declared type outside the three is refused as `400 image_type_unsupported`; `application/octet-stream` counts as
  no claim at all, since that is what a client sends when it does not know its own file's type. Bytes that are not
  one of the three are `400 image_invalid`, whatever they were labelled. The limit is 2 MB, enforced by a bounded
  read — `400 image_too_large` costs 2 MB of buffering, not the size of what was sent.
- **Both writes answer the whole profile** in the shape the reads return — `200` with the item, no `201` and no
  `204` — so a client re-renders from the payload it was just handed. `DELETE` on a profile with no picture is not
  an error and writes no audit row, because nothing changed.
- **The item carries `imageUrl` and `imageUpdatedAt`**, not the storage key: `imageUrl` is the relative path of the
  route above and is `null` when there is no picture, which is the ordinary state of a built-in. `imageUpdatedAt`
  is the cache-busting value — replacing a picture does not change its URL — and it is a timestamp of the picture
  being set, replaced *or removed*, so a client must read it as "when this changed last" rather than as "when the
  current picture was taken".
- **The image route needs the caller's token**, like every other read, and the set of profiles it will serve is the
  set the list returns: a built-in (everyone) or the account's own. That is why a client *fetches* the bytes and
  hands the browser an object URL instead of pointing an `img` tag at it — the alternative was a second place a
  bearer token is accepted from a query string, which `/hubs` holds alone (`02-design/06` §2). The route answers
  `404 image_not_set` for a profile with no picture and `404 not_found` for one the caller may not see, so the two
  are distinguishable without disclosing anything a foreign id would.

### 4.4 Devices

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/devices` | `device.read` | fleet list with status, firmware, RSSI, last seen, uptime — **as built**, see below. Nothing is filtered out: a detached board stays as `provisioning` and a revoked one still appears |
| GET | `/devices/{id}` | `device.read` | one board, by public id — **as built** |
| PATCH | `/devices/{id}` | `device.write` | rename, set sampling/publish interval — **as built**, stored rather than queued |
| DELETE | `/devices/{id}` | `device.write` | **detach only** (2026-10-25): unbinds the board and returns it to `provisioning`, keeping its credentials and its row. `?purge=true` erases the row instead, refused with `409 device_has_history` while anything references it — **as built** |
| POST | `/devices/claim-by-mac` | `device.provision` | bind a self-registered board by the MAC printed on it — **as built**, §2.4 |
| POST | `/devices/{id}/move` | `device.provision` | `{terrariumId}` — bind the board, releasing the enclosure it held and displacing the board that held the target — **as built** (2026-10-23) |
| POST | `/devices/{id}/rotate-secret` | `device.provision` | returns a new secret once; old one valid for the 10-min grace window |
| POST | `/devices/{id}/revoke` | `device.provision` | immediate disconnect + credential invalidation |
| POST | `/devices/{id}/calibration` | `device.write` | `{tempOffsetC?, rhOffsetPct?, luxGain?}` |
| POST | `/devices/{id}/commands` | `device.write` | `{type, parameters}` → queued, ack-tracked |
| GET | `/devices/{id}/commands/{cmdId}` | `device.read` | `queued` \| `sent` \| `acked` \| `failed` |
| POST | `/devices/{id}/snapshots` | Device | multipart JPEG ≤ 512 KB, ≤ 1 per 30 s (FR-17) |
| GET | `/devices/{id}/snapshots/latest` | `device.read` | metadata + short-lived URL |

**As built (2026-10-14, TASKS item 9b).** The routes marked above are implemented — the five of that pass, plus
`move` below; `calibration`, `commands` and the two `snapshots` routes are not, and stay listed so the gap is visible
rather than implied. What the built ones settle:

- **`GET /devices` and `GET /devices/{id}` are scoped by the caller's `UserId`**, which claiming sets. A board that
  self-registered but was never claimed has no owner and therefore appears in nobody's fleet — and answers
  `404 not_found` to everyone, which is also how a foreign board is refused, so the routes cannot be used to
  enumerate boards in provisioning mode.
- **Both the stored state and the derived badge are sent.** `status` is the lifecycle column (`provisioning`,
  `online`, `offline`, `revoked`, `maintenance`); `runtime` is the online/offline badge derived through the same
  `DeviceSilencePolicy` call the terrarium header uses (BR-07.2), so the two screens cannot disagree about one board.
  They answer different questions — "is it misconfigured?" versus "is it reporting?".
- **`PATCH` is a patch**, and it stores the desired values rather than reaching into the node: `deviceName` (≤ 60
  characters; an empty string restores the default label), `samplingIntervalSec` and `publishIntervalSec` (10–300 s
  each, `errors[0].code = interval_out_of_range` otherwise). A per-device calibration write and a queued
  `set_config` remain unbuilt, so a board picks the new interval up on its next connection. `PATCH` is **not
  audited**: `BR-18.4` names the credential operations, and renaming a node is the ordinary use of a settings screen.
- **`DELETE` unbinds, and only `?purge=true` erases (2026-10-25).** The plain call is the enclosure gesture: the
  board leaves its enclosure — freeing the DI-04 slot for a replacement — goes back to `provisioning` and keeps its
  credentials, owner and row, so it can be bound again without being claimed a second time. It stays in
  `GET /devices`, because it is still the account's board. Recorded as `device.unbound` with the enclosure it left.
  **Killing the credentials is `POST /devices/{id}/revoke`** — a different act, with a different consequence, and one
  that leaves the binding alone; a board revoked that way stays `revoked` when it is later unbound, because detaching
  must not resurrect a credential that was killed on purpose.
  `?purge=true` is the only thing that removes a row: it revokes first (so the audit entry outlives the board it
  names) and is **refused with `409 device_has_history`** while any reading, alert, health sample or event still
  names the board. The refusal is not caution for its own sake: those foreign keys are `ON DELETE CASCADE`, so a
  purge that were allowed would silently erase the history that explains the board.

**`POST /devices/{id}/move` — added 2026-10-23.** This is the path the fleet and enclosure screens use to put a
board somewhere, and it is what `rebind` was meant to be. Body: `{ "terrariumId": "<guid>" }`. Answer: the board as
`GET /devices/{id}` describes it, plus `previousTerrariumId` (null when it was unbound) and `displacedDeviceId` (the
public id of the board the move pushed out, null when the target was free).

- **One enclosure holds one board (DI-04).** A target that already has a board does not answer a conflict: the
  board that was there is unbound — back to `provisioning`, `provisionedAt` cleared, credentials and ownership
  untouched — and the newcomer takes the slot. Refusing instead would leave a keeper no way to replace a failed node.
- **A board sits in one enclosure.** A move is a reassignment of the single `TerrariumId`, not a second binding, so
  the enclosure the board came from is free afterwards.
- **Owners reach only their own rows.** A foreign board or a foreign enclosure answers `404 not_found`, identically
  to one that does not exist (BR-02.2); `data.write.any` lifts that scoping, which is how an administrator pairs any
  board with any enclosure. A move never transfers ownership.
- **Refusals:** `409 device_revoked` (credentials dead), `409 device_not_claimed` (the board has no owner, so there is
  nobody to bind it for), `409 terrarium_already_bound` (a concurrent move won the slot), `400 move_invalid` (no
  `terrariumId` in the body).
- **Audited** as `device.moved` with the before/after enclosure and the displaced board, plus a `device.unbound` row
  for the displaced board so the account that lost its slot can see why. Moving a board to the enclosure it already
  holds is a no-op and writes no row.

### 4.5 Alerts, notifications, ops

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/alerts?family=&state=&severity=&metric=&from=&to=` | `alert.read` | paged, newest first; every row carries its own `family` |
| GET | `/alerts/{id}` | `alert.read` | includes band, snapshot reference, value series excerpt. `data.read.any` opens any account's row (2026-10-25) |
| POST | `/alerts/{id}/ack` | `alert.write` | `409 alert_not_open` if already resolved. `data.write.any` acts on any account's row |
| POST | `/alerts/{id}/resolve` | `alert.write` | `{reason: Recovered\|FalsePositive\|SensorFault\|Accepted, note?}`. `data.write.any` acts on any account's row |
| GET | `/alerts/{id}/timeline` | `alert.read` | trigger → escalation → ack → resolve with actors. `data.read.any` opens any account's row |
| GET | `/notifications?unreadOnly=true` | `notification.read` | in-app inbox |
| POST | `/notifications/{id}/read`, `/notifications/read-all` | `notification.read` | |
| GET | `/exports/{jobId}`, `/exports/{jobId}/download?token=` | `terrarium.read` | token valid 24 h |
| GET | `/audit?entity=&entityId=&from=&to=` | `audit.read` | own terrariums only |
| GET | `/health`, `/ready`, `/metrics` | A | no secrets; `/ready` → `503` when a dependency is down |
| GET | `/version` | A | `{api, schema, minFirmware}` |

**As built (roadmap 3.4, 2026-10-10).** Every alert route above and all three silence routes are implemented;
notifications, exports and `/audit` are 3.5, 3.7 and the export task. **Updated 2026-10-16:** notifications are built
(3.5) and `/audit` is built (3.7); the export task is still M4's. Eight readings this build settles, stated here
because they are contract rather than implementation:

- **`GET /alerts` is the first endpoint to implement the documented paging convention** (`?cursor=&pageSize=`,
  default 50, max 200, `nextCursor`). The cursor is opaque — base64 of `triggeredAt|id` — and the order is
  `TriggeredAt DESC, Id DESC`: newest first by *when the excursion started*, so a back-filled alert appears where it
  happened, not where the system learned of it. The pair is what makes the order total; an identity alone would not
  say which of two alerts that share an instant came first.
- **Filters are the contract's five**, and their vocabulary is the response's: `state` is `Open`, `Acknowledged` or
  `Resolved`, `severity` is `Info`, `Warning` or `Critical` (case-insensitive), `metric` is the REST key (`tempC`),
  `family` is `enclosure` or `species` — the row's own `family`, carried even by the device-level alerts that have no
  metric to derive it from (2026-10-17) — and `from`/`to` filter on `TriggeredAt`. An unknown value is a
  `400` naming the alternatives rather than an empty page, and an unknown `family` is `400 family_invalid`.
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

**An administrator may act on any account's alert (2026-10-25).** `GET /alerts/{id}`, `GET /alerts/{id}/timeline`,
`POST /alerts/{id}/ack` and `POST /alerts/{id}/resolve` widen to any account's row for a caller holding
`data.read.any`/`data.write.any`. The reason is that the widening already existed where it could not be used:
`GET /admin/alerts` lists every account's incidents, and until this date a row named there could not be opened,
acknowledged or resolved — 404, because the owner-scoped lookup found nothing. The scope now follows the permission
rather than the enclosure, and two limits stay deliberate:

- **`GET /alerts` (the list) is still owner-scoped.** It answers the caller's own enclosures even for an
  administrator, because the cross-account view has its own route with its own paging, its own filters and the owner
  and enclosure names a list of unknown rows needs to be readable. A single list that silently changed shape for one
  account would make every client's empty-state untrustworthy.
- **An alert of a soft-deleted enclosure stays out of reach.** The cross-account lookup keeps the join that applies
  `Terrarium`'s query filter, so the rows the administrator's audit list still shows are a record of what happened
  rather than a queue of things to close.

The audit row of a cross-account acknowledgement or resolution names the administrator as the actor — the alert
already names its enclosure, so "who acted, and on what" is complete without a second column. Two readings for a
client: a refusal for an alert that exists but belongs to another account is still `404 not_found` for anyone
without the cross-account permission (BR-02.2's id-probing rule), and `alert.write` alone is not the cross-account
grant — a Technician holds it and still answers `404` for another account's row.

**Silences as built.** `POST` answers `200` with the created window rather than `201` with a `Location`, because
there is no read-by-id route to point at; `GET` returns only the windows that are still in force, since an expired
one is history and history is the audit trail's job. `metric` may be omitted, and `null` means **every metric of the
terrarium including its device-level alerts** — a keeper going away for the weekend silences the box, not five
metrics one at a time. The cap is 24 hours and the reason is mandatory, both from §02-design/05 §5, and
`DELETE` is idempotent: a second cancel changes nothing, writes no second audit row and still answers `204`, because
a `DELETE` that failed on a retry would leave a keeper believing a suppression was still in force. A silence
suppresses **notification and not detection** — alerts are still raised, counted, touched and resolved during one,
and the dispatcher of 3.5 reads the same `IsActiveAt`/`Covers` rule that the list endpoint renders.

The two writes name **`silence.write`** rather than `alert.write` (changed 2026-10-17 with the permission matrix):
going quiet is a decision about being told, not about an alert's lifecycle, so a deployment that wants a technician
who may acknowledge but not silence can express it with that one grant. All four shipped roles that may acknowledge
already hold both, so nothing about the seeded behaviour changed.

**Notifications as built (roadmap 3.5, 2026-10-16).** The three inbox routes above exist, plus the timeline entry
they feed. Five readings of the contract, stated here because a client depends on each:

- **`GET /notifications` answers `{items, unread}`** where `unread` is the account's whole unread count rather than
  the page's, so a badge can be rendered from the same response as the list. An item is
  `{id, channel, title, body, deepLink, alertId, terrariumId, step, isRead, sentAt, readAt}`; `step` is `0` for a
  first delivery and `1`–`3` for §02-design/05 §6's ladder, which is what lets a client label a repeat rather than
  showing the same sentence twice with no explanation.
- **Only delivered rows appear in the inbox.** A suppressed row is the report's material, not the keeper's: the
  screen answers "what was I told", and an entry that says "you were told nothing about this" would be a second
  inbox of things that did not happen. The suppression table is read from the database, not from this route.
- **`POST /notifications/{id}/read` is idempotent** and answers `204`; a second call on a read entry answers `204`
  again, because the request changed nothing and that is not an error. An id that belongs to somebody else answers
  `404 not_found`, the same answer a nonexistent id gets (BR-02.2).
- **`POST /notifications/read-all` answers `{markedRead: n}`**, so a client can prove the badge cleared without a
  second read; `n` is `0` once nothing is unread.
- **`GET /alerts/{id}/timeline` gained a `notified` entry**, carrying the channel and the step or the suppression in
  `reason` and the delivered text in `note`. This is the sentence §02-design/05 §6 was written for — "the full
  response timeline of one incident" — assembled from the delivery rows rather than from a second set of escalation
  columns on the alert, and `notified` joins `opened`/`escalated`/`acknowledged`/`resolved` in one vocabulary.

### 4.6 Reference data

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/time-zones` | `terrarium.read` | the time-zone catalogue: `{items: [{ianaId, displayName, countryCode, utcOffsetMinutes, utcOffset}]}`, ordered west to east |

**Built 2026-10-14 (TASKS item 11).** The catalogue is a `TimeZone` table keyed by the **IANA id**, seeded from
`ReferenceDataSeeder.Catalogue` — a curated list covering every offset the world uses and every populated region,
rather than the ~600 ids of the full database, most of which name an uninhabited island or a historical alias. It is
readable by every authenticated account, because a picker is not privileged data and both clients need it before they
can create anything.

- **The value to send back is `ianaId`**, which is exactly the string `Terrarium.TimeZoneId` and `User.TimeZoneId`
  store (ADR-015). `POST /terrariums` and `PATCH /auth/me` validate against this table and store the catalogue's
  spelling, so `asia/ho_chi_minh` lands as `Asia/Ho_Chi_Minh` and an id the picker could not have offered is
  `400 terrarium_timezone_invalid` / `timezone_invalid`. This replaced a bare `TimeZoneInfo` lookup.
- **The offset is standard time, never arithmetic.** Which offset applies at a given instant — including daylight
  saving, which the table deliberately does not model — stays the platform time-zone database's answer through
  `SystemClock.ResolveZone`, which degrades to UTC for an id it cannot resolve. `utcOffset` is pre-formatted
  (`UTC+07:00`) so neither client has to agree on a spelling; `utcOffsetMinutes` is the same number for a client that
  wants to group by it.
- **This is a time-zone table, not a province table.** Vietnam is one zone (`Asia/Ho_Chi_Minh`, `+07:00`), so a list
  of its provinces could only have repeated that row; the earlier reading of item 11 was replaced at the requester's
  direction on 2026-10-14 (`05-release/03` §5).

### 4.7 Accounts (administrator)

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/users?search=&skip=&take=` | `user.read` | paged accounts with `total`, `skip`, `take`; `search` matches username or email, case-insensitively |
| POST | `/users` | `user.manage` | `{username, email, password, role?, preferredLanguage?}`; `role` defaults to `Owner`, and a taken identifier is `400 account_invalid` with the field to fix |
| GET | `/users/{id}` | `user.read` | one account, or `404 user_not_found` |
| PATCH | `/users/{id}` | `user.manage` | `{username?, email?, preferredLanguage?, minNotifySeverity?, quietHoursStart?, quietHoursEnd?}` — a patch; the time zone is not one of its members |
| POST | `/users/{id}/reset-password` | `user.manage` | `{newPassword}`; ends every session the account holds |
| PATCH | `/users/{id}/role` | `user.manage` | `{role}` — a role **code**; an unknown code is `400 role_invalid`. It is a `PATCH`, not a `PUT` |
| POST | `/users/{id}/disable` | `user.manage` | ends every session the account holds; audited `account.disabled` |
| POST | `/users/{id}/enable` | `user.manage` | reverses the disable; audited `account.enabled` |
| DELETE | `/users/{id}?purge=` | `user.manage` | soft delete (= disable) by default; `?purge=true` erases the row instead |

**Built 2026-10-14 (TASKS item 12); creation, edit, password reset and deletion added 2026-10-17.** Every entry carries
`{id, username, email, role, roleId, permissions, preferredLanguage, timeZoneId, quietHoursStart, quietHoursEnd,
minNotifySeverity, createdAt, lastLoginAt, disabledAt, isActive}`, so a screen renders a row without a second call:
`role` is the role's code for display, `roleId` its identity, and `permissions` the codes it currently grants — which is
what a client gates on. Reads name `user.read` and every write `user.manage`, so a deployment that wants an auditor
account can grant the first without the second. **A role change, a password reset and a delete all end the account's
refresh tokens** (§4.1). The guards sit in the service rather than on the route, because both depend on the target
rather than on the caller: `409 self_change_refused` for a role change, a disable or a delete aimed at the caller's
**own** account — the route that would undo it is behind the permission just given up — and
`409 last_administrator_refused` when the change would leave no enabled account holding `user.manage`.
`404 user_not_found` names the failure rather than answering `not_found`, because this is the one caller entitled to
enumerate.

- **Creation answers `201` with the item and a `Location`**, stores the first password rather than generating one —
  a generated password would have to be shown once and stored nowhere, and this deployment has no delivery channel for
  it (`R-13`) — and is audited as `account.created`. `role` defaults to `Owner`; a taken username or email is
  `400 account_invalid` with `errors[]` naming the field to fix, and an unknown `role` is field code `role_invalid`.
- **`PATCH /users/{id}` is a patch**, and the account's **time zone is deliberately not one of its members**: an
  administrator edits the identifiers, the language, the severity floor and the quiet hours, while the zone comes from
  `PATCH /auth/me`, where it is validated against the seeded catalogue. Quiet hours are a pair — both empty clears the
  window, one alone is `400 quiet_hours_incomplete`. Audited as `account.updated`, and a no-op writes nothing.
- **`POST /users/{id}/reset-password`** validates the new password against the same policy registration uses and ends
  **every** session of the account, so a stolen session cannot outlive the reset performed to kill it. Audited as
  `account.password_reset`; a password the policy refuses is `400 password_invalid`.
- **`PATCH /users/{id}/role`** takes a role **code** — the four the product ships or a custom one (§4.8), matched
  case-insensitively; an unknown code is `400 role_invalid`. Audited as `account.role_changed` with the code before and
  after, and a change to the code the account already holds writes nothing.
- **`POST /users/{id}/disable` and `/enable` are idempotent** (`account.disabled` / `account.enabled`). Disabling ends
  every session the account holds; re-enabling does not restore them, so the account signs in again.
- **`DELETE /users/{id}` is a hard delete** (2026-10-20). It erases the account and, in one transaction, everything
  that would otherwise point at a row nobody can load: its boards are unbound (not revoked — they stay in the
  deployment to be claimed again), its enclosures go with their readings, samples, alerts, summaries and overrides,
  its own catalogue profiles go, and its inbox goes. There is no `?purge` switch and no soft-delete mode: the two
  verbs for one act made the console's "delete" mean "disable" to whoever used it. `POST /users/{id}/disable` is the
  reversible act. Audited as `account.deleted` with what went (`terrariums`, `devicesUnbound`, `speciesProfiles`,
  `notifications`), distinct from `account.disabled`, and the answer describes the account as it was.

### 4.8 Administrative dashboard and permission matrix

| Method | Path | Role | Notes |
|---|---|---|---|
| GET | `/admin/overview` | `data.read.any` | the header's counts, every account with its holdings, and both alert families with their metric keys and open counts |
| GET | `/admin/terrariums?search=&skip=&take=` | `data.read.any` | every account's enclosures, newest first, paged with `total`; `search` matches the enclosure's name or its owner's username |
| DELETE | `/admin/terrariums/{id}` | `data.write.any` | hard delete; boards are unbound (not revoked) and their broker sessions closed; `terrarium.deleted` plus one `device.unbound` per board; `404 not_found` for an unknown or already-deleted id |
| GET | `/admin/devices?search=&skip=&take=` | `data.read.any` | every board, bound or not, last seen first with the never-published last; `search` matches the public id, the name or the owner's username |
| GET | `/admin/alerts?family=&state=&skip=&take=` | `data.read.any` | every account's alerts, both families, newest first; `400 family_invalid` / `state_invalid` on an unknown key. A row here is actionable through `POST /alerts/{id}/ack` and `/resolve`, which take the same cross-account permission (2026-10-25) |
| GET | `/admin/roles`, `/admin/roles/{id}` | `user.read` | the matrix: each role with its codes, whether the product ships it, how many accounts hold it, and which of its codes cross accounts |
| POST | `/admin/roles` | `user.manage` | `{code, name, description?, permissions?, rank?}` → `201` with the role |
| PATCH | `/admin/roles/{id}` | `user.manage` | `{name?, description?, permissions?, rank?}`; `permissions` **replaces** the set |
| DELETE | `/admin/roles/{id}` | `user.manage` | a custom role nothing holds; answers the role |
| GET | `/admin/permissions` | `user.read` | the catalogue: every code with its group, its label, what granting it allows and whether it crosses accounts |

**Added 2026-10-17 (the role/permission re-framing).** The group exists because of the rule that follows; the four
dashboard routes and the matrix's own routes are described together here because they are the two halves of the
console.

- **The cross-account rule.** Every keeper-facing query is scoped to `Terrarium.UserId` in the store, so this group is
  the only surface that *lists* another account's data: its store has no owner parameter at all, which is why a
  dashboard could not have been assembled out of the routes a keeper calls. The catalogue's two **cross-account** codes
  also widen a keeper-facing route *by id* — `GET /terrariums/{id}` and its writes answer for somebody else's enclosure
  when the caller holds `data.read.any`/`data.write.any`, checked by the service that scopes its own query
  (`02-design/06` §3.2) — but one id at a time is not a get-all page. That is why the matrix can say "Admin = toàn bộ
  dữ liệu": it is a fact about this group plus those two codes, not a branch inside the keeper's queries.
- **`DELETE /admin/terrariums/{id}` is the administrator's hard delete** (2026-10-20). Unlike the keeper's own
  `DELETE /terrariums/{id}` — a soft delete that keeps the readings — this removes the row and everything cascading
  from it: samples, readings, alerts, summaries, threshold overrides. A bound board is **unbound, not revoked**: its
  `terrariumId` clears, it returns to `Provisioning`, its owner and credentials stay, and its open broker sessions are
  closed after the commit (ingest refuses an unbound device, so a session that lingers a moment writes nothing). Two
  audit rows are staged on the same unit of work — `terrarium.deleted`, whose payload names the owner and the enclosure,
  and one `device.unbound` per released board. An unknown id, or one its owner already deleted, is `404 not_found`.
- **`GET /admin/overview` is one answer for the first screen**: `counts` (accounts, accounts that may sign in,
  enclosures, enclosures with a board, boards, boards online, open alerts — total, per family and critical — and
  species profiles), `accounts` (every account with its `role` code, `isActive`, its enclosures, boards, open alerts,
  `createdAt` and `lastLoginAt`) and `families` (`key`, the `metrics` belonging to it and its open-alert count), so a
  client labels its two tabs without carrying a list of metric keys of its own. The three lists page with `skip`/`take`
  (default 50, max 200) and answer `{items, total, skip, take}`; `/admin/alerts` speaks the keeper's own
  `family`/`state` vocabulary, refusal included, so one client parses one thing — an unknown `family` is
  `400 family_invalid` and a `state` outside `Open`/`Acknowledged`/`Resolved` is `400 state_invalid`. Enclosures whose
  owner deleted them are not listed, the same rule every other read follows.
- **The matrix is editable data, and these are the routes that edit it.** Reads name `user.read` — an auditor may see
  which role grants what — and every write `user.manage`. `POST` and `PATCH` are refused with `400 role_invalid` when
  the code is not identifier-shaped (`errors[0].code = role_code_invalid`), is already taken (`role_code_taken`), names
  no code (`role_name_invalid`) or names a permission the catalogue does not define (`permission_unknown`) — a typo
  that silently granted nothing is the failure that check exists to prevent. `permissions` on a `PATCH` **replaces**
  the set, because a merge would make a permission impossible to take away; the guard is
  `409 last_administrator_refused` when the edit would remove the last grant of `user.manage`, checked by *counting
  holders of the permission* rather than by comparing codes, so a re-banded or split administrator role is still
  protected. A missing role is `404 role_not_found`; deleting one the product ships is `409 system_role_immutable`
  (the next start-up would recreate it with the shipped defaults, silently undoing whatever was tuned) and deleting one
  accounts still hold is `409 role_in_use` with the count. Every write is audited — `role.created`, `role.updated` with
  the set before and after, `role.deleted` — and a no-op writes nothing. **None of them ends a session**: accounts
  holding the role keep their tokens until they expire, so a revocation takes effect within one 15-minute access-token
  lifetime, which the console warns about rather than hiding.
- **`GET /admin/permissions` is the catalogue** — `{items: [{code, group, name, description, crossesAccounts}]}` — and
  it is code rather than data: a permission the software does not implement cannot be granted by inserting a row. The
  four roles it grants over are rows, with their ids fixed in `SystemRoles` (a deterministic id is what lets
  registration assign `Owner` without a lookup that can fail on an unseeded database), and `IdentitySeeder` reconciles
  them on every start-up asymmetrically: a missing row is created with its shipped grants, an existing grant is never
  re-applied, and a permission added to the catalogue appears granted to nobody.

---

## 5. Error model (RFC 7807 + stable `code`)

```json
{
  "type": "https://smartreptile.example/problems/validation_failed",
  "title": "The request was refused.",
  "status": 400,
  "code": "validation_failed",
  "traceId": "00-4bf92f…-01",
  "errors": [
    { "field": "bands[0].targetMin", "code": "threshold_ordering_invalid", "message": "TargetMin must be lower than TargetMax." }
  ]
}
```

**The client translates from `code`, never from `title`/`detail`** (2026-10-20, BE-05). Three members are
contract:

- **`code`** is stable, snake_case and documented in the table below. It is what a client maps to a localised
  sentence, so adding one is a contract change even when the status does not move.
- **`title`** and **`detail`** are English and exist for logs and as a last-resort fallback. No screen shows them
  to a user.
- **`errors`** is an **array** of `{ field, code, message }` — one entry per refused field, each with its own
  stable code, and every violation present rather than only the first, so a form can mark all of its inputs from
  one response. `field` is the wire path the client sent (`bands[0].targetMin`, `username`); a refusal that is not
  about a field carries no `errors`. Every route projects this shape through one helper
  (`Api/Endpoints/ApiProblem.cs`), so the seven route files cannot drift apart.

| HTTP | Code | Meaning / typical cause |
|---|---|---|
| 400 | `validation_failed` | Field or schema problem (see `errors`) |
| 400 | `threshold_ordering_invalid` / `threshold_critical_invalid` | Band arithmetic violated |
| 400 | `schema_invalid` | Telemetry payload shape wrong |
| 400 | `payload_too_large` | > 32 KB batch or > 120 samples |
| 400 | `password_policy_violation` | New password failed the policy (see `errors`) |
| 400 | `registration_invalid` | Username, email or password failed validation (see `errors`) |
| 400 | `mac_invalid` | A MAC that is not twelve hex digits, or one no board can own: broadcast, all zeros, or a multicast/group address (inside `400 registration_invalid`'s `errors[]`, `field = "macAddress"` — 2026-10-23) |
| 400 | `malformed_request` | The body could not be read as JSON for this endpoint — nothing was processed |
| 401 | `invalid_credentials` | Wrong username/password |
| 401 | `auth_failed` | Device credential missing, malformed, wrong, revoked, unbound, or naming a different device than the payload — **one** answer for all of them, on `/ingest/http` only |
| 401 | `invalid_recovery_code` | Wrong, spent, malformed or account-less backup recovery code — one answer for all of them |
| 401 | `invalid_reset_code` | Wrong, spent, expired, foreign or account-less reset code — one answer for all of them |
| 401 | `token_invalid` / `token_reused` | Expired, consumed, or replayed refresh token (family revoked) |
| 403 | `insufficient_role` | The caller's token does not carry the permission the route names (2026-10-17) |
| 403 | `builtin_immutable` | Attempt to edit a built-in species profile |
| 400 | `image_required` / `image_too_large` / `image_type_unsupported` / `image_invalid` | The profile picture was missing, over 2 MB, not one of PNG/JPEG/WebP, or not an image at all. All four carry `errors[0].field = "file"`, so a form marks the picker |
| 404 | `image_not_set` | The profile is visible to the caller but has no picture — distinct from `not_found`, which is about the profile |
| 404 | `not_found` | Unknown id **or a foreign resource** (deliberate: no id probing) |
| 404 | `identifier_unknown` | No account uses that username or email — `/auth/forgot-password` only, a deliberate disclosure (`ADR-020`) |
| 404 | `claim_code_invalid` | Unknown, expired or consumed claim code |
| 404 | `device_not_found` | `/devices/claim-by-mac`, and the create-with-a-device body: no board matches, already bound, or revoked — one answer for all three |
| 403 | `forbidden_terrarium` | The terrarium exists and belongs to another account. **Terrarium routes only**: the deliberate narrowing of the rule below (2026-10-14, TASKS item 3) |
| 410 | `terrarium_deleted` | The terrarium was soft-deleted: its history survives, so it is not "gone without trace" (2026-10-14, TASKS item 6) |
| 400 | `device_already_bound` / `device_selector_ambiguous` / `device_not_found` | Create-with-a-device field errors, inside `400 validation_failed`'s `errors[]`, because the board is named in the body |
| 400 | `owner_change_forbidden` / `account_not_found` | `PATCH /terrariums/{id}` with `ownerUserId`: a keeper may not give their enclosure away, and an unknown account cannot receive it (both inside `400 validation_failed`'s `errors[]`, `field = "ownerUserId"`) |
| 409 | `terrarium_already_bound` / `conflict_device_bound` | Binding conflicts |
| 409 | `device_revoked` | `POST /devices/{id}/move`: the board's credentials are dead, so it cannot take an enclosure |
| 409 | `device_not_claimed` | `POST /devices/{id}/move`: the board has no owner yet — claim it first. Only an administrator can reach such a board at all |
| 400 | `move_invalid` | `POST /devices/{id}/move` without a `terrariumId`; the violation names the field (`terrarium_required`) |
| 409 | `registration_conflict` | Username and/or email already has an account (see `errors` for which) |
| 409 | `alert_not_open` | Ack/resolve on a resolved alert |
| 409 | `version_conflict` | Optimistic concurrency (`rowversion` mismatch) |
| 409 | `profile_in_use` | Deleting an assigned profile — including a terrarium that was soft-deleted, whose row still holds the foreign key |
| 409 | `profile_name_taken` | A custom species profile with that name already exists for this account (the unique index is `(createdByUserId, name)`) |
| 409 | `profile_conflict` | A profile edit lost a race: another request changed the row's `RowVersion` first, so the editor reloads (2026-10-20) |
| 400 | `profile_invalid` | `PATCH /auth/me`: the language is not `vi`/`en`, the zone is not in the catalogue, the quiet-hours pair is incomplete, or the severity is outside `info`/`warning`/`critical` (see `errors`) |
| 400 | `device_invalid` | `PATCH /devices/{id}`: the name is longer than the column or an interval is outside 10–300 s (see `errors`) |
| 400 | `family_invalid` | `GET /alerts` and `GET /admin/alerts`: the `family` filter is not `enclosure` or `species`; `/admin/alerts` answers `state_invalid` the same way (2026-10-17, the alert-family re-framing) |
| 400 | `role_invalid` | `PATCH /users/{id}/role`: no role uses that code. `POST`/`PATCH /admin/roles` answer it too, with `errors[]` naming the field — `role_code_invalid`, `role_code_taken`, `role_name_invalid`, `permission_unknown` (2026-10-17, the role/permission re-framing) |
| 400 | `account_invalid` | `POST`/`PATCH /users`: a creation or an edit was refused — a taken username or email, or a field the account rules reject (see `errors`, 2026-10-17) |
| 400 | `password_invalid` | `POST /users/{id}/reset-password`: the new password failed the same policy registration applies (see `errors`, 2026-10-17) |
| 404 | `role_not_found` | `/admin/roles/*`: no role holds that id (2026-10-17) |
| 404 | `user_not_found` | `/users/*`: no account holds that id. Readable by the administrator, who is the one caller entitled to enumerate |
| 409 | `self_change_refused` | `/users/*`: an administrator tried to change or disable **their own** account — the one change that cannot be undone from inside the API (2026-10-14, TASKS item 12) |
| 409 | `last_administrator_refused` | `/users/*` and `/admin/roles/*`: the change would remove the last grant of `user.manage`, and no route could restore one. Counted by holders of the permission rather than by role code (2026-10-17, the role/permission re-framing) |
| 409 | `system_role_immutable` | `DELETE /admin/roles/{id}`: the role is one of the four the product ships, and the next start-up would recreate it with the shipped defaults (2026-10-17) |
| 409 | `role_in_use` | `DELETE /admin/roles/{id}`: accounts still hold the role; the refusal carries the count (2026-10-17) |
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
| `alertChanged` | `{terrariumId, alertId, state, severity, metric, family, triggeredAt, resolvedAt?}` | open/escalate/ack/resolve |
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
and `family` is the same REST key the alert row carries (`enclosure` or `species`, 2026-10-17), so a banner can be
placed on the right tab and a badge counted on the right total without a second request, so one client parsing
`GET /alerts` and one handling this event are reading the same words. A touch is deliberately
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
6. A **permission code** is part of the contract the way an error code is, and it widens the same way: adding one is
   safe, and a deployment hands it out by adding the row to a role through the console, so a client that gates on
   codes rather than on a role name needs no new release (`02-design/06` §3, 2026-10-17).
