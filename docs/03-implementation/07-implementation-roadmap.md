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
dictionary, three profiles and their bands) is also complete and enforced by the `integration` CI job, and 2.3 is
partly in place (the broker is hosted in-process and refuses anonymous connections, but credential verification
against `DeviceCredential` is not written). The M2 table below is not starting from zero.

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
**4.13 decides what M4's web surface actually is — answered on 2026-10-03 as `ADR-018`: `web/legacy/`**.

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
| 2.4 | `IngestWorker` + `IngestPipeline` stages + counters | backend | `TC-U-01…09`, `TC-I-03/04` green |
| 2.5 | Firmware: sampler task, filters, ring buffer, MQTT publish, LWT, health | firmware | 60 s samples visible in DB; `TC-U-FW-*` (native tests) green |
| 2.6 | Firmware: HTTPS fallback + back-fill after outage | firmware | `TC-I-08` passes with a 30-minute broker stop |
| 2.7 | Provisioning: SoftAP portal + self-register + claim code on OLED | firmware | Code appears within 60 s of boot; `TC-I-05` |
| 2.8 | REST: readings/latest, readings (range, bucketing), coverage, terrariums CRUD | backend | `TC-I-10/11`; p95 within NFR-01 on the seeded dataset |
| 2.9 | SignalR hub + broadcast on ingest | backend | A browser console client receives a push < 1 s after ingest |
| 2.10 | First end-to-end: real node → broker → DB → `readings/latest` | all | `TC-E2E-01` recorded with a screenshot for the report |

**DoD:** the chain works with the real device, and the pipeline survives a broker restart without losing a
sample. This is the milestone where the design either holds or is corrected — expect ADR updates.

**Progress so far (measured 2026-10-03).** M1 is closed except its hardware line and 2.1 is complete, so M2 starts
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
free, so there is no email-confirmation flow at all (limitation L-02 — there is no email infrastructure).
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

**What is deliberately still open in 2.3.** Two things:

- **No audit rows.** `BR-18.4` wants `device.secret_rotated`, `device.revoked` and the rejected-connection cases
  in an `AuditLog`, and that table is still not in the schema (see 2.2 above), so this acceptance line reads
  "rejected and counted, not yet audited". The counters and the structured warnings are what exists today.
- **The ingest subscriber is 2.4.** A message that passes the ACL is counted and allowed through the broker, but
  nothing consumes `sr/v1/d/+/telemetry` yet, so a published batch goes nowhere — `IngestWorker` and
  `IngestPipeline` are the next task, and this one stopped at the transport boundary on purpose.

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

**Two web surfaces, one deliverable.** M4 opens with `web/legacy/` (static, real API) *and* the mock-data
TERRAGUARD prototype in `web/` (ADR-017). Task 4.13 named the milestone's web surface on 2026-10-03: `web/legacy/`
carries it (`ADR-018`) and the prototype stays a reference — so the prototype may appear in a screenshot only with
its mock-data notice visible, and never as a measurement.

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
| 4.10 | Web dashboard: wallboard + live + history + alerts + thresholds + devices + report | web | Manual checklist `04-quality/03` §5 |
| 4.11 | Localisation pass (vi default, en), formatting, accessibility labels | app | `TC-W-18`; contrast check recorded |
| 4.12 | Fix the web serving layout: point compose's `web` service at the prototype build and keep the M1 pages reachable at `/legacy/*` | web | `:8081/` serves a working page; `/legacy/wallboard.html` + `/legacy/health.html` still `200`; **BUG-03** closed, with that `curl` pair written down as its regression check |
| 4.13 | Decide the prototype's fate in an ADR: wire TERRAGUARD to the real API and retire `web/legacy/`, or keep it as a mock-data reference and finish the static dashboard | web/doc | ADR appended; `02-design/04` §1.2, `05-release/01` §5 and this table agree with the decision |
| 4.14 | Prototype honesty and single-source pass: mark the UI as mock data, reconcile `web/src/index.css`'s status→colour palette with `02-design/04` §3 (or state why it differs), and decide the Vietnamese-only copy | web | `TC-I-15` either covers the prototype or its exclusion is written into `04-quality/01`; no second palette is left undocumented |

**DoD:** the demo can be given entirely from the phone, with the wallboard on a second screen; every screen
shows real data; no screen renders a value without a timestamp. 4.12–4.14 must be closed in the same milestone:
4.13 decides which web surface carries the DoD, and the prototype cannot be it while its numbers are invented.

**Prototype absorption status — measured 2026-10-03.** All three follow-on tasks are closed, so the M4 web
surface is now named and no prototype caveat is left floating:

| # | Result |
|---|---|
| 4.12 | **Done.** `web/nginx.conf` plus sibling compose mounts (`/srv/prototype`, `/srv/legacy`): `:8081/` serves the committed prototype build, `/legacy/wallboard.html` and `/legacy/health.html` return `200`, and `/dashboard` returns `200` through the SPA fallback (a `/legacy/` miss still `404`s). **BUG-03 closed**, with the `curl` pair in `05-release/01` §5 as its regression check |
| 4.13 | **Decided — `ADR-018`.** The prototype stays a mock-data UI reference; `web/legacy/` remains the M4 web surface, so task 4.10 extends the static dashboard toward W1–W8 and the prototype supplies the visual reference |
| 4.14 | **Done.** The UI carries a mock-data notice (`src/components/MockDataNotice.tsx`, rendered on the login page and in the app shell); the status palette is documented as prototype-only with measured contrast ratios (`web/src/index.css`, `02-design/04` §1.2); the Vietnamese-only copy is decided, not pending, and the prototype's exclusion is written into `04-quality/01` §6 |

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
