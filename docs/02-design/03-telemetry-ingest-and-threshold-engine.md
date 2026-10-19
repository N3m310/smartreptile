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

- `LastEvaluatedSampleId` keeps the highest database identity seen, while `LastEvaluatedAtUtc` and
  `LastEvaluatedSequence` are the event-time idempotency watermark. Database identity order is not used as a proxy
  for device event order.
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

**The live ordering guarantee is built (2026-10-19).** `InProcessEvaluationQueue` holds committed samples for a
30-second event-time window and releases them in `(RecordedAt, device sequence)` order per device. The persisted
`EvaluationState` event-time watermark makes retries idempotent and prevents a late sample from recovering or
reopening an alert after newer evidence has already been evaluated. Such a late sample remains in telemetry
history and summaries and is logged when skipped by live evaluation. The queue itself is in memory: automatic
replay of committed-but-unprocessed samples after a process restart remains open and is not claimed as part of
this guarantee.


### 4.3 Derived signals beyond per-metric bands

| Signal | Rule | Why |
|---|---|---|
| `DeviceSilent` (FR-07) | no sample for `3 × samplingInterval` → Warning; 30 min → Critical | Silence must never look like safety |
| `SensorFault` | device `events` topic reports ≥ 3 consecutive read failures | Distinguishes "habitat is wrong" from "sensor is dead" |
| `DeviceClockSkew` | `\|ReceivedAt − RecordedAt\| > 120 s` | Protects the day/night phase logic and daily bucketing (NFR-10) |
| `GradientWarning` (SHOULD) | `SurfaceTempC − TempC > 12 °C` when both exist | A basking spot far hotter than air is a burn risk; a genuinely useful extra the sensors make cheap |
| `LightDeficit` (SHOULD) | accumulated `LightLux > profile.lightThresholdLux` < `profile.minLightHoursPerDay` by 21:00 local | Detects a failed lamp/timer without any actuator integration |

`GradientWarning` and `LightDeficit` are marked SHOULD. **Both are built (2026-10-19, roadmap 3.6)** — see the
as-built block at the end of this section.

**As built (2026-10-10, roadmap 3.3) — the three device-health signals.**
The rule is `DeviceSilencePolicy` (pure): `3 × samplingInterval` → Warning, 30 minutes → Critical, and `>` rather
than `>=` so that the online/offline badge `TerrariumService.DeriveStatus` renders from the same threshold and the
alert the watchdog opens cannot disagree at the boundary — both call one `SilenceThreshold`. The sweep is
`DeviceSilenceMonitor` (Application) driven by `SilenceWatchdogWorker` on a 30-second timer, because silence is the
*absence* of messages and nothing arrives to trigger a pass; it is the mirror image of §4.2's engine, which is
queue-driven precisely because readings do arrive. Four readings of the design are settled here rather than left to
the code: the alert is a **device-level row** (`Metric`, band, `TriggeringValue` and `PeakValue` all null — silence
is not a reading, and inventing a number for it is how a dashboard ends up charting one nobody measured); it is
**back-dated** to the instant the device should have been heard from, so its duration measures the outage rather
than the sweep interval; escalation changes the open row's severity and dedupe key and keeps its original
`TriggeredAt`, which is the same rule §4.2 note 4 applies to a band excursion; and resolution happens at the instant
the device spoke again, not at the instant a sweep noticed. Two things it deliberately does not do: a device in
`Maintenance` is not judged (that state exists so routine servicing does not create false alerts) and nothing is
sent — delivery is the dispatcher's (3.5), the same boundary the band engine keeps.

**The other two signals, same day.** `SensorFault` is decided where its evidence arrives — the `events` channel,
from the parsed payload's failure count — through `SensorFaultPolicy` (BR-07.1's three consecutive failures; an
event carrying no count is taken at its word, because the device only raises it after its own threshold). It opens
one Warning per **metric**, where §02-design/02 §3.14 sketches `MetricId` as null for `SensorFault` alongside
`DeviceSilent`: the sketch fits silence, which is a property of the board, but a fault names a probe, the dedupe key
has a slot for it, and a device-level row would collapse "the humidity probe died" and "the light sensor died" into
one entry that names neither. `sensor_recovered` closes the row with `Recovered` rather than
`ResolvedReason.SensorFault` — that label is the opposite direction (a *band* alert closed because the probe, not
the habitat, was wrong) and blurring the two would cost the v2 dataset its negative examples (BR-12.5). A faulted
metric reading `Unavailable` needs no new code either: a faulted reading carries quality bit 1, `TerrariumService`
already maps the non-evaluable bits to that status, and `QualityRules.IsEvaluable` is what makes the band engine
skip it — which is exactly what the requirement's "no alerts raised while faulted" means.
`DeviceClockSkew` is decided on the sample path from `QualityFlags.ClockUnsynced` — the flag V-09 sets, which also
carries V-08's deliberate exclusion of a long back-fill — and raises an **Info** entry, one per episode: while it is
open, later skewed batches move its `LastObservedAt`; a *new* entry waits the hour V-09 asks for, and the entry
closes when a trusted sample arrives. That is the reading of "one Info signal per hour" this build takes: an hourly
*row* would read as a new problem every hour, when what it reports is that one problem is still there.

**The two SHOULD signals, built (2026-10-19, roadmap 3.6).** Both had their `AlertSource` members and their
localised notification strings since 3.5; what 3.6 added is the rule that raises them.

- **`GradientWarning`** is `GradientWarningPolicy` (Domain, pure): `SurfaceTempC − TempC > 12 °C`, strictly greater
  so 12.1 raises and 11.9 does not, and **null when either reading is missing** — the rule needs both numbers, and
  closing an open episode on absent evidence would report a recovery nobody observed. It is decided inside
  `SampleEvaluator`, because the evidence is a pair of readings from one instant and a sweep would have to guess
  which surface reading belonged with which air reading. The row names `SurfaceTempC` and the species family, so it
  sits beside that probe's own band alerts rather than replacing them, and carries the difference as its triggering
  and peak value instead of a band, because no band was compared.
- **`LightDeficit`** is `LightDeficitPolicy` (Domain, pure) plus `LightDeficitMonitor`, swept every 15 minutes by
  `LightDeficitWorker`. It is an **accumulation**, so no single sample can violate it: the sweep judges one closed
  local day at a time, from 21:00 local (`EvaluationLocalTime`, inclusive), over the minutes from local midnight to
  that instant. The hours come from the same `MetricGridSummarizer` pass the daily summary uses, so the alert and
  `DailyEnvironmentalSummary.LightDeficitHours` cannot disagree; the deficit is rounded to the column's
  `decimal(8,3)` scale for the same reason. It raises **one** row per terrarium per day, dated from the instant the
  day closed, touches it while the shortfall stands, and closes it either when back-filled readings meet the
  requirement or when the local date rolls over. No per-sample light alert is raised: a dark minute at noon is not
  a failure, and the instantaneous value is already the per-metric band's business.
- **One deviation, recorded rather than implied.** A derived signal now appends its `AlertSource` to
  `Alert.DedupeKey`, so a gradient row on `SurfaceTempC` and a surface-band row can both be open for the same
  severity and phase. Without the suffix the filtered unique index would refuse the second one with a 2601 error.
  Band alerts keep the documented three-segment key byte-for-byte, so nothing already stored is rewritten; the four
  derived signals all name themselves. §02-design/02 §3.8 records the two shapes.

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
| Every 15 min after startup | `SummaryWorker` | Recompute hourly rollups for the last 48 h and daily summaries for today and yesterday in each terrarium's timezone |
| Nightly 02:00 local | RetentionSweeper | Purge raw > 90 d, snapshots > 7 d, expired export files; yearly: purge rollups > 24 months |
| Back-fill arrives | `SummaryWorker`'s next sweep | Recompute from raw telemetry and upsert affected existing hours/days; corrections are reflected within 15 min and never double-counted |

The summary worker deliberately reads persisted raw telemetry rather than maintaining a running accumulator. That
makes retries and back-fills use the same calculation path. A current local day is summarized only through its
completed minute; yesterday is computed over its full timezone-aware day, including DST-length days.

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
| Out-of-order delivery within the live window | Per-device evaluation buffer ordered by `(RecordedAt, Sequence)` with a 30 s window; persistent event-time state watermark | Late samples do not change live alert state; they remain available for history and summaries |
| Server restart mid-batch | Evaluation buffer is in memory; durable replay of committed-but-unprocessed samples is not implemented | **Open:** a process restart can leave a committed sample unevaluated |
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
