# 03 — Telemetry Ingest and Threshold Engine

This is the heart of SmartReptile: turning a stream of numbers into a defensible "is my animal safe?"
answer, without lying when data is missing or wrong.

## 1. Ingest pipeline

```mermaid
flowchart LR
  A["MQTT sr/v1/d/{id}/telemetry<br/>or HTTPS POST /api/v1/ingest/http"] --> B[Parse + schema check]
  B --> C{Device authenticated?}
  C -- no --> X1["Drop + audit auth_failed<br/>+ metric ingest_rejected_total"]
  C -- yes --> D[Plausibility check per metric]
  D --> E[Apply calibration offsets]
  E --> F{Duplicate<br/>(deviceId, seq)?}
  F -- yes --> X2["Ack as duplicate, no insert"]
  F -- no --> G[Insert TelemetrySample + MetricReadings<br/>+ DeviceHealthSample]
  G --> H[Update Device.LastSeenAt / status / health denorms]
  H --> I[Push SignalR readingAdded + statusChanged]
  H --> J[Enqueue evaluation work item]
  J --> K[ThresholdEvaluator]
```

Stages and their failure semantics:

| Stage | Rejects the batch? | Notes |
|---|---|---|
| Parse/schema | Yes `400`/`puback nack` with reason | Missing `seq`, non-ISO `ts`, unknown metric code |
| Auth | Yes | Constant-time secret compare; failure is audited (never silently dropped) |
| Plausibility | **No** | Value is stored with `QualityFlags & 2` and excluded from evaluation; excluding it keeps the diagnostics for calibration review (DI-06) |
| Calibration | No | Applied to `Value`, `RawValue` preserved |
| Duplicate | No | Idempotent success; counter `ingest_duplicates_total` |
| Insert | Yes `5xx` | Device keeps the sample and retries from the buffer (NFR-03) |
| Evaluation | Never blocks ingest | Runs after commit; failure is logged and retried by the next sample |

**Back-pressure.** MQTT subscription uses `MaxInflight = 100`, manual ack, and a bounded channel
(capacity 2 000 batches) between the subscriber and the ingest worker. When the channel is full the
subscriber stops acking, so the broker holds messages rather than the process losing them. Ingest rate
out of the box: ~1 batch/s per device, measured headroom ≥ 100 msg/s (NFR-01).

## 2. Transport contract (summary — full spec in `07-appendices/03`)

| Topic | Direction | QoS | Retained | Payload |
|---|---|---|---|---|
| `sr/v1/d/{deviceId}/telemetry` | device → broker | 1 | no | sample batch |
| `sr/v1/d/{deviceId}/health` | device → broker | 1 | no | RSSI/uptime/heap/battery/firmware (every 5 min) |
| `sr/v1/d/{deviceId}/status` | device → broker | 1 | **yes** | `online` / `offline` (LWT) / `maintenance` |
| `sr/v1/d/{deviceId}/events` | device → broker | 1 | no | `boot`, `sensor_fault`, `buffer_overflow`, `clock_unsynced`, `calibrated` |
| `sr/v1/d/{deviceId}/cmd` | server → device | 1 | no | `set_config`, `take_snapshot`, `ping` |
| `sr/v1/d/{deviceId}/ack` | device → broker | 1 | no | command result with `cmdId` |

Example telemetry payload (1 minute, 1 sample):

```json
{
  "deviceId": "sr-3f9a2c",
  "seq": 10457,
  "fw": "1.2.0",
  "ts": "2026-09-21T08:15:00Z",
  "samples": [
    { "t": 0, "tf": 28.75, "rh": 41.2, "lux": 1820.5, "uvi": 0.3, "st": 31.2, "q": 0,
      "raw": { "tf": 28.9, "rh": 40.8, "lux": 1790 } }
  ],
  "health": { "rssi": -63, "up_s": 86400, "heap_kb": 142, "bat": null, "src": "mains" }
}
```

`t` = seconds offset from `ts` (lets one publish carry several minutes, which is exactly what
back-fill does). Validation rules: `samples` length 1…120, `t` strictly increasing and ≤ 86 400,
metric keys drawn from the dictionary, arrays truncated at the plausibility ranges rather than rejected
wholesale.

## 3. Validation rules table

| Id | Rule | On violation |
|---|---|---|
| V-01 | Payload ≤ 32 KB, `samples` ≤ 120 | Reject batch `payload_too_large` |
| V-02 | `seq` present, integer, > 0 | Reject batch `schema_invalid` |
| V-03 | `ts` parseable ISO-8601 with `Z` | Reject batch `schema_invalid` (fallback: server uses `ReceivedAt` and flags `clock_unsynced` — configurable, default reject) |
| V-04 | Device exists and is not `Revoked` | Reject, audit |
| V-05 | Secret matches (constant time) | Reject, audit, increment failure counter |
| V-06 | Value within plausibility range | Accept, set `QualityFlags & 2`, skip evaluation |
| V-07 | `RecordedAt` not more than 5 min in the future | Accept, clamp to `ReceivedAt` when computing durations, flag `clock_ahead` |
| V-08 | `RecordedAt` older than 12 h (beyond buffer) | Accept as back-fill (quality bit 8), never notify |
| V-09 | `\|ReceivedAt − RecordedAt\|` > 120 s | Accept, store `ClockSkewSeconds`, raise an Info-level `DeviceClockSkew` signal once per hour |
| V-10 | Metric code known and not deprecated | Reject that reading only, keep the rest, log `unknown_metric` |

## 4. Threshold evaluation engine

### 4.1 Inputs and state

For each `(terrarium, metric, phase)` the evaluator keeps a small in-memory state machine persisted to
`EvaluationState` (Redis-free: a table with `(TerrariumId, MetricId, Phase)` PK and a `rowversion`):

```
State = {
  Violation: None | Hot | Cold,
  FirstOutOfBandAt: ts?,          // start of the current excursion
  CriticalSinceAt: ts?,           // start of the current critical excursion
  ConsecutiveRecoveryMinutes: int,
  OpenAlertId: long?,
  LastEvaluatedSampleId: long,
  LastNotificationAt: ts?
}
```

**As built (2026-10-09, roadmap 3.2's scaffolding).** The table exists with every field above, created by
`20261009140128_AddTerrariumRowVersionAndDeviceEvaluationTables`, and the queue in front of it is real: a bounded
`IEvaluationQueue` drained by an `EvaluatorWorker` into a `SampleEvaluator`. Two of the fields are written today and
the rest are written by 3.2:

- `LastEvaluatedSampleId` is advanced per sample, and **only forward** — a re-delivered or back-filled sample
  carries a lower id than the evaluator has already seen, and taking it would make the next pass re-read history.
- The key is resolved the way §4.2's `phase` line says, using the same `ThresholdPhaseResolver` the read surface
  uses, and against the same configured bands (profile + enabled overrides) — so the evaluator cannot track a key
  no band covers, nor miss one a card is already colouring.
- `Violation`, `FirstOutOfBandAt`, `CriticalSinceAt`, `ConsecutiveRecoveryTicks`, `OpenAlertId` and
  `LastNotificationAt` are written as "nothing decided yet". The one rule already in force is §4.2's first line,
  which needs no band: a faulted or implausible sample is stored as evidence and never evaluated.

**Built (2026-10-09, roadmap 3.2).** Every field above is now written by `ThresholdDecision.Decide`
(`Domain/Evaluation/ThresholdDecision.cs`), a pure static function over a band, a reading and this state, and by
`SampleEvaluator`, which resolves the band, calls it, and writes the alert row it asks for. Nothing about the state
changed: the same table, the same key, the same columns — which is what the scaffolding was for. Three details are
worth reading back, because they are where the code had to choose:

- **`LastNotificationAt` is still unwritten, and that is deliberate.** It exists so an episode cannot notify twice
  inside the policy window (§02-design/05 §5), and notification is 3.5's. Writing a timestamp here for a dispatcher
  that does not exist yet would be a value nobody can check.
- **The band the engine judges against is the band the read surface shows.** Both call `ThresholdResolver`, so the
  override/profile precedence of `03-implementation/06` §1 has one implementation rather than two.
- **`FirstOutOfBandValue` is not a column, and does not need to be.** The alert contract wants the value at the
  first violation and the peak of the episode, which are both read back from the stored `MetricReading` rows of the
  dwell window when an alert opens — a handful of rows, once per alert, instead of two columns rewritten on every
  sample. The window is the same evidence a human would look at.

### 4.2 Decision procedure (per sample, per metric)

```
phase      = (metric has Any-phase threshold) ? Any
           : (nowLocal within [LightsOn, LightsOn + PhotoperiodHours)) ? Day : Night
band       = resolveBand(terrarium, metric, phase)        // override -> profile -> default
if sample.QualityFlags & (1|2) : return                    // fault or implausible: no evaluation

hot  = value > band.TargetMax
cold = value < band.TargetMin
crit = value > band.CriticalMax or value < band.CriticalMin

if hot or cold:
    if state.Violation == None: state.FirstOutOfBandAt = sample.RecordedAt
    state.Violation = hot ? Hot : Cold
    state.ConsecutiveRecoveryMinutes = 0
    sustained = now - state.FirstOutOfBandAt >= band.DwellWarnMinutes
    critSustained = crit and (state.CriticalSinceAt ??= now) is older than band.DwellCritMinutes
    if sustained and state.OpenAlertId == null:      open(Warning)
    if critSustained and alertIsWarning():           escalate(Critical)
    else if alertOpen():                             touch(LastObservedAt, PeakValue)
else:
    recovered = insideByAtLeast(band, value, band.RecoveryMargin)
    state.ConsecutiveRecoveryMinutes = recovered ? +1 : 0
    if state.ConsecutiveRecoveryMinutes >= 3 and state.OpenAlertId != null:
        resolve(reason = Recovered); state.Violation = None; state.CriticalSinceAt = null
```

Design notes worth defending in the report:

1. **`TriggeredAt` is back-dated** to `FirstOutOfBandAt`, so an alert reports the true start of the
   excursion, not the moment the dwell timer expired. This is why alerts say *"out of range for 9 min"*
   instead of *"4 min"*.
2. **Dwell is a filter, not a delay.** A 4-minute spike on a 5-minute dwell produces nothing; a
   slow drift produces one alert instead of sixty.
3. **Escalation does not depend on acknowledgement.** A Critical condition notifies again even if the
   Warning is still unacknowledged — the point of escalation is that nobody has responded.
4. **Recovery margins are separate per metric** (0.5 °C, 3 %RH, 5% of lux threshold) because sensor
   noise differs; one global margin would flap on one metric and lag on another.
5. **Phase comes from the profile's photoperiod, not from the measured lux.** Using lux would couple
   two failure modes: a broken light sensor would silently switch the temperature band.
6. **Back-fill older than 6 h is evaluated for the record but never notifies** (BR-06.6/11.8) —
   otherwise a device reconnecting after a day away would fire a burst of stale alarms.

**As built (2026-10-09, roadmap 3.2) — the pseudocode above is implemented as written, with five points
settled.** `ThresholdDecision.Decide(band, value, observedAt, state, nowUtc, openAlertSeverity)` is the whole
procedure, pure and dependency-free, which is what makes `TC-U-10…20` an exhaustive matrix rather than a
sampling of it. The five points, each visible in the code:

- **The dwell comparisons measure against `nowUtc`, the evaluation instant**, while everything recorded into the
  state and onto the alert is the sample's own `observedAt`. For a live sample the two coincide; for a batch that
  arrives after an outage they do not, and the reading is that the excursion *did* last that long — it opens with
  `TriggeredAt` back-dated to when it happened. What stops that from becoming a burst of alarms is note 6 above,
  which is the dispatcher's rung, not this one's.
- **A reading inside the target band ends the excursion**, whether or not it cleared the recovery margin. The
  margin decides when an *open alert* may close; it does not decide when the dwell window re-arms, or a value
  hovering a hair inside the band would let the next excursion inherit the previous one's start and open instantly.
- **Escalation happens once per episode**, which needs the severity the open alert already has. The schema keeps
  severity on `Alert`, not on `EvaluationState` (§07-appendices/02 §3.9), so the caller passes it in — the
  pseudocode's `alertIsWarning()` — rather than the state carrying a second copy that could drift.
- **`CriticalSinceAt` is cleared when the value comes back inside the critical band**, so "29 critical minutes
  spread over an hour" cannot add up to an escalation. The critical dwell is consecutive by construction.
- **The alert's `TriggeringValue` and `PeakValue` are read from the stored readings** of
  `[FirstOutOfBandAt, observedAt]` when the alert opens, and moved outward by `Touch` afterwards. Both describe
  readings the engine has already judged but deliberately does not keep a second copy of.

**What 3.2 does not do.** It writes alerts and sends nothing: delivery is the dispatcher's (§5, 3.5). It does not
push `alertChanged` over SignalR either — that arrives with the lifecycle API that can also acknowledge and
resolve an alert (3.4), so an event nobody can act on is not emitted. And the device-level signals of §4.3 are
3.3's.

**One ordering guarantee is not built.** The per-device reorder window of §7 (a 30-second buffer ordered by
`RecordedAt`) is not in this milestone: samples are evaluated in ingest order, which is per-device publish order
and therefore `RecordedAt` order for one node, but an out-of-order delivery from a second publisher or a broker
redelivery would reach the engine out of order. Recorded as open in `03-implementation/07` rather than implied by
the `EvaluationState` watermark, which is an id watermark and cannot see a `RecordedAt` that regressed.


### 4.3 Derived signals beyond per-metric bands

| Signal | Rule | Why |
|---|---|---|
| `DeviceSilent` (FR-07) | no sample for `3 × samplingInterval` → Warning; 30 min → Critical | Silence must never look like safety |
| `SensorFault` | device `events` topic reports ≥ 3 consecutive read failures | Distinguishes "habitat is wrong" from "sensor is dead" |
| `DeviceClockSkew` | `\|ReceivedAt − RecordedAt\| > 120 s` | Protects the day/night phase logic and daily bucketing (NFR-10) |
| `GradientWarning` (SHOULD) | `SurfaceTempC − TempC > 12 °C` when both exist | A basking spot far hotter than air is a burn risk; a genuinely useful extra the sensors make cheap |
| `LightDeficit` (SHOULD) | accumulated `LightLux > profile.lightThresholdLux` < `profile.minLightHoursPerDay` by 21:00 local | Detects a failed lamp/timer without any actuator integration |

`GradientWarning` and `LightDeficit` are marked SHOULD: they are implemented if M4 finishes early.

## 5. Alert state machine and notification handoff

Evaluation writes alerts; it does not send them. The dispatcher decides delivery:

```
open/escalate ──► NotificationJob(alertId, event=Opened|Escalated|Resolved)
                       │
        ┌──────────────┼───────────────────────────┐
        │              │                           │
  preference      quiet hours                 rate limit
  (channel,       (Warning suppressed;        (>10/h → digest)
   min severity)   Critical bypasses)
        │              │                           │
        └──────► channel send (FCM | SMTP | inbox)
                       │
                  NotificationLog row per attempt
```

Every suppression is recorded with a reason in `NotificationLog.SuppressedReason`, so "why did I not get
an alert?" is answerable from data — a question every real alerting system eventually faces.

## 6. Rollup and summary pipeline

| Trigger | Job | Output |
|---|---|---|
| Every minute (in-memory accumulate) | HourlyRollupWorker | Upsert `TelemetryHourlyRollup` for the current hour; recompute the last 48 h hourly once per night to absorb back-fill |
| Local midnight + 5 min per terrarium | DailySummaryWorker | `DailyEnvironmentalSummary` for the day that just ended, plus `IsLowConfidence` if coverage < 80% |
| Nightly 02:00 local | RetentionSweeper | Purge raw > 90 d, snapshots > 7 d, expired export files; yearly: purge rollups > 24 months |
| On ingest of back-fill | RollupWorker (targeted) | Recompute only the affected hours/days (idempotent upsert) |

Exposure index computation (used by the daily summary and shown in the report):

$$E_{\text{hot}} = \sum_i \max(0,\; T_i - T_{\max})\cdot \Delta t_i \quad [\text{°C·h}], \qquad
E_{\text{cold}} = \sum_i \max(0,\; T_{\min} - T_i)\cdot \Delta t_i \quad [\text{°C·h}]$$

with $\Delta t_i$ in hours (1-minute interpolation between samples) and $T_{\min}, T_{\max}$ the target
band of the phase in force at that minute. Humidity uses the same shape in %RH·h; light uses a deficit
in hours. Worked examples are in `03-implementation/06-threshold-and-species-profile-logic.md` §5.

## 7. Idempotency and ordering guarantees

| Concern | Mechanism | Guarantee |
|---|---|---|
| Duplicate delivery (QoS 1 / retry) | Unique `(DeviceId, Sequence)` + upsert path | At-most-once persistence, at-least-once delivery |
| Out-of-order back-fill | Queries order by `RecordedAt`; evaluation runs on a per-device **ordered queue** keyed by `RecordedAt` with a 30 s reorder window | A late sample cannot retroactively "recover" an alert before its cause |
| Server restart mid-batch | Insert + evaluation in one transaction; evaluation jobs rebuilt from `LastEvaluatedSampleId` on start-up | No samples skipped |
| Threshold changed mid-episode | `ThresholdSnapshot` recorded; open alert keeps its band | History stays explainable |
| Rollup recomputation | Upsert with `ComputedAt` | Recomputable, never double-counted |
| Client mutation retries | `Idempotency-Key` on `POST` endpoints, 24 h dedupe window | No duplicate terrariums/devices from flaky mobile networks |

## 8. Failure modes and responses (design-level)

| Failure | Detection | Response | Data consequence |
|---|---|---|---|
| Wi-Fi down at the device | Device cannot publish | Buffer until full (≥ 12 h), then drop oldest + report count | Gap in raw data; summary marks low confidence |
| Broker down | API `/ready` reports `broker:false`; ingest counter flat | Device buffers; API keeps serving reads; dashboard shows "live updates paused" | None if outage < buffer capacity |
| Database down | Ingest insert fails | Subscriber stops acking → broker holds messages; device retries | None if outage < broker persistence / buffer |
| API down, broker up | Broker queues to persistent store | Telemetry accumulates in broker | None |
| Sensor dead | Device reports fault | Metric marked unavailable; evaluation skipped for that metric; user told | Gap, explicitly labelled — never imputed |
| Clock unsynced | Skew metric | Samples accepted with flag; phase logic uses server time as fallback | Phase attribution flagged as approximate |
| Evaluator exception | Logged with correlation id + counter | Alert not created; next sample retries | Possible missed alert — surfaced by `alerts_opened_total` being flat while `out_of_range_minutes` rises (a documented monitoring heuristic) |
