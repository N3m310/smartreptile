# 01 — Test Strategy and Plan

## 1. What is being de-risked

SmartReptile has three failure modes that matter more than "does it compile":

1. **A wrong number looks right.** A stale value rendered as current, or a band applied in the wrong phase,
   tells the keeper their animal is fine when it is not.
2. **A missing alert.** The dwell/hysteresis/dedupe logic is stateful and time-dependent, so it is exactly
   the kind of code that passes a happy-path test and fails in week two.
3. **Silent data loss.** A dropped batch or a duplicate sample corrupts both the alerts and the future
   training set.

The test strategy is therefore weighted accordingly: **the pure decision logic and the ingest/idempotency
path get the most tests**, not the CRUD endpoints.

## 2. Test pyramid and tooling

| Layer | Count target | Tooling | Scope | Runtime budget |
|---|---|---|---|---|
| Firmware unit (native) | TC-U-FW-01…12 | PlatformIO `native` + Unity | Filters, ring buffer, payload builder, plausibility, state machine transitions | < 5 s total |
| Backend unit | TC-U-01…50 | xUnit + FluentAssertions, `Clock.Fake`, fakes for all ports | Validators, calibration, `ThresholdDecision` matrix, band resolution, exposure maths, notification policy matrix | < 20 s total |
| Backend integration | TC-I-01…15 | xUnit + Testcontainers (SQL Server) + in-process MQTT client + `WireMock.Net` | Ingest → persist → evaluate → notify, retention, exports, coverage, SignalR, auth | < 3 min total |
| App widget/unit | TC-W-01…18 | `flutter_test` + `mocktail` | Providers, metric cards, staleness, claim flow, threshold validation, role gating, navigation, a11y labels | < 60 s total |
| End-to-end | TC-E2E-01…06 | Real ESP32 + Docker host + real phone/browser, scripted manual runs | The whole chain including hardware and push delivery | One 30 min session per milestone |
| Exploratory / chaos | (unscripted) | Human | Kill broker/DB, unplug node, wrong clock, Wi-Fi change | M5 |

Coverage gates (NFR-07): **≥ 70% line coverage** on `SmartReptile.Domain` + `SmartReptile.Application`,
`app/lib/core` + `app/lib/state`, and `firmware/lib`. Coverage on `Infrastructure` and screens is reported
but not gated — chasing coverage there produces assertion-free tests, which is worse than no test.

## 3. Test-type mapping per requirement

| FR | Unit | Integration | Widget | E2E | Note |
|---|---|---|---|---|---|
| FR-01 auth | ✅ U-32…35 | ✅ I-13 | ✅ W-01…03 | | Hashing, rotation, throttle |
| FR-02 RBAC | ✅ U-36 | ✅ I-13 | ✅ W-12 | | 404 vs 403 distinction |
| FR-03 terrariums | | ✅ I-07 | ✅ W-04 | ✅ E2E-01 | Cascade + soft delete |
| FR-04 provisioning | | ✅ I-05, I-13 | ✅ W-14 | ✅ E2E-01 | Claim code TTL/single-use |
| FR-05 device auth | ✅ U-05 | ✅ I-05, I-13 | | ✅ E2E-01 | Revoke + rotate grace |
| FR-06 ingest | ✅ U-01…09 | ✅ I-01…04, I-08 | | ✅ E2E-01 | Duplicate/implausible/back-fill |
| FR-07 sensor health | ✅ U-27…31 | ✅ I-09 | ✅ W-07 | ✅ E2E-05 | Silence ≠ safety |
| FR-08 live dashboard | | ✅ I-11 | ✅ W-04…08 | ✅ E2E-03 | Freshness + staleness |
| FR-09 history | | ✅ I-10, I-11 | ✅ W-09, W-10 | | Bucketing + gap-awareness |
| FR-10 thresholds | ✅ U-21…26 | ✅ I-07 | ✅ W-13 | ✅ E2E-04 | Band ordering + source precedence |
| FR-11 engine | ✅ U-10…20 | ✅ I-06 | | ✅ E2E-03 | **Highest-value tests in the project** |
| FR-12 alert lifecycle | ✅ U-26 | ✅ I-07 | ✅ W-11, W-12 | ✅ E2E-03 | dedupe index, resolve reasons |
| FR-13 notifications | ✅ U-37…45 | ✅ I-12 | ✅ W-16 | ✅ E2E-03 | Policy matrix + retries |
| FR-14 summaries | ✅ U-46…50 | ✅ I-06 | ✅ W-15 | | Exposure maths |
| FR-15 retention/export | ✅ U-49 | ✅ I-14 | ✅ W-15 | | Purge + recompute |
| FR-16 fleet | | ✅ I-07 | ✅ W-14 | | Command ack timeout |
| FR-17 camera (optional) | | ✅ I-15 (partial) | ✅ W-17 | | Rate limit + retention |
| FR-18 ops/audit | | ✅ I-15 | | ✅ E2E-06 | Counters + `/ready` semantics |
| NFR-01/02 | | ✅ I-11 | | ✅ E2E-03 | Latency measurement |
| NFR-03 | | ✅ I-08 | | ✅ E2E-02 | Offline resilience + soak |
| NFR-04 | | ✅ I-13 | | ✅ E2E-06 | Security checklist |
| NFR-05 | | | | ✅ E2E-04 | Sensor accuracy vs reference |
| NFR-06 | | | ✅ W-01…18 | | A11y + localisation |
| NFR-10 | ✅ U-25, U-29 | | | | UTC + skew + timezone |
| NFR-11 | ✅ U-49 | ✅ I-14 | | | Storage measurement |
| NFR-12 | | ✅ I-15 | | ✅ E2E-06 | Metrics + health |

## 4. Determinism rules (hard-won, non-negotiable)

| Rule | Reason |
|---|---|
| **Never `await Task.Delay` / `Thread.Sleep` in a test.** Advance `Clock.Fake` instead. | Real delays make suites slow and flaky; the decision logic is time-driven, so the clock must be injectable |
| **Never `await Future.delayed` inside `testWidgets`.** | Fake async → the test hangs until the 10-minute timeout |
| **Never `pumpAndSettle()` while a periodic ticker runs** (live values, staleness timer). Use explicit `pump()` calls. | `pumpAndSettle` never settles when a `Timer.periodic` exists |
| **Scroll before asserting text** in a `ListView`/scrollable screen. | Off-screen widgets are not built, so `find.text` fails confusingly |
| **Seed fixed data, never "now".** Integration tests insert samples at fixed UTC timestamps. | Day/night phase logic and local-day bucketing otherwise depend on when CI runs |
| **One fake MQTT broker per test class**, torn down deterministically. | Ports leak between runs otherwise |
| **Testcontainers SQL Server reused across a collection** (`ICollectionFixture`), schema created by migrations once. | Starting SQL Server per test costs ~20 s and hides real failures in noise |
| Provider tests inject a fake `LiveClient`; no socket is ever opened in a unit test. | Determinism |

## 5. Test data strategy

| Dataset | Scope | Where |
|---|---|---|
| `minimal` | 1 user, 1 terrarium, 1 bound device, 30 samples over 30 min, all in range | `TestData/Minimal.cs` / `seed_minimal.dart` |
| `daily` | 24 h at 60 s (1 440 samples), one 40-minute hot excursion (34.0 °C), one 15-minute humidity dip, one 2 h data gap | `TestData/Daily.cs` — the primary fixture for summaries, exposure and coverage |
| `monthly` | 30 days of hourly rollups (720 rows/metric) for range/perf tests | generated, checked into `TestData/Rollups/` as a compressed fixture |
| `multi-tenant` | 2 users × 2 terrariums each, to prove isolation (FR-02) | `TestData/MultiTenant.cs` |
| `hardware` | Real node on a bench, values recorded in the QA log | `04-quality/03-manual-qa-checklist.md` §2 |

Threshold fixtures always use the seeded semi-arid profile so expected results can be computed by hand in
the test comment (`// target 26–32, 34.0 for 40 min → 1.33 °C·h hot`).

## 6. What is *not* automated (and why)

| Not automated | Reason | Compensating control |
|---|---|---|
| Real push delivery to a device | Needs a real Google Play services device and is flaky in CI | Telegram channel is automated in `TC-I-12`; FCM is checked manually in `TC-E2E-03` |
| Sensor accuracy | Physics, not software | Manual reference comparison in `TC-E2E-04` with recorded numbers |
| 24 h soak | Time | Manual soak with an incident log (`TC-E2E-02`) |
| Capacitive/wear behaviour of NVS writes | Requires a long-term hardware study | Documented as an assumption with the measured write rate |
| Usability with a real keeper | No test participants guaranteed | Heuristic review against the UX rules + accessibility checks |
| Load beyond 50 RPS | Out of scope for a single-host demo | Trend recorded, no claim made (NFR-01 note) |

## 7. Test execution plan

| When | Suite | Gate |
|---|---|---|
| Pre-commit (local) | Backend unit + app unit (fast subset) | Must pass |
| CI on PR | All unit + integration + `flutter analyze` + `pio run`/`pio test -e native` + secret scan | Must pass; coverage delta reported |
| Per milestone (M2…M6) | E2E script set, one full session, results recorded in the QA log | Required for the milestone DoD |
| Weekly (M3 onward) | 8 h soak overnight with the real node | Trend recorded; regression investigated |
| Before submission | Full run: unit + integration + widget + E2E + soak + chaos drills | All evidence saved as screenshots/logs for the report |

## 8. Defect management

- Bugs found by tests get an id `BUG-xx` in `05-release/03-risks-assumptions-decisions.md` §4 with: symptom,
  root cause, fix commit, and the test that now prevents regression. A bug fixed without a regression test is
  an open bug.
- Severity: `S1` wrong data shown as correct / lost sample / alert missed · `S2` feature broken with a
  workaround · `S3` cosmetic.
- `S1` blocks the milestone. No exceptions, because an `S1` in a monitoring system is exactly what the
  product exists to prevent.

## 9. Evidence checklist for the report (rubric + honesty)

| Evidence | Where it goes |
|---|---|
| `dotnet test` summary with pass counts | Report §Testing, appendix screenshots |
| `flutter test` summary | Report §Testing |
| `pio test -e native` + `pio run -t size` output | Report §Firmware |
| Coverage reports (backend + app core) | Report §Testing, with the gate values |
| k6 latency results against NFR-01/02 | Report §Performance |
| Soak log (24 h) with coverage % and alert count | Report §Reliability |
| Security checklist output (`02-design/06` §9) | Report §Security |
| Chaos drill observations | Report §Reliability |
| Sensor accuracy comparison table | Report §Hardware, appendix |
| Screenshots: red alert state, wallboard, release-mode app | Report figures |
