# 01 — Vision and Scope

## 1. Problem statement

Reptile keepers judge the living conditions of their animals by feel and personal
experience. Separate thermometers and hygrometers exist, but nothing integrates
**measuring, storing, displaying and alerting** in one place. An unsuitable environment
(too hot, too cold, too dry, too little light/UV) is the most common cause of stress and
disease in captive reptiles, and keepers cannot watch the terrarium continuously —
especially when they are away from home for a day or more.

Two consequences follow:

1. **Reactive care.** Problems are noticed only after the animal shows symptoms.
2. **No evidence.** There is no record to compare against published husbandry ranges, no
   way to ask "how long was it above 34 °C last week?", and no dataset to learn from later.

## 2. Vision

> A keeper opens one app, sees the last 60 seconds of their terrarium and whether their
> animal is currently inside its published comfort envelope — and gets a push notification
> within a minute when it is not.

SmartReptile is a **single-terrarium, monitoring-first IoT system**. It does not try to
control the habitat in v1; it makes the habitat observable, comparable to published
science, and impossible to ignore. The value is not the sensor reading — it is the
**trustworthy, species-aware history** built from those readings.

## 3. Objectives → requirement mapping

| # | Objective (from the brief) | Requirement(s) | Observable outcome |
|---|---|---|---|
| O1 | Continuously measure temperature, humidity, light | FR-06, FR-07, NFR-05 | A reading exists for every 60 s window, flagged when quality is doubtful |
| O2 | Show current state and history on a dashboard | FR-08, FR-09, NFR-02 | Dashboard shows values ≤ 5 s old; 1 h / 24 h / 7 d / 30 d charts render < 2 s |
| O3 | Alert when values leave the safe range of the selected species | FR-10, FR-11, FR-12, FR-13 | Out-of-band condition sustained past the dwell time raises exactly one alert and a notification |
| O4 | Derive thresholds from literature for the chosen species | FR-10, appendix `07-appendices/05` | Every built-in profile row cites a source reference and is reviewable |
| O5 | Accumulate an environmental dataset for the v2 AI | FR-14, FR-15, appendix `07-appendices/06` | Raw + hourly rollups + daily summaries retained with a documented feature schema |
| O6 | Integrate the pieces instead of loose devices | FR-01…FR-05, FR-16 | One account, one terrarium, one device fleet view, one alert inbox |
| O7 | Keep the animal safe when the network fails | FR-07, NFR-03, NFR-12 | Device buffers ≥ 12 h offline and back-fills without duplicates |
| O8 | Be defensible as an engineering artefact | FR-18, NFR-07, NFR-08 | Tests, `/health`, structured logs, reproducible builds, traceability matrix |

## 4. Target users and personas

### P1 — "Weekend keeper Linh" (primary)
- 24-year-old student, one leopard gecko in a 20×10 cm starter box on a desk.
- Away from the room 8–10 h a day; worries about the heat mat on warm days.
- **Goal:** know the animal is fine without staring at it.
- **Pain:** a cheap analog hygrometer tells her the number *now* but not whether yesterday was fine.
- **Needs:** live temperature/humidity, one clear "in range / out of range" signal, phone alerts.

### P2 — "Collector Tung" (primary)
- 31-year-old hobbyist, 4 tanks across three species (tropical, semi-desert, desert).
- Runs a heat lamp on a timer and a humidity box.
- **Goal:** one place for all terrariums, with per-species thresholds.
- **Pain:** re-checking each tank manually; no record of which tank drifted.
- **Needs:** multiple terrariums, species profiles, per-metric thresholds, historical comparison, weekly report.

### P3 — "Lab technician Mai" (secondary)
- Manages a small teaching collection; must document environmental conditions for reports.
- **Goal:** export evidence (CSV/JSON) of the conditions over a period, including excursions.
- **Pain:** manual log sheets; no audit trail.
- **Needs:** export, daily summaries, exposure index, alert history with acknowledgement attribution.

### P4 — "Small breeder Hanh" (secondary, partly out of v1)
- Breeds semi-desert geckos; needs seasonal photoperiod and night temperature drop.
- **Goal:** verify that the day/night cycle is actually being delivered.
- **Needs:** day/night phase thresholds, light-hours-per-day report. **Actuator control is v2.**

## 5. Scope

### In scope — v1.0

| Area | Included |
|---|---|
| Accounts | Register / login / logout, JWT access + refresh token, profile update, roles: `Owner`, `Technician`, `Viewer` |
| Terrarium | Create / edit / delete a terrarium record, assign a species profile, set location + notes |
| Species profiles | 3 built-in seeded profiles (tropical / semi-desert / desert) + user-created custom profiles with per-metric bands |
| Device onboarding | Claim-code provisioning, bind device → terrarium, list fleet, rotate secret, revoke device |
| Telemetry | ESP32 node samples temp / humidity / light / UV / surface temperature every 60 s; MQTT over TLS primary, HTTPS POST fallback; 12 h offline ring buffer with back-fill |
| Pipeline | Ingest validation, quality flags, dedupe + idempotency, server-authoritative receive time, hourly rollups |
| Dashboard | Live values (≤ 5 s freshness), online/offline + last-seen, in-range status per metric, 1 h / 24 h / 7 d / 30 d charts, min/max/avg |
| Thresholds | Per-metric target band + critical band, optional day/night phases, dwell time, hysteresis |
| Alerts | Automatic evaluation, open / acknowledge / resolve lifecycle, auto-resolve on recovery, alert history, silence a metric temporarily |
| Notifications | In-app inbox, FCM push (Flutter app), Telegram bot channel, optional SMTP email; quiet hours; per-severity subscription; rate limiting |
| Reporting | Daily environmental summary (min/max/avg per metric, light-hours, out-of-range minutes, exposure index), weekly view, CSV/JSON export |
| Retention | Raw readings 90 days, hourly rollups 24 months, daily summaries indefinite; manual purge per terrarium |
| Ops | `/health`, `/ready`, `/metrics`, structured logs, audit log of device + threshold changes |
| Camera (optional) | Manual on-demand still snapshot from an ESP32-CAM node; no automatic analysis *(FR-17, MAY)* |

### Out of scope — v1.0 (candidates for v1.1 / v2)

| Item | Why deferred |
|---|---|
| **AI disease/behaviour prediction** | Explicitly the subject of the next version (brief §8). v1's job is to produce clean data. |
| **Automatic actuation** (heat lamp, misting pump, fan, relay control) | Safety-critical, adds hardware and failure modes; the brief scopes v1 to monitoring + alerting (ADR-008). |
| Camera *video* streaming and behaviour analytics | Bandwidth + storage cost, and it is AI territory. |
| Native iOS release build | Team is Android-first; iOS kept buildable but not released. |
| Multiple simultaneous terrariums per device | One device ↔ one terrarium in v1 keeps the data model honest. |
| Mobile app offline editing of thresholds | Threshold changes require connectivity; read-only offline cache only. |
| OTA firmware updates | Fleet metadata is captured (`firmwareVersion`), but flashing is manual in v1. |
| Multi-tenancy / organisation accounts | Single-owner model; sharing is by role invitation on one terrarium. |
| Cloud hosting with a managed broker | Deployed as Docker Compose on one host for the demo. |

## 6. `[TBC]` decisions inherited from the brief

The source document lists the questions this doc set *assumes an answer to* so it can be concrete. Each is
reversible but must be confirmed by the team. **The canonical, complete list is `05-release/03` §6
(TBC-1 … TBC-6)** — the four that shape the product itself are repeated here, and TBC-4 is now a recorded
decision rather than an assumption (`ADR-016`).

| # | Question in the brief | Assumption used here | Close it by |
|---|---|---|---|
| TBC-1 | Which reptile species / branch? | **Semi-desert: leopard gecko (*Eublepharis macularius*)** as the demo species; tropical and desert profiles are also seeded so all three climate zones are demonstrable. See `07-appendices/05`. | Milestone M1 (§`03-implementation/07`) |
| TBC-2 | Is 20×10 cm the floor or the whole box? | **Floor area** of a small starter box; treated as a scale model, and the docs note that a leopard gecko adult would need a larger enclosure. The system is size-agnostic. | M1 |
| TBC-3 | Alert channel and camera? | **FCM push (app) + Telegram bot** as the two demo channels, SMTP email optional; **camera included as optional FR-17**, snapshot only, no analysis. | M1 |
| TBC-4 | How does the claim secret reach the device? | **8-digit one-time pairing token** displayed on the OLED, typed nowhere: the app hands the secret to the device over the local network (option A). Manual entry stays as the demo fallback. Recorded as **`ADR-016`**. | M1 |

## 7. Success metrics

| Metric | Target | Measured by |
|---|---|---|
| Telemetry continuity | ≥ 99% of expected 60 s intervals present over a 24 h soak | `TC-E2E-02`, ingest counters |
| Alert latency | Out-of-range condition → notification delivered in ≤ 90 s (dwell 5 min + 60 s sampling + push) | `TC-E2E-03` with a lamp/icing test |
| Alert precision (demo) | ≤ 1 false-positive alert during a 24 h soak with stable conditions | `TC-E2E-02`, alert history |
| Dashboard freshness | p95 ≤ 5 s from device timestamp to browser paint | `TC-I-11`, `05-release/02` |
| Data durability | 0 lost samples after a 30-minute broker outage | `TC-I-08` |
| Threshold provenance | 100% of built-in profile rows reference a literature source | `07-appendices/05` §5 checklist |
| Rubric readiness | Release APK + ≥ 1 unit test + ≥ 1 widget test + commented code + report | `06-report/01` evidence checklist |

## 8. Value and novelty (from the brief §7, restated as design commitments)

1. **Integration** — measurement, storage, display and alerting in one system; the device
   alone is useless without the pipeline, and the pipeline alone has nothing to show.
2. **Data over intuition** — every claim about the habitat is traceable to a timestamped
   reading and a cited threshold.
3. **A dataset, not just a dashboard** — the retention + rollup + summary design
   (`FR-14`, `FR-15`) exists specifically so v2 has features to train on; see
   `07-appendices/06-v2-ai-roadmap.md`.
4. **Species-aware thresholds with physics** — out-of-range *duration* and *magnitude* are
   both recorded (exposure index), which is what actually harms an ectotherm, not a single
   peak reading.
