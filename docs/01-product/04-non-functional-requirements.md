# 04 — Non-Functional Requirements

Each NFR is written as a **measurable budget** with a **verification method**. Where a target
depends on the demo hardware, the reference rig is stated.

**Reference rig (used by all budgets):** 1 terrarium · 1 ESP32 node at 60 s sampling ·
backend on a single host (4 vCPU / 8 GB RAM, Docker Compose: API + broker + SQL Server) ·
1 browser dashboard + 1 Android phone · home Wi-Fi.

| Id | Quality attribute | Budget | Verification |
|---|---|---|---|
| NFR-01 | **Performance (API)** | p95 ≤ 300 ms for `readings/latest`, `readings` (24 h), `alerts`; p95 ≤ 800 ms for 30-day aggregate queries; chart payload ≤ 200 KB per metric | `TC-I-11` with 30-day seeded dataset; k6/`bombardier` ramp to 50 RPS |
| NFR-02 | **Latency / freshness** | Device timestamp → stored ≤ 2 s (p95); stored → dashboard paint ≤ 5 s (p95); alert notification ≤ 90 s after the condition first exits the band | `TC-E2E-03` (freshness probe + stopwatch on push), `05-release/02` |
| NFR-03 | **Availability and offline resilience** | Ingest availability ≥ 99% during a 24 h soak; **zero** sample loss for outages ≤ 12 h; automatic recovery with no manual intervention; degraded read-only mode when the broker is down | `TC-I-08`, `TC-E2E-02` (broker restart + Wi-Fi drop) |
| NFR-04 | **Security and privacy** | TLS 1.2+ everywhere (MQTT `8883`, HTTPS); password hashing PBKDF2-SHA256 ≥ 210 k iters or Argon2id; device secrets 256-bit, stored hashed, constant-time compare; no secrets in the repository; all terrarium data scoped to its owner; OWASP IoT Top 10 reviewed item by item | `02-design/06` §7 checklist, `TC-I-13`, secret scan in CI |
| NFR-05 | **Measurement accuracy** | Temperature ±0.5 °C, humidity ±3 %RH, illuminance ±10% after factory calibration; 1-point user offset calibration supported; surface probe ±1.0 °C; values stored to 2 decimals without lossy rounding drift | `TC-E2E-04` reference-comparison (ice bath / saturated-salt jar / lux meter) |
| NFR-06 | **Usability and accessibility** | Live dashboard reachable in ≤ 3 taps from app launch; visible focus + ≥ 4.5:1 contrast for text; status colour always paired with a text/icon label (never colour-only); UI strings localised `vi` (default) + `en`; portrait phone layouts from 320 dp | `TC-W-01…18`, `04-quality/03` checklist, Flutter golden/A11y checks |
| NFR-07 | **Maintainability and code quality** | ≥ 70% line coverage on backend domain + Flutter core logic; `dotnet format` and `flutter analyze` clean in CI; no compiler warnings-as-errors violations; every public class documented; single-responsibility folders per `03-implementation/02` | CI gates, `TC-U-*` coverage report |
| NFR-08 | **Deployability and reproducibility** | `docker compose up` brings up the full backend + DB in ≤ 5 min on a clean host; DB created/seeded by migrations only (no manual SQL); firmware builds reproducibly from a pinned `platformio.ini`; all versions pinned | `05-release/01` runbook drill, clean-clone build in CI |
| NFR-09 | **Cost** | Hardware BOM ≤ 1 500 000 VND for one node (excluding the host PC and phone); software stack must be free/open-source (no paid tier required to demo) | `07-appendices/04` BOM table with prices |
| NFR-10 | **Time correctness** | All persisted timestamps UTC (`datetime2(3)`); device clock NTP-synced, drift tolerance ±2 s over 24 h; server `ReceivedAt` authoritative for ordering fallback; displayed times use the terrarium's `timeZoneId`; skew > 120 s flagged | `TC-U-25`, `TC-U-29` (timezone + skew), `TC-I-12` (skew injection end to end) |
| NFR-11 | **Retention and storage** | Raw: 90 days. Hourly rollups: 24 months. Daily summaries: indefinite. Storage for 1 device at 60 s < 60 MB/90 days (raw) — verified by measuring after a 7-day soak and extrapolating | `TC-I-14` retention sweep, `07-appendices/02` §storage estimate |
| NFR-12 | **Observability and diagnosability** | `/health`, `/ready`, `/metrics` as per FR-18; every request carries a correlation id; ingest rejections carry a machine-readable reason; logs structured JSON; a stuck component is detectable from metrics without reading logs | `TC-I-15`, `05-release/02` §4 |

## 1. Notes on how the budgets were chosen

- **NFR-01 / NFR-02.** The dashboard polls nothing critical: chart endpoints are the only heavy
  reads, and they read pre-aggregated rollups for ranges > 48 h, which is what makes the 300 ms
  budget realistic without caching infrastructure.
- **NFR-03.** "Zero loss ≤ 12 h" is the product of the device ring buffer requirement
  (FR-07 / BR-07.3). The buffer size is the binding constraint, not the server.
- **NFR-04.** We deliberately **do not** claim resistance to physical device tampering: the
  ESP32 has no secure element in v1 and the flash can be read out. This is recorded as an
  accepted limitation in `05-release/03` (risk R-06) rather than dressed up as a control.
- **NFR-05.** ±0.5 °C / ±3 %RH matches the SHT31 datasheet (ADR-014) with margin for enclosure
  self-heating; the DHT22 alternative would force ±0.5 °C / ±5 %RH and is the reason it was
  rejected.
- **NFR-09.** Cost was a stated constraint in the brief ("adjustable to budget"); the BOM keeps a
  spare budget line for the optional camera as a stretch goal.

## 2. Explicit non-goals (quality attributes deliberately not claimed)

| Attribute | Why not claimed in v1 |
|---|---|
| Horizontal scalability / multi-node clustering | Single-host deployment; the design keeps the ingest path stateless so it *could* scale, but no claim is made |
| 99.9% availability | Home Wi-Fi + single host; not achievable or verifiable |
| Sub-second telemetry latency | Sampling is 60 s; the framing of the product is "a minute of freshness" |
| Certified metrology (calibration certificate) | Hobby-grade sensors; accuracy is *indicative*, and the docs say so in the app UI footnote |
| Tamper-proof firmware | See NFR-04 note |
| Sub-second alerting | Deliberate: dwell time prevents alert spam, at the cost of latency |
