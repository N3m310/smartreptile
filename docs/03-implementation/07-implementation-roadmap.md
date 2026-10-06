# 07 — Implementation Roadmap

Six milestones. Each ends with a **demo-able increment** and an explicit definition of done, so the team
always has something to show and always knows what "finished" means. Parallel tracks are marked.

A web prototype was delivered **outside** this milestone plan on 2026-10-02; it is absorbed by the M4 table below
and recorded in "Prototype baseline" before M2.

```mermaid
gantt
  dateFormat  YYYY-MM-DD
  title SmartReptile v1 (6 weeks, indicative — W5-W10 re-baseline: 03-implementation/08)
  section M1 Foundations
  Specs frozen, env works, bench sensor  :a1, 2026-09-22, 5d
  section M2 Pipeline
  Firmware to broker, ingest, DB, ingest tests :a2, after a1, 8d
  section M3 Domain
  Threshold engine, alerts, summaries, backend tests :a3, after a2, 7d
  section M4 Clients
  Flutter app screens + web dashboard, widget tests :a4, after a2, 10d
  section M5 Hardening
  Security, retention, calibration, soak :a5, after a3, 6d
  section M6 Release
  Release APK, report, demo rehearsal :a6, after a5, 5d
```

---

## M1 — Foundations (5 days)

**Goal:** every member can run the whole stack locally and one real sensor produces a number.

| # | Task | Owner track | Acceptance | Status (2026-09-23, measured) |
|---|---|---|---|---|
| 1.1 | Close the open decisions `[TBC-1…4]` with the mentor (species, box size meaning, channels, pairing method) | all | Values written into this doc set; `01-product/01` §6 updated | **Partial** — the decisions are now in one canonical list (`05-release/03` §6, TBC-1…TBC-6), the product-facing four are repeated in `01-product/01` §6, TBC-4 is closed as **`ADR-016`**, and `02-design/06` §5 no longer describes it as open. What is still missing is *evidence of sign-off*: an agreement reached verbally is not in the repository, so `05-release/03` §6.1 carries a dated line to fill in. Partial until that line is real |
| 1.2 | Freeze FR/NFR list and ids | all | No id changes after this point without a note in `05-release/03` | **Complete** — the id scheme is in use across all 34 documents |
| 1.3 | Create the monorepo skeleton + CI job running `dotnet build`, `flutter analyze`, `pio run`, `pio test -e native` | backend | CI green on an empty scaffold | **Complete, exceeded** — five green jobs on every push (`backend`, `integration`, `app`, `firmware`, `secret-scan`); the firmware job executes the 22 host tests |
| 1.4 | Docker Compose: SQL Server + API skeleton + broker with `/health` | backend | `/health` green, `/health/ready` reports `database` + `mqtt-broker` | **Complete** — stack up and healthy; `/health/ready` reports both checks Healthy; the DB-outage drill returns `503` in 3.0 s and recovers in 7 ms with zero API restarts |
| 1.5 | Bench bring-up: SHT31 + BH1750 (+ DS18B20) on a breadboard, serial print at 1 Hz | firmware | Serial shows plausible values; `04-quality/03` §2 checklist partially signed | **Not started** — no hardware has been connected; `main.cpp` reports placeholder values and the QA checklist is unsigned |
| 1.6 | Sensor accuracy spot check (fridge/room/lamp, hygrometer comparison) | firmware | Numbers within datasheet tolerance, recorded in the QA log | **Not started** — depends on 1.5 |
| 1.7 | Flutter app scaffold: routing, theme, providers wired to a stub API, `flutter test` green | app | App runs, empty screens, tests green | **Complete** — `flutter analyze` clean, 19 tests pass, provider wiring and en/vi localisation in place |
| 1.8 | `07-appendices/05` literature pass 1 (collect sources, mark verified rows) | doc/report | ≥ 60% of threshold rows have a citation | **Partial** — every seeded band carries a `SourceRef`, but **0 of 14 rows are signed**, so 14 of the 17 bands still read `PENDING VERIFICATION` (the three light bands are inert by design and cite §5 row 12 instead). The two sides are now held together mechanically: `SourceUrl` is `null` while a range is unverified rather than a dead placeholder link, and `ReferenceDataSeeder.BandsAwaitingVerification` + `SchemaAndSeedingTests` fail CI if the checklist and the database disagree. What is missing is still a person with a book open: the page/table column, the signature and rows 12–13's two statements |

**DoD:** `README` quick start reproduces the environment on a teammate's machine in ≤ 30 min; sensor data on
serial; CI green. **Risk burn-down:** toolchain/JDK/Wi-Fi-band issues surface here, not in week 5.

**DoD status, measured 2026-09-21:** two of the three lines hold — the quick start has been run repeatedly on this
machine, and CI is green — but *"sensor data on serial"* does not, because 1.5 and 1.6 are unstarted. **M1 is
therefore not closed**, and the milestone's own goal statement ("one real sensor produces a number") is the single
line keeping it open. Everything the milestone asked for that does not need a breadboard is done, and the Status
column above records how each was verified rather than that it was attempted.

**Re-measured 2026-09-23:** the milestone gained its first real evidence set on 2026-09-22 —
`06-report/snapshots/` holds the live `/health/ready`, `/version` and `/metrics` responses plus three captured
dashboard pages, produced from the running stack rather than drawn. `06-dashboard-health.png` is the one that shows
a complete path today: browser → nginx → API → SQL Server + MQTT broker. That pass also found a genuine defect,
now fixed: the dashboard reported *any* non-2xx as an unreachable server, so the `404` from the not-yet-built
`/api/v1/...` routes read as "cannot reach the server" and implied the animal was unmonitored when in fact nothing
was wrong. Failure messaging is now classified once (`web/legacy/js/api.js` → `failureKind`), with the distinction
recorded as a UX rule (`02-design/04` §6) and a drill that would catch its return (`04-quality/03` §5.11); the
four cases were exercised in a browser on 2026-09-23. **The DoD verdict is unchanged** — two of three lines hold,
and *"sensor data on serial"* is still the only missing line. What moved: 1.1 went from "decision recorded" to
"decision recorded and consistent across the doc set" (`ADR-016`), and M1 now has report-grade screenshots of
what it actually delivers instead of prose claims about it.

**Re-measured 2026-09-29:** no new gate was run, because 1.8 moved on the documentation side only. The checklist
gained its 14th row: the seeded `Arid` **surface** band (38–45 °C) is now in §3 and in §5, closing the coverage gap
where the database shipped a band no document described (the rule is still "pick an answer and make §3, §5 and the
seeder agree"). The **other** gap was left open on purpose — row 7 verifies an `Arid-cool` ambient band the seeder
never creates, and closing it means either seeding a fourth profile or deleting a row, both of which move band
counts quoted in `README`, here and `05-release/01`. So of the three M1 tasks still short of complete, two (1.5,
1.6) are short of hardware and one (1.1) is short of a mentor's dated confirmation; **1.8 is short only of a
person with the books open**. The DoD verdict stays 2 of 3.

Beyond M1: task **2.1** (domain entities, EF Core model, `InitialSchema` migration, seeders for the metric
dictionary, three profiles and their bands) is also complete and enforced by the `integration` CI job. The M2
table below is not starting from zero, and as of 2026-10-04 its backend half — FR-01, 2.2, 2.3 and 2.4 — is done,
with the per-task evidence in the progress blocks that follow the table.

---

## Prototype baseline — the TERRAGUARD web prototype (landed 2026-10-02, recorded 2026-10-03)

Not a milestone: a deliverable that arrived **outside** the plan, recorded here so the M4 tables below are read
against reality rather than against the plan as it was written in September. The decision is `ADR-017`.

| What exists | Where | State |
|---|---|---|
| **TERRAGUARD** prototype — 8 screens (login, dashboard, terrariums, detail, devices, alerts, history, settings) | `web/` — React 19 + Vite + TypeScript + Tailwind v4 | Built; **mock data only**, Vietnamese only, outside CI, covered by no test case |
| M1 static dashboard — Live / Wallboard / Health | `web/legacy/` | Unchanged, and still the only web surface with a real API behind it |
| Committed Vite build (3 files) | `web/dist/` | Deliberate (`ADR-017`): keeps npm out of the demo path |

**What it is not.** It is not task 4.10, and no number on it is a measurement: its alerts are `value > max` over
mock bands, and it contains no dwell, hysteresis, phase or dedupe (`ADR-005`). M4's DoD — every screen showing real
data with a timestamp — is therefore still entirely ahead of it, and a rehearsal must not be run from it.

**What it changed.** The dashboard pages moved path (`web/` → `web/legacy/`), the compose `web` mount broke
(**BUG-03**), and `web/dist/` entered the repository against the standing "no build output" rule. The three tasks
4.12–4.14 are the cost of absorbing it. They were added to M4 rather than given a milestone of their own because
**4.13 decides what M4's web surface actually is — answered on 2026-10-03 as `ADR-018`: `web/legacy/`. Revoked on
2026-10-06 by `ADR-019`**, which promotes TERRAGUARD instead and adds 4.15–4.20 to carry it out; read the M4 section
below for the live plan, and `ADR-018`'s status line for the record of what was decided and why.

**Ownership.** `DOC` — the same track that owns 4.10 — because the alternative is a prototype nobody owns and a
dashboard nobody reconciles it with, which is how one repository ends up documenting two contradictory UIs.

---

## M2 — Data pipeline (8 days)

**Goal:** a real sample walks from the terrarium into SQL Server and back out through the API.

| # | Task | Track | Acceptance |
|---|---|---|---|
| 2.1 | Domain entities + EF Core model + `InitialSchema` migration + seeders (metrics, 3 profiles, thresholds) | backend | `TC-I-01/02`; DB has seeded profiles with sources |
| 2.2 | Device self-register + claim + credentials (hash, rotate, revoke) | backend | `TC-I-05/13`; UC-01 walkthrough on paper |
| 2.3 | MQTT broker hosted in API; subscriber; auth against credentials; TLS on 8883 | backend | Bad credentials refused + audited; `1883` loopback-only |
| 2.4 | `IngestWorker` + `IngestPipeline` stages + counters | backend | `TC-U-01…09`, `TC-I-01/02/03` green (TC-I-04's health half green; its `sensor_fault` half is 3.3, see the 2.4 block below) |
| 2.5 | Firmware: sampler task, filters, ring buffer, MQTT publish, LWT, health | firmware | 60 s samples visible in DB; `TC-U-FW-*` (native tests) green |
| 2.6 | Firmware: HTTPS fallback + back-fill after outage | firmware | `TC-I-08` passes with a 30-minute broker stop |
| 2.7 | Provisioning: SoftAP portal + self-register + claim code on OLED | firmware | Code appears within 60 s of boot; `TC-I-05` |
| 2.8 | REST: readings/latest, readings (range, bucketing), coverage, terrariums CRUD | backend | `TC-I-10/11`; p95 within NFR-01 on the seeded dataset |
| 2.9 | SignalR hub + broadcast on ingest | backend | A browser console client receives a push < 1 s after ingest |
| 2.10 | First end-to-end: real node → broker → DB → `readings/latest` | all | `TC-E2E-01` recorded with a screenshot for the report |

**DoD:** the chain works with the real device, and the pipeline survives a broker restart without losing a
sample. This is the milestone where the design either holds or is corrected — expect ADR updates.

**Progress so far (measured 2026-10-03, extended 2026-10-04 and 2026-10-06).** M1 is closed except its hardware line and 2.1 is complete, so M2 starts
from a schema-current database rather than from scratch. **Task 2.2 needed a prerequisite: user authentication
(FR-01) is implemented**, because `POST /devices/claim` is `Owner`-gated and there was nothing to be `Owner` with.
What exists in `src/`:

| Piece | Where | Verified by |
|---|---|---|
| Registrations, sessions, profile, change-password | `Application/Identity/AuthService.cs`, `Api/Endpoints/AuthEndpoints.cs` | 67 new unit tests; live HTTP run below |
| PBKDF2-HMAC-SHA256, 210 000 iterations, **stored per user** so a stale hash is upgraded on the next login | `Infrastructure/Security/Pbkdf2PasswordHasher.cs` | `TC-U-32` |
| HS256 access tokens, 15 min, `sub`/`role`/`iat`/`exp`/`jti`/`ver` | `Infrastructure/Security/JwtAccessTokenService.cs` | `TC-U-33` |
| Single-use refresh rotation; reuse of a consumed token revokes the whole family | `AuthService.RefreshAsync` | `TC-U-34` |
| Failed-login throttling, 5 per identifier / 20 per address per 15 min | `Domain/Identity/LoginThrottlePolicy.cs`, `Infrastructure/Security/InMemoryLoginThrottleStore.cs` | `TC-U-35` |
| 10 requests per minute per address on the whole `/api/v1/auth` group | `Program.cs` rate-limit policy | live 429 below |

Run end to end against a real SQL Server and a real HTTP surface: register `202`, login `200` (role `Owner`),
`/me` `200`, refresh `200`, reuse of the consumed token `401 token_reused`, the rotated token afterwards
`401 refresh_token_invalid`, wrong password `401 invalid_credentials`, no token `401 unauthenticated`, and the
11th auth call inside a minute `429 rate_limited` — all as RFC 7807 with a stable `code`. **121 backend unit tests
pass** (was 54).

**What is deliberately still open in FR-01.** `register` answers identically whether or not the identifiers were
free, so there is no email-confirmation flow at all (limitation L-02 — there is no email infrastructure). The other
half of L-02 is closed by the password-recovery block below: resetting a forgotten password no longer means
recreating the account, and what remains of the limitation is the transport, not the flow.
The common-password deny-list carries the highest-frequency subset of the documented top 1 000, to be completed in
M5's hardening pass. The `Technician`/`Viewer` half of `TC-U-36` is no longer waiting on anything: 2.2's endpoints
carry the policies, and both refusals were seen in the live run below.

**Task 2.2 — self-register, claim, credentials, rotate, revoke.** Complete on the HTTP surface. The rules are
`02-design/06` §5 and the contract is `07-appendices/03` §2:

| Piece | Where | Verified by |
|---|---|---|
| Claim code: 31-symbol alphabet, 8 characters, 15-minute TTL, single use, normalised so `k7m2-qp4t` = `K7M2QP4T` | `Domain/Devices/{ClaimCode,DeviceProvisioningRules}.cs` | 27 new unit tests |
| Anonymous self-register throttled 1 per address per 5 min, 20 per hour globally | `Domain/Devices/OnboardingThrottlePolicy.cs`, `Infrastructure/Security/InMemoryOnboardingThrottleStore.cs` | unit tests + live `429` |
| Secret: 256-bit CSPRNG, base32 (52 chars), stored as `SHA-256(secret ‖ 16-byte salt)`, compared with `FixedTimeEquals` | `Infrastructure/Security/{Base32,DeviceCredentials,ClaimCodeGenerator}.cs` | unit tests + the stored row shape |
| Use cases: self-register, claim, rotate (10-minute grace), revoke, `VerifyCredentialAsync` for the telemetry path | `Application/Devices/DeviceProvisioningService.cs` | unit tests + live run below |
| Routes, `Owner`-gated except self-register, policies from the role matrix | `Api/Endpoints/DeviceEndpoints.cs`, `Api/Security/AuthorizationPolicies.cs` | live `403 insufficient_role` |

Run end to end against a real SQL Server and the real HTTP surface — 50 assertions, all green: `self-register`
`201` with an 8-character code from the documented alphabet and a ~15-minute expiry, a second attempt from the same
address `429 rate_limited`, a malformed body `400 registration_invalid`; `claim` anonymous `401 unauthenticated`,
unknown / malformed / consumed / superseded codes all `404 claim_code_invalid`, a **foreign** terrarium
`404 not_found` (not `403`, so ids cannot be probed), the right code `200` with a 52-character base32 secret, and a
claim into a terrarium whose live device is bound `409 terrarium_already_bound`; `rotate-secret` `200` with a fresh
secret and a 10-minute `previousUsableUntilUtc`; `revoke` `204` and idempotent, after which `rotate-secret` is `404`
and both credential rows read `32/16/revoked`; re-registering a **claimed** chip `409 device_already_registered`
with the `deviceId` and no code, re-registering an **unclaimed** chip `409` with the same `deviceId` and a **fresh**
code; `Technician` `403 insufficient_role` on claim and rotate. **186 backend unit tests pass** (was 121).

**What is deliberately still open in 2.2.** Four caveats, three of which a later task has to clear:

- **Revoking does not disconnect a live MQTT session.** A revoked board is refused on its next connect or publish
  rather than mid-session, because the kick needs the broker that task 2.3 owns.
- **No audit rows are written** for claim, rotate or revoke. `BR-18.4` asks for them and `02-design/02` §3.18
  specifies an `AuditLog` table, but that table is not in `InitialSchema` — it was never created in M1, so the
  acceptance line "audit rows written" in `TC-I-13` cannot pass yet. Task 2.3 inherits this directly, because its
  own acceptance line is "bad credentials refused **+ audited**".
- **The last write is not guarded against a race.** Two simultaneous claims of one terrarium can both pass the
  pre-check; the filtered unique index `IX_Device_TerrariumId` then refuses the second insert and the caller sees a
  `500` instead of `409 terrarium_already_bound`. Correct, but unpolished until exception translation lands.
- **One address buys one registration per five minutes**, so every board behind one NAT shares the budget — on a
  home network a second board waits out the window. That is what `07-appendices/03` §2.2 specifies (a demo
  setting), but it is worth knowing before a demo with two boards.

**Task 2.3 — broker authentication, TLS, topic ACL, session kick.** Complete. The broker now does everything
`02-design/06` §4 and `07-appendices/03` §3.1 require of it:

| Piece | Where | Verified by |
|---|---|---|
| A connection needs `username = deviceId` and a password matching a usable `DeviceCredential`; everything else gets the identical refusal (BR-05.1/BR-05.3) | `Infrastructure/Mqtt/MqttBrokerHostedService.OnValidatingConnectionAsync` | 3 live refusals + `mqtt_rejected_connections_total` |
| Two listeners: TLS on `8883` for devices, plaintext on `1883` bound to **loopback only** and switchable off entirely (`Mqtt:DisablePlaintextEndpoint`) | `MqttBrokerHostedService.BuildOptions` | `netstat` in both shapes, live `1883`/`8883` runs |
| Own-prefix-only ACL: publish on `telemetry`/`health`/`status`/`events`/`ack`, subscribe on `cmd`, nothing else, no wildcards, no other device's id | `Domain/Devices/MqttTopicScheme.cs`, used by both interceptors | 20 new unit tests + live refusals |
| Revoke closes the live session (BR-05.4) | `Application/Abstractions/IDeviceSessionRegistry.cs`, `Infrastructure/Mqtt/DeviceSessionRegistry.cs`, called from `DeviceProvisioningService.RevokeAsync` | live: closed in **0.0 s**, and the device cannot reconnect |
| Refused publications are refused *and* drop the client, so a session held by a since-revoked device dies on its next publish | `OnInterceptingPublishAsync` | live |

Run against a real TLS listener with a self-signed certificate, and checked with `paho-mqtt` (30 assertions, all
green): anonymous, wrong-secret and unknown-device connects all answered `Bad user name or password`; a claimed
device connected over TLS and over plaintext; wildcard and cross-device subscriptions refused; a publish under
another device's prefix refused and the session dropped while a fresh connection still worked; `POST
/devices/{id}/revoke` closed the live session immediately and the same credential could not reconnect; and in the
release shape (`DisablePlaintextEndpoint=true`) `1883` was not listening at all while TLS kept working.
**226 backend unit tests pass** (was 186).

**What is deliberately still open in 2.3.** One thing, now that 2.4 has landed:

- **No audit rows.** `BR-18.4` wants `device.secret_rotated`, `device.revoked` and the rejected-connection cases
  in an `AuditLog`, and that table is still not in the schema (see 2.2 above), so this acceptance line reads
  "rejected and counted, not yet audited". The counters and the structured warnings are what exists today.

The other half of that acceptance — "nothing consumes the accepted publish" — is closed by the block below.

**Task 2.4 — `IngestWorker` and the ingest pipeline.** Complete, with two placeholders declared at the fan-out
boundary rather than left implicit. Each stage is a separate type so each rule has one implementation and one
test, and the pure ones have no database in front of them at all:

| Stage | Where | Verified by |
|---|---|---|
| Bytes → document | `Infrastructure/Ingest/JsonTelemetryPayloadParser.cs` | adapter tests: tolerance, unknown keys kept for the validator to judge, the 32 KB limit |
| Schema, shape, timing (V-01…V-03, V-07…V-09, V-10) | `Application/Ingest/TelemetryPayloadValidator.cs`, `Domain/Readings/TelemetryIngestRules.cs` | `TC-U-01…04` plus the timing rules |
| Device: match, lifecycle, credential (V-04/V-05) | `Application/Ingest/DeviceAuthenticator.cs` | `TC-U-05` |
| Plausibility, never a refusal (V-06) | `Application/Ingest/PlausibilityGuard.cs` | `TC-U-06…08` |
| Calibration on `Value`, never on `RawValue` (BR-07.4) | `Application/Ingest/{DeviceCalibration,CalibrationApplier}.cs` | `TC-U-09` |
| Dedupe + stage the rows (DI-02/DI-03) | `Application/Ingest/TelemetryWriter.cs` + `Infrastructure/Ingest/EfTelemetryStore.cs` | `TC-I-01…03` |
| Device state: `LastSeenAt`, status, firmware, health denorms | `Application/Ingest/DeviceStateUpdater.cs` | `TC-I-01`, and `TC-I-04`'s health half |
| Transport, back-pressure, counters | `Infrastructure/Ingest/{InProcessTelemetryBus,IngestWorker,IngestOutcomeRecorder}.cs`, broker `OnInterceptingPublishAsync` | the live run below |
| Fan-out after the commit | `Infrastructure/Ingest/PendingFanOut.cs` (**placeholder**) | the recorder tests assert a failed push cannot fail a stored batch |

Two decisions worth reading back. **The transport hop carries raw envelopes, not batches**: the design's sketch
deserialises in the reader and queues the batch, but a payload that is not JSON has to be *counted* under
`schema_invalid`, and the writer loop is the one place that can count anything — so the queue holds bytes and the
parse is the writer's first stage. **Back-pressure is the write itself**: the broker is in-process (ADR-012), so
MQTTnet sends the QoS 1 PUBACK only after `InterceptingPublishAsync` returns. Awaiting the bounded channel write is
therefore the "stop acking so the broker holds the messages" rule of §02-design/03 §1, with no timeout on purpose —
a timeout would have to choose between losing the batch and failing the broker, and "the device retries" is already
the right answer.

Run against a real SQL Server and a real MQTT listener (`paho-mqtt` over loopback, 2026-10-04): a batch of two
samples published twice produced **2** `TelemetrySample` rows with contiguous sequences `1, 2`, **8**
`MetricReading` rows with `Value = 28.750` / `RawValue = 28.900` intact, the device's `LastSeenAt`,
`FirmwareVersion = 1.2.0`, `SignalStrengthDbm = -63` and `FreeHeapKb = 142` from the batch's health block, and a
`DeviceHealthSample` row; `/metrics` read `ingest_samples_total=2`, `ingest_duplicates_total=2`,
`ingest_rejected_total=0`. A sample of `tf = 85` was **stored** with `QualityFlags = 2` (the row exists, the flag
is what will exclude it), and a 121-sample batch and a malformed body were refused as `payload_too_large` and
`schema_invalid`, taking `ingest_rejected_total` to **2**. **325 backend unit tests pass** (was 226) and the
integration project passes **15 of 15** against a fresh database, `TC-I-01…03` among them.

**What is deliberately still open in 2.4.**

- **The fan-out has no consumer.** `PendingTelemetryBroadcaster` (2.9) and `PendingEvaluationQueue` (3.2/3.3) are
  wired into the worker and do nothing, so a stored sample is broadcast to nobody and evaluated by nobody. That is
  why `TC-I-03`'s "zero alerts created" holds *trivially* rather than because the evaluator skipped the flagged
  row — the flag is on the row, which is what the evaluator will read.
- **`TC-I-04`'s `sensor_fault` half moved to 3.3.** The task's acceptance named it, but a `sensor_fault` arrives on
  `sr/v1/d/{id}/events`, and "humidity is `Unavailable`" needs the `SensorFault` derived signal — which is 3.3, and
  which has no table to record it in today (`InitialSchema` has no device-event storage, the same class of gap as
  the missing `AuditLog`). The health half of the test is green; the fault half is recorded against 3.3 where the
  signal lives, rather than half-built here.
- **Only the `telemetry` channel is forwarded.** `health`, `status` and `events` are part of the topic scheme but
  have no consumer yet. The broker leaves them unforwarded on purpose: accepting a status payload and then ignoring
  it would look like it worked.
- **The HTTPS fallback endpoint shipped 2026-10-06** (`POST /api/v1/ingest/http`,
  `Api/Endpoints/IngestEndpoints.cs`); what is still open is the *firmware* half of 2.6 — the device-side switch to
  HTTPS and its back-fill loop. `ITelemetryPayloadParser`, `DeviceAuthenticator` (presented secret) and the
  `IngestSource` column were already in place for it, so the endpoint is composition: the same `IngestPipeline` as
  MQTT, summoned synchronously so the `202` can carry real counts, which is what lets a device clear its ring buffer
  after an outage. The credential header's parse lives in `Application/Ingest/DeviceCredentialHeader.cs` as a pure
  function, because a value that parsed to an empty secret would be refused exactly like a wrong one and the bug
  would look like an authentication problem forever.
- **`ClockSkewSeconds` is the observed skew, not the stored difference.** Rule V-07 clamps a future timestamp to
  `ReceivedAt`, and storing the clamped difference would read as zero and hide the fault the column exists to
  surface. A sample from the future is therefore stored with a clamped timestamp *and* its real skew, flagged
  `ClockUnsynced` (16). `QualityFlags` has no `clock_ahead` bit, so that is where the condition lands.

**Task 2.8 — the terrarium read surface (built, in part).** The six routes both clients call are implemented; the
rest of FR-03 (`PATCH`/`DELETE`, thresholds, silences, summaries, exports) is not, and is recorded as open rather
than implied by a green row. Contract: `07-appendices/03` §4.2.

| Piece | Where | Verified by |
|---|---|---|
| List, create, detail | `Api/Endpoints/TerrariumEndpoints.cs`, `Application/Terrariums/TerrariumService.cs` | service tests + the live run below |
| Ownership is a query parameter: foreign, missing and soft-deleted are one `404 not_found` | `Infrastructure/Persistence/EfTerrariumStore.cs` | service tests + live `404` |
| `readings/latest`: newest value per metric, the effective band, the device state | `TerrariumService.LatestReadingsAsync` | service tests + live run |
| Band resolution: an override beats the profile, an exact phase beats `Any` (BR-10.3); the phase comes from the photoperiod (BR-11.2) | `TerrariumService.EffectiveBand`, `Domain/Thresholds/ThresholdPhaseResolver.cs` | `ThresholdPhaseResolverTests` + service tests |
| Status vocabulary, `Unavailable` for a faulted or implausible value, `Maintenance` for a device in maintenance | `Application/Terrariums/TerrariumContracts.cs`, `TerrariumService.StatusFor` | service tests + live run |
| `readings`: bucketing by width, gap-aware series, the 720-point ceiling | `Application/Readings/RangeQueryRules.cs`, `TerrariumService.BucketedPoints` | `TC-I-10` at both levels |
| `coverage`: expected against received, from the device's own interval | `TerrariumService.CoverageAsync` | service tests + live run |
| Dashboard sign-in, session rotation, and the calls above | `web/legacy/js/{api.js,pages/live.js}`, `web/legacy/index.html` | the browser run below |

Three decisions worth reading back. **Status is instantaneous, alerts are not**: a card has to be coloured, so a
reading outside its band reads `OutOfRange` immediately — while the dwell time that turns an excursion into an alert
stays with the evaluator (3.2), which is why an `OutOfRange` reading implies no alert. **`device.status` is
derived**: a node silent for three sampling intervals reads `offline` (FR-07 BR-07.2), because the silence watchdog
is 3.3 and a board unplugged during a demo must not keep claiming to be online. **The bucket grid is global**: an
unaligned `from` puts the first bucket start before the window, which is how a 30-day hourly series would otherwise
return 721 points, so the loop is clamped to the budget (`RangeQueryRules.MaxBucketPoints`) instead of quietly
truncating the client's data.

Run end to end against a real SQL Server and the real HTTP surface on 2026-10-06: register `202`, login `200`
(role `Owner`), `GET /api/v1/terrariums` `200 {"items":[]}`, `POST /api/v1/terrariums` `201` with the species name
resolved and the configured time zone applied, a blank name and an unknown `speciesProfileId` both
`400 validation_failed` with field-level `errors[]`, an unknown id `404 not_found`, and `coverage` without a bound
device `409 device_not_bound`. With a device and three samples in place, `readings/latest` returned `tempC = 35` as
`Critical` against the **night** band `24…28` of an `Arid (desert)` profile at 21:57 local time (its day band is
`38…42`), `humidityPct = 50` as `OutOfRange` against `30…40`, and `device.status` derived to `offline` once the
newest sample was six minutes old; `readings?metric=tempC` over 1 h returned `bucket=raw` with 3 points, over 24 h
`bucket=5min` with the gaps as null points, over 40 days `400 range_too_large`, an unknown metric
`400 metric_invalid`, and an unaligned 30-day window exactly 720 points; `coverage` over 1 h at a 60 s interval
reported `expected=60, received=3, coveragePct=5`. **386 backend unit tests pass** (was 325) and the integration
project passes **15 of 15** against a fresh database — the two failures seen against the long-lived dev volume are
stale seeded rows from an earlier revision, not this change.

The same run doubled as the client check: `web/legacy/index.html` served on `8081` shows the sign-in form when
signed out (no invented numbers behind it), and after signing in renders the real values — `Nhiệt độ 35.0°C ·
Nguy hiểm · Ngưỡng 24.0–28.0 °C` and `Độ ẩm 50%RH · Ngoài ngưỡng · Ngưỡng 30–40 %RH` — from
`GET /api/v1/terrariums` and `readings/latest`, with the browser confirming
`Access-Control-Allow-Origin: http://localhost:8081`.

**What is deliberately still open in 2.8.**

- **`PATCH`/`DELETE`, thresholds, silences, summaries and exports are not built.** FR-03's update and delete halves
  and FR-09's summaries are still designs. `PATCH` is blocked on a `RowVersion` column `Terrarium` does not have, so
  it is a migration plus the optimistic-concurrency contract rather than a route.
- **`alerts` is always `[]` in a `readings` response.** BR-09.3 wants alert overlays, and there are no alert rows to
  overlay until 3.2/3.4. An empty array means "no alerts recorded" and must not be read as "no excursions happened".
- **Hourly buckets are computed from raw samples**, because the rollup worker is 3.6. That is correct at demo scale
  (assumption A-08) and will not be at 24 months of retention, which is what rollups exist for.
- **`readings/latest` walks back at most 50 samples per metric.** A metric absent for longer than that window (a UV
  probe unplugged for a day) is reported as having no reading — the same answer as never having reported, which
  removes the distinction between "stopped reporting" and "never reported".
- **The dashboard's session is a demo session.** `web/legacy` keeps the tokens in `localStorage` and rotates once on
  a `401`; there is no idle timeout, no per-role UI gating, and the wallboard shares whatever the Live page signed in
  as. Per tasks 4.10–4.14 that surface is still M4 work.

---

## Password recovery — the FR-01 follow-on that closes half of L-02 (built 2026-10-06)

Not a numbered M2 task: it is a change to FR-01's interface requested after the M2 surface was already standing, so
it is recorded the way the FR-01 prerequisite for 2.2 was. Contract: `07-appendices/03` §4.1; decisions:
`02-design/06` §2; schema: `07-appendices/02` §3.13.

| Piece | Where | Verified by |
|---|---|---|
| Backup recovery code: 31-symbol alphabet without `0/O/1/I/L`, 20 characters (≈ 99 bits), normalised so `dem2d-em2de-m2dem-2dem2` = `DEM2DEM2DEM2DEM2DEM2` | `Domain/Identity/RecoveryCode.cs` | 6 new unit tests |
| Salted `SHA-256(secret ‖ 16-byte salt)` in one place, shared with device credentials | `Infrastructure/Security/Sha256SecretHasher.cs` | `TC-U-51` |
| `register` returns a code on **both** paths — a decoy when the identifier is taken, so the response stays identical | `AuthService.RegisterAsync` | `TC-U-51`, live run |
| `POST /auth/recover` spends the backup code, rotates it, ends every session | `AuthService.RecoverAsync`, `Api/Endpoints/AuthEndpoints.cs` | `TC-U-52`, live run + browser |
| Single-use server-issued code, 30-minute TTL, **unsalted** hash so the row is found by the value presented | `Domain/Identity/PasswordResetCode.cs`, `IUserStore.FindPasswordResetCodeAsync` | `TC-U-53` |
| `POST /auth/forgot-password` always `202` with an empty body; `POST /auth/reset-password` spends the code, rotates the backup code, ends every session | `AuthService.{ForgotPasswordAsync,ResetPasswordAsync}` | `TC-U-53/54`, live run + browser |
| Delivery as a port; the demo's implementation writes the code to the log behind `PasswordReset:LogCode`, which is **false** in `appsettings.json` and true only in `Development` | `Application/Abstractions/IPasswordResetNotifier.cs`, `Infrastructure/Security/LogPasswordResetNotifier.cs` | `TC-U-55` |

Three decisions carry the feature. **There are two codes because there are two moments.** Registration is the only
time the server can hand the keeper something without a delivery channel, so it returns a backup code; the issued
code covers the keeper who no longer has it, and it needs no mail server either — only a working sender. **Both
paths are non-disclosing.** An unknown identifier, a disabled account, a spent code and a foreign code all answer
with one opaque `401`, and `forgot-password` answers `202` even when it issued nothing, so neither endpoint can be
turned into a question about whether an account exists (BR-02.2). Because a `500` on the known-account path would
be exactly that oracle, `ForgotPasswordAsync` commits the code first and then calls the notifier inside a guard that
tolerates a throwing implementation. **A password change is not a patch, it is a reset**: both paths rotate the
backup code and revoke every refresh token, because the person who forgot the password is not necessarily the only
one who has it.

Run end to end against a real SQL Server and the real HTTP surface: `register` `202` with a 20-character code;
`forgot-password` `202` with **size 0** for a live account and for `nobodyatall9876` alike, with a code row created
only for the first; the code typed lower-case with separators accepted by `reset-password` `200` and refused a
second time and after its replacement was issued (`401 invalid_reset_code` — byte-identical to the answer for a
wrong code, a foreign account's code and an unknown identifier); the old password `401 invalid_credentials` and the
new one `200`; a session issued before the reset `401 refresh_token_invalid` afterwards; and the rotated backup code
working through `POST /auth/recover`. **422 backend unit tests pass** (was 386) and the integration project passes
**15 of 15** against a fresh database.

The same run doubled as the client check: on `web/legacy/index.html` the recovery form offers both paths — *Gửi mã
đặt lại* relabels the field to *Mã đặt lại*, states that the code went to the server log, and the reset completed in
the browser with the rotated backup code shown once and the identifier carried to the sign-in form, which then
signed in with the new password.

**What is deliberately still open.**

- **The code is delivered by a log line, not by email.** That is limitation L-02's remainder: the flow is complete
  and testable, but a real keeper cannot receive the code on a host whose log they cannot read. `IPasswordResetNotifier`
  exists precisely so the SMTP sender replaces one registration in `DependencyInjection` and nothing else.
- **Issued codes are not swept.** A spent or expired row stays until the account is deleted. The rows are tiny and
  the index is on `(UserId, ExpiresAt)`, so this is a housekeeping gap rather than a correctness one — it belongs
  with `RetentionSweeperWorker` (task 5.3).
- **`PasswordReset:LogCode` must never be turned on in production.** It is off in `appsettings.json` and on in
  `appsettings.Development.json`; a production log holding a live credential is a leak, which is why the log
  implementation says plainly when it delivered nothing.
- **The dashboard's recovery form is M4 work in every other respect.** Both paths live on the Live page, and the
  Flutter app has no login screen yet (task 4.10), so the recovery UI exists in `web/legacy` only.

---

## M3 — Domain logic (7 days)

**Goal:** the system can tell a keeper something they did not already know, correctly.

| # | Task | Track | Acceptance |
|---|---|---|---|
| 3.1 | `ThresholdService` + resolution order + `effectiveThresholds` endpoint + `ThresholdSnapshot` | backend | `TC-U-21…26`; UC-03 flows verified |
| 3.2 | `ThresholdDecision.Decide` (pure) + `EvaluatorWorker` + ordered queue + `EvaluationState` | backend | `TC-U-10…20` (dwell/hysteresis/escalation matrix) green |
| 3.3 | Derived signals: `DeviceSilent`, `SensorFault`, `DeviceClockSkew` | backend | `TC-U-27…31`, `TC-I-09` |
| 3.4 | Alert lifecycle: open/ack/resolve/silence + audit + role gating | backend | `TC-I-07`, `TC-U-36` |
| 3.5 | `NotificationDispatcher` + policy matrix + FCM + Telegram + inbox + retries | backend | `TC-U-37…45` (policy matrix), live Telegram message demoed |
| 3.6 | Rollup worker + daily summary worker + exposure index maths | backend | `TC-U-46…50`, recomputation after back-fill (`TC-I-06`) |
| 3.7 | Ops: `/metrics`, structured logs, audit endpoints | backend | `TC-I-15` |
| 3.8 | `07-appendices/05` literature pass 2 — every shipped row cited | doc/report | 100% citation gate met |

**DoD:** inducing a real excursion (lamp on / ice pack) produces exactly one alert and one notification,
and a 4-minute disturbance produces nothing. **This is the milestone to demo to the mentor.**

---

## M4 — Clients (10 days, overlaps M2/M3)

**Goal:** everything the backend knows is visible in a way a keeper would accept using.

**One web surface, and it is TERRAGUARD — wired.** M4 opened with two web surfaces: `web/legacy/` (static, real API)
and the mock-data TERRAGUARD prototype in `web/` (ADR-017). Task 4.13 named legacy the milestone's surface
(`ADR-018`), and **`ADR-019` (2026-10-06) revokes it**: the prototype is promoted to the M4 web surface, and it
carries the obligations it was previously excused from — real data, server-side verdicts, vi+en, §3 tokens and CI
coverage. Tasks 4.15–4.20 are that promotion, staged by endpoint availability; `web/legacy/` is retired by 4.19.
4.12–4.14 stay closed as the work that produced the decision and the labelled reference. Until a screen is wired it
keeps its mock-data notice, and that notice goes when the mock data does — not before.

| # | Task | Track | Acceptance |
|---|---|---|---|
| 4.1 | Auth flow in app (login/register/refresh/logout, secure storage) | app | `TC-W-01…03` |
| 4.2 | Home dashboard: metric cards, band labels, staleness, status badge, device header | app | `TC-W-04…07`; UX rules from `02-design/04` §6 enforced by widgets |
| 4.3 | Live subscription + reconnect re-fetch + polling fallback | app | `TC-W-08`; values update < 5 s |
| 4.4 | History: range selector, gap-aware chart, alert overlays, bucket labels | app | `TC-W-09/10` |
| 4.5 | Alerts: inbox, filters, detail, ack/resolve with reason, badge | app | `TC-W-11/12`; role gating verified |
| 4.6 | Threshold editor with inline validation + source display | app | `TC-W-13` |
| 4.7 | Devices: fleet list, claim flow, rename/rebind/rotate/revoke, calibration | app | `TC-W-14`; UC-01 through the UI |
| 4.8 | Report/summary screen + export request/download | app | `TC-W-15` |
| 4.9 | Settings: language, theme, notification prefs, quiet hours; Diagnostics screen | app | `TC-W-16/17` |
| 4.10 | Web dashboard, built in React (`ADR-019`): wallboard + live + history + alerts + thresholds + devices + report | web | Manual checklist `04-quality/03` §3–§5; **decomposed by 4.15–4.20**, and a screen counts only when it renders the API's data with a timestamp |
| 4.11 | Localisation pass (vi default, en), formatting, accessibility labels | app | `TC-W-18`; contrast check recorded |
| 4.12 | Fix the web serving layout: point compose's `web` service at the prototype build and keep the M1 pages reachable at `/legacy/*` | web | `:8081/` serves a working page; `/legacy/wallboard.html` + `/legacy/health.html` still `200`; **BUG-03** closed, with that `curl` pair written down as its regression check |
| 4.13 | Decide the prototype's fate in an ADR: wire TERRAGUARD to the real API and retire `web/legacy/`, or keep it as a mock-data reference and finish the static dashboard | web/doc | ADR appended; `02-design/04` §1.2, `05-release/01` §5 and this table agree with the decision |
| 4.14 | Prototype honesty and single-source pass: mark the UI as mock data, reconcile `web/src/index.css`'s status→colour palette with `02-design/04` §3 (or state why it differs), and decide the Vietnamese-only copy | web | `TC-I-15` either covers the prototype or its exclusion is written into `04-quality/01`; no second palette is left undocumented |
| 4.15 | **Promotion, stage 1 — API client, session and auth screens** (`ADR-019`): an `api` module owning the base URL, token storage, refresh-on-401 and typed errors; `Login.tsx` plus both recovery forms wired to `/auth/*`; a route guard on the shell | web | Sign in, sign out and recover against the real API; a reload keeps the session and a `401` rotates it once; each failure the API can return is rendered from its stable `code`, not a generic message; no invented value on any auth screen |
| 4.16 | **Promotion, stage 2 — wire the read surface**: `Dashboard.tsx`, `History.tsx`, `Terrariums.tsx`, `TerrariumDetail.tsx` against `/terrariums`, `/terrariums/{id}`, `readings/latest`, `readings` and `coverage`, polling until 2.9; delete `mockData.ts`'s `Terrarium`/`Device`/`HistoryDataPoint` shapes | web | Every number on those screens equals the API's; staleness is shown; an empty account, an unbound device and an unreachable API each render their own honest state instead of a plausible number; `04-quality/03` §3 (functional walkthrough) and §5.11 (diagnosis is not wrong) |
| 4.17 | **Promotion, stage 3 — the verdict comes from the server, not the screen (ADR-005)**: card status and band from `readings/latest`; no threshold comparison anywhere in TSX | web | Every card's status, target and phase-resolved band equal the payload's; a search of `web/src` finds no comparison of a metric value against a bound outside displaying what the server sent |
| 4.18 | **Promotion, stage 4 — make it shippable**: vi+en key set shared with the app, `02-design/04` §3 tokens at ≥ 4.5:1, `tsc`/`vite build`/key-parity in CI, and `MockDataNotice` removed | web | `TC-I-15`'s key-parity half passes for the web key set; contrast ratios measured and recorded; the CI job fails on a missing key or a type error; the notice is gone because no screen renders mock data |
| 4.19 | **Promotion, stage 5 — switch the surface and retire `web/legacy/`**: single nginx mount, wallboard rebuilt as a React route, BUG-03 regression pair replaced, `web/legacy/` and its compose mount deleted | web | `:8081/` serves the wired app; `/wallboard` and a deep route both `200` through the SPA fallback; the wallboard renders real values with timestamps and recovers after an outage; `web/legacy/` is gone and no doc or script still points at it |
| 4.20 | **Promotion, stage 6 — wire the remaining screens as their endpoints land**: `Devices.tsx` (device read routes), `Alerts.tsx` (3.4), the threshold editor (3.1), `Settings.tsx` (`PATCH /auth/me`), report/export (3.6), and the live subscription of 2.9 | web | Each screen's acceptance is the matching M2/M3 task's; no screen is wired ahead of its endpoint, and a screen still on mock data carries the notice |

**DoD:** the demo can be given entirely from the phone, with the wallboard on a second screen; every screen
shows real data; no screen renders a value without a timestamp. 4.13 is closed (`ADR-018`, since revoked by
`ADR-019`), and `ADR-019` names TERRAGUARD the web surface — so the wallboard named here is the React route of
4.19, and the mock-data notice is gone by the end of the milestone because the mock data behind it is gone, not
because the label was deleted. A screen still rendering `mockData.ts` counts for nothing.

**Prototype absorption status — measured 2026-10-03, revoked 2026-10-06.** All three follow-on tasks are closed.
They named the M4 web surface and closed the caveats around the prototype; **`ADR-019` then revoked `ADR-018`
outright**, so 4.13's row below is history rather than policy and 4.15–4.20 are the live plan:

| # | Result |
|---|---|
| 4.12 | **Done.** `web/nginx.conf` plus sibling compose mounts (`/srv/prototype`, `/srv/legacy`): `:8081/` serves the committed prototype build, `/legacy/wallboard.html` and `/legacy/health.html` return `200`, and `/dashboard` returns `200` through the SPA fallback (a `/legacy/` miss still `404`s). **BUG-03 closed**, with the `curl` pair in `05-release/01` §5 as its regression check |
| 4.13 | **Decided — `ADR-018`**; **revoked 2026-10-06 by `ADR-019`.** The prototype stays a mock-data UI reference; `web/legacy/` remains the M4 web surface, so task 4.10 extends the static dashboard toward W1–W8 and the prototype supplies the visual reference. `ADR-019` revokes that in full and 4.15–4.20 carry the promotion out; nothing in this row is policy any more — it is the record of what was decided, and `ADR-019` cites it as the reasoning the revocation was decided on |
| 4.14 | **Done.** The UI carries a mock-data notice (`src/components/MockDataNotice.tsx`, rendered on the login page and in the app shell); the status palette is documented as prototype-only with measured contrast ratios (`web/src/index.css`, `02-design/04` §1.2); the Vietnamese-only copy is decided, not pending, and the prototype's exclusion is written into `04-quality/01` §6 |

**Promotion staging — measured 2026-10-06 (`ADR-019`).** What each of TERRAGUARD's screens needs, and what exists
today. The order of 4.15–4.20 follows this table rather than preference: a screen is wired when its endpoints
exist, and not before.

| TERRAGUARD screen | Endpoints it needs | Built today? | Task |
|---|---|---|---|
| `Login.tsx` | `/auth/{register,login,refresh,logout,me,change-password,recover,forgot-password,reset-password}` | **Yes — all nine** (FR-01; the three recovery routes added 2026-10-06) | 4.15 |
| `Dashboard.tsx` | `/terrariums`, `readings/latest`; live push | **Read: yes** (2.8). Push is task 2.9 — the hub is mapped but the broadcaster is a placeholder — so polling until then | 4.16 |
| `History.tsx` | `/terrariums/{id}/readings` (bucketed, gap-aware) | **Yes** (2.8) | 4.16 |
| `Terrariums.tsx`, `TerrariumDetail.tsx` | `GET`/`POST /terrariums`, `GET {id}`, `readings` | **Read + create: yes** (2.8). `PATCH`/`DELETE` and the thresholds routes are not built | 4.16 / 4.20 |
| `Alerts.tsx` | `/alerts`, `GET {id}`, `POST {id}/ack`, `POST {id}/resolve` | **No** — task 3.4; nothing evaluates yet, so `alerts` is `[]` | 4.20 |
| `Devices.tsx` | `GET /devices`, `GET {id}`, `PATCH {id}`, `rebind`, `calibration` | **No** — only the four provisioning routes of 2.2 exist | 4.20 |
| `Settings.tsx` | `PATCH /auth/me` (language, time zone, quiet hours) | **No** | 4.20 |
| Wallboard (W1 — not a prototype screen) | `/terrariums` + `readings/latest` on a kiosk route | **Data: yes** (2.8). The page itself does not exist in React; it is 4.19's work | 4.19 |

Three of the eight screens — plus the wallboard's data — are wireable now. That is why 4.15–4.17 and 4.19 can start
immediately while 4.20 cannot, and why this is a staged plan rather than a rewrite.

---

## M5 — Hardening (6 days)

**Goal:** it survives an awkward demo and a skeptical question.

| # | Task | Track | Acceptance |
|---|---|---|---|
| 5.1 | Security checklist from `02-design/06` §9 executed and recorded | backend | Every row has evidence or an explicit limitation |
| 5.2 | Rate limits, CORS, headers, secret scan, `--vulnerable` package scan | backend | No findings above low; results screenshotted |
| 5.3 | Retention sweeper + purge + export jobs | backend | `TC-I-14`; measured storage for the report |
| 5.4 | 24 h soak with the real device; incident log; alert precision check | all | ≥ 99% coverage, ≤ 1 false positive, no reset |
| 5.5 | Chaos drills: kill broker, kill DB, unplug node, change Wi-Fi, wrong clock | all | Behaviours match `02-design/03` §8; results recorded |
| 5.6 | Calibration offsets applied and documented | firmware | Offset evidence in the QA log; `TC-E2E-04` |
| 5.7 | Performance runs (k6 50 RPS, 30-day dataset) + execution plans captured | backend | NFR-01/02 met or the doc is corrected honestly |
| 5.8 | Bug bash: 1 hour, everyone plays adversary, findings triaged | all | Findings either fixed or listed in `05-release/03` |

**DoD:** every NFR has either a measurement or a documented, explicit shortfall. Nothing is claimed without
evidence.

---

## M6 — Release and report (5 days)

| # | Task | Track | Acceptance |
|---|---|---|---|
| 6.1 | Firmware release build, tagged, version in health payload | firmware | `v1.0.0` flashed and reporting |
| 6.2 | Backend images built and `docker compose up` on the demo host from a clean clone | backend | NFR-08 drill passes with a timer |
| 6.3 | Android release APK + App Bundle signed, installed on a real phone | app | Screenshot of the release-mode app (rubric requires proof) |
| 6.4 | Test suite final run: counts and coverage captured for the report | all | `dotnet test`, `flutter test`, `pio test` outputs saved |
| 6.5 | Source archive `.zip` prepared (excludes `bin/`, `obj/`, `.dart_tool/`, `.env`, secrets) | all | Clean-clone build verified from the archive |
| 6.6 | Report written per `06-report/01-report-outline.md`, with screenshots and the traceability matrix | doc | Rubric checklist 100% covered |
| 6.7 | Demo rehearsal ×2, timed, with a fallback plan (recorded video + seeded data reset script) | all | 15-minute demo runs twice without touching a shell |
| 6.8 | Contribution table completed and agreed by all members | all | Signed off before submission |

**DoD:** submission package complete; the demo works from a cold start; every claim in the report points at
a file, a test, or a screenshot.

---

## FR coverage order (what can be demoed when)

| Milestone | FRs demonstrable |
|---|---|
| M1 | — (environment only) |
| M2 | FR-01 (partial), FR-03, FR-04, FR-05, FR-06, FR-07, FR-09 (partial), FR-16 (partial), FR-18 (partial) |
| M3 | FR-10, FR-11, FR-12, FR-13, FR-14, FR-15, FR-02 (enforced) |
| M4 | FR-08, FR-09 (complete), FR-16 (complete), FR-17 (optional), FR-02 (visible) |
| M5 | NFR evidence for all |
| M6 | Release artefacts + report |

---

## Team allocation (5 people, re-baselined 2026-09-29)

The original three-person allocation is **superseded**: the group has five members, and the project entered
**week 4 of a 10-week semester** with M1's hardware half still unstarted. The week-by-week schedule for
**W5–W10 (2026-10-05 … 11-15)** lives in `03-implementation/08-work-distribution-w5-w10.md`; this table is the
stable summary of who owns what.

| Code | Track | Accountable for | Backup |
|---|---|---|---|
| `FW` | Firmware + hardware + bench/accuracy tests + calibration | M1 close-out, firmware half of M2, task 6.1 | `BE-1` |
| `BE-1` | Backend ingest + API surface + device credentials | M2 | `BE-2` |
| `BE-2` | Threshold engine, alerts, notifications, rollups, hardening | M3, M5 | `BE-1` |
| `APP` | Flutter app (screens, state, widget tests) + release APK | M4 | `DOC` |
| `DOC` | Web dashboard (incl. the TERRAGUARD prototype and tasks 4.12–4.14) + doc set + report + submission + demo | M6 | `APP` |

The **backend is deliberately split across two owners** — `BE-1` for ingest and the API surface, `BE-2` for the
evaluation engine — because it is the largest workstream (70 h in `06-report/02` §3) and one owner would sit on
the critical path for the whole project. Report sections are written by whoever owns the evidence for them and
coordinated by `DOC` (`06-report/01` → "Writing order and owners").

Every milestone has exactly **one accountable owner**. Parallel work is safe because the three interfaces
(MQTT payload, REST contract, UI states) are frozen in `07-appendices/03` and `02-design/04` §5 at M1.

## Standing rules

1. **No id renumbering.** Requirements, tests and ADRs keep their ids forever; superseded items are marked
   superseded, not renumbered.
2. **Doc-first for interface changes.** A changed payload field or endpoint is written into
   `07-appendices/03` in the same PR as the code.
3. **Never fake a number.** If a chart has a gap, the gap is shown; if a sensor is dead, the metric is
   `Unavailable`; if coverage is 40%, it is printed. This rule exists because the entire product value is
   "you can trust the screen".
4. **Demo path stays green.** From M3 onward, `main` must always be demo-able; experiments live on branches.
