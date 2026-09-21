# 03 — Risks, Assumptions, Decisions and Open Questions

## 1. Risk register

`L` = likelihood, `I` = impact (1 low … 3 high), `Score` = L×I. Owner is a role, not a name.
Mitigations are the actions already designed into the system; **contingencies** are what we do if the risk
materialises anyway.

| Id | Risk | L | I | Score | Mitigation (designed) | Contingency | Owner |
|---|---|---|---|---|---|---|---|
| R-01 | Threshold values are wrong for the chosen species → false confidence or alert spam | 2 | 3 | 6 | Bands live in data with a `SourceRef` per row; verification checklist in `07-appendices/05` §5; sanity warnings for implausible bands | Tighten bands manually before the demo and state the corrected values in the report | Product/doc |
| R-02 | Sensor drift after installation → quiet wrong data | 2 | 3 | 6 | 1-point offset calibration (FR-07), plausibility guard, QA §2 reference comparison, `SensorFault` events | Recalibrate and re-run the accuracy check; report the measured drift | Firmware |
| R-03 | Wi-Fi unreliable at the terrarium location (weak RSSI, 2.4 GHz congestion) | 3 | 2 | 6 | Ring buffer ≥ 12 h, back-fill, HTTPS fallback, RSSI reported in health | Move the node or add a repeater; report the gap honestly | Firmware |
| R-04 | TLS certificate/hostname problems at the demo host | 2 | 3 | 6 | Self-signed/internal CA with the CA flashed into the ESP32 and trusted by the app; `/ready` shows broker connectivity | Fall back to a documented local HTTPS setup or (as an explicitly disclosed last resort) plaintext on the isolated demo network, stated in the report | Backend |
| R-05 | Team schedule slips (3 courses, exams) | 3 | 3 | 9 | Milestones are independently demoable; M3 (the mentor demo) is the priority; M4’s optional items (FR-17, gradient/light signals) are cut-first | Cut FR-17 and the SHOULD signals; ship M1–M3 + a reduced app | All |
| R-06 | Device flash read-out reveals the secret (no secure element) | 1 | 2 | 2 | Per-device secret with minimal blast radius (own terrarium only), revoke path, TLS-only | Revoke the credential and re-provision; disclose as limitation L-01 | Security |
| R-07 | Hardware failure (dead SHT31/BH1750, cracked box, dead board) | 2 | 2 | 4 | Spare sensor modules in the BOM, `SensorFault` reporting, bench-mode fake sensors to keep developing | Swap the module; demo with the fake-sensor bench mode and say so | Hardware |
| R-08 | Threshold edits during an open alert confuse history | 1 | 2 | 2 | `ThresholdSnapshot` per change; open alerts keep their band | Explain with the snapshot rows in the demo | Backend |
| R-09 | Alert spam makes the demo look broken | 2 | 3 | 6 | Dwell + hysteresis + dedupe + cooldown + hourly rate limit + digest + silence + maintenance mode | Silence the metric or set the device to Maintenance during the demo; the alert history still shows the earlier events | Backend |
| R-10 | 24 h soak reveals a leak/reset that invalidates performance claims | 2 | 2 | 4 | Heap logging every 5 min, watchdog with reset counter reported in health, device-side counters | Report the reset with the root cause and the fix; if unfixed, say so in the limitations | Firmware |
| R-11 | Database grows beyond expectations during testing | 1 | 2 | 2 | 90-day retention with nightly sweeper; measured storage in NFR-11 | Run the purge manually before the demo | Backend |
| R-12 | Mentor requires a direction change (e.g. insists on actuators or AI in v1) | 2 | 3 | 6 | Architecture isolates the evaluator so actuation could be added as a *sink* of alerts, not a rewrite; v2 roadmap already drafted | Present ADR-008 with the safety and scope argument; if overruled, schedule a v1.1 actuator milestone rather than squeezing it in | All |
| R-13 | FCM delivery fails on demo day (Google Play services, device quirks) | 2 | 2 | 4 | Telegram as a second channel; in-app inbox always works | Demo the alert via Telegram + inbox and explain the FCM fallback | Backend |
| R-14 | Two people edit the same doc/code with conflicting intent | 2 | 1 | 2 | Stable ids, single accountable owner per milestone, PR review, doc-first interface rule | Same-PR correction; no id renumbering | All |
| R-15 | Public repository leaks secrets | 1 | 3 | 3 | `.env` git-ignored, secret scan in CI, `.env.example` only | Rotate the leaked credentials immediately and note it in the risk log | All |

Top three by score: **R-05 (schedule)**, then R-01/R-02/R-03/R-04/R-09/R-12 (all 6). The list is ordered by
score so the team knows where to spend attention, and it is re-scored at each milestone.

## 2. Assumptions

| Id | Assumption | If false… |
|---|---|---|
| A-01 | The terrarium holds one animal of one species (single microclimate, one control point) | Multi-sensor support would be needed; the data model already allows multiple devices per terrarium in the future (currently constrained to 1 for clarity) |
| A-02 | Conditioned indoor space: the room can be heated/cooled by the keeper; the system only observes | Alerts would need an "actuation" tier (v1.1) |
| A-03 | Mains power is available at the node (no battery life constraint) | Would need deep-sleep scheduling; 60 s sampling becomes impractical on a small battery |
| A-04 | 2.4 GHz Wi-Fi reaches the terrarium (ESP32 has no 5 GHz radio) | Move the node or add an access point (R-03) |
| A-05 | One backend user accounts for the whole demo fleet (no multi-tenant isolation requirements beyond role scoping) | A tenant/org model would be needed; deferred (out of scope) |
| A-06 | Hobby-grade sensor accuracy is acceptable because the product's value is *trend and threshold* monitoring, not metrology | Would need laboratory sensors; the cost budget (NFR-09) would break |
| A-07 | The keeper can act on an alert within hours, not seconds — hence dwell time over instant alerting | A life-support use case would invert the priority |
| A-08 | Time-series queries stay within a single SQL Server instance at demo scale | Would need a time-series store; the bucketing/rollup design already reduces pressure |
| A-09 | Users accept that the app requires a network for live values (offline shows cached values with timestamps only) | Would need a full offline-first sync engine |
| A-10 | No legal/regulatory constraint applies to a personal terrarium monitor in the demo jurisdiction | A privacy/consent review of the camera feature would be required before enabling FR-17 by default |

## 3. Decision log (index)

Full text for each decision is in `07-appendices/01-adr-log.md`.

| ADR | Decision (one line) | Status |
|---|---|---|
| ADR-001 | MQTT as primary telemetry transport, HTTPS POST as fallback | Accepted |
| ADR-002 | ASP.NET Core 10 + EF Core + SQL Server for the backend | Accepted |
| ADR-003 | Flutter for the mobile app; light vanilla-JS web dashboard (no build step) | Accepted |
| ADR-004 | Normalised telemetry (`TelemetrySample` + `MetricReading`) with hourly rollups | Accepted |
| ADR-005 | Threshold evaluation server-side only; device reports raw values and faults | Accepted |
| ADR-006 | Per-device 256-bit secret over TLS; no mTLS in v1 | Accepted (mTLS = v1.1) |
| ADR-007 | Alert channels: FCM push primary, Telegram secondary, SMTP optional | Accepted |
| ADR-008 | Monitoring only in v1; no actuators | Accepted (see R-12) |
| ADR-009 | Retention: raw 90 days, hourly rollups 24 months, daily summaries indefinite | Accepted |
| ADR-010 | No AI/ML in v1; instead a documented dataset and feature schema for v2 | Accepted |
| ADR-011 | SignalR for live push, REST for history, `provider` for app state | Accepted |
| ADR-012 | Single-host Docker Compose deployment | Accepted |
| ADR-013 | Bilingual UI (vi default, en), strings localised at render time, not stored per language | Accepted |
| ADR-014 | SHT31 + BH1750 (+ LTR390/DS18B20) over DHT22; median-of-5 + EMA filtering | Accepted |
| ADR-015 | UTC everywhere; server-authoritative `ReceivedAt`; local-day bucketing only in the rollup layer | Accepted |

## 4. Bug and issue log (append-only)

| Id | Date | Symptom | Root cause | Fix | Regression test | Severity |
|---|---|---|---|---|---|---|
| BUG-01 | | *(example)* Zig-zag alerts around the band edge | Recovery compared to the band edge, not the margin | Compare with `RecoveryMargin` | TC-U-15 | S1 |
| BUG-02 | | *(example)* Duplicate samples after an outage | Sequence number reset on reset before NVS commit | Persist the counter with the sample, commit before publish | TC-I-02, TC-U-FW-06 | S1 |
| | | | | | | |

Rule: an `S1` (wrong data shown as correct, lost sample, missed alert) blocks the milestone until the
regression test exists and fails on the pre-fix commit (`04-quality/01` §8).

## 5. Documentation log

| Date | Change | Reason |
|---|---|---|
| 2026-09-21 | Doc set v1 created (34 files: 33 documents + this index) | Initial design from the SmartReptile project description (v1, no AI) |
| | | |

## 6. Open questions to close with the mentor

| Id | Question | Our proposed answer | Impact if different |
|---|---|---|---|
| TBC-1 | Which species/branch defines the demo thresholds? | Semi-desert — leopard gecko, with tropical and arid profiles also seeded | Threshold tables in `07-appendices/05` are re-derived; the engine itself does not change |
| TBC-2 | Is 20×10 cm the floor or the whole box? | Floor of a small starter box, treated as a scale model | Enclosure planning and the accuracy discussion in the report; no software impact |
| TBC-3 | Alert channel and camera? | FCM + Telegram for the demo; camera optional (FR-17, snapshot only, no analysis) | If a camera is required, the BOM grows ~250–400 k VND and FR-17 moves from MAY to MUST |
| TBC-4 | How does the claim secret reach the device? | Pairing token over the local network (option A); manual entry (option B) as fallback | Firmware adds/removes one endpoint; the provisioning UX changes |
| TBC-5 | Is a web dashboard required in addition to the app? | Both, because the coursework requires the mobile app and the dashboard is cheap on the same API | If only one is required, the web dashboard is reduced to the wallboard page |
| TBC-6 | Minimum evidence for "release mode proof"? | APK signature output + About screen screenshot + a push received while installed from the APK | Changes the M6 checklist only |

## 7. Explicit limitations shipped with v1

| Id | Limitation | Why accepted | Where it is disclosed |
|---|---|---|---|
| L-01 | Device secret extractable from flash by physical access | No secure element on the chosen board; blast radius limited to one terrarium and revocation exists | Report §Security; `02-design/06` §7 (I10) |
| L-02 | No password reset (self-service) | No email infrastructure in the demo environment | Report §Limitations; settings screen note |
| L-03 | No OTA firmware update | Removes an unsigned-update attack surface; patching means USB | Report §Limitations; `02-design/06` §7 (I4) |
| L-04 | Hobby-grade accuracy, indicative only | Cost budget and the product's purpose (trending, not metrology) | Report §Hardware; app footnote on the diagnostics screen |
| L-05 | No actuator control | Safety and scope discipline (ADR-008) | Report §Future work |
| L-06 | Storage/retention figures extrapolated, not observed over 90 days | One-semester timeline | Report §Performance, with the measured 7-day basis |
| L-07 | No multi-tenant organisation model | Single-owner product scope | Report §Limitations |
| L-08 | UV readings are indicative; no calibration reference available | No reference instrument in the lab | Report §Hardware; the profile treats UV bands as advisory with long dwell |
