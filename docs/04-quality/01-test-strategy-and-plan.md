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
| App widget/unit | TC-W-01…18 | `flutter_test` + `mocktail` | Providers, metric cards, staleness, claim flow, threshold validation, permission gating, navigation, a11y labels | < 60 s total |
| App end-to-end (automated) | E2E-AI-01…15 | `integration_test` on an emulator: `smoke` (no backend), `hermetic` (in-process `HttpServer` stub), `fullstack` (compose), `alerts` (telemetry through the device-facing ingest route) | What a person would do **in the app**: sign-in, refusal, registration, language, empty states, enclosure list/detail, history, sign-out, and an excursion appearing as an alert. Runs unattended: `scripts/e2e.ps1` | ~8 min for all four on one emulator |
| End-to-end (hardware) | TC-E2E-01…06 | Real ESP32 + Docker host + real phone/browser, scripted manual runs | The whole chain including hardware and push delivery — the half no software test can reach | One 30 min session per milestone |
| Exploratory / chaos | (unscripted) | Human | Kill broker/DB, unplug node, wrong clock, Wi-Fi change | M5 |

The two E2E rows are one case set split by what each half can honestly assert: `docs/04-quality/05-e2e-ai-testing-plan.md`
§1.1 draws the boundary case by case, and `02-test-cases.md` §E.1 records which ids are green.

Coverage gates (NFR-07): **≥ 70% line coverage** on `SmartReptile.Domain` + `SmartReptile.Application`,
`app/lib/core` + `app/lib/state`, and `firmware/lib`. Coverage on `Infrastructure` and screens is reported
but not gated — chasing coverage there produces assertion-free tests, which is worse than no test.

## 3. Test-type mapping per requirement

| FR | Unit | Integration | Widget | E2E | Note |
|---|---|---|---|---|---|
| FR-01 auth | ✅ U-32…35 | ✅ I-13 | ✅ W-01…03 | | Hashing, rotation, throttle |
| FR-02 RBAC | ✅ U-36 | ✅ I-13 | ✅ W-12 | | the matrix is data (`02-design/06` §3); 404 vs 403 distinction |
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
| Real push delivery to a device | Needs a real Google Play services device and is flaky in CI | The FCM channel is faked in `TC-I-12`; a real push is checked manually in `TC-E2E-03` |
| Sensor accuracy | Physics, not software | Manual reference comparison in `TC-E2E-04` with recorded numbers |
| 24 h soak | Time | Manual soak with an incident log (`TC-E2E-02`) |
| A **real** ESP32 in the alert chain | The node is the only thing that can cause the excursion physically; the pipeline behind it is not the part being doubted | `E2E-AI-14` injects a batch through the device-facing ingest route (`POST /api/v1/ingest/http`), which runs the same `IngestPipeline` as MQTT; the hardware run of `TC-E2E-03` stays in the QA log |
| The offline/silence presentation, timed | The watchdog needs `3 × interval` of wall clock (≥ 3 min) | `E2E-AI-15` is written but not gated; `TC-I-09` covers the decision itself |
| Capacitive/wear behaviour of NVS writes | Requires a long-term hardware study | Documented as an assumption with the measured write rate |
| Usability with a real keeper | No test participants guaranteed | Heuristic review against the UX rules + accessibility checks |
| Load beyond 50 RPS | Out of scope for a single-host demo | Trend recorded, no claim made (NFR-01 note) |
| The three TERRAGUARD screens still on `mockData.ts` (`Devices`, `Alerts`, `Settings`) | A screen on mock data has no behaviour to assert, and their endpoints do not exist yet (roadmap 4.20) | The screen keeps the mock-data notice (`web/src/components/MockDataNotice.tsx`), rendered by the shell on exactly those routes, and counts for nothing in the DoD. **The rest of the client left this table on 2026-10-07** when 4.15–4.18 wired it: it now has an API client, a shared key set checked by `TC-I-15`'s parity gate in CI, and screens whose acceptance is the walkthrough in `04-quality/03` §3. The exclusion ends completely when 4.20 can wire the last three |

## 7. Test execution plan

| When | Suite | Gate |
|---|---|---|
| Pre-commit (local) | Backend unit + app unit (fast subset) | Must pass |
| CI on PR | All unit + integration + `flutter analyze` + `pio run`/`pio test -e native` + secret scan | Must pass; coverage delta reported |
| Per milestone (M2…M6) | E2E script set, one full session, results recorded in the QA log | Required for the milestone DoD |
| Weekly (M3 onward) | 8 h soak overnight with the real node | Trend recorded; regression investigated |
| Before submission | Full run: unit + integration + widget + E2E + soak + chaos drills | All evidence saved as screenshots/logs for the report |

> **As-built vs. plan (measured 2026-09-21).** The CI row above is the target, and most of it now exists. The five
> jobs contain: 54 backend unit tests, `flutter analyze` + 19 app tests, 22 firmware host tests +
> `pio run -e esp32dev`, the full-history secret scan, and an **integration job** with a SQL Server 2022 service
> container that runs 8 tests — migrations applied, reference data seeded idempotently, the SQL-level invariants —
> and then boots the API and requires `/health/ready` to report Healthy.
>
> **Corrected 2026-10-17.** That count is a dated snapshot, not the current pipeline: there are **six** jobs now —
> `web` (typecheck, locale-key parity, production build) was added with the web client in `5dd9c6a` — and the five
> measured figures have since moved to **947 backend unit tests**, `flutter analyze` + **182 app tests**, 22 firmware
> host tests, the full-history secret scan, and an integration job running **55 tests**. All six are green on run
> `38949606361`.
>
> Still missing from that row: the E2E script set, the overnight soak and the chaos drills, which belong to M3/M5 as
> planned. One caveat worth stating rather than letting a green check imply otherwise: the integration tests run
> against a single SQL Server instance that is already up, so they say nothing about behaviour when the database is
> unreachable mid-run — that is what the M5 drills are for.
>
> **Corrected 2026-10-30.** The E2E script set is no longer missing: a seventh job, `app-e2e`, runs the four
> `integration_test` suites on an Android emulator against `docker compose`, nightly and by `workflow_dispatch`
> (never on a push, where the `app` job already covers the UI). Locally it is one command —
> `powershell -File scripts\e2e.ps1` — and it exits `0`/`1`/`2` with a JSON report per suite. Measured on the
> development machine that day: `smoke` + `hermetic` + `fullstack` + `alerts` green, 13 cases, ~8 minutes for all
> four including the Gradle build. What remains manual is unchanged and listed in §6.

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
| `powershell -File scripts\e2e.ps1` summary + `build/e2e/*.json` | Report §Testing — the "runs without a person" evidence |
| `pio test -e native` + `pio run -t size` output | Report §Firmware |
| Coverage reports (backend + app core) | Report §Testing, with the gate values |
| k6 latency results against NFR-01/02 | Report §Performance |
| Soak log (24 h) with coverage % and alert count | Report §Reliability |
| Security checklist output (`02-design/06` §9) | Report §Security |
| Chaos drill observations | Report §Reliability |
| Sensor accuracy comparison table | Report §Hardware, appendix |
| Screenshots: red alert state, wallboard, release-mode app | Report figures |
