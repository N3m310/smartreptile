# 02 — Performance and Reliability

Claims in this document are budgets to be **measured**, not aspirations. Where a number has been measured,
the measurement is recorded here; where it has not, the cell says `not yet measured`.

## 1. Performance budgets

| Path | Budget | Why this number |
|---|---|---|
| `GET /terrariums/{id}/readings/latest` p95 | ≤ 300 ms | Called on every app foreground; must feel instant |
| `GET /terrariums/{id}/readings?range=24h` p95 | ≤ 300 ms | 288 buckets from raw data |
| `GET /terrariums/{id}/readings?range=30d` p95 | ≤ 800 ms | 720 buckets from rollups |
| `GET /terrariums/{id}/summaries?range=30d` p95 | ≤ 400 ms | 30 rows |
| `POST /ingest/http` p95 | ≤ 150 ms | Device fallback path, must not back up |
| MQTT ingest throughput | ≥ 100 msg/s sustained | 100× the expected load (1 device = 1/60 msg/s) |
| Chart payload | ≤ 200 KB per metric | Mobile data friendliness |
| App cold start to first painted value | ≤ 2.5 s on a mid-range phone | Feels like a glanceable app |
| Alert condition → notification | ≤ 90 s | dwell (5 min internal) + sampling + push |
| Rollup job (1 day, 1 device) | ≤ 2 s | Comfortably inside the nightly window |

## 2. Load profile (k6)

| Scenario | Shape | Measures |
|---|---|---|
| `browse` | 20 VUs, 3 min: latest + 24 h + summaries in a loop | NFR-01 read paths |
| `range_30d` | 5 VUs, 2 min: 30-day queries on the 30-day fixture | p95 of the heaviest query |
| `ingest_burst` | 1 device publishing 60 batches/s for 60 s (back-fill simulation) | Ingest throughput + duplicates counter |
| `mixed` | 25 VUs read + 2 devices ingesting | Realistic worst case on the demo host |

Script sketch:

```js
import http from 'k6/http'; import { check, sleep } from 'k6';
export const options = { scenarios: {
  browse:   { executor: 'constant-vus', vus: 20, duration: '3m' },
  ingest:   { executor: 'constant-arrival-rate', rate: 60, timeUnit: '1s', duration: '60s', preAllocatedVUs: 5 },
}};
export default function () {
  const base = __ENV.API, t = __ENV.TERRARIUM, h = { Authorization: `Bearer ${__ENV.TOKEN}` };
  const r = http.get(`${base}/api/v1/terrariums/${t}/readings/latest`, { headers: h });
  check(r, { 'latest 200': (x) => x.status === 200, 'latest < 300ms': (x) => x.timings.duration < 300 });
  sleep(1);
}
```

Recorded results (fill in, do not fabricate):

| Scenario | p50 | p95 | p99 | Errors | Verdict |
|---|---|---|---|---|---|
| `browse` | | | | | |
| `range_30d` | | | | | |
| `ingest_burst` | | | | | |
| `mixed` | | | | | |

## 3. Database performance defence

| Query | Plan expectation | Verification |
|---|---|---|
| Latest per metric | Index seek on `(TerrariumId, RecordedAt DESC)`, `ROW_NUMBER()` per metric, no full scan | Execution plan screenshot + `SET STATISTICS IO ON` logical reads |
| 24 h range | Index range seek, ~288 rows | Reads ≈ rows returned, not table size |
| 30 d range | `TelemetryHourlyRollup` seek on `(TerrariumId, MetricId, HourStartUtc)` | Reads < 50 pages |
| Alert inbox | Seek on `(TerrariumId, State, TriggeredAt DESC)` | Reads proportional to page size |
| Coverage count | Count over the index with `RecordedAt` in the key | Reads proportional to the window, not to 90 days |

Recording rule: any query that exceeds the budget gets its plan captured in `report/evidence/plans/` and a
short paragraph in the report explaining what was changed (index, projection, or bucketing) — a plan
screenshot with no narrative is not evidence of understanding.

## 4. Reliability and failure behaviour (measured during M5 drills)

| Drill | Expected behaviour | Observed | Verification |
|---|---|---|---|
| Broker restart (30 min) | Device buffers ≥ 720 samples; zero loss; zero duplicates | | `TC-I-08`, QA 5.1 |
| DB stop (10 min) | Ingest back-pressures (no ack) instead of dropping; reads fail with `503`; `/health/ready` reports `database: Unhealthy` | **Readiness returns `503` in 3.0 s** with `database: Unhealthy` ("Database probe timed out after 3 s") and `mqtt-broker: Healthy`, while `/health/live`, `/version` and `/metrics` keep answering `200`; readiness recovers to `200` in 7 ms after the database returns, with 0 API restarts. Getting there took two attempts — see the note below | QA 5.2, measured 2026-09-21 |
| API restart mid-soak | Gap ≤ 1 sampling interval; evaluation resumes from the stored watermark | | QA 5.3 |
| Node unplug (10 min) | `DeviceSilent` warning → critical → auto-resolve; no fake metric alerts | | QA 5.4 |
| Wi-Fi restart | Reconnect ≤ 60 s with jittered backoff, no intervention | | QA 5.5 |
| NTP blocked | `clock_unsynced` flag; server time used for phase; no wrong timestamps stored | | QA 5.6 |
| Phone offline 10 min | No stale value rendered as current; explicit staleness chips | | QA 5.9 |
| 24 h soak | Coverage ≥ 99%, ≤ 1 false positive, heap stable ±2 KB, no watchdog reset | | `TC-E2E-02`, QA 5.10 |
| Disk full (simulated) | Ingest fails loudly; alerts still evaluated from existing data; `/health` degraded | | Optional (M5 if time allows) |

> **Readiness probe timeout — measured twice, and only the second fix held.** A 3 s `CancellationTokenSource`
> inside `DatabaseHealthCheck` did not bound the probe: with SQL Server stopped, `/health/ready` still took
> **16.1 s**, because SqlClient does not honour a token during the pre-login/TCP phase and gives up only at its
> own `Connect Timeout` (15 s). Racing the call against `Task.Delay(3 s)` also measured 16.1 s, because
> `CanConnectAsync` blocks its caller synchronously and returns a task only after that wait is over, so the timer
> started too late. What holds is running the probe on a thread-pool thread
> (`Task.Run(async () => await …)`) and racing *that* against the delay — `/health/ready` then answers in 3.0 s.
> Confirmed by rebuilding the image, checking the container's image id matched the fresh build, and re-running the
> drill. A regression test belongs in `tests/SmartReptile.Tests.Integration` (M2): the unit project deliberately
> references only `Domain`, and "the probe answers within N seconds" is exactly the kind of claim that rots
> silently — the first attempt *looked* correct and was wrong by 13 s.

## 5. Resource figures to record

| Resource | Where measured | Recorded value |
|---|---|---|
| Firmware flash usage | `pio run -t size` | |
| Firmware static RAM + heap watermark at 24 h | serial log | |
| Firmware average current (mains modem-sleep mode) | optional USB power meter | |
| API container CPU/RSS under `mixed` load | `docker stats` snapshot | |
| SQL Server data + log size after 7 days | SQL query | |
| Extrapolated 90-day storage vs NFR-11 (< 60 MB) | calculation from the 7-day measurement | |
| Alert notification latency (5 samples) | stopwatch during `TC-E2E-03` | |

## 6. Profiling workflow (when something is slow)

1. **Reproduce with a fixed dataset** (`monthly` fixture), never with live data — otherwise the measurement
   changes every run.
2. **Measure the layer before optimising it:**
   - API: `dotnet-trace` for CPU, or Serilog request timings for endpoint latency.
   - SQL: execution plan + `SET STATISTICS IO, TIME ON`.
   - App: Flutter DevTools timeline; check for `setState` storms and rebuild counts before blaming the API.
   - Firmware: `esp_timer` around sensor reads, heap watermark logging; never measure with the OLED and
     Wi-Fi both active without noting it.
3. **Change one thing**, re-measure, and write the before/after into the report. A performance claim without
   a before/after pair is not a claim.
4. **Do not optimise what is not measured.** If the p95 is inside budget, the correct action is to stop.

## 7. Observability in practice

| Signal | Source | Alerting threshold for the team (not the user) |
|---|---|---|
| `ingest_samples_total` flat while a device shows `online` | `/metrics` | Page yourself — the pipeline is stuck |
| `ingest_duplicates_total` spiking | `/metrics` | Expected after a back-fill; investigate if it grows without an outage |
| `ingest_rejected_total` increasing | `/metrics` | Firmware/schema mismatch or a credential problem |
| `alerts_opened_total` flat while `out_of_range_minutes` rises | `/metrics` + summaries | Evaluator failure (documented heuristic, `02-design/03` §8) |
| `device_last_seen_age_seconds` > 180 | `/metrics` | Wi-Fi or power problem |
| `notifications_sent_total{status=failed}` | `/metrics` | Channel credentials expired (Telegram token, FCM service account) |
| `eval_duration_ms` p95 climbing | `/metrics` | Database contention |

Logging rules: structured JSON, one correlation id per ingest batch propagated through evaluation and
notification, `Warning` for rejections with the machine-readable reason, `Error` only for genuine bugs, and
**never** log a secret, a token, or a full telemetry payload above debug level.

## 8. Honest limitations of these claims

| Claim | Caveat |
|---|---|
| "p95 ≤ 300 ms" | Measured on one demo host with a 30-day dataset, 1 device. Not a scalability claim. |
| "≥ 99% coverage in soak" | Home Wi-Fi; a 3-minute router hiccup would show as a gap and would be reported, not hidden. |
| "Zero loss ≤ 12 h outage" | Bound by the 720-sample ring buffer; a longer outage drops the **oldest** samples and reports the drop count. |
| "Alert within 90 s" | Includes the deliberate 5-minute dwell; the system is designed to be *right* before it is *fast*. |
| Storage < 60 MB / 90 days | Extrapolated from a measured 7-day run; the actual 90-day figure is not observed in a one-semester project, and the report says so. |
