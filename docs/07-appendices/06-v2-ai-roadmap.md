# 06 — v2 Roadmap: From Monitoring to Disease-Risk Prediction (AI)

The brief (§8) says: once v1 is stable and enough data exists, extend the project with behaviour monitoring
and AI that predicts disease from environmental and behavioural data, then warns the keeper early. This appendix
is the plan for that, written now — while v1 is being built — because **the shape of v1's data decides whether
v2 is possible at all** (ADR-010).

---

## 1. Why v1 has no AI (and why that is not a gap)

| Reason | Consequence for v1 |
|---|---|
| The brief scopes v1 as "without AI" | Adding a model would contradict the requirement document |
| No labelled data exists yet | A model needs positive/negative examples; v1 is the machine that collects them |
| A toy "risk score" would be dishonest | It would compete with the threshold engine for authorship of alert semantics, and neither the user nor the report could defend it |
| The threshold logic is fully explainable | A keeper can see *why* an alert fired (band, dwell, duration). A black-box score cannot be interrogated, and unexplained alarms get ignored |

So v1's job is not "AI-ready features" bolted on — it is **clean, retained, correctly-labelled time-series** plus a
schema that can absorb the v2 inputs (behaviour, camera) without a redesign.

---

## 2. What v1 already provides for v2

| v1 artefact | v2 use |
|---|---|
| `DailyEnvironmentalSummary` (per local day, per terrarium) | Nearly a ready-made feature row: min/max/avg temp, humidity, light hours, out-of-range minutes, exposure index in degree-hours, alert counts, coverage |
| `TelemetryHourlyRollup` | 24-month history for longer-term trends and seasonality |
| `Metric` dictionary (ADR-004) | A new v2 signal = one dictionary row, not a migration |
| Alert resolve reasons (`FalsePositive`, `SensorFault`) | **Supervision labels**: a documented, timestamped record of "the reading was wrong" or "this was a real event" |
| `ThresholdSnapshot` | Explains which band applied at any past time — prevents label leakage from later band edits |
| Alert lifecycle timestamps | Response latency features (how long a condition persisted before human action) |
| `NotificationLog` suppressions | Records what the keeper did or did not see — important for evaluating whether a warning was actionable |
| `DeviceHealthSample` / quality flags | Feature-quality covariates (a bad RSSI or a sensor fault must be maskable, not learned as a condition) |
| Camera snapshots (FR-17, optional, manual) | Sources of behavioural observations for labelling — with the consent/notice question still open (§6) |

---

## 3. Target definition (the hard part, stated first)

"Predict disease" is not a well-formed target. The v2 work must choose one, and the choice determines the data
volume needed.

| Candidate target | Definition | Data needed | Difficulty |
|---|---|---|---|
| **A. Health-event onset flag** | The keeper logs a health event (refused food, shedding problem, respiratory signs, vet visit) with a date; predict "event within the next 3 days" | Event log (human input) + daily summaries | Medium — needs a lightweight keeper log in the app (a v1.1 feature) |
| **B. Out-of-envelope exposure risk** | Not a prediction: an explainable score of accumulated thermal/humidity stress (the exposure index already computed) | Already available | Low — but it is arithmetic, not AI, and should be labelled as such |
| **C. Behavioural anomaly detection** | Unsupervised: detect a change in activity pattern (motion/thermal-image occupancy) versus the animal's own baseline | Continuous behaviour signal | High — new sensing layer, then anomaly detection, then interpretation |

**Recommendation:** start with **A** (with **B** shipped in v1 as the explainable, non-AI indicator), because A
has a defensible label, a clear user benefit ("your animal often has a bad shed 3 days after a dry spell"), and
it does not require new hardware to begin collecting. C is the eventual research direction but needs the hardware
and the baseline data first.

### 3.1 Example hypotheses to test (not to assume)

- A dry period (humidity below target for > N hours) precedes shedding difficulty by 2–5 days.
- A sustained high-temperature exposure followed by a night with no drop precedes reduced feeding.
- Light-hours shortfall correlates with reduced activity over the following week.
- Persistent humidity above target correlates with respiratory signs.

Stated as hypotheses *to be falsified*, with the sample size reported — the failure mode of student AI projects is
asserting a pattern from four observations.

---

## 4. Feature schema (v2 candidate)

Per terrarium, per local day (extending `DailyEnvironmentalSummary`):

| Group | Features | Source |
|---|---|---|
| Thermal | `tempMin/Max/Avg`, `tempNightDrop`, `degreeHoursHot`, `degreeHoursCold`, `maxGradientSurfaceAir`, `minutesAboveCrit` | v1 tables |
| Humidity | `rhMin/Max/Avg`, `dryHours`, `wetHours`, `maxContinuousDryStreakHours` | v1 tables |
| Light/UV | `lightHours`, `lightDeficitHours`, `uvMax`, `uvBelowTargetMinutes` | v1 tables |
| Cycle regularity | `photoperiodAdherence` (did the light hours land in the expected window?), `nightDropPresent` | v1 + phase logic |
| Device quality | `coveragePct`, `sensorFaultMinutes`, `clockSkewMax`, `isLowConfidence` | v1 |
| Keeper behaviour | `daysSinceLastMaintenance`, `silenceHoursActive`, `alertsAcknowledgedRatio` | v1 audit + notifications |
| Behaviour (v2 sensing) | `activityIndex` (motion events/min), `baskingMinutes`, `hideTimeRatio` (thermal occupancy), `nightActivityRatio` | **new**: PIR/IMU + low-res thermal or camera analytics |
| Interaction (v2 log) | `fedY/N`, `sheddingObservedY/N`, `weightG` (if the keeper logs it) | **new**: keeper log in the app |
| Labels | `healthEventNext3Days`, `eventType`, `labelSource` (`keeper` / `vet` / `inferred`) | **new**: keeper log |

Rules that keep v2 honest:
1. Every feature is computed from stored data — no manual spreadsheet steps.
2. `isLowConfidence` days are excluded from training, not imputed.
3. Features are computed with the thresholds in force at that time (`ThresholdSnapshot`), so a later band edit
   cannot leak into the training set.
4. Behaviour features are normalised per animal (per terrarium), because baseline activity differs between
   individuals and species.

---

## 5. Modelling plan

| Stage | Approach | Why |
|---|---|---|
| 0 | Descriptive statistics + correlation with the candidate hypotheses | Cheap, and it may show the "AI" is unnecessary for some signals |
| 1 | Explainable baseline: logistic regression on a handful of features | Interpretable coefficients, works with tens of samples, sets the floor that any fancier model must beat |
| 2 | Gradient-boosted trees (XGBoost / LightGBM) | Strong on tabular data, handles missing values, feature importances for explanation |
| 3 | Sequence models (LSTM / temporal CNN) on the hourly series | Only after enough data (see below); needs far more examples |
| 4 | Behaviour anomaly detection (Isolation Forest / autoencoder) | Unsupervised, for the case where labels are scarce — the realistic situation |
| Interpretation layer | Rules that translate model output into a keeper sentence | A probability is useless to a user; "3 days of dry air, and shedding problems followed twice before" is not |

**Data volume reality check.** A prediction target with a monthly-ish event rate needs *at least* a few dozen
events per class for a meaningfully validated model. One terrarium at ~4–8 shedding-related events a year means
**v1's single-terrarium dataset cannot train a supervised model** — it can only support hypothesis generation.
Therefore v2 requires either (a) many months of single-animal data, or (b) a multi-owner dataset. This is stated
plainly here so nobody is surprised in a viva.

**Evaluation metrics:** precision/recall at a stated operating point (not accuracy), plus lead time
(days of warning), false alarms per month, and a calibration curve. A model that fires 12 times a month is worse
than no model, because it teaches the keeper to ignore alerts.

**Baselines to beat:** the threshold engine, the exposure index, and "always predict no event" (the trivial
baseline whose precision/recall must be reported — a student report that omits it is untrustworthy).

---

## 6. Ethics, privacy and welfare

| Topic | Position |
|---|---|
| Camera imagery | Off by default. If enabled for behaviour labelling, require explicit opt-in, store locally, shorten retention, and state clearly whether images are used for training. A home interior in frame is a privacy concern (BR-17.3). |
| Data ownership | The keeper's data stays theirs; exports are already available (FR-15). Any future multi-owner dataset requires explicit consent wording, not a buried toggle. |
| Welfare | The system must never *cause* the conditions it predicts. Induced excursions are done with bench mode or with the keeper's own equipment and supervision (`07-appendices/04` §7). |
| Model harm | A false negative in v2 must not replace a threshold alert; v2 output is an *additional* advisory layer, never the only warning path. |
| Explainability | Every v2 prediction must name the features that drove it. An unexplainable "risk: 73%" is not acceptable for an animal's health. |
| Claims | Until validated on a labelled dataset, v2 output is labelled "experimental — not a diagnosis", and the app never implies veterinary advice. |

---

## 7. Prerequisites to start v2 (checklist)

| # | Prerequisite | Owner | Status |
|---|---|---|---|
| 1 | v1 stable: ≥ 30 days of continuous data at ≥ 95% coverage | firmware/backend | ☐ |
| 2 | Keeper log in the app (feeding, shedding, weight, health events) | app | ☐ (v1.1 feature) |
| 3 | Label workflow: resolve reasons and health events exportable with timestamps | backend | ☐ partial (resolve reasons exist) |
| 4 | Feature pipeline: daily summaries → a training table, versioned and reproducible | backend/DS | ☐ |
| 5 | Data quality report: coverage, fault minutes, gaps per day | backend | ☐ (coverage exists; the report does not) |
| 6 | Behaviour sensing decision: PIR/IMU vs thermal vs camera-only | hardware | ☐ |
| 7 | Ethics/consent wording reviewed | all | ☐ |
| 8 | Baseline benchmark recorded (threshold engine + majority class) | DS | ☐ |

**If a dataset cannot be obtained, the honest v2 is:** a better *explainable* exposure model plus behaviour
visualisation for the keeper — no ML claims. That is a legitimate outcome and should be stated rather than
dressed up.

---

## 8. v2 milestone sketch (post-course, or a follow-on project)

| Milestone | Content | Exit criterion |
|---|---|---|
| V2-M1 | Keeper log + label export | ≥ 50 labelled days |
| V2-M2 | Feature pipeline + quality report | Reproducible training table from raw data in one command |
| V2-M3 | Descriptive analysis + hypothesis test | At least one hypothesis accepted *or* rejected with a stated sample size |
| V2-M4 | Baseline models (logistic + GBDT) vs trivial baseline | Reported precision/recall/lead time with confidence intervals |
| V2-M5 | Explanation layer + app surface | Every prediction shows its driving features; experimental label present |
| V2-M6 | Behaviour sensing (if M1–M5 succeed) | Activity index collected for ≥ 30 days and correlated with the log |

---

## 9. What would make this project genuinely different

Most IoT terrarium projects stop at "here is a chart". The three things that make SmartReptile worth extending
are already in v1 and should be preserved in v2:

1. **Thresholds as cited data** — the model's features inherit provenance, so a prediction can be traced back to
   a husbandry source rather than a programmer's guess.
2. **Exposure in degree-hours, not peak readings** — physiologically meaningful, and a strong feature for v2
   without any new hardware.
3. **Honest gaps** — the system records what it did not measure (coverage, faults, low-confidence days). Most
   datasets that students train on are silently imputed; this one is not, which is exactly what a model needs.
