# 01 — Architecture Decision Log (ADR-001 … ADR-022)

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
**Status:** Accepted — **superseded in part by `ADR-017` and `ADR-019`** (the "no build step" half: ADR-017 applied it to the prototype surface the team demonstrates, and `ADR-019` promotes that surface to the deliverable while `web/legacy/` is retired, so the vanilla-JS web half of this decision ends with task 4.19; the Flutter-app half stands) · **Related:** rubric (state management), NFR-06, ADR-017, ADR-019

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
**Status:** Accepted, **superseded in part (2026-10-07) by `ADR-021`** — the Telegram half is no longer in force; "FCM primary, SMTP optional" and the `INotificationChannel` seam stand · **Related:** FR-13, risk R-13

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

---

## ADR-016 — The claim secret reaches the device by an 8-digit pairing token (closes TBC-4, option A)
**Status:** Accepted · **Date:** 2026-09-23 · **Related:** FR-04, FR-05, BR-04.2, ADR-006, TBC-4

**Context.** `POST /devices/claim` returns the 256-bit device secret exactly once (BR-04.2, ADR-006), but at that
moment the device is factory-fresh and unauthenticated: it holds no key that could protect the hand-over, and the
app is the only party that has the secret. TBC-4 asked how the secret physically gets there. The answer decides
what someone else on the same Wi-Fi can observe and how much the owner has to type.

**Decision.** **Option A.** The device displays an **8-digit one-time pairing token** on the OLED (5-minute TTL,
single use). The app posts the secret to the device over the local network — HTTPS with the device's self-signed
certificate pinned by fingerprint — reusing the same channel that already carries the Wi-Fi configuration. Manual
entry (option B, the base32 string typed into the captive portal) is kept **only** as the demo-day fallback if the
pairing endpoint is not finished in time.

**Consequences.**
- The secret is never transcribed by the user, so claiming stays a one-tap flow (UC-01 steps 5–7).
- Firmware gains one authenticated endpoint: it must reject an expired or already-used token, regenerate while in
  provisioning mode, and refuse a second secret while one is stored.
- The pinned self-signed certificate is what makes "the same Wi-Fi" a reasonable trust boundary. The backend leg
  is unaffected — that is TLS with a real certificate (ADR-001).
- A person standing next to the terrarium can read the token off the screen and claim the device; like `L-01`
  (secret readable from flash) this is accepted physical-access exposure, bounded by revocation (FR-05, BR-05.4).
- The choice is invisible to the API: `claim` behaviour, error codes and audit rows are unchanged either way,
  which is why it could be recorded as a decision rather than a schema change.

**Rejected.** Option B as the primary path (no firmware work, but a long base32 string must be transcribed
correctly — an unacceptable demo and support risk). Proxying the secret through the backend (creates a second
authoritative path for a secret the server already stores hashed). BLE-assisted pairing (extra stack for one flow,
and chipset support varies across the cheap dev boards in the BOM).

---

## ADR-017 — The web dashboard gets a React prototype, and `web/` is split around it
**Status:** Accepted · **Date:** 2026-10-03 · **Related:** ADR-003, ADR-005, FR-08, NFR-06, `02-design/04` §1.2, roadmap 4.10 · **Note (2026-10-03):** the BUG-03 consequence below is closed (task 4.12) and this entry's open follow-up — which web surface carries M4 — is settled by `ADR-018` · **Note (2026-10-06):** `ADR-019` **revokes** that settlement — `ADR-018` is revoked in full, the prototype is promoted to the M4 web surface, and the split this entry created is undone when `web/legacy/` is retired (task 4.19)

**Context.** ADR-003 chose a "light vanilla-JS dashboard, no build step" and rejected React because it "needs a
build step and adds a language to the project for no coursework benefit". On 2026-10-02 a commit
(`5581ea1`, branch `tuanTV2`) landed a **React UI prototype of the keeper's screens** under the working title
**TERRAGUARD**: React 19 + Vite 6 + TypeScript 5.7 + Tailwind CSS v4, eight routes (`/` login, `/dashboard`,
`/terrariums`, `/terrariums/:id`, `/devices`, `/alerts`, `/history`, `/settings`), Vietnamese-only copy, driven
entirely by `web/src/data/mockData.ts`. It makes **no network calls at all** (no `fetch`, no `axios`, no
`import.meta.env`, no `SignalR`/`WebSocket`), and its build output (`web/dist/`) was committed. The commit put the
prototype at the root of `web/` and moved the M1 static dashboard to `web/legacy/`, which left the doc set, the
compose `web` service and `.gitignore` describing a repository that no longer existed.

**Decision.** Keep both surfaces, and say which is which rather than blending them:

| Path | What it is | Stack | Data |
|---|---|---|---|
| `web/legacy/` | The **M1 static dashboard** — `index.html` (Live), `wallboard.html`, `health.html` | HTML + CSS + vanilla-JS ES modules + Chart.js 4 | The **real API** (`<meta name="api-base">`), empty/offline states included |
| `web/` | The **TERRAGUARD prototype** — a UI exploration of the keeper's screens | React 19 + Vite 6 + TypeScript + Tailwind v4 + `react-router-dom` + `recharts` + `lucide-react` | **Mock data only**; no request leaves the page |

`web/dist/` stays committed as an **explicit exception** to the "no generated build output in git" rule, so
`docker compose up` can serve the prototype without a Node toolchain in the image — the same reasoning ADR-003
used to reject npm in the critical path in the first place.

**Consequences.**
- The prototype is a **UI artefact, not the dashboard**. It carries no dwell, hysteresis, phase, dedupe or
  escalation logic — those live in the backend (ADR-005) — so its alerts are plain `value > max` comparisons
  against mock thresholds, and no number on it may be quoted as engine behaviour. This is stated in the
  prototype's own summary (`TERRAGUARD_SUMMARY.md`) and repeated here because that is exactly the kind of
  confusion the "never fake a number" standing rule exists to prevent.
- **Two web codebases exist and neither is the M4/M6 deliverable** (roadmap 4.10). The next decision is a real
  one and is tracked as task 4.13: wire the prototype to the API and retire `web/legacy/`, or keep the prototype
  as a mock-data reference and extend the static dashboard. Wiring it is a rewrite of its data layer, not a patch.
- The prototype defines a **second status→colour palette** (`web/src/index.css` — `--status-{normal,warning,
  danger,offline}`) with no mechanical link to the app's single mapping in `core/status.dart` or to `02-design/04`
  §3. Tracked as task 4.14 alongside its localisation gap.
- The prototype is **outside every existing gate**: not in the five CI jobs, not covered by any `TC-*` case, and
  it escapes `TC-I-15` (i18n key parity) because it has no ARB keys to compare. This is recorded as a known hole
  rather than papered over; 4.14 either closes the localisation half or writes the exclusion down.
- The move broke a documented path: compose's `web` service still mounts `web/`, so `:8081/` serves the Vite dev
  entry (`<script src="/src/main.tsx">`) as a blank page and `/wallboard.html` is a 404. Recorded as **BUG-03**
  (`05-release/03` §4) and tracked as task 4.12.

**Rejected.** Deleting `web/legacy/` (it is the only web surface that renders real data, and the M1 evidence set in
`06-report/snapshots/` could no longer be reproduced); building the prototype at image-build time with a
multi-stage `web/Dockerfile` (correct in general, but it re-imports exactly the npm-in-the-path risk ADR-003
rejected, to serve a UI whose numbers are invented); editing ADR-003 in place to say React was always acceptable
(the log is append-only, and the reversal — not its erasure — is the part worth reading).

---

## ADR-018 — The TERRAGUARD prototype stays a mock-data UI reference; `web/legacy/` remains the M4 web surface
**Status:** **REVOKED (2026-10-06) by `ADR-019`** — no part of this decision remains in force. `ADR-019` promotes the TERRAGUARD client to the M4 web surface and retires `web/legacy/` (roadmap 4.15–4.20), which reverses the decision below and every consequence of it. An accepted ADR is revoked by a later one, never edited or deleted, so this entry keeps its original text as the record of what was decided on 2026-10-03 and why; `ADR-019` cites that reasoning as what the reversal was decided on. · **Date:** 2026-10-03 · **Revoked by:** ADR-019 · **Related:** ADR-003, ADR-005, ADR-017, ADR-019, FR-08, NFR-06, `02-design/04` §1.2, `05-release/01` §5, roadmap 4.13

**Context.** ADR-017 absorbed the prototype and left one decision open, tracked as task 4.13: wire TERRAGUARD to
the real API and retire `web/legacy/`, or keep it as a mock-data reference and finish the static dashboard. The two
candidate surfaces are not equivalent:

| | `web/legacy/` — static dashboard | `web/` — TERRAGUARD prototype |
|---|---|---|
| Data | The **real API** (`js/api.js`, `<meta name="api-base">`) | `src/data/mockData.ts`; no request leaves the page |
| Threshold verdicts | Renders what the engine computed | Re-implements `value > max` in a screen (the ADR-005 violation) |
| Failure behaviour | `failureKind` classifies unreachable / not-built / not-found | Nothing can fail, so nothing is exercised |
| Evidence | `06-report/snapshots/` — the M1 screenshots were taken from it | None; no number on it may be quoted |
| Cost to make it the deliverable | Extend toward W1–W8 as M2/M3 endpoints land | Rewrite the data layer **and** wait for the same endpoints |

M2 and M3 — the endpoints any real web surface needs — are the critical path, so neither option removes the
dependency on them. The difference is what happens to the surface that already works.

**Decision.** The prototype stays a **mock-data UI reference**, and `web/legacy/` remains the M4 web surface: the
one task 4.10 extends toward W1–W8, and the one the milestone's DoD ("every screen shows real data with a
timestamp") is measured on.

Two consequences are accepted deliberately:

1. **The prototype is not promoted.** It is labelled as mock data in the UI (task 4.14) and stays outside CI and the
   `TC-*` set (ADR-017). What the team reuses is its **screens**, not its code: the card hierarchy, the layout and
   the Vietnamese copy are the visual reference for the same screens in the static dashboard.
2. **M4 web effort goes into the static dashboard.** That is vanilla-JS work rather than React — the stack ADR-003
   chose for it — and it keeps the M1 evidence reproducible.

**Consequences.**
- `02-design/04` §1.2, `05-release/01` §5 and roadmap table 4.10–4.14 now agree on which surface ships, which is
  the acceptance condition for task 4.13.
- React is **deferred, not rejected on merit**: if extending the static dashboard to W1–W8 becomes the bottleneck,
  the prototype is the ready-made starting point and this decision is revisited.
- The prototype keeps a maintenance cost — a second codebase and a second palette — that buys nothing at demo time.
  The exit stays cheap and stated: it is one folder, reachable only through the `/` mount, and removing it would
  touch `ADR-017`'s rejected-alternatives note and `TERRAGUARD_SUMMARY.md` only.

**Rejected.** Wiring TERRAGUARD to the API now (the right call one milestone later, but it would spend the M4 web
budget re-rendering data the static dashboard already renders, while M2/M3 remain the critical path). Deleting the
prototype now that `web/legacy/` is confirmed as the surface (it is the team's UI reference for the W1–W8 work and
it already exists, labelled). Editing ADR-017 in place to record this outcome (append-only log).

---

## ADR-019 — TERRAGUARD is promoted to the M4 web surface; `web/legacy/` is retired
**Status:** Accepted · **Date:** 2026-10-06 · **Revokes:** `ADR-018` **in full** (not amended, not superseded in part — its decision and all of its consequences are no longer in force) · **Related:** ADR-003, ADR-005, ADR-013, ADR-017, ADR-018, FR-01, FR-08, NFR-06, `02-design/04` §1.2, `05-release/01` §5, roadmap 4.10 and 4.15–4.20

**Context.** ADR-018 named `web/legacy/` the M4 web surface and kept TERRAGUARD a mock-data UI reference, with one
escape clause: *"React is deferred, not rejected on merit: if extending the static dashboard to W1–W8 becomes the
bottleneck, the prototype is the ready-made starting point and this decision is revisited."* The team is invoking
that clause, and two things made it the right moment:

1. **The read half of ADR-018's argument expired.** Its cost table charged the prototype with "rewrite the data
   layer **and** wait for the same endpoints". The second half no longer applies to the screens that matter most:
   FR-01 authentication (including the recovery flows of 2026-10-06) and task 2.8's terrarium read surface
   (`GET`/`POST /terrariums`, `GET /{id}`, `readings/latest`, `readings`, `coverage`) are built and verified, so
   the endpoints that wire TERRAGUARD's *read* screens exist today, and what remains is shared with any surface.
2. **Reaching W1–W8 in `web/legacy/` is a design job, not a wiring job.** `02-design/04` §1.2 lists eight web
   pages. Legacy implements a subset as plain HTML; the prototype already carries the card hierarchy, layout,
   charting (`recharts`) and Vietnamese copy for eight screens. Extending legacy means re-deciding that design in
   vanilla JS, while wiring the prototype means writing an API client against endpoints that now exist. The team
   judged the second cheaper and the result better — which is the comparison ADR-018 said it would revisit on.

**Decision.** The **TERRAGUARD prototype becomes the M4 web surface**, and `web/legacy/` is retired when the switch
completes (task 4.19). Promotion is not a relabel: it carries the obligations the prototype was previously excused
from, and those obligations are the substance of this decision rather than footnotes to it.

| The prototype was… | As the shipped surface it must be… | Task |
|---|---|---|
| `src/data/mockData.ts`; no request leaves the page | Wired to the real API, with the mock shapes deleted as each screen lands | 4.15, 4.16, 4.20 |
| `value > max` over mock bands, evaluated in a screen | Fed by the server's verdict and band (`readings/latest`), with no comparison operator on a metric value outside the engine's contract — the ADR-005 rule that ADR-018 used as an argument *against* it | 4.17 |
| Vietnamese-only, no ARB keys, unreachable by `TC-I-15` | Bilingual (vi default, en) like the app, so `TC-I-15`'s key-parity check covers it (NFR-06, ADR-013) | 4.18 |
| A prototype-only palette measuring 4.7–8.6:1 on its own surfaces | `02-design/04` §3's tokens, at ≥ 4.5:1 for body text (`04-quality/03` §4.4) | 4.18 |
| Outside CI and every `TC-*` case | `tsc`, `vite build` and the key-parity check in CI, and the mock-data notice removed **because the mock data is gone** | 4.18 |
| No wallboard, while the M4 DoD requires one on a second screen | A wallboard route rebuilt in React, since retiring legacy would otherwise strand the page the DoD names | 4.19 |

**Consequences.**
- **The promotion is staged by the backend, not by preference.** Only three of the eight screens have endpoints
  today (`Login`, `Dashboard`, `History`, plus the read half of `Terrariums`/`TerrariumDetail`). `Alerts` waits on
  task 3.4, `Settings` on `PATCH /auth/me`, `Devices` on device read routes, and live push on task 2.9. Task 4.20
  exists so those screens are wired when their endpoints land rather than ahead of them; the per-screen readiness
  table is in `03-implementation/07`.
- `web/nginx.conf` collapses from two mounts to one, and its SPA fallback becomes the only routing rule.
  `05-release/01` §5's BUG-03 regression pair changes with it: `/legacy/*` stops being checked because the pages
  stop existing, and a deep route returning `200` through the fallback takes its place — otherwise the fix for
  BUG-03 would quietly become an untested assumption of the new layout.
- **The M1 report screenshots are not re-taken.** `06-report/snapshots/` is evidence of the M1 surface at the time
  it existed; re-shooting it from a different client would misrepresent what was measured then.
- `web/dist/` stays committed (ADR-017) and stays what nginx serves.
- **A screen that still renders `mockData.ts` keeps its mock-data notice.** `MockDataNotice` is removed when the
  last invented number is removed, not before — a notice that outlives the mock data is as misleading as mock data
  without one.
- The prototype's maintenance cost — a second codebase, which ADR-018 kept at arm's length — is now borne
  deliberately, and that second codebase becomes the first.
- **`ADR-018` is revoked outright, not superseded in part.** Its status line now reads `REVOKED` and there is no
  surviving half of it to check a future change against. Two things it recorded are nevertheless **not** undone by
  the revocation, because they were never its to revoke: the mock-data labelling of task 4.14 (which 4.18 removes
  when the last invented number goes, not before) and `web/dist/` being committed (ADR-017's decision). Revoking a
  decision also does not rewrite the history that cites it, so 4.12–4.14 stay closed and `06-report/snapshots/`
  keeps describing what M1 measured.

**Rejected.** **Keeping both surfaces** (ADR-018's outcome): the DoD has to name one surface anyway, so the second
one becomes a reference cost and a divergence risk rather than a free option. **Wiring the read-only screens and
leaving `web/legacy/` for the write screens**: two clients that must stay visually identical, which is worse than
either alone. **Promoting the prototype without its obligations** — wiring it and leaving the palette, the
Vietnamese-only copy and the CI exclusion as they are: that is how a mock prototype becomes a shipped surface that
still cannot be measured, and it would empty `TC-I-15`'s gate of meaning. **Editing or deleting ADR-018** to record
this (append-only log: a revoked entry keeps its text, and its status line is what declares the revocation).

---

## ADR-020 — Registration and `forgot-password` disclose whether an identifier has an account
**Status:** Accepted · **Date:** 2026-10-07 · **Related:** FR-01, BR-01.5, `02-design/06` §2, `07-appendices/03` §4.1/§5, `03-implementation/03` §6, `04-quality/02` (`TC-U-54`, `TC-U-57`), BUG-05

**Context.** Both endpoints were built on a strict non-disclosure rule: `register` answered `202` with a recovery
code whether or not the identifiers were free (the code was a decoy on the taken path), and `forgot-password`
answered `202` with an empty body whether or not any account used the identifier. The intent was to keep the API from
being usable as an account-existence oracle — the same rule `login`, recovery and terrarium ownership follow.

Running it on 2026-10-07 showed the rule is a net loss in these two places, and the loss is a *lie with a cost*:

1. **Registration told the caller the account had been created.** Registering a second time with the same email but
   a fresh username returned `202 {"status":"accepted","recoveryCode":"…"}`, the DB (correctly, the unique index
   holds) created nothing, and the person was left holding a recovery code that can never verify — while the natural
   next step, signing in, failed with `invalid_credentials`. From the product's point of view that is a
   duplicate-email "success" that leaves someone hunting for an account that does not exist, and it can also cost
   them the *real* recovery code if they store the decoy and discard the first one.
2. **`forgot-password` could not be told apart from a delivery failure.** An address nobody holds produced the same
   `202` as a real one, and delivery is a log line at best (limitation L-02), so a mistyped address is
   indistinguishable from a code that never arrived. The value the endpoint protects — "does this address have an
   account?" — is worth less than the support cost of the confusion, and it is a question the caller is asking about
   *their own* address, which they typed themselves.

Two facts made the change cheap and honest: the DB already enforces email and username uniqueness, so nothing about
storage or concurrency had to change, and the spec (`07-appendices/03` §4.1) had already sketched `409` for a taken
identifier while noting the built API answered `400` instead.

**Decision.** These two endpoints disclose, and nothing else does.

| Path | Before | Now |
|---|---|---|
| `POST /auth/register` with a taken username and/or email | `202` + a decoy recovery code, no account created | `409 registration_conflict` with `errors[]` naming `username_taken` and/or `email_taken`; nothing created; no code returned |
| `POST /auth/forgot-password` with an unknown identifier | `202`, no code issued | `404 identifier_unknown`; no code row created |
| `POST /auth/forgot-password` for a disabled account | `202`, no code issued | `404 identifier_unknown` — it cannot be recovered either, and a third answer would tell a stranger more, not less |
| `POST /auth/login` | one opaque `401` | unchanged |
| `POST /auth/recover`, `POST /auth/reset-password` | one opaque `401` for an unknown identifier, a spent code, an expired code and another account's code | unchanged |
| Terrarium, device and ingest routes | ownership and credentials indistinguishable (`BR-02.2`) | unchanged |

The decoy mechanism is deleted rather than kept: `RegisterAsync` generates the recovery code only after the account
row is created, and `IUserStore.UserExistsAsync` became `FindTakenIdentifiersAsync` returning one flag per identifier,
so the conflict can name the field the caller has to fix.

**Consequences.**
- **`forgot-password` is now an account-existence oracle**, and that is accepted rather than overlooked: anyone can
  learn whether a given address has an account by watching for `404` against `202`. The exposure is bounded by the
  group's rate limit (10 requests/minute per client address, `Program.cs`) and by the fact that the answer is the one
  the caller already knows about their own address. It is *not* bounded by any attempt to make the timing identical,
  and no such attempt is claimed.
- **Registration discloses too**, which is the ordinary signup behaviour and the reason it is the safer of the two:
  a stranger learns nothing about an address they do not already associate with the product, and the person who
  typed the identifier is told what to fix.
- The two endpoints no longer have "identical answers" as an invariant, so the tests that asserted it are rewritten
  (`TC-U-54`) and a new case pins the conflict's field-level shape (`TC-U-57`). `BUG-05` records what the old
  behaviour looked like from the outside.
- `AuthService.ForgotPasswordAsync` still commits the code before calling the notifier, and still swallows a
  throwing delivery channel: a `500` where the code exists would be indistinguishable from a broken deployment and
  would tell the keeper nothing about the code they were just issued.
- The earlier reasoning is **not** erased. `05-release/03` §5 keeps its 2026-10-06 row stating that both paths
  answered identically, because that is what was decided and verified then; this ADR is why it changed, and the
  history that cites it stays intact.

**Rejected.** **Keeping both endpoints non-disclosing** and fixing only the messaging in the client: the client
cannot distinguish the two cases without the server saying so, which is the whole problem. **Disclosing at
`login`/`recover`/`reset-password` too** (so the whole auth surface answers consistently): registration and the
forgot-password prompt are the two moments where the identifier's existence is already the caller's own business,
while a reset code that discloses an account would let anyone enumerate accounts for no usability gain. **Keeping
the decoy code but returning `409` as well**: contradictory, and it is the decoy that made the old answer a lie.
**Making the timing of the two `forgot-password` paths identical** to hide the new distinction: the distinction is
now the documented contract, so spending effort to conceal it would be working against this decision.

---

## ADR-021 — Telegram is dropped as a notification channel
**Status:** Accepted · **Date:** 2026-10-07 · **Supersedes in part:** ADR-007 · **Related:** FR-13, BR-13.1, BR-13.5, `02-design/05` §3/§6, `05-release/03` (R-13, TBC-3), roadmap 3.5, migration `DropTelegramChannel`, `07-appendices/02`, `07-appendices/07`

**Context.** `ADR-007` made a Telegram bot the secondary channel and the one the live demo would use, and `BR-13.1`
listed it as a per-user option. The notification engine itself (roadmap 3.5) is unbuilt, so nothing has ever sent
through it; what existed was three declarations and no behaviour — `ChannelTelegramEnabled` and `TelegramChatId` on
`User` (written by nobody, read by nobody, at their defaults on every account: all `false`/`null` in the
`07-appendices/07` dump of 2026-10-07), a `TELEGRAM_BOT_TOKEN` line in `.env.example`, a `Channel.telegram` member in
the Flutter prototype, and the channel's place in the requirement, the design and the test documents. On 2026-10-07
the team narrowed the notification scope to the channels it intends to build and asked for every Telegram artefact to
be removed.

**Decision.** Telegram is dropped as a channel — from the requirement, the domain model, the configuration surface,
the prototype and the current-state documents. The two columns are removed by the `DropTelegramChannel` migration
(applied to the development database on 2026-10-07). The surviving channels are the in-app inbox (always on; the
audit trail), FCM push (optional per user) and SMTP email (optional per user, off by default).

**Consequences.**
- **Nothing delivered is lost today.** No code path read either column and the dispatcher does not exist, so the only
  test touched is the prototype's `notification_policy_test.dart` (one expectation removed); `TC-U-37…45` and
  `TC-I-12` stay unbuilt-and-untouched. `BR-13.1`, `BR-13.5`, `02-design/05` §3/§6, the appendix DDL and the
  traceability row now name FCM and SMTP only.
- **The M3 demo loses its easiest live path, and that cost is real rather than nominal.** A Telegram message proved a
  genuine out-of-band delivery with nothing to install and no Firebase project. What remains is the in-app inbox —
  which lives on the same screen as the alert, so it demonstrates the policy but not delivery to a second device — or
  SMTP, which needs a relay this environment does not have (the same gap as `L-02`, where the reset code's
  *transport* is a log line). `R-13` is therefore rewritten to say what is actually left instead of pointing at a
  channel that no longer exists, and `TBC-3` records the change. If the demo needs a real out-of-band notification,
  the cheapest replacement is a local SMTP sink in compose (MailHog/Mailpit) — recorded here as the rejected-*for-now*
  option, to be its own task with its own tests.
- `ADR-007` is **superseded in part, not revoked**: its primary channel, its SMTP option and its
  `INotificationChannel` seam all stand. Its entry keeps its original text and gains a status note, per this log's
  rule.
- The prototype moves with the requirement: the `Channel.telegram` member, its settings label, its place in the
  default channel set and the fake user's `telegramChatId` are gone, so the settings screen and the rule matrix
  describe the same three channels the requirement now names.
- **Past records are not rewritten.** `ADR-007`'s text, the earlier rows of `05-release/03` §5, `InitialSchema` and
  the older migration snapshots, and every roadmap/doc-log mention of what was decided at the time stay as written.
  The sweep that closed this change left no Telegram string in code, configuration, current policy or the shipped web
  client — only in those historical records and in this entry.

**Rejected.** **Keeping the columns unused** and dropping only the documentation: two dead columns and a
`TELEGRAM_BOT_TOKEN` line invite a future reader to assume a channel exists, which is the belief this ADR exists to
correct. **Building Telegram properly** (bot linking, webhook, chat-id verification): a week of work and a second
credential type for a channel no requirement demands, in the same milestone as the alert engine the product actually
needs. **Adding the SMTP sink inside this change**: it is the right compensating control for `R-13`, but it is a new
component with its own tests, and folding it into a scope reduction would hide it.

---

## ADR-022 — A password change keeps the session that made it and ends every other
**Status:** Accepted · **Date:** 2026-10-10 · **Related:** FR-01, `02-design/06` §2, BUG-07

**Context.** `POST /auth/change-password` revoked every refresh token of the account, and the web client turned that
into a visible sign-out: a keeper changed their password and was returned to the sign-in form, while the form's own
prompt promised only that *other* sessions would end ("Mọi phiên đăng nhập khác sẽ kết thúc"). The rule came from the
session-invalidation row of `02-design/06` §2 — "password change revokes all refresh tokens for that user" — written
as the shortest way to say "a password change evicts the sessions the keeper no longer trusts". The session that made
the change is not one of those: its caller typed the very credential the change is about, seconds earlier.

**Decision.** The change-password request names its own session with its refresh token
(`ChangePasswordRequest.RefreshToken`, optional), and every *other* rotation family of the account is revoked
(`IUserStore.RevokeUserTokensExceptFamilyAsync`). A caller that omits the token, or names one this account does not
hold or one already revoked, keeps nothing: it falls back to `RevokeAllUserTokensAsync`. The response stays `204`
with no body — the caller's access token remains valid for the rest of its 15 minutes and its refresh token keeps
working, because it is the one row the change did not touch.

**Consequences.**
- The prompt the form already showed is now true, so the web form reports the change in place and `Login.tsx` no
  longer consumes a `location.state.notice` that only this flow ever produced; the deck's `passwordChanged` sentence
  now names which sessions ended.
- The eviction a keeper actually asks for still happens: a second browser, a second device or a stolen token stops
  working at its next rotation (`401 refresh_token_invalid`) because the family is revoked server-side rather than
  left to expire.
- The safe direction stays the default. Naming no session revokes everything, so a client that cannot say who it is
  cannot keep a session alive by omission.
- `recover` and `reset-password` keep ending **every** session: they are reached without a session, so there is none
  to keep, and the person holding a forgotten password may still hold a live one.

**Rejected.** **Keeping the old rule and rewording the prompt** (the form would say "you will be signed out"):
honest, but it makes a device the keeper is holding collateral of a routine action, and puts the cost on the user
rather than on an attacker. **Rotating the caller's token as well** (a fresh pair in the response): three moving
parts — the response shape, the client's session write, the single-use invariants — for a session nobody questioned.
**Identifying the caller's session by a claim in the access token** (a `sid` beside `sub`): a token-format change
that lands on every client, when the refresh token that names the session is already in the caller's hand.

---

## ADR-023 — A resolved threshold alert re-arms its dwell key, and silence stays a separate mechanism
**Status:** Accepted · **Date:** 2026-10-10 · **Related:** FR-12, FR-13, BR-11.4, `02-design/02` §4.2, `02-design/05` §4/§5, `07-appendices/03` §4.5, `07-appendices/02` §3.8/§3.15, roadmap 3.4, `TC-I-07`

**Context.** Task 3.4 gives a keeper two ways to end an alert: resolve it (`Recovered`, `FalsePositive`,
`SensorFault`, `Accepted`) or silence the metric for a while. The first is a state change with an actor and a reason;
the second is time-boxed suppression with a reason, capped at 24 hours, visible on the dashboard. Until now nothing
wrote either, so the question the evaluator needs answered had never been asked: when a human closes a threshold
alert while the value is **still outside the band**, what happens next?

The engine's state for `(terrarium, metric, phase)` holds `FirstOutOfBandAt` — the start of the current excursion —
and the alert pointer. Resolution through the API writes the alert row, and the evaluator's next pass finds no *open*
alert (`Adopt` reconciles the pointer, which 3.2 built for exactly this case) but does find an excursion whose dwell
expired long ago. Left alone, the decision is `Open`: the alert the keeper just closed reappears on the next sample,
one interval later. A keeper who chose `Accepted` — "this is real and I am handling it" — would be told the same
thing until they learned to ignore the product.

**Decision.** Resolving a threshold alert **clears the excursion window** for its key
(`EvaluationState.Rearm()`: `Violation`, `FirstOutOfBandAt`, `CriticalSinceAt`, recovery ticks and the alert pointer;
the watermark stays). A new alert can therefore only be raised once the band has been left for the dwell *again*.
Suppression that lasts longer than that is what a **silence window** is for, and the two are not interchangeable: a
resolve ends an episode, a silence stops the telling while detection continues.

**Consequences.**
- "Resolved" means resolved for both the record and the engine, which is what a keeper expects when they close
  something by hand.
- A condition that persists re-alerts after one dwell window rather than one sample — the same latency the engine
  gives a fresh excursion, and the flapping control `02-design/05` §5 already relies on.
- A keeper who wants quiet for hours has a mechanism that says so: a silence with a reason, a visible end instant and
  an audit row, all of which an endless stream of re-alerts cannot express.
- Detection is untouched. During a silence the evaluator keeps opening, touching and resolving alerts, so the
  dashboard, the counts and the report show exactly what happened while nobody was being told.
- The re-arm is a domain predicate (`AlertLifecycle.RearmsItsKey`), so it applies to threshold alerts only: the
  device-level families (`DeviceSilent`, `SensorFault`, `DeviceClockSkew`) keep their own state — a device row, an
  open alert, the event stream — and have no dwell key to clear.

**Rejected.** **Leaving the window alone** (the alert returns at the next sample): the cheapest change, and it makes
manual resolution a button that does nothing for a condition that is still true. **Treating a resolve as a silence
for the same metric and duration**: it would hide the alert *and* the evidence, and it fakes a window nobody set —
the dashboard would show a suppression the keeper never asked for. **Suppressing new alerts while an
`Accepted`-resolved alert's condition continues** (a permanent "accepted" state on the key): that is a suppression
with no end, which is the one thing a silence explicitly may not be.
