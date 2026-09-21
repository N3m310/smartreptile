# 05 — User Stories and Use Cases

## 1. User stories

Format: *As a `<role>`, I want `<capability>`, so that `<benefit>`.* Acceptance is inherited
from the referenced FR unless a story adds a specific check.

### Onboarding and access

| Id | Story | FR | Priority |
|---|---|---|---|
| US-01 | As a new keeper I want to create an account with just a username, email and password so that I can start without a long setup. | FR-01 | Must |
| US-02 | As a returning keeper I want to stay logged in on my phone so that I do not retype credentials every time. | FR-01 | Must |
| US-03 | As a keeper I want to log out on a shared device so that nobody else sees my terrarium data. | FR-01 | Must |
| US-04 | As an owner I want to invite a friend as a viewer so that they can see my animal's conditions. | FR-02 | Must |
| US-05 | As an owner I want a technician role for the person who fixes the setup so that they can acknowledge alerts without deleting my data. | FR-02 | Should |

### Setup

| Id | Story | FR | Priority |
|---|---|---|---|
| US-06 | As a keeper I want to describe my terrarium (name, species, location) so that the app knows which environment to expect. | FR-03 | Must |
| US-07 | As a keeper I want to pick my species from a built-in list so that the thresholds are correct without me researching them. | FR-10 | Must |
| US-08 | As a keeper I want to power on the device and enter a short code so that I do not have to type MAC addresses or IPs. | FR-04 | Must |
| US-09 | As a keeper I want the device to connect to my home Wi-Fi by itself so that I do not need a laptop to configure it. | FR-04 | Must |
| US-10 | As an advanced keeper I want to define my own thresholds for a species that is not in the list. | FR-10 | Should |
| US-11 | As a keeper with two enclosures of the same species I want to reuse one profile so that I configure the bands once. | FR-10 | Should |

### Daily use

| Id | Story | FR | Priority |
|---|---|---|---|
| US-12 | As a keeper I want to see the current temperature, humidity and light of my terrarium in one glance so that I know my animal is fine. | FR-08 | Must |
| US-13 | As a keeper I want the app to tell me whether each value is inside the range for my species instead of me remembering numbers. | FR-08, FR-10 | Must |
| US-14 | As a keeper I want a push notification when the habitat goes out of range so that I can act even when I am not home. | FR-13 | Must |
| US-15 | As a keeper I want to know when the device itself stopped reporting so that I do not mistake silence for safety. | FR-07 | Must |
| US-16 | As a technician I want to acknowledge an alert so that the owner knows someone has seen it. | FR-12 | Must |
| US-17 | As a technician I want to silence a metric for a few hours during maintenance so that I am not spammed by expected excursions. | FR-12 | Should |
| US-18 | As a keeper I want to see how the last 7 days looked as a chart so that I can judge whether my setup is stable. | FR-09 | Must |

### Review and evidence

| Id | Story | FR | Priority |
|---|---|---|---|
| US-19 | As a keeper I want a summary of each day (min/max/avg, minutes out of range, light hours) so that I can see trends without reading a chart. | FR-14 | Must |
| US-20 | As a lab technician I want to export the environment record as CSV so that I can attach it to a report. | FR-14, FR-15 | Must |

### Extra stories covered by design but not in the 20 above
Device fleet view (FR-16), calibration offsets (FR-07), camera snapshot (FR-17), audit log (FR-18) —
see their acceptance criteria in `03-functional-requirements.md`.

---

## 2. Use cases

### UC-01 — Provision and bind a sensor node

| Field | Value |
|---|---|
| **Actors** | Owner (primary), Device (secondary) |
| **Goal** | A factory-fresh node becomes a trusted, bound data source for one terrarium |
| **FRs** | FR-03, FR-04, FR-05 |
| **Preconditions** | Owner is logged in; a terrarium exists; 2.4 GHz Wi-Fi credentials are available; device has power |
| **Trigger** | Device has no stored secret and boots into provisioning mode |

**Main flow**
1. Device starts provisioning mode and broadcasts an access point / BLE service named `SmartReptile-XXXX`.
2. Owner supplies the Wi-Fi SSID/password (in-app or captive portal).
3. Device connects to Wi-Fi, syncs NTP, registers itself as `provisioning` with the backend, and obtains a claim code.
4. Device shows the 8-character code (OLED or serial log) with a 15-minute countdown.
5. Owner opens *Add device*, enters the code and selects the target terrarium.
6. Backend validates the code, binds device ↔ terrarium, generates a 256-bit secret, stores its hash, returns the secret once.
7. Device stores the secret in NVS, reboots out of provisioning mode, connects to MQTT over TLS, publishes `status = online`.
8. Dashboard shows the device with `online` and the first sample within 90 s.

**Alternative / exception flows**
- **A1 — Code expires:** step 6 returns `404 claim_code_invalid`; device regenerates a new code every 15 min while in provisioning mode; Owner retries the newest code.
- **A2 — Wrong Wi-Fi password:** step 3 fails; device stays in provisioning mode and re-opens the config portal; no claim code is issued.
- **A3 — Terrarium already bound:** step 6 returns `409 terrarium_already_bound`; Owner unbinds or picks another terrarium.
- **A4 — Code already consumed:** returns the same `404` as an unknown code (anti-enumeration, BR-04.3).
- **A5 — No backend reachable:** device retries with exponential backoff and keeps showing the provisioning screen; Owner is shown "backend unreachable" guidance.
- **E1 — Secret lost after claim (device factory reset):** device boots without a secret, generates a *new* claim code; Owner must re-claim, which unbinds the old secret.

**Postconditions** — `Device.status = provisioned/online`, `DeviceCredential` row exists, audit entry `device.claimed` written, first sample stored.

---

### UC-02 — Monitor live conditions

| Field | Value |
|---|---|
| **Actors** | Owner, Technician, Viewer |
| **Goal** | Answer "is my animal fine right now?" in one screen and under five seconds |
| **FRs** | FR-08, FR-10, FR-11 (indirectly), NFR-02, NFR-06 |
| **Preconditions** | Logged in; terrarium has a bound, online device and at least one sample |
| **Trigger** | User opens the terrarium or the app foregrounds |

**Main flow**
1. App fetches `GET /terrariums/{id}/readings/latest` and subscribes to the SignalR group `terrarium:{id}`.
2. UI renders metric cards with value, unit, band, status badge and freshness; header renders device state and last-seen.
3. A new sample arrives; the backend pushes `readingAdded` + `statusChanged`; cards update within 5 s with a subtle animation.
4. If a status changes to `OutOfRange`/`Critical`, the card shows the violated bound and a link to the open alert.

**Alternative / exception flows**
- **A1 — SignalR unavailable:** client falls back to 30 s polling and shows a "live updates paused" chip.
- **A2 — No data yet:** empty state names the next action; no misleading zeros are shown.
- **A3 — Stale data:** header shows `offline · last seen 12 min ago`, cards are dimmed, values carry their timestamp.
- **A4 — Metric unavailable (sensor fault):** that card shows `unavailable` and the reason, and does not display a stale number as current.

**Postconditions** — No state change; the view is read-only except for navigation.

---

### UC-03 — Configure species thresholds

| Field | Value |
|---|---|
| **Actors** | Owner |
| **Goal** | The alerting engine matches the published envelope of the kept species |
| **FRs** | FR-10, FR-11, FR-18 (audit) |
| **Preconditions** | Logged in as Owner with rights on the terrarium |
| **Trigger** | New terrarium, new species, or a season/seasonal adjustment |

**Main flow**
1. Owner opens *Thresholds*, which shows the effective bands per metric with `source` (profile / override / default).
2. Owner either selects another built-in profile or edits an override for one metric.
3. UI enforces `TargetMin < TargetMax` and `CriticalMin ≤ TargetMin ≤ TargetMax ≤ CriticalMax` inline.
4. Owner optionally sets day/night variants, dwell minutes, hysteresis margin.
5. UI shows a sanity warning if the band contradicts the climate zone's plausible range (non-blocking).
6. Owner saves; backend validates, audits before/after, and returns the new effective thresholds.
7. The evaluator uses the new bands from the next sample onward; the UI states this explicitly.

**Alternative / exception flows**
- **A1 — Invalid ordering:** save is blocked with per-field errors; nothing is persisted.
- **A2 — Editing a shared/built-in profile:** built-ins are read-only; the UI offers "Duplicate to custom profile".
- **A3 — Concurrent edit:** second writer gets `409 version_conflict` with the current values (optimistic concurrency via `RowVersion`).
- **A4 — Change while an alert is open:** the alert keeps its original band (for the record) and is re-evaluated against the new band on the next sample; if now in range it auto-resolves.

**Postconditions** — New `Threshold` rows/override, audit entry, effective thresholds change from the next sample.

---

### UC-04 — Receive and act on an alert

| Field | Value |
|---|---|
| **Actors** | Scheduler (evaluator/notifier), Owner, Technician |
| **Goal** | A dangerous excursion is noticed, acted upon, and closed with a reason |
| **FRs** | FR-11, FR-12, FR-13 |
| **Preconditions** | Online device; effective thresholds exist; user has a notification channel enabled |
| **Trigger** | Temperature leaves the target band for ≥ dwell time |

**Main flow**
1. Evaluator detects the value outside `[TargetMin, TargetMax]` for the dwell window.
2. An `Alert` is created (`Warning`, `Open`) with the first out-of-band timestamp and the triggering value.
3. Notifier selects channels by user preference and severity, checks quiet hours and the rate limit, and dispatches.
4. User receives an FCM push with terrarium, metric, value, band and duration; tapping opens the terrarium with the alert highlighted.
5. Technician acknowledges it; the alert becomes `Acknowledged` with actor + time.
6. Technician fixes the cause (repositions the lamp).
7. Values return inside the band with the recovery margin for 3 minutes; the system auto-resolves and (optionally) sends a recovery notification.
8. The alert appears in history with trigger time, duration, and both actors' actions.

**Alternative / exception flows**
- **A1 — Value worsens:** the alert escalates to `Critical` (BR-11.6) and one new notification is sent even if the previous one is unacknowledged.
- **A2 — Nobody acknowledges within 30 min (Critical):** escalation path — repeat notification once to the Technician, then to the Owner (`02-design/05` §6).
- **A3 — Quiet hours:** Warning is deferred/suppressed; Critical bypasses quiet hours (BR-13.2).
- **A4 — Root cause is a sensor:** Technician resolves with `FalsePositive`/`SensorFault`; the outcome is retained as a label for the v2 model (BR-12.5); calibration is corrected (FR-07).
- **A5 — Notification channel down:** retries ×3, failure logged; in-app inbox still contains the alert (BR-13.4/5).
- **A6 — Device goes silent mid-alert:** the metric alert stays open; a `DeviceSilent` alert is added; no further metric alerts are raised from missing data.

**Postconditions** — Alert row with a complete lifecycle, notifications logged, status back to `InRange` (or an accepted exception recorded).

---

### UC-05 — Review history and export evidence

| Field | Value |
|---|---|
| **Actors** | Owner, Viewer, lab technician |
| **Goal** | Produce evidence of the environmental conditions over a period |
| **FRs** | FR-09, FR-14, FR-15 |
| **Preconditions** | ≥ 1 day of data |
| **Trigger** | Report writing, or a health concern about the animal |

**Main flow**
1. User opens *History*, selects a range (1 h / 24 h / 7 d / 30 d / custom).
2. Backend selects raw or rollup data per BR-09.1 and returns bucketed min/max/avg plus alert overlays.
3. Chart renders gaps honestly; below it, the daily summary list shows coverage, out-of-range minutes, light hours and the exposure index.
4. User requests an export (CSV/JSON) for the range; the job runs asynchronously and returns a link valid for 24 h.
5. User downloads the file and attaches it to the report.

**Alternative / exception flows**
- **A1 — Range with partial data:** coverage badges show e.g. 72% and low-confidence days are marked, not hidden.
- **A2 — Range longer than retention:** the UI caps the range to available data and explains why (raw 90 days, rollups 24 months).
- **A3 — Export job fails:** status shows `failed` with a reason and the user can retry.
- **A4 — Viewer requests export:** allowed (read-only action) — exports are not privileged.

**Postconditions** — Export artefact + audit entry `export.generated`.

---

### UC-06 — Handle device silence / offline recovery

| Field | Value |
|---|---|
| **Actors** | Scheduler, Owner |
| **Goal** | Silence is never mistaken for safety, and data lost during an outage is recovered |
| **FRs** | FR-07, FR-06, FR-12, FR-13 |
| **Preconditions** | A bound device that was online |
| **Trigger** | No sample for > `3 × samplingInterval` |

**Main flow**
1. Evaluator notices the missing sample window and raises `DeviceSilent` (Warning) — no metric alerts from missing data.
2. Dashboard marks the device `offline` with `last seen`; a banner explains that threshold evaluation is paused for this terrarium.
3. At 30 minutes of silence the alert escalates to `Critical` and notifies by FCM/Telegram.
4. Device reconnects (Wi-Fi restored) and back-fills the ring buffer with quality bit `8`.
5. Ingest stores the back-filled samples in chronological order; the evaluator processes them for reporting but does not notify for excursions older than 6 h (BR-06.6).
6. `DeviceSilent` auto-resolves; daily summaries for affected days are recomputed; coverage reflects the gap honestly.

**Alternative / exception flows**
- **A1 — Device never returns:** alert stays open; Owner checks power/Wi-Fi; the device can be re-claimed after a factory reset (UC-01 E1).
- **A2 — Outage longer than the buffer (> 12 h):** the device keeps the newest 720 samples and drops the oldest, reporting the drop count in the next health payload; coverage shows the missing span.
- **A3 — Clock drift after a long power-off:** device re-syncs NTP before publishing; if NTP fails it publishes with `q & 16` (clockUnsynced) and the server stores `ReceivedAt` as the ordering fallback; skew > 120 s is flagged (NFR-10).
- **A4 — Broker outage only (device keeps sending):** device buffers and back-fills after the broker returns; ingest dedupes by `(deviceId, seq)`.

**Postconditions** — Buffer drained, `LastSeenAt` current, device `online`, summaries and coverage updated, no duplicates.

---

### UC-07 — Verify and calibrate sensors

| Field | Value |
|---|---|
| **Actors** | Owner, Technician |
| **Goal** | Trust the numbers, or correct them knowingly |
| **FRs** | FR-07, FR-08, FR-16 |
| **Preconditions** | Device online; a reference instrument available (ice bath, saturated-salt jar, lux meter) |
| **Trigger** | Periodic verification, or suspicion after an implausible alert |

**Main flow**
1. Technician compares the live value against a reference for 10 minutes.
2. If a systematic offset is measured, the Owner enters `tempOffsetC` / `rhOffsetPct` / `luxGain` on the device record.
3. Backend applies offsets at ingest so raw values stay untouched; a quality bit `16` (calibrationApplied) marks affected samples.
4. Dashboard shows corrected values with a footnote that the sensors are hobby-grade and indicative (no metrology claim).
5. The verification result is noted in the manual QA log (`04-quality/03`).

**Alternative / exception flows**
- **A1 — Sensor fails outright:** marks metric `unavailable`, disables evaluation for that metric, and tells the user which replacement part to buy (link in `07-appendices/04`).
- **A2 — Drift discovered after alerts were acted upon:** the Technician resolves the affected alerts with `SensorFault`; the labels are retained.
- **A3 — Offset makes a value implausible:** ingest rejects/flag the value (BR-06.4) and the app warns that the offset is probably wrong (typo guard, e.g. |offset| > 5 °C).

**Postconditions** — Calibration stored and audited; documented evidence of calibration limits for the report.
