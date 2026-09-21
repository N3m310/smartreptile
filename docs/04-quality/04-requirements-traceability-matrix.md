# 04 — Requirements Traceability Matrix

Every requirement traced end to end: **requirement → design decision → code module → test → report section**.
This is the document that answers "where is this implemented, and how do you know it works?" in one table.

Report section codes: `R1` Introduction · `R2` Case study & business analysis · `R3` Requirements ·
`R4` Architecture & design · `R5` Hardware & firmware · `R6` Implementation · `R7` Testing & QA ·
`R8` Security · `R9` Performance & reliability · `R10` Deployment & release · `R11` Results, limitations,
future work · `R12` Conclusion · `R13` References · `R14` Appendices.

---

## 1. Functional requirements

| FR | Design doc | Code module | Tests | Report § | Status |
|---|---|---|---|---|---|
| FR-01 Account & session | `02-design/06` §2 | `Api/Endpoints/AuthEndpoints.cs`, `Application/Auth/*`, `Infrastructure/Security/Pbkdf2PasswordHasher.cs`, `app/lib/state/auth_provider.dart` | TC-U-32…35, TC-I-13, TC-W-01…03 | R3, R6, R8 | ☐ |
| FR-02 RBAC | `02-design/06` §3 | `Api/Security/TerrariumAccessHandler.cs`, policies in `Program.cs`, role gates in UI widgets | TC-U-36, TC-I-13, TC-W-12 | R3, R6, R8 | ☐ |
| FR-03 Terrarium management | `02-design/02` §3.5 | `Api/Endpoints/TerrariumEndpoints.cs`, `Application/Terrariums/*`, `app/lib/screens/terrariums/*` | TC-I-07, TC-W-04, TC-E2E-01 | R3, R4, R6 | ☐ |
| FR-04 Provisioning | `02-design/06` §5, `02-design/02` §4.1 | `firmware/src/net/wifi_manager.cpp`, `Application/Devices/ClaimService.cs`, `app/lib/screens/devices/claim_screen.dart` | TC-I-05, TC-I-13, TC-W-14, TC-E2E-01 | R5, R6, R8 | ☐ |
| FR-05 Device auth | `02-design/06` §4, ADR-006 | `Infrastructure/Mqtt/*`, `Infrastructure/Security/DeviceSecretHasher.cs`, `firmware/src/net/mqtt_transport.cpp` | TC-U-05, TC-I-05, TC-I-13, TC-E2E-01 | R8 | ☐ |
| FR-06 Telemetry ingest | `02-design/03` §1–3, `07-appendices/03` | `Api/Workers/IngestWorker.cs`, `Application/Ingest/*`, `Infrastructure/Mqtt/*`, `firmware/src/net/*` | TC-U-01…09, TC-U-FW-07…09, TC-I-01…04, TC-I-08, TC-E2E-01 | R4, R5, R6 | ☐ |
| FR-07 Sensor health & buffering | `02-design/03` §4.3, §8 | `Domain/Devices/QualityFlags.cs`, `Api/Workers/DeviceSilenceWatchdog.cs`, `firmware/lib/ringbuffer/*` | TC-U-27…31, TC-U-FW-04…06, TC-I-04, TC-I-09, TC-W-07, TC-E2E-05 | R5, R9 | ☐ |
| FR-08 Live dashboard | `02-design/04` §4.1, §6 | `app/lib/state/telemetry_provider.dart`, `app/lib/widgets/metric_card.dart`, `web/js/live.js` | TC-I-11, TC-W-04…08, TC-E2E-03 | R6, R9 | ☐ |
| FR-09 History & range queries | `02-design/03` §6, `07-appendices/03` §4.5 | `Application/Readings/RangeQueryService.cs`, `app/lib/widgets/chart_panel.dart` | TC-I-10, TC-I-11, TC-W-09, TC-W-10 | R6, R9 | ☐ |
| FR-10 Species profiles & thresholds | `03-implementation/06`, `07-appendices/05`, ADR-005 | `Domain/Thresholds/*`, `Application/Thresholds/ThresholdService.cs`, `app/lib/screens/thresholds/*` | TC-U-21…26, TC-I-07, TC-W-13, TC-E2E-04 | R3, R6, R13 | ☐ |
| FR-11 Threshold engine | `02-design/03` §4, ADR-005 | `Domain/Alerts/ThresholdDecision.cs`, `Api/Workers/EvaluatorWorker.cs`, `Application/Alerts/*` | TC-U-10…20, TC-I-06, TC-E2E-03 | R4, R6, R7 | ☐ |
| FR-12 Alert lifecycle | `02-design/03` §5, `02-design/02` §3.14 | `Domain/Alerts/Alert.cs`, `Api/Endpoints/AlertEndpoints.cs`, `app/lib/screens/alerts/*` | TC-U-26, TC-I-07, TC-W-11, TC-W-12, TC-E2E-03 | R4, R6 | ☐ |
| FR-13 Notifications | `02-design/05`, ADR-007 | `Application/Notifications/*`, `Infrastructure/Channels/{Fcm,Telegram,Smtp}Channel.cs`, `Api/Workers/NotificationWorker.cs` | TC-U-37…45, TC-I-12, TC-W-16, TC-E2E-03 | R4, R6, R9 | ☐ |
| FR-14 Summaries & exposure | `03-implementation/06` §5, `02-design/03` §6 | `Application/Summaries/DailySummaryBuilder.cs`, `Domain/Summaries/ExposureCalculator.cs`, `app/lib/screens/report/*` | TC-U-46…50, TC-U-31, TC-I-06, TC-W-15 | R4, R6, R11 | ☐ |
| FR-15 Retention & export | `02-design/02` §6, ADR-009 | `Api/Workers/RetentionSweeperWorker.cs`, `Application/Exports/*` | TC-U-49, TC-I-14, TC-W-15 | R6, R11 | ☐ |
| FR-16 Fleet management | `02-design/02` §3.6, `02-design/03` §2 | `Api/Endpoints/DeviceEndpoints.cs`, `Application/Devices/*`, `app/lib/screens/devices/*` | TC-I-07, TC-W-14 | R6 | ☐ |
| FR-17 Camera snapshot (optional) | `02-design/04` §1.1 S15, BR-17.x | `Api/Endpoints/DeviceEndpoints.cs` (snapshots), `Infrastructure/Storage/SnapshotStore.cs` | TC-I-15 (partial), TC-W-17 | R6, R11 | ☐ optional |
| FR-18 Ops & audit | `01-product/04` NFR-12, `02-design/01` §8 | `Api/Endpoints/OpsEndpoints.cs`, `Infrastructure/Observability/Metrics.cs`, `Application/Audit/AuditWriter.cs` | TC-I-15, TC-E2E-06 | R9, R10 | ☐ |

## 2. Non-functional requirements

| NFR | Budget | Design / mechanism | Verification | Report § | Status |
|---|---|---|---|---|---|
| NFR-01 API performance | p95 ≤ 300 ms, payload ≤ 200 KB | Rollup-first bucketing, covering indexes (`03-implementation/03` §10) | TC-I-11, k6 50 RPS | R9 | ☐ |
| NFR-02 Freshness/latency | ≤ 5 s to screen; ≤ 90 s to notify | SignalR push, post-commit broadcast, dwell timing | TC-E2E-03, TC-I-11 | R9 | ☐ |
| NFR-03 Availability/offline | 0 loss ≤ 12 h outage | Device ring buffer (720), broker back-pressure, idempotent ingest | TC-I-08, TC-E2E-02, drill 5.1/5.2 | R9 | ☐ |
| NFR-04 Security | TLS everywhere, hashed secrets, scoped data | `02-design/06` §2–7, OWASP IoT review §7 | Security checklist §9, TC-I-13, secret scan | R8 | ☐ |
| NFR-05 Measurement accuracy | ±0.5 °C / ±3 %RH / ±10% lux | SHT31+BH1750 (ADR-014), filter chain, offset calibration | TC-E2E-04, QA log §2 | R5, R11 | ☐ |
| NFR-06 Usability/accessibility | ≤ 3 taps, ≥ 4.5:1, vi+en | `02-design/04` §6–8, `core/status.dart`, Semantics labels | TC-W-01…18, QA log §4 | R6, R11 | ☐ |
| NFR-07 Maintainability | ≥ 70% core coverage, clean analyzers | Layered architecture with enforced dependency rules, `Result` pattern | CI gates, coverage reports | R7 | ☐ |
| NFR-08 Deployability | Compose up ≤ 5 min, pinned versions | `docker-compose.yml`, `platformio.ini`, `pubspec.lock` | M6 drill 6.2, clean-clone CI job | R10 | ☐ |
| NFR-09 Cost | BOM ≤ 1.5 M VND | `07-appendices/04` §1 BOM with prices | BOM total, receipts recorded | R5, R11 | ☐ |
| NFR-10 Time correctness | UTC storage, ±2 s drift, TZ rendering | `IClock`, device NTP, `ReceivedAt` authority, local-day bucketing | TC-U-25, TC-U-29, TC-I-12 | R4, R6 | ☐ |
| NFR-11 Retention/storage | 90 d raw < 60 MB, rollups 24 m | Sweeper + rollup pipeline, measured estimate | TC-U-49, TC-I-14, measured 7-day extrapolation | R9, R11 | ☐ |
| NFR-12 Observability | health/ready/metrics, correlation ids | `OpsEndpoints`, Serilog, counters per stage | TC-I-15, TC-E2E-06 | R9, R10 | ☐ |

## 3. Use-case coverage

| Use case | Flows exercised by | Report § |
|---|---|---|
| UC-01 Provision & bind | TC-I-05, TC-I-13, TC-W-14, TC-E2E-01, QA 3.1–3.3 | R5, R8 |
| UC-02 Monitor live | TC-I-11, TC-W-04…08, TC-E2E-03, QA 6.1/6.4 | R6 |
| UC-03 Configure thresholds | TC-U-21…26, TC-W-13, QA 3.6/3.7 | R3, R6 |
| UC-04 Alert lifecycle | TC-U-10…20, TC-U-37…43, TC-I-07, TC-E2E-03, QA 3.8/3.9 | R4, R7 |
| UC-05 Review & export | TC-U-46…50, TC-I-14, TC-W-15, QA 3.10 | R6 |
| UC-06 Device silence/recovery | TC-U-27, TC-I-08/09, TC-E2E-05, QA 5.1/5.4 | R9 |
| UC-07 Verify & calibrate | TC-U-09, TC-E2E-04, QA §2 | R5, R11 |

## 4. User-story coverage (US-01…US-20 → evidence)

| US | FR | Evidence |
|---|---|---|
| US-01, US-02, US-03 | FR-01 | TC-W-01…03, QA 3.4 |
| US-04, US-05 | FR-02 | TC-U-36, TC-W-12, QA 3.4/3.5 |
| US-06 | FR-03 | TC-I-07, TC-W-04 |
| US-07 | FR-10 | `07-appendices/05` profiles + TC-U-24 |
| US-08, US-09 | FR-04 | TC-E2E-01, QA 3.1/3.2 |
| US-10, US-11 | FR-10 | TC-U-21…25, TC-W-13 |
| US-12, US-13 | FR-08, FR-10 | TC-W-04, TC-W-07, QA 6.1 |
| US-14 | FR-13 | TC-E2E-03, QA 9.5 |
| US-15 | FR-07 | TC-U-27, TC-E2E-05, QA 6.4 |
| US-16, US-17 | FR-12 | TC-I-07, QA 3.8/3.9 |
| US-18 | FR-09 | TC-W-09/10 |
| US-19 | FR-14 | TC-U-46…50, TC-W-15, QA 6.2 |
| US-20 | FR-14, FR-15 | TC-I-14, QA 3.10 |

## 5. Acceptance criteria → test index (the ones reviewers ask about)

| Acceptance criterion (from `01-product/03`) | Test |
|---|---|
| Duplicate batch does not create a second sample | TC-I-02 |
| Implausible value stored but never alerted | TC-U-06, TC-I-03 |
| Four minutes out of band → no alert | TC-U-10 |
| Five minutes out of band → exactly one alert, back-dated | TC-U-11 |
| Critical crossing escalates the same alert | TC-U-13 |
| Recovery requires the margin for 3 minutes | TC-U-15/16 |
| Night band prevents a false alert | TC-U-25, worked example C in `03-implementation/06` §3 |
| Silence ≠ safety (no metric alerts from missing data) | TC-U-27, TC-I-09 |
| Coverage badges always accompany a summary | TC-W-15, TC-U-49 |
| Exposure index matches the hand-computed example (1.67 °C·h) | TC-U-46 |
| Expired/consumed claim code returns the same error as unknown | TC-I-05, TC-I-13 |
| Foreign terrarium returns 404, insufficient role returns 403 | TC-I-13, TC-U-36 |
| Revoked device blocked within 60 s | TC-I-13, QA 5.8 |
| Quiet hours suppress Warning but not Critical | TC-U-38 |
| Rate limit coalesces into a digest (alerts still recorded) | TC-U-40 |

## 6. Known gaps (stated, not hidden)

| Gap | Consequence | Where declared |
|---|---|---|
| No automated test for real FCM delivery | Push regressions are caught only manually | `04-quality/01` §6 |
| Sensor accuracy is verified manually | A drifting sensor is caught by the 24 h soak/QA log, not CI | `04-quality/01` §6 |
| Physical tampering of the node is unprotected | A stolen node's secret can be extracted | `02-design/06` §7 (I10), limitation L-01 |
| No password reset flow | A forgotten password means account recreation in the demo | `05-release/03` limitation L-02 |
| No OTA firmware update | A deployed node cannot be patched remotely | `02-design/06` §7 (I4), limitation L-03 |
| Threshold literature verification is manual | A wrong band ships if the checklist is skipped | `07-appendices/05` §5 (gate) |
| Load tested only to 50 RPS on one host | No scalability claim is made | `01-product/04` §1 NFR-01 note |

## 7. Maintenance rule

This matrix is updated **in the same PR** as any change to an FR, a design decision, or a code module
(see `03-implementation/02` §6). A row whose "Tests" cell names a non-existent test id is treated as a
failing CI check in review: the traceability matrix is only useful if it cannot drift from the code.
