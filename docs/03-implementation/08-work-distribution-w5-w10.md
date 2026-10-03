# 08 — Work Distribution, Weeks 5–10 (5 members, app-first)

**Short version.** M1's missing DoD line lands in W5, the pipeline and the engine land in W6–W7, **the app and
the dashboard are feature-complete by the end of W8**, W9 is hardening plus the release artefacts, and W10 is
only the report, two rehearsals and the submission. **Nothing new is built after W9 Friday.**

- Calendar: W1 = Mon 2026-09-07 → W4 = 09-28…10-04 (contains 2026-09-29) → **W5 = 10-05 … W10 = 11-15**.
  Shift the dates if your week 1 differs; the week numbers are what matter.
- ~30 working days left, so the app gets **four weeks and never fewer**. §3 is how that is protected.
- Task ids and definitions of done live in `03-implementation/07-implementation-roadmap.md`; this file owns
  *who, when, and what must be true on Friday*.

## 1. Roles — fill in the names

| Code | Track | Owns | Backup |
|---|---|---|---|
| `FW` | firmware + hardware | M1 close-out (1.5, 1.6), 2.5–2.7, 5.6, 6.1 | `BE-1` |
| `BE-1` | backend — ingest + API surface | **M2** (2.2, 2.3, 2.4, 2.8, 2.9), 5.2, 5.7 | `BE-2` |
| `BE-2` | backend — engine + alerts | **M3** (3.1–3.7), **M5** (5.1, 5.3–5.5) | `BE-1` |
| `APP` | Flutter app | **M4** (4.1–4.9, 4.11), 6.3 | `DOC` |
| `DOC` | web dashboard + docs/report + submission | **M6** (4.10, 1.8, report, 6.5–6.8) | `APP` |

| Code | Full name | Student id |
|---|---|---|
| `FW` | | |
| `BE-1` | | |
| `BE-2` | | |
| `APP` | | |
| `DOC` | | |

## 2. The six weeks

### W5 · 10-05 … 10-11 — M1 closes, the pipe starts
- `FW` sensors in hand **Monday**; QA §1 + §2 (1.5, 1.6)
- `BE-1` 2.2 device claim + 2.3 MQTT credential check + TLS
- `BE-2` 2.4 ingest worker and pipeline (`TC-U-01…09`)
- `APP` 4.1 auth + 4.2 home — **built against the stub API, not waiting for a real endpoint**
- `DOC` book the mentor slot for W7; close the `Arid-cool` gap; literature pass; web 4.10 live page
- **Gate:** real sensor values on serial — the line M1 has been missing

### W6 · 10-12 … 10-18 — first sample end to end
- `FW` 2.5 sampler, filters, ring buffer, MQTT publish, LWT
- `BE-1` 2.8 readings API + coverage + terrariums; 2.9 SignalR
- `BE-2` finish 2.4 (`TC-I-03/04`); start 3.1 threshold resolution
- `APP` 4.3 live subscription + polling fallback; 4.4 history chart
- `DOC` 4.10 live page on real data; report R1 + R2 — take the enclosure photos this week
- **Gate: 2.10 first E2E** — node → broker → SQL Server → `/readings/latest`, screenshot saved

### W7 · 10-19 … 10-25 — the engine can tell the keeper something
- `BE-2` 3.1 + **3.2 `ThresholdDecision` + `EvaluatorWorker`** + 3.3 derived signals
- `BE-1` 3.4 alert lifecycle + 3.5 notifications — get one real Telegram message through
- `FW` 2.6 HTTPS back-fill; 2.7 provisioning + claim code on the OLED
- `APP` 4.5 alerts inbox/detail/ack; 4.6 threshold editor — **6 of 9 screens done by Friday**
- `DOC` mentor meeting: demo M3 and write the dated 1.1 line; report R3
- **Gate:** an induced excursion produces **exactly one alert and one notification**; a 4-minute disturbance
  produces **nothing**

### W8 · 10-26 … 11-01 — app and dashboard finish (the week the deadline depends on)
- `APP` 4.7 devices/claim/calibration; 4.8 report screen; 4.9 settings/diagnostics; 4.11 l10n + accessibility
- `BE-2` 3.6 rollups/summary/exposure index; 3.7 ops endpoints
- `BE-1` integration tests for the new surface; 5.2 rate limits, CORS, headers, package scan
- `FW` finish 2.7; 5.6 calibration offsets documented in the QA log
- `DOC` remaining 4.10 pages; report R4 + R5
- **Gate: APP + WEB FEATURE FREEZE.** The demo must run entirely from the phone with the wallboard on a second
  screen, every screen showing real data with a timestamp. After this gate the app takes **bug fixes only**.

### W9 · 11-02 … 11-08 — hardening and release artefacts (code freeze Friday)
- all: 5.1 security checklist · 5.3 retention/purge/export · 5.4 soak · 5.5 chaos drills · 5.7 performance ·
  5.8 one-hour bug bash
- `FW` 6.1 firmware `v1.0.0` tag + size output · `BE-1` 6.2 clean-clone `docker compose up` with a timer
- `APP` 6.3 signed APK + App Bundle installed on a real phone, release-mode screenshot
- `DOC` QA §5 drill evidence; report R6–R9
- **Gate:** every NFR measured or explicitly short; release artefacts signed; **code freeze**

### W10 · 11-09 … 11-15 — ship
- Mon: 6.5 source `.zip` (no `bin/`, `obj/`, `.dart_tool/`, `.env`, keystores) + 6.4 final test counts
- Tue: 6.6 report assembly — figures numbered, TOC/figure list generated, `06-report/01` quality gates walked
- Wed: **rehearsal #1**, timed, from a cold start, no shell allowed
- Thu: fix only what the rehearsal exposed; 6.8 contribution table agreed by all
- Fri: **rehearsal #2 + submission**
- No feature work is allowed in W10. Anything still missing goes to the cut list or into R11.3 as a limitation.

## 3. Why the app can finish by W8

| App work | Needs | Week | If the dependency slips |
|---|---|---|---|
| 4.1 auth, 4.2 home | nothing — the contracts were frozen at M1 | W5 | — |
| 4.3 live, 4.4 history | 2.9 SignalR, 2.8 readings | W6 | ship the polling-only mode that is already specified, switch when SignalR lands |
| 4.5 alerts, 4.6 thresholds | 3.4 alert lifecycle | W7 | build against the stub and recorded fixtures, wire in W8 |
| 4.7 devices/claim | 2.2 claim API | W8 | claim from the web page for the demo |
| 4.8 report screen | 3.6 rollups | W8 | export from the API only, no summarised view |
| 4.9 settings, 4.11 l10n/a11y | nothing | W8 | — |

**The app never waits for the backend.** `07-appendices/03` froze the payload and REST contracts at M1, so every
screen is built against the stub and switched to the live API as endpoints land: a backend slip costs the app an
integration afternoon, not a week. **The W8 freeze date does not move** — if the app is behind on Friday of W8,
the response is to move 4.8 and 4.9 to `DOC`, not to extend the freeze.

## 4. Milestone and report accountability

| Milestone | Gate | Accountable | Report sections owed |
|---|---|---|---|
| M1 close-out | real values on serial | `FW` | R5 |
| M2 | 2.10 E2E screenshot | `BE-1` | R6.2, R6.3, R4.5 |
| M3 | one alert, no false alarm | `BE-2` | R4.6, R7, R8, R9 |
| M4 | demo entirely from the phone | `APP` | R6.4–R6.6 |
| M5 | every NFR measured or short | `BE-2` | R9.6, R11.3 |
| M6 | submission + 2 rehearsals | `DOC` | R1–R4, R10–R14 |

The report is 30% of the grade and cannot start in W10: R1–R3 are writable now, R4–R5 in W8, R6–R9 in W9,
R10–R14 in W10. `DOC` owns the template, figure numbering and deadlines; each owner writes the sections their
own evidence covers.

## 5. Workload

| Workstream | Hours left | `FW` | `BE-1` | `BE-2` | `APP` | `DOC` |
|---|---|---|---|---|---|---|
| Hardware bench + accuracy (1.5, 1.6) | 25 | 25 | | | | |
| Firmware (2.5–2.7, 5.6, 6.1) | 45 | 45 | | | | |
| Backend ingest + API (2.2–2.4, 2.8, 2.9, 5.2, 5.7) | 55 | | 55 | | | |
| Backend engine + hardening (3.x, 5.1, 5.3–5.5) | 60 | | | 60 | | |
| App (4.1–4.9, 4.11, 6.3) | 60 | | | | 60 | |
| Web (4.10) + literature (1.8) + QA walkthroughs | 45 | | | | | 45 |
| Report, demo, packaging (R1–R14, 6.4–6.8) | 55 | 5 | 5 | 5 | 5 | 35 |
| **Total** | **345** | **75** | **60** | **65** | **65** | **80** |

The same hours spread over the six weeks (indicative, read off §2's task assignments):

| Week | `FW` | `BE-1` | `BE-2` | `APP` | `DOC` | Team |
|---|---|---|---|---|---|---|
| W5 | 16 | 12 | 12 | 12 | 12 | 64 |
| W6 | 11 | 14 | 8 | 14 | 12 | 59 |
| W7 | 12 | 12 | 16 | 16 | 14 | 70 |
| W8 | 11 | 11 | 12 | 18 | 15 | 67 |
| W9 | 15 | 7 | 13 | 3 | 12 | 50 |
| W10 | 10 | 4 | 4 | 2 | 15 | 35 |
| **Total** | **75** | **60** | **65** | **65** | **80** | **345** |

Read it the way the team experiences it: **W7 and W8 are the two heavy weeks for everybody** (the engine, the
app screens and the freeze land together), and W10 is deliberately light on code. `FW`'s W5 is front-loaded on
purpose — if the bench does not produce a number that week, the W6 end-to-end gate is already at risk.

`APP` carries ~60 h inside four weeks — **≈15 h/week in W5–W8**, which is the price of finishing before W10 and
the one number to check every Monday. `DOC` peaks at ~13 h/week; if either is unrealistic, move 4.8/4.9 to `DOC`
or the web pages to the cut list — never the freeze.

## 6. Cut list (use it the Friday a week slips)

1. FR-17 camera snapshot · 2. web pages beyond wallboard + live · 3. 5.7 k6 → a measured single-client sample
4. 24 h soak → 8 h with the same instrumentation · 5. 4.8 report screen (the API export stays)
6. SMTP email channel (keep FCM + Telegram)

**Never cut:** the report, the rehearsals, the release-mode proof, the rubric's unit + widget tests, the
alert-correctness tests, and the **W8 app freeze**.

## 7. Cadence

Mon 15 min plan · Wed async "on track for Friday?" · Fri 15 min gate demo + effort into `06-report/02` (a graded
artefact — fill it every week, never at the end). Every merge: CI green, `main` demo-able from M3 onward,
interface changes written into `07-appendices/03` in the same commit.
