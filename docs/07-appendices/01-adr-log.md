# 01 — Architecture Decision Log (ADR-001 … ADR-015)

Append-only. Each entry: **Context → Decision → Consequences → Rejected alternatives.** Numbers are never
reused; a superseded ADR keeps its id and gains a "Superseded by" note.

Status values: `Accepted` · `Provisionally accepted` (review at the stated milestone) · `Superseded` · `Rejected`.

---

## ADR-001 — MQTT as the primary telemetry transport with an HTTPS fallback
**Status:** Accepted · **Date:** 2026-09-21 · **Related:** FR-06, NFR-03

**Context.** A device publishes one small batch per minute and must keep working through broker outages. The
brief suggests WiFi transport to a server or cloud service. Two credible options: MQTT (pub/sub, QoS levels,
Last Will and Testament) or plain HTTPS POST from the device.

**Decision.** MQTT over TLS (`8883`) is the primary path, with `QoS 1`, a retained LWT for fast offline
detection, and a topic scheme under `sr/v1/`. HTTPS `POST /api/v1/ingest/http` is implemented as a fallback
used only after 60 s of MQTT failure.

**Consequences.**
- Broker outage handling and offline detection come from the protocol instead of hand-rolled polling.
- A broker becomes infrastructure to run (hosted in-process with MQTTnet for the demo).
- Two ingest paths must produce identical results — mitigated by both calling the same `IngestPipeline`, with
  the HTTP path covered by `TC-I-01…04`.
- At-least-once delivery means duplicates are expected, hence the `(DeviceId, Sequence)` idempotency rule.

**Rejected.** HTTPS-only (loses QoS, LWT and downlink commands; more device-side retry logic);
a managed IoT platform as the transport (vendor lock-in, no place for our domain logic — see ADR-002).

---

## ADR-002 — ASP.NET Core 10 + EF Core + SQL Server for the backend
**Status:** Accepted · **Related:** NFR-07, NFR-08

**Context.** The backend must ingest, evaluate, aggregate, alert and serve two clients. Team familiarity and
the mentor's reference direction (a comparable reference brief used ASP.NET Core + EF Core + SQL Server) both
point that way. Alternatives: Node/Express, Spring Boot, Supabase/Firebase as the whole backend.

**Decision.** ASP.NET Core 10 Web API with EF Core 10 and SQL Server 2022, deployed with Docker Compose.

**Consequences.**
- Migrations are the only schema path (NFR-08: "DB created by migrations only").
- Filtered unique indexes and check constraints are available and are used for real invariants (ADR-004,
  `02-design/02` §5).
- Requires a JDK/SDK toolchain on the dev machine; the team already has it.
- SQL Server licensing is irrelevant at this scale (Developer/Express container).

**Rejected.** Firebase/Supabase as the entire backend (would remove most of the engineering the report is
supposed to show, and device auth + species thresholds do not map cleanly); PostgreSQL (equally viable, but
the team's SQL Server familiarity reduces schedule risk).

---

## ADR-003 — Flutter app + light vanilla-JS web dashboard
**Status:** Accepted · **Related:** rubric (state management), NFR-06

**Context.** The brief says "dashboard (web **or** app)". The course rubric requires a mobile app with state
management. A web dashboard is invaluable for the demo wallboard and report screenshots.

**Decision.** Flutter (Android) for the app; a static HTML/CSS/JS + Chart.js dashboard with **no build step**.
Both consume the same REST + SignalR API.

**Consequences.**
- One API contract, two clients; UI state vocabulary must stay identical (`02-design/04` §5), verified by
  `TC-I-15` comparing i18n key sets.
- No npm/bundler in the critical path — a demo cannot fail because `npm install` broke.
- Two places to implement a chart; mitigated by sharing the bucket-size contract and the "gap-aware" rule.

**Rejected.** Flutter Web for the dashboard (heavier, requires the CanvasKit download problem already
documented on the team's machine); React (needs a build step and adds a language to the project for no
coursework benefit).

---

## ADR-004 — Normalised telemetry (sample + readings) with hourly rollups
**Status:** Accepted · **Related:** FR-06, FR-09, FR-15, `07-appendices/06`

**Context.** Two shapes are possible: a **wide** table (one row per sample with a column per metric, as in the
reference brief's `SensorLog`) or a **normalised** pair (`TelemetrySample` + one `MetricReading` row per
metric) with a `Metric` dictionary.

**Decision.** Normalised, plus `TelemetryHourlyRollup` and `DailyEnvironmentalSummary` derived tables.

**Consequences.**
- Adding a metric (v2 behaviour/interaction metrics, camera events) is a dictionary row, not a migration.
- Range queries must join readings; mitigated by covering indexes and by bucketing to rollups beyond 48 h.
- Chart queries are naturally "one metric over time", which matches the normalised shape well.
- The `Metric` dictionary gives one place for units, precision and plausibility ranges — used by the firmware
  contract, the API validator and the UI formatter.

**Rejected.** Wide `SensorLog` (simpler queries, but every new metric is a schema change and a sparse-table
problem, and the v2 dataset would be frozen at today's metric list).

---

## ADR-005 — Threshold evaluation server-side only
**Status:** Accepted · **Related:** FR-11, FR-10, UC-03

**Context.** The device could evaluate bands and publish alerts (fewer server round-trips, works offline), or
the server could evaluate every stored sample (single source of truth).

**Decision.** Server-side only. The device reports values, quality flags and faults; it holds no thresholds.

**Consequences.**
- Editing bands takes effect immediately with no reflash (UC-03), which is the behaviour a keeper needs when
  a species' autumn bands change.
- Alert history is centralised and consistent across app/dashboard/telegram.
- The system cannot alert while the backend is unreachable; mitigated by the device buffer, and by the fact
  that a monitoring-only product has no actuation to protect in the meantime.
- Phase (day/night) logic lives with the profile data, not duplicated in firmware.

**Rejected.** Device-side evaluation (would duplicate the band data, require firmware updates for every
tuning change, and create two sources of truth for the same alert).

---

## ADR-006 — Per-device 256-bit secret over TLS; no mTLS in v1
**Status:** Accepted (mTLS = v1.1 candidate) · **Related:** FR-04, FR-05, limitation L-01

**Context.** Devices need a credential. Options: (a) per-device secret as MQTT password / bearer token,
(b) mutual TLS with per-device client certificates, (c) a secure element holding a key.

**Decision.** (a) for v1: 256-bit CSPRNG secret, shown once at claim time, stored server-side as
`SHA-256(secret ‖ salt)`, compared in constant time, usable only over TLS. Claim codes are short-lived,
single-use, distinct from credentials, and return an identical error for unknown/expired/consumed.

**Consequences.**
- Simple provisioning and rotation with a 10-minute grace window; revocation is a single column.
- Physical access to the node allows flash read-out → the secret is recoverable. Accepted, disclosed as L-01,
  with a limited blast radius (one terrarium) and a documented v1.1 path (ATECC608A or `esp_secure_cert`).
- No PKI to operate for the demo.

**Rejected.** mTLS in v1 (CA + per-device cert provisioning on an MCU without a secure element adds a
milestone of work for protection that flash read-out already defeats); a shared fleet-wide secret (one leak
would expose every device).

---

## ADR-007 — Alert channels: FCM push primary, Telegram secondary, SMTP optional
**Status:** Accepted · **Related:** FR-13, risk R-13

**Context.** The brief leaves the alert channel open ("thông báo qua ứng dụng nhắn tin hoặc app").

**Decision.** In-app inbox always; FCM push to the app as the primary channel; a Telegram bot as the second
channel (and the one used in the live demo); SMTP email optional.

**Consequences.**
- Telegram gives a channel that works from a laptop and is trivial to demonstrate live, reducing demo risk.
- FCM pushes require a real device with Play services; if it misbehaves, the demo still shows an alert.
- Channel implementations sit behind `INotificationChannel`, so all policy (quiet hours, severity, rate limit)
  is tested once with fakes.
- Two credential types to manage (bot token, service account) — both in `.env`/mounted files, never in the repo.

**Rejected.** Email-only (too slow to feel like an alert); SMS (cost, and unnecessary for a demo); a custom
websocket-to-phone push (essentially FCM with extra failure modes).

---

## ADR-008 — Monitoring only in v1; no actuators
**Status:** Accepted · **Related:** scope, risk R-12, limitation L-05

**Context.** The system could switch a heat lamp, mister or fan (the reference aeroponic brief does exactly
that). The brief for v1 describes measuring, storing, displaying and alerting.

**Decision.** v1 has no actuator outputs. The device is monitor-only; the backend never sends a control command
that changes the physical environment (`cmd` is limited to `set_config`, `take_snapshot`, `ping`).

**Consequences.**
- A software failure cannot harm the animal; the worst case is a missed alert, which is stated plainly.
- Hardware BOM stays small (no relays/MOSFETs/mains switching, which is also a safety gain for students).
- The evaluator's outputs (alerts) are already the natural trigger for a future actuator layer — a v1.1
  actuator simply subscribes to alert events, so this decision does not block the roadmap.
- A demo of the induced excursion is done physically (lamp/ice) rather than by automation.

**Rejected.** Actuation in v1: safety-critical, expands hardware/failure modes, and scope creep against a
bounded requirement set (FR-01…FR-18).

---

## ADR-009 — Retention: raw 90 days, hourly rollups 24 months, daily summaries indefinite
**Status:** Accepted · **Related:** FR-15, NFR-11

**Context.** Continuous monitoring accumulates data fast relative to a student's laptop, but the v2 AI needs
history.

**Decision.** Raw readings 90 days; hourly rollups 24 months; daily summaries indefinitely; camera snapshots
7 days (feature off by default).

**Consequences.**
- Charts beyond 48 h read rollups, which also serves NFR-01 (bounded payloads).
- Rollups must be recomputable for at least 48 h to absorb back-fill (idempotent upsert).
- A purge is irreversible, so it is audited and requires typed confirmation; alerts are retained for audit.
- The storage estimate (~47 MB per device per 90 days raw) is close to the 60 MB NFR-11 budget, so the sweeper
  is load-bearing and is covered by `TC-I-14`.

**Rejected.** Keep everything (storage growth with no benefit at this scale); 30-day raw retention (too short
to compare seasons, which is a real keeper need).

---

## ADR-010 — No AI/ML in v1; ship a documented dataset instead
**Status:** Accepted · **Related:** scope, `07-appendices/06`

**Context.** The brief explicitly titles v1 "without AI". The temptation is to add an "AI-ish" heuristic to
look impressive.

**Decision.** No model, no classifier, no "smart" scoring in v1. Instead: clean, labelled, retained data with a
documented feature schema, plus label sources (resolve reasons `FalsePositive`/`SensorFault`, alert outcomes,
maintenance windows).

**Consequences.**
- Every v1 statement is verifiable arithmetic with a citation, which is defensible in a viva.
- v2 has a real foundation: `DailyEnvironmentalSummary` is essentially a feature row per day.
- Reviewers may ask "where is the AI?" — answered by the roadmap appendix and the scope table, not by a
  bolted-on model.

**Rejected.** A toy threshold-based "risk score" badged as AI (dishonest labelling, and it would compete for
authorship of the alert semantics with the threshold engine).

---

## ADR-011 — SignalR for live push, REST for history, `provider` for app state
**Status:** Accepted · **Related:** FR-08, rubric (state management)

**Context.** The UI needs "current values within 5 s" and historical ranges. Options: polling only, WebSocket/
SignalR push, or a streaming protocol like SSE.

**Decision.** REST for everything historical and authoritative; SignalR for `readingAdded`, `statusChanged`,
`alertChanged`; clients degrade to polling if the hub is unavailable. Flutter state via `provider`
(`ChangeNotifier` + `Selector`) — one provider per concern, providers own their own cache and staleness logic.

**Consequences.**
- A reconnected client must re-fetch `readings/latest` (documented rule) or it would show pre-disconnect
  values as current.
- SignalR group membership must be authorised on join to avoid a cross-tenant leak.
- Degraded mode is visible ("live updates paused"), never silent.
- `provider` is explicitly on the rubric, and its simplicity suits a 16-screen app without the ceremony of
  a full BLoC/event architecture.

**Rejected.** Polling only (battery and latency, and it makes "live" a lie); BLoC (more boilerplate than the
app's complexity justifies, and `provider` is already accepted by the rubric); SSE (no bidirectional
commands, worse client support).

---

## ADR-012 — Single-host Docker Compose deployment
**Status:** Accepted · **Related:** NFR-08, NFR-09

**Context.** The backend needs SQL Server, a broker, the API and the static dashboard. Options: a managed
cloud, a single VM with manual setup, or a Compose stack.

**Decision.** One `docker-compose.yml` with `db`, `mqtt`, `api`, `web` services, pinned images, named volumes
and an `.env` file for secrets.

**Consequences.**
- Cold start ≈ 3 minutes, reproducible on any machine with Docker — the property that makes NFR-08 verifiable.
- No cloud account, no cost, works offline at the demo venue.
- No horizontal scaling claim is made; the ingest path is stateless so the design does not preclude it.

**Rejected.** Managed cloud (cost, network dependency at the demo, and less reproducibility); bare-metal
install scripts (drifting environments — exactly the class of problem the project should avoid).

---

## ADR-013 — Bilingual UI (vi default, en), localised at render time
**Status:** Accepted · **Related:** NFR-06, FR-13

**Context.** The brief and the users are Vietnamese; the code, comments and doc set are English. Notifications
must be readable by the user, while alert *history* must stay queryable.

**Decision.** UI strings live in ARB files (`app_vi.arb`, `app_en.arb`) and a mirrored `i18n.js` for the
dashboard. The database stores structured alert fields (metric, value, band, duration, state), and the message
text is composed at render/send time from the user's `PreferredLanguage`.

**Consequences.**
- Changing a user's language changes all past notifications' rendering — acceptable and desirable for a
  single-user product.
- No per-language message columns, so history stays queryable by structured fields.
- ARB and `i18n.js` key sets are compared in `TC-I-15` so a translation cannot silently go missing.
- Vietnamese strings are ~20–30% longer than English → the layout checks in `04-quality/03` §4 matter.

**Rejected.** Storing rendered message strings per language (denormalised, unqueryable, and it freezes wording
bugs); English-only UI (wrong for the users, and the brief is Vietnamese).

---

## ADR-014 — Sensor set: SHT31 + BH1750 (+ LTR390, DS18B20) and median-of-5 + EMA filtering
**Status:** Accepted · **Related:** NFR-05, FR-07, `07-appendices/04`

**Context.** Temperature/humidity options: DHT22/DHT11 (cheap, slow, ±0.5 °C/±2–5 %RH, 2 s min interval),
SHT31/SHT35 (I²C, ±0.2–0.3 °C/±2 %RH), BME280 (also pressure, slower to respond in enclosures), AHT20 (cheap
I²C). Light: BH1750 (lux, I²C) vs a photoresistor (uncalibrated) vs VEML7700 (higher range).

**Decision.** SHT31-D for air temperature/humidity, BH1750 for illuminance, LTR390 for UV index (optional but
recommended), DS18B20 for surface temperature. Filtering: median-of-5 at 50 ms spacing, then EMA(α = 0.3).

**Consequences.**
- Accuracy budget (±0.5 °C, ±3 %RH) is achievable with margin for enclosure self-heating.
- The UV index is reported as an index, not a UVB dose in µW/cm², and is labelled indicative.
- Median filtering removes single-sample I²C glitches (the main cause of phantom alerts) while EMA damps slow
  noise without hiding trends.
- Surface temperature enables `GradientWarning`, a genuinely useful signal (burn risk) that costs one probe.

**Rejected.** DHT22 (accuracy and 2 s minimum interval make 60 s sampling with a median filter awkward, and
±5 %RH would force a looser humidity alert band); BME280 (pressure is irrelevant here; slower thermal
response in a still enclosure); photoresistor (uncalibrated, no meaningful lux).

---

## ADR-015 — UTC everywhere; server-authoritative receive time; local-day bucketing in the rollup layer
**Status:** Accepted · **Related:** NFR-10, FR-14

**Context.** Day/night phases, daily summaries and alert durations all depend on time. Devices have unreliable
clocks after a power cut; users think in local days.

**Decision.** All stored timestamps are UTC (`datetime2(3)`). The device supplies `RecordedAt` from NTP and a
monotonic `Sequence`; the server assigns `ReceivedAt`. Skew > 120 s is recorded and flagged. Local-day
bucketing happens **only** in `RollupWorker`/`SummaryWorker`, using the terrarium's `TimeZoneId`, and the
generated `DailyEnvironmentalSummary` stores the `TimeZoneId` it was computed with.

**Consequences.**
- Ordering, duration and comparison logic are timezone-free; only the summary boundary needs a timezone.
- A DST-shifting terrarium timezone (not Vietnam, but supported) can produce a 23- or 25-hour local day — the
  summary stores its own coverage/expected counts so a short day is visible rather than silently wrong.
- Clients render and never bucket, so a device with the wrong timezone setting cannot corrupt history.
- Phases are computed from the photoperiod and the server's view of local time, falling back to server time
  when the device clock is unsynced.

**Rejected.** Local time in the database (DST and multi-device ambiguity, painful queries); device-authoritative
ordering (a device with a bad clock could reorder an entire day's alerts).
