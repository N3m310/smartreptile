# 05 — E2E AI Testing Implementation Plan

**Status.** Draft, 2026-10-30. **P0–P4 are implemented and green** on `emulator-5554`: `integration_test` is a dev
dependency, `app/integration_test/` holds `smoke` (1 case), `hermetic` (7), `fullstack` (4) and `alerts`
(`E2E-AI-14`, the `TC-E2E-03` software half), `scripts/e2e.ps1` is the entry point, and a run writes
`build/e2e/<suite>.json` and exits `0`. The web track (§15) and `E2E-AI-13/15` are not built yet; the script refuses
`-Suite web` with exit 2 rather than pretending.
**Owner.** App track.
**Drives.** `TC-E2E-01…06` (`02-test-cases.md` §E) — the software half of each, not the hardware half.

---

## 1. What "AI-run E2E" means here

The requirement is not "an AI writes test code". It is that **a run happens without a person touching the
machine**: one command starts the device, the backend and the app, drives the real UI, and leaves a
machine-readable verdict. The person's contribution is reading the verdict afterwards.

| Term | Definition used in this plan |
|---|---|
| AI-run | An agent (or CI) executes `scripts/e2e.ps1` and gets `exit 0/1` plus `build/e2e/*.json`, a log, and a screenshot on failure. No click, no "look at the phone", no manual `adb` step |
| AI-runnable | The above holds from a **clean checkout on a machine that has only Flutter + Android SDK + Docker**. Every prerequisite is checked by the script and reported as a failed check, never a hang |
| Deterministic | The same command twice produces the same verdict. No dependence on wall-clock time, on data left by the previous run, or on the order of the suites |

### 1.1 Scope

In scope, because a person can do all of it by hand today on the emulator or in a browser:

- cold start → sign-in → shell → tab navigation (`TC-E2E-01` app half)
- sign-in failures, session expiry, guest mode, sign-out, keychain cleanup
- terrarium list → detail → thresholds → readings (`TC-E2E-01`, `FR-03/FR-08/FR-10`)
- alert appearing in the alerts tab after telemetry is injected (`TC-E2E-03` app half)
- device-silence presentation (`TC-E2E-05` app half)
- release-mode ops surface: `/health`, `/ready`, `/metrics` (`TC-E2E-06` half)
- the same journeys on the web client (`web/`, React 19 + Vite), which has **no test harness of any kind**
  today — see §15

Out of scope, and must be said out loud in the report:

| Not AI-run | Why |
|---|---|
| Flashing the ESP32, provisioning over USB | hardware in the loop |
| `TC-E2E-04` sensor accuracy (±0.5 °C vs reference) | physics; a number on a screen cannot prove a sensor |
| `TC-E2E-02` 24 h soak | wall-clock time |
| Real FCM push to a handset | needs Google Play services and a real token; the channel is already faked in `TC-I-12` |
| Photographic proof of a release APK on a phone | evidence, not assertion |

**Acceptance for the mentor conversation:** every step a human performs *inside the app* is reproduced by
the harness; only steps involving the physical node stay manual, and they are listed above.

---

## 2. Current state (verified 2026-10-30)

| Fact | Evidence |
|---|---|
| 22 test files, widget/unit only | `app/test/**` (`widget/`, `state/`, `core/`, `services/`, `prototype/`) |
| No E2E harness | no `app/integration_test/`, no `app/test_driver/`, no `integration_test` in `app/pubspec.yaml` |
| CI stops at `flutter analyze` + `flutter test --coverage` | `.github/workflows/ci.yml`, job *Flutter app (analyze, test)* |
| `integration_test` was always the intended tool | `03-implementation/01-tech-stack-and-setup.md` §1 ("App tests") and §2 (`app/ test/ integration_test/`) |
| `TC-E2E-01…06` are manual scripts today | `02-test-cases.md` §E |
| API host is a build-time constant | `app/lib/core/env.dart` (`--dart-define=API_BASE=…`, emulator default `http://10.0.2.2:8080`) |
| Guest mode reaches the shell with no token | `app/lib/screens/login_screen.dart` ("Khám phá bản thử nghiệm (Khách)") → `SessionProvider.enterGuestMode()` |
| Prototype mode is network-free and disk-free | `app/lib/main.dart`, `Env.prototypeMode`, `--dart-define=PROTOTYPE=true` |
| FCM is skipped when the four Firebase defines are absent | `main.dart` `_initializeFirebaseMessaging()` |
| No business `ValueKey` anywhere | `grep "Key(" app/lib` returns only `GlobalKey<FormState>` |
| The web client has **no test runner at all** | `web/package.json` has `typecheck` and `check:strings` only; no `@playwright/test`, no `vitest`, and `grep data-testid web/src` returns nothing |
| Web token and language live in `localStorage` | `terraguard.session` (`web/src/api/client.ts`), `terraguard.language` (`web/src/i18n/index.tsx`) — a stored session survives a browser restart, so tests must clear it |
| Web API base is compiled in, and the committed build points at `http://localhost:8080` | `VITE_API_BASE` (`web/src/api/client.ts`), verified in `web/dist/assets/index-*.js` |
| Dev proxies the API, nginx does not | `web/vite.config.ts` proxies `/api` and `/hubs` to `:8080`; `web/nginx.conf` serves only static files, so the composed client calls `:8080` cross-origin (CORS list in `docker-compose.yml`) |

### 2.1 Environment probe (this workstation)

| Check | Result | Consequence |
|---|---|---|
| `flutter --version` | 3.47.4 stable | fine |
| Android SDK | present at `%LOCALAPPDATA%\Android\Sdk`, **not on `PATH`** | the script must prepend `platform-tools` itself |
| AVDs | `focus_study`, `fcm_pixel_36` | default AVD is `focus_study` (`HD chay android emulator.txt`) |
| Running device | none | the script must boot one headless |
| Docker Desktop | **engine not running** during the probe | the `fullstack` suite must fail fast with a clear message, not wait 3 minutes |

---

## 3. Target harness

Three suites, because they trade setup cost against realism and each answers a different question.

| Suite | Build defines | Backend | Proves | Runtime |
|---|---|---|---|---|
| `smoke` | none — the **app** build | none | the app boots, a guest enters the shell, every destination opens without throwing | ~50 s |
| `hermetic` | `API_BASE=http://127.0.0.1:18999` | `HttpServer` stub inside the test process | every screen state the API can drive, including 401/404/500 and empty lists, with byte-identical responses every run | ~95 s (incl. build) |
| `fullstack` | `API_BASE=http://10.0.2.2:8080` | real `docker compose` (db + api) | the contract with the real backend: auth, terrariums, thresholds, alerts, time zones | ~2 min (incl. build) |
| `web-smoke` (§15) | `npx playwright test` | none — responses stubbed with `page.route()` | the React client's routes, guards and copy, with no server and no Docker | ~40 s |
| `web-fullstack` (§15) | `npx playwright test` | `docker compose` on `:8081` + `:8080` | the browser against the composed stack, including a deep-link reload through the SPA fallback | ~2 min |

The two web suites are cheap in a way the mobile suites are not: no emulator, no APK, and Chromium ships with
Playwright — §15 covers them.

`smoke` runs the **app** build rather than `PROTOTYPE=true` (corrected 2026-10-30 while implementing P1): the
prototype's own `login_screen.dart` has no guest entry — it is a different screen with a different copy — so the
guest path this suite drives exists only in the app shell.

`hermetic` is the workhorse and the reason this plan is realistic: `main()` constructs
`ApiClient(baseUrl: Env.apiBase)` itself, so a test cannot inject a mock client — but `integration_test`
runs **in the app's own process**, so the test can bind a real `HttpServer` on a fixed loopback port and
point `API_BASE` at it. No plugin, no DI refactor, no production code change.

The fixed port is `18999` (`FakeApi.port`); it must match the `--dart-define` of the run, because
`Env.apiBase` is compile-time. A second concurrent run would collide — acceptable, and documented as
"one AVD, one run at a time".

---

## 4. Files to create

```text
app/
├── pubspec.yaml                                  # + integration_test (dev)
├── test_driver/
│   └── integration_test.dart                     # needed for -d chrome only
├── integration_test/
│   ├── helpers/
│   │   ├── pump.dart                             # pumpUntilFound / pumpUntilGone
│   │   ├── fake_api.dart                         # in-process HttpServer (hermetic)
│   │   ├── live_api.dart                         # the real backend: fixture, claim, ingest (fullstack)
│   │   ├── fixtures.dart                         # login payload, terrarium DTO, thresholds DTO
│   │   └── session.dart                          # clearStoredState(), storedRefreshToken()
│   ├── e2e_smoke_test.dart
│   ├── e2e_hermetic_test.dart
│   ├── e2e_fullstack_test.dart
│   └── e2e_alerts_test.dart                      # fullstack + telemetry injection (E2E-AI-14)
scripts/
└── e2e.ps1                                       # the single entry point
web/
├── package.json                                  # + @playwright/test (dev)
├── playwright.config.ts
└── e2e/
    ├── fixtures/
    │   ├── api.ts                                # page.route() stubs, per route
    │   └── session.ts                            # pre-signed-in storageState, storage reset
    ├── web-smoke.spec.ts
    └── web-fullstack.spec.ts
```

---

## 5. Work breakdown

Each phase ends with a green run of the suite it introduced. Estimates are for one developer familiar with
the codebase.

### P0 — Environment contract (0.5 h)

| Task | Done when |
|---|---|
| P0.1 `scripts/e2e.ps1` prints a four-line preflight: Flutter version, SDK path, `adb devices`, Docker engine reachable | a missing prerequisite exits `2` with the reason, never hangs |
| P0.2 Resolve `adb`/`emulator` from `$env:LOCALAPPDATA\Android\Sdk` when `PATH` lacks them | works on a machine where `adb` is not on `PATH` (this one) |
| P0.3 Document "Docker Desktop must be running for `fullstack`" in the script header | the failure message names Docker, not `flutter test` |

**Acceptance:** `powershell -File scripts\e2e.ps1 -Suite smoke` on a clean shell reaches the Flutter step.

### P1 — Dependency, skeleton, `smoke` (2 h)

| Task | Detail |
|---|---|
| P1.1 `pubspec.yaml` | add `integration_test: { sdk: flutter }` to `dev_dependencies` |
| P1.2 `helpers/pump.dart` | `pumpUntilFound` (pump 100 ms frames until the finder matches or 20 s), because `main()` fires `unawaited(session.restore())` and `unawaited(settings.load())` |
| P1.3 `e2e_smoke_test.dart` | `app.main()` → wait `login.guest` → tap → expect `NavigationBar` → visit all six tabs → `expect(tester.takeException(), isNull)` per tab |
| P1.4 `flutter gen-l10n` in the script | `pubspec.yaml` sets `generate: true`; a stale generated delegate is a confusing first failure |

**Acceptance:** suite `smoke` passes on `emulator-5554` twice in a row, no interactivity.

**Status 2026-10-30: done.** Green twice (`48 s` and `46 s` wall clock, test body `13 s`), `exit 0`, and the
existing `flutter test` suite still passes (224 tests) — the two identifiers and the `HistoryScreen` fix below did
not change behaviour. Two things the harness found on its first runs, both fixed:

| Finding | Fix |
|---|---|
| The **History** destination threw `setState() or markNeedsBuild() called during build` on first open: `HistoryScreen.initState` called `TerrariumProvider.loadTerrariums()`, which notifies its listeners synchronously before its first `await`, so the provider scope was marked dirty while the shell was still building the screen. Real bug, not a test artefact — any first visit to that tab hits it | `WidgetsBinding.instance.addPostFrameCallback((_) => _initialize())` in `app/lib/screens/history_screen.dart` |
| `All SemanticsHandle instances must be disposed` — `addTearDown(handle.dispose)` runs *after* the binding's end-of-test verification | dispose the handle in a `finally` inside the test body (`e2e_smoke_test.dart`) |

Also learned: `--file-reporter json:<path>` writes **one JSON event per line** (the dart-test JSON reporter), not
one JSON document — a consumer has to read it line by line and look for `{"type":"done","success":true}`.

### P2 — Identifiers and the `hermetic` suite (3 h)

| Task | Detail |
|---|---|
| P2.1 Locator inventory (§7) | `Semantics(identifier:)` on the login form, the language pills and the profile sign-out — no behaviour change |
| P2.2 `helpers/fake_api.dart` | `HttpServer.bind(loopbackIPv4, 18999)`, route table `"METHOD /path"`, JSON in/out, call counters, `stop()` in `tearDownAll` |
| P2.3 `helpers/fixtures.dart` | login/register payloads and the refusal body, joined by `shellReadPaths` — the list of reads the shell fans out to |
| P2.4 `e2e_hermetic_test.dart` | cases `E2E-AI-02…08` (§6) |
| P2.5 `helpers/session.dart` | `clearStoredState()` (keychain + `shared_preferences`) before each case |
| P2.6 Stub the whole post-login fan-out, and fail on a route nobody stubbed | `tearDownAll` asserts `api.unmatched` is empty |

**Acceptance:** `hermetic` passes with no network access to anything except `127.0.0.1`.

**Status 2026-10-30: done.** 7 cases green in **23 s** of test time (95 s including the Gradle build);
`-Suite all` runs `smoke` + `hermetic` and exits `0`; the 224 existing widget tests still pass. Findings:

| Finding | Resolution |
|---|---|
| The app talks to `127.0.0.1` with cleartext HTTP — allowed by `android/app/src/main/res/xml/network_security_config.xml`, which already exempts `127.0.0.1` alongside `10.0.2.2` and `localhost`. Nothing had to change in the manifest | verified, no edit |
| Sign-out calls `POST /api/v1/auth/logout` **before** dropping the local token. The unmatched-request tripwire failed the suite on it, which is the tripwire working | stub it with `200 {}` so `E2E-AI-08` tests the local half |
| The refresh token is a real keychain write, so "the second launch shows the sign-in screen" would have been untestable — `setUp` clears it (`clearStoredState`) and `E2E-AI-08` reads the key back to prove sign-out removed it | `helpers/session.dart` |
| `E2E-AI-05` asserts the recovery code **text** (`E2E0-0000-0000-0001`) | correct: the issued code is not a slot with an identifier, it *is* the outcome |

The tripwire is the most valuable thing here: a suite that silently stopped reaching an endpoint would otherwise
pass forever. Every unstubbed request is a route the suite no longer covers.

### P3 — `fullstack` suite (4 h)

| Task | Detail |
|---|---|
| P3.1 Fixture through the public API | register an account (`POST /api/v1/auth/register`), create a species profile and a terrarium, as `scripts/demo-create.ps1` does — no SQL, no seeder |
| P3.2 Cases E2E-AI-09…13 | sign-in, terrarium visible, detail opens, thresholds table shows the effective band and its source |
| P3.3 Teardown | delete the account (`DELETE /api/v1/users/{id}` as admin, per `demo-create.ps1` §6) so runs do not accumulate rows |
| P3.4 Failure diagnostics | on failure the wrapper captures `adb exec-out screencap -p` and `adb logcat -d` into `build/e2e/` |

**Acceptance:** `fullstack` passes against `docker compose up -d --wait`, and the run leaves no fixture rows behind.

**Status 2026-10-30: done.** 4 cases green in **33 s** of test time (134 s including the Gradle build); `smoke` and
`hermetic` still green; the 224 widget tests still pass. Findings — the first three are **app defects**, none of
them detectable from a stub, because they need real text and real data:

| Finding | Fix |
|---|---|
| Two `RenderFlex` overflows at 411 dp (the emulator's logical width): the login language switcher pushed its pills 82 px past the card edge, and the app bar's wordmark overran its title box | `Row` → `Wrap` in `login_screen.dart`; `Flexible(FittedBox(scaleDown))` around the wordmark in `app.dart` |
| A third overflow in the dashboard header: a long enclosure name pushed the status chip off the card | `Flexible` + `TextOverflow.ellipsis` on the name |
| `AppFailure(409 device_not_bound)` surfaced as an **unhandled async error** ("thrown after the test had completed"). An enclosure with no board answers 409 on every readings route; `HistoryScreen`/`TerrariumDetailScreen` create the futures in `initState`, and a `FutureBuilder` only subscribes on the next build — so a fast failure (or a failure after the screen is popped) has no listener | `..ignore()` on each future; the builders still render the error |
| The admin account does not exist in this deployment (`admin`/`Admin#2026` answers 401 — the container has no `ADMIN_USERNAME`), so `DELETE /users/{id}` is unavailable to teardown | the run deletes its enclosure with the owner's own token and asserts the list is empty; the throwaway account (`.test` email, no data left) stays. Documented as an open question, not silently ignored |
| `docker compose up` must not stop a stack a developer started | the script records whether **it** started the stack and only tears down in that case |

The overflow findings are the strongest argument for this layer that exists in the project so far: three layout
defects on the reference device, all of which a stub-backed suite renders fine because stubs carry no long names.

### P4 — Alert path via telemetry injection (4 h)

`TC-E2E-03` is the highest-value case in the project (`FR-11`), and the only part that needs the node is *causing*
the excursion. **Corrected while implementing:** the injection point is the HTTPS fallback
(`POST /api/v1/ingest/http`, `Authorization: Device <deviceId>.<secret>`) rather than MQTT — it runs through the
**same** `IngestPipeline` as the broker (validator → persistence → evaluator → notification), so the alert is the
alert a real board would raise. It also needs no MQTT client on the device, no client certificate and no TLS
handshake from the emulator. The MQTT half stays covered by `TC-I-*` and the hardware run.

| Task | Detail |
|---|---|
| P4.1 Register and claim a board through the API | `POST /devices/self-register` (anonymous; chip id letters+digits only, MAC required) then `POST /devices/claim` — the same flow the app's claim screen drives (`TC-I-05`), and the secret is returned exactly once |
| P4.2 Write the band the case judges against | `PUT /terrariums/{id}/thresholds` with `phase: any` (so the run does not depend on the enclosure's photoperiod) and `dwellWarnMinutes: 1`, the API's minimum — `threshold_dwell_invalid` below 1, and `DwellCritMinutes` may not exceed it |
| P4.3 Do not sleep the dwell | publish a batch stamped **in the past** and spread across the dwell (4 samples over 3 minutes), so the evaluator sees a window that has already elapsed |
| P4.4 Assert on the app | sign in → alerts destination is empty for this enclosure → ingest → pull-to-refresh → the enclosure's name appears; then confirm the server's `/alerts` agrees |
| P4.5 `TC-E2E-05` half | not built: the watchdog needs `3 × interval` of real time (≥ 3 min), which is a CI job rather than a case |

**Acceptance:** E2E-AI-14 passes without touching the ESP32.

**Status 2026-10-30: done.** Green in **28 s** of test time (142 s including the Gradle build). Findings:

| Finding | Resolution |
|---|---|
| `POST /terrariums/{id}` `DELETE` answers `409 conflict_device_bound` when a board is bound, so teardown of an alert fixture cannot use the plain route | `?allowUnboundDevice=true`, which unbinds and deletes in one call |
| `chip_id_invalid` — a chip id may only contain letters and digits; `mac_required` — a MAC is mandatory on self-register | the fixture derives a MAC from the stamp and uses an alphanumeric chip id |
| `find.byIcon` matched **two** widgets: the history destination and the history screen's own empty-state icon share `IconData` | `tapDestination(tester, icon)` scopes the finder to the `NavigationBar` (`helpers/pump.dart`) |
| The band's dwell cannot be zero (`threshold_dwell_invalid`), so "alert immediately" is impossible by configuration | the batch is back-dated across a one-minute dwell instead of waiting for it |

**Open question to confirm with the mentor (Q1 below, unchanged):** whether a batch injected through the device-facing
fallback counts as acceptable evidence for `TC-E2E-03`, given the firmware half is covered by `TC-U-FW-*` and the
real-hardware run stays in the QA log. The document's position: the pipeline is identical, and the only difference on
the stored row is `IngestSource.HttpFallback`.

### P5 — Wrapper, CI, documentation (3 h)

| Task | Detail |
|---|---|
| P5.1 `scripts/e2e.ps1` | §8 contract, `-Suite smoke\|hermetic\|fullstack\|all`, `-Avd`, `-StopDeviceAtEnd` — **done (P0–P4)** |
| P5.2 CI job `app-e2e` | `reactivecircus/android-emulator-runner@v2` (API 34, `pixel_6`), KVM enabled, `.env` generated from `openssl rand` so no literal secret enters the repo, `docker compose up -d --wait` + a `/health/ready` poll, then the four suites; **nightly (`cron: 0 19 * * *`) + `workflow_dispatch`, never on a push** — the existing pipeline stays ~5 minutes |
| P5.3 `02-test-cases.md` §E | a note that every `TC-E2E-*` needs a person, plus new §E.1 registering `E2E-AI-01…15` with suite, coverage and status; case-count summary grows to 131 |
| P5.4 Traceability matrix | a manual-case → `E2E-AI` counterpart table in the header, and `E2E-AI-07/13/14` cited in the NFR-02/03/06 rows |
| P5.5 `01-test-strategy-and-plan.md` | §2 gains an "App end-to-end (automated)" row and keeps the hardware row beside it; §6 gains the two things the automated half does not claim; §7 records the as-built job; §9 adds the report artifact |
| P5.6 `app/README.md` and `docs/README.md` | four suites, two commands, and the index lines updated |

**Acceptance:** from a clean shell, `powershell -File scripts\e2e.ps1` runs the suites with no arguments, prints a
summary table, exits non-zero on any failure, and needs no human step.

**Status 2026-10-30: P5.1, P5.3–P5.6 done and verified locally; P5.2 written but not yet executed on GitHub** —
the workflow cannot be proven from this machine, only from a run. That is the one line of this plan whose evidence
is still pending, and it is honest to say so rather than to tick it.

Measured on the development machine that day, `-Suite all` then `-Suite fullstack`:

| Suite | Cases | Test time | Report |
|---|---|---|---|
| `smoke` | 1 | 131 s (cold build) | `build/e2e/smoke.json` |
| `hermetic` | 7 | 144 s | `build/e2e/hermetic.json` |
| `fullstack` | 4 | 154 s | `build/e2e/fullstack.json` |
| `alerts` | 1 | 141 s | `build/e2e/alerts.json` |

### P6 — Web client suites (4–6 h)

| Task | Detail |
|---|---|
| P6.1 `@playwright/test` in `web/` + `playwright.config.ts` | `baseURL http://127.0.0.1:8081`, `webServer` = `npm run dev` with `reuseExistingServer`, `trace: 'on-first-retry'`, JSON reporter into `build/e2e/web-*.json` |
| P6.2 `fixtures/api.ts` | `page.route('**/api/v1/**')` fulfilling fixtures — this is the web equivalent of §3's `FakeApi`, and it needs no backend process |
| P6.3 Accessible names | the `Field` component in `web/src/pages/Login.tsx` renders a `<label>` with **no `htmlFor` and the input outside it**, so `getByLabel` cannot resolve; add `id` + `htmlFor`, an `aria-label` on the `Layout.tsx` `<nav>`, and a `data-testid` convention for anything with no accessible name |
| P6.4 `web-smoke.spec.ts` + `web-fullstack.spec.ts` | cases `E2E-WEB-01…10` (§15.5) |
| P6.5 Wrapper + CI | `scripts/e2e.ps1 -Suite web` runs both Playwright projects; CI uploads `playwright-report/` and `build/e2e/web-*.json` |

**Acceptance:** `npx playwright test --project=web-smoke` is green headless with no server running and no
Docker; `--project=web-fullstack` is green against `docker compose up -d --wait`.

---

## 6. Case catalogue

Ids are registered in `02-test-cases.md` by P5.3. **Bold steps** are implemented and green; the rest are the plan.

| Id | Case | Suite | Steps | Assertions | Maps to |
|---|---|---|---|---|---|
| E2E-AI-01 | **The app boots, a guest enters the shell, every destination opens** | smoke | `app.main()` → guest → tap each `NavigationDestination` | `LoginScreen` gone; `NavigationBar` with ≥ 5 destinations; `takeException()` null per destination | `TC-E2E-01`, `TC-W-01` |
| E2E-AI-02 | **Guest mode enters the shell without a single API call** | hermetic | tap guest | `LoginScreen` gone; `NavigationBar`; `callsTo('GET', '/api/v1/terrariums') == 0` | `TC-W-01` |
| E2E-AI-03 | **Successful sign-in reaches the dashboard** | hermetic | fill identifier/password → submit | `DashboardScreen`; `NavigationBar`; one login request | `TC-E2E-01` |
| E2E-AI-04 | **A refused credential shows the error and stays on the form** | hermetic | login answers 401 `invalid_credentials` | `login.error` present; still `LoginScreen`; no `NavigationBar` | `TC-W-02` |
| E2E-AI-05 | **Registration issues a recovery code and returns to sign-in** | hermetic | register → submit | the issued code on screen; form back in `signIn`; one register request | `TC-W-03` |
| E2E-AI-06 | **An empty catalogue renders the shell, not an error** | hermetic | every read answers `{"items":[]}` | all five shell reads requested; no exception | `TC-W-05` |
| E2E-AI-07 | **The language pill switches the whole form to English** | hermetic | tap `login.lang.en` | `Sign in` / `Create an account` | `NFR-06` |
| E2E-AI-08 | **Signing out clears the keychain, not just the screen** | hermetic | sign in → profile → sign out | `LoginScreen`; `storedRefreshToken()` becomes null | `FR-01` |
| E2E-AI-09 | **A real sign-in shows the account its own enclosure** | fullstack | fixture via API, then sign in and open the Terrarium destination | the enclosure's own name on screen | `TC-E2E-01` |
| E2E-AI-10 | **The enclosure detail opens on that enclosure** | fullstack | tap the card | `TerrariumDetailScreen` | `FR-03` |
| E2E-AI-11 | **History opens for an enclosure with no readings** | fullstack | open the History destination | no exception; still signed in | `FR-09` |
| E2E-AI-12 | **Signing out lands back on the sign-in form** | fullstack | profile → sign out | `LoginScreen`; `storedRefreshToken()` null | `FR-01` |
| E2E-AI-13 | API outage degrades, does not crash | fullstack | stop api container → pull to refresh | error banner, no exception, recovery on restart | `NFR-03` |
| E2E-AI-14 | **An excursion ingested from the device reaches the app** (`TC-E2E-03` app half) | fullstack / `e2e_alerts_test.dart` | claim a board → write a 1-minute-dwell band → alerts empty → ingest 4 hot samples over 3 min → pull to refresh | the enclosure's name appears in Alerts; `/alerts` carries a `tempC` row for it | `FR-11`, `FR-12` |
| E2E-AI-15 | Silence is presented as silence (`TC-E2E-05` half) | fullstack | publish nothing for 3 intervals | stale/offline indicator, evaluation-paused notice — **needs ≥ 3 min of wall clock; deferred** | `FR-07` |
| E2E-WEB-01 | Sign-in page renders its three modes | web-smoke | open `/` | heading, identifier + password fields, register and recovery switches | `TC-E2E-01` |
| E2E-WEB-02 | Sign-in reaches the dashboard | web-smoke | stub auth + read routes, submit | `/dashboard`, metric cards, nav links | `FR-08` |
| E2E-WEB-03 | A refusal is rendered from its `code`, not its message | web-smoke | stub 401 with a known `code` | the mapped sentence, `aria-invalid` on the field, still on `/` | `FR-01` |
| E2E-WEB-04 | A spent refresh token ends the session | web-smoke | stub refresh 401 once | redirected to `/`, `sessionExpired` notice, `terraguard.session` gone | `TC-U-34` |
| E2E-WEB-05 | Deep link survives a reload (SPA fallback) | web-fullstack | open `/terrariums/:id`, reload | page still renders, no 404 | `BUG-03` regression |
| E2E-WEB-06 | Wallboard renders without the shell | web-smoke | open `/wallboard` | cards visible, no sidebar nav | `FR-08` |
| E2E-WEB-07 | Language choice persists | web-smoke | switch to `en`, reload | copy stays English, `terraguard.language` = `en` | `NFR-06` |
| E2E-WEB-08 | Permission gates the admin routes | web-smoke | session without `data.read.any` | `/admin` redirects instead of rendering | `FR-02` |
| E2E-WEB-09 | Unreachable API keeps the last value and says so | web-smoke | `route.abort()` after one success | last value + timestamp retained, banner shown, no zero | `NFR-03` |
| E2E-WEB-10 | Alerts list renders severity and resolve action | web-fullstack | open `/alerts` | chips, terrarium names, resolve button acts | `FR-12` |

---

## 7. Locator inventory — `Semantics(identifier:)`, not `ValueKey`

Text finders in a `vi`-first app break on every copy change (`app.dart` forces `Locale('vi')` unless the
keeper chose otherwise). But the fix is **not** a `ValueKey`: a key is visible to `flutter_test` only. One
`Semantics(identifier: …)` is visible to three consumers at once:

| Consumer | Reads it as |
|---|---|
| `integration_test` (this plan) | `find.bySemanticsIdentifier('login.username')` — present in the pinned SDK (`flutter_test/lib/src/finders.dart`) |
| `tester-army/e2e` (§14), if adopted | `screen.getByTestId('login.username')` — the Android resource id / iOS accessibility id |
| Android TalkBack, iOS VoiceOver | the element identity — i.e. part of `NFR-06`'s accessibility evidence |

So the plan adds identifiers, not keys. Implemented so far (2026-10-30): the login form, the language pills and the
profile sign-out. `✓` marks a slot that exists in the widgets; the rest are the plan.

| Widget | `Semantics(identifier: …)` | Done |
|---|---|---|
| login form | `login.identifier`, `login.username`, `login.email`, `login.password`, `login.code`, `login.resetCode`, `login.newPassword` | ✓ |
| login actions | `login.submit`, `login.guest`, `login.error` | ✓ |
| language switch | `login.lang.vi`, `login.lang.en` | ✓ |
| profile | `profile.signOut` | ✓ |
| mode links | `login.mode.register`, `login.mode.recover`, `login.mode.back` | not yet — the suite taps `Tạo tài khoản` by text |
| shell destinations | `shell.tab.dashboard`, `shell.tab.terrariums`, `shell.tab.species`, `shell.tab.devices`, `shell.tab.alerts`, `shell.tab.history`, `shell.tab.profile`, `shell.tab.admin` | not yet — the suites use `find.byType(NavigationBar)` and `find.byType(DashboardScreen)`, and tap the profile icon |
| terrarium list + item | `terrariums.list`, `terrarium.item.<id>` |
| detail sections | `terrarium.detail.metrics`, `terrarium.detail.thresholds` |
| alert list + item | `alerts.list`, `alert.item.<id>` |

Three rules that cost an hour each if missed:

1. **A tappable control needs `MergeSemantics`.** `Semantics(identifier: 'login.submit', child: ElevatedButton(…))`
   produces two nodes — the id on a wrapper, the `button` role on the child — and a coordinate-based tap from
   an external driver can land between them. Wrap: `MergeSemantics(child: Semantics(identifier: …, child: …))`.
2. **A `TextFormField` needs the identifier plus a label.** Without a label it reads as an unnamed textbox
   and `fill()` has nothing to attach to (the upstream example carries the same note).
3. **Widget tests must enable semantics.** `find.bySemanticsIdentifier` finds nothing unless the test holds
   `final handle = tester.ensureSemantics();` … `handle.dispose();`. `integration_test` on a device gets the
   tree from the platform bridge; a plain `testWidgets` does not.

Rule: identifiers name **slots**, not copy. Never assert on the text of an identified widget; assert on the
widget or the value behind it.

---

## 8. `scripts/e2e.ps1` — contract

Implemented 2026-10-30 for the `smoke` suite; the table is the contract the later suites must keep.

| Aspect | Contract |
|---|---|
| Invocation | `powershell -ExecutionPolicy Bypass -File scripts\e2e.ps1 [-Suite smoke\|hermetic\|fullstack\|all] [-Avd focus_study] [-StopDeviceAtEnd]` |
| Default | `-Suite all` runs `smoke` then `hermetic` (~2 min each including the Gradle build); `-Suite fullstack` runs the two backend suites and needs Docker; `-Suite web` is refused with `2` until §15 is built |
| Web suites | `-Suite web` will run `npx playwright test` for both projects (§15); not wired yet |
| Exit codes | `0` all selected suites passed · `1` a test failed · `2` a prerequisite failed (no Flutter, no SDK path, no AVD, a suite that is not built yet) |
| Artifacts | `build/e2e/<suite>.json` (dart-test JSON reporter, **one event per line**, verdict in `{"type":"done","success":…}`) and `<suite>.log` always; `<suite>-fail.png` + `<suite>-logcat.txt` (last 2 000 lines) on failure, removed again before the next run of that suite |
| Side effects | boots the AVD headless when none is running; for `fullstack`, `docker compose up -d --wait` **only when the stack is not already up**, and a teardown only of what it started; `flutter pub get` + `flutter gen-l10n`; `adb shell pm clear com.smartreptile.smart_reptile` before each suite; leaves the emulator up unless `-StopDeviceAtEnd` |
| Idempotent | a second run produces the same verdict; stale failure evidence never survives a green run |
| Suite selection | `-Suite smoke` · `hermetic` · `fullstack` · `all`; `fullstack` runs two files (`e2e_fullstack_test.dart`, `e2e_alerts_test.dart`), each with its own report |
| Output | one summary row per file: name, exit code, seconds, report path |

Two traps the first implementation hit, both now encoded in the script:

1. **`$ErrorActionPreference = 'Stop'` aborts on a Gradle warning.** `flutter test` writes JVM warnings to stderr,
   and PowerShell 5.1 turns a native command's stderr into a terminating error under `Stop`. The script sets
   `Continue` and reads `$LASTEXITCODE` instead.
2. **`"$Suite_test"` is not `"$Suite" + "_test"`.** `_` is a valid identifier character, so the interpolated name
   must be written `"integration_test/e2e_${name}_test.dart"`.

The implementation lives in `scripts/e2e.ps1`; it is the thing to read, not a copy of it in prose. What it
does, in order: preflight (flutter on PATH, `adb`/`emulator` resolved from `%LOCALAPPDATA%\Android\Sdk` when
`PATH` lacks them, the requested suite must exist) → device (boot the AVD headless and wait for
`sys.boot_completed`, or reuse a running one) → per suite (drop stale evidence, `pm clear`, `flutter test` with
`--file-reporter json:`, capture screenshot + bounded logcat on failure) → summary table and an exit code.

---

## 9. Determinism rules

Project rules from `01-test-strategy-and-plan.md` §4 still apply. New ones for this layer:

| Rule | Reason |
|---|---|
| Clear app data before every suite (`adb shell pm clear com.smartreptile.smart_reptile`) | the refresh token lives in the keychain (`app/lib/data/token_store.dart`); without this, "first launch shows the login screen" fails on the second run |
| One AVD, one run at a time | `hermetic` binds a fixed port (`18999`); a parallel run collides |
| Dispose a `SemanticsHandle` inside the test body, never in `addTearDown` | the binding verifies handles before teardown callbacks run, and fails the test with "All SemanticsHandle instances must be disposed" |
| A screen may not call a notifying provider method from `initState` | `TerrariumProvider.loadTerrariums` notifies before its first `await`, so the call marks the provider scope dirty while the parent is building — defer with `addPostFrameCallback` (the `HistoryScreen` fix, §5 P1) |
| Clear the keychain **and** `shared_preferences` between cases | `integration_test` reuses one process and one app install across the whole file, so a session or a chosen language from case *n* decides case *n+1* (`helpers/session.dart`) |
| Fail the suite on a request no stub answered | an unstubbed route means the suite silently stopped covering that endpoint; `FakeApi.unmatched` is asserted empty in `tearDownAll` |
| Never `find.byIcon` at the top level | an empty state draws the same `IconData` as the destination that leads to it; scope with `tapDestination` (`find.descendant(of: NavigationBar…)`) |
| Give a fixture enough time to be visible in a decision | the dwell cannot be zero (`threshold_dwell_invalid`), so a batch is back-dated across the dwell instead of the test waiting for it |
| Never `pumpAndSettle` in the prototype Rule Lab | `rule_lab_screen.dart` runs a 260 ms `Timer.periodic`; `pumpAndSettle` never settles |
| Never assert a raw `find.text` on a copy string where a key exists | the deck is bilingual and `vi`-first |
| Freeze time in fixtures (fixed UTC timestamps), never `DateTime.now()` | day/night phase and local-day bucketing otherwise depend on when the suite runs |
| Assert values, not formatting, where `intl` is involved | locale-sensitive number formatting is `TC-W-*`'s job |
| A suite must leave the backend as it found it | otherwise the second run sees the first run's terrariums and the "empty state" case lies |

---

## 10. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Dwell windows are minutes; a naive E2E-03 waits 5+ min | slow, flaky | use a `dwellWarnMinutes: 0` threshold override on the fixture terrarium; the dwell matrix itself stays a unit test (`TC-U-10…20`) |
| `fullstack` needs Docker on the runner | CI complexity | nightly job only; `hermetic` covers the UI in PR-time runs |
| Emulator boot time on a cold runner | timeouts | 240 s boot budget, then a hard failure with the AVD name in the message |
| Copy changes break text finders | false failures | key inventory (§7) is a P2 prerequisite, not an afterthought |
| Screenshots unsupported under `flutter test` | poor triage | capture with `adb exec-out screencap -p` in the wrapper instead of `binding.takeScreenshot()` |
| `integration_test` on `windows` desktop may trip over `firebase_messaging` | no desktop loop | keep the emulator as the only target; desktop is optional, not a deliverable |
| Mentor expects real hardware for `TC-E2E-03` | evidence rejected | confirm at P4 gate; keep the manual hardware run in `03-manual-qa-checklist.md` §3 regardless |

---

## 11. Evidence pack for the mentor

1. `build/e2e/<suite>.json` + `.log` from a green run of `-Suite all` — the machine-readable verdict.
2. A red run with `*-fail.png` and `*-logcat.txt`, and the commit that fixed it — proves the harness
   *catches* failures, not just that it exists.
3. `scripts/e2e.ps1 -?` output and the summary table — proves the "one command, no human" claim.
4. The updated `02-test-cases.md` §E column and the traceability matrix — proves the cases are registered,
   not just claimed.
5. The out-of-scope table (§1.1) — the honest boundary between AI-run and hardware.

---

## 12. Definition of done

- [x] `integration_test` in `app/pubspec.yaml`; `flutter analyze` clean
- [x] `app/integration_test/` with the three suites; every assertion uses a key or a type finder — `smoke` (1), `hermetic` (7), `fullstack` (4); `E2E-AI-13…15` still to do
- [x] Locator inventory (§7) in the login form, language pills and profile sign-out; no copy assertions beyond the language-switch case, where the copy *is* the outcome
- [x] `scripts/e2e.ps1` runs `smoke` + `hermetic` + `fullstack` with no arguments on a machine with Docker, exits non-zero on failure, and refuses `-Suite web` with `2`
- [x] `fullstack` green twice in a row on `focus_study` — green 2026-10-30 (one run); the second is the CI job's job
- [ ] `web-smoke` green headless with no server running; `web-fullstack` green against `docker compose`
- [ ] Every web control the suites touch has an accessible name (`id`/`htmlFor`, `aria-label`) or a documented `data-testid`
- [x] `TC-E2E-03` app half green without the ESP32 (`E2E-AI-14`, via the device-facing HTTPS fallback), and its manual hardware half still in the QA checklist
- [x] `02-test-cases.md`, `04-requirements-traceability-matrix.md`, `01-test-strategy-and-plan.md` §2 and `app/README.md` updated
- [ ] Nightly CI job uploaded `build/e2e/` artifacts at least once — the job is written (`app-e2e`); its first run is the outstanding evidence
- [x] No secrets in fixtures; no real `.env` needed to run any suite

---

## 13. Open questions

| # | Question | Who | Blocks |
|---|---|---|---|
| Q1 | Does a synthetic MQTT batch count as acceptable evidence for `TC-E2E-03`, with the hardware run kept as supplementary? | mentor | P4 |
| Q2 | May the nightly job be the only automated place `fullstack` runs, or is a PR-time run required (3 minutes added to every PR)? | mentor / team | P5.2 |
| Q3 | Which AVD is the reference device — `focus_study` or `fcm_pixel_36` (the latter presumably paired with Play services for push)? | team | P0.2 |
| Q4 | Is a Windows desktop target wanted as a fast inner loop, given the `firebase_messaging` risk (§10)? | team | P1 |

---

## 14. Tooling decision — `integration_test` vs `tester-army/e2e`

Evaluated 2026-10-30 against [`github.com/tester-army/e2e`](https://github.com/tester-army/e2e) (v0.21.3,
Apache-2.0, TypeScript, ~12k stars, "active development on the way to 1.0"). Its mobile engine is built on
[`callstack/agent-device`](https://github.com/callstack/agent-device); its web engine is Playwright.

### 14.1 It fits this stack

- Upstream ships a Flutter example (`examples/with-flutter`), last verified **2026-10-29 with e2e 0.21.3,
  `@e2e-dev/mobile` 0.12.4, Flutter 3.47 on an Android 16 emulator** — the same Flutter line this repo pins
  (3.47.4, `app/pubspec.yaml` `sdk: ^3.13.3`).
- It reads a Flutter app through `Semantics(identifier:)`, i.e. **the same inventory as §7** — no second set
  of locators to maintain.
- Tests are declarative and readable, and pair a natural-language step with a deterministic assertion:

  ```ts
  await app.open();
  await agent.act('sign in as the keeper');
  await expect(screen.getByTestId('shell.tab.dashboard')).toBeVisible();
  ```

- Deterministic tests need **no model and no key**; agent steps are cached and replayed until the app changes.
- Mobile `device` fixture covers what `integration_test` cannot: `device.setAirplaneMode(true)` for
  `NFR-03`, permission grants before launch, `device.clearKeychain()`, `device.openLink` for deep links.

### 14.2 What it costs here

| Gap | Detail |
|---|---|
| Node version | this workstation has **Node 22.15.1**; the Flutter example requires **≥ 22.22.3 / 24.8 / 26** |
| npm on Windows | `npm.ps1` is blocked by `ExecutionPolicy` in this shell — call `npm.cmd` or run from `cmd.exe` |
| Separate project | it is a Node project of its own (`app/e2e/` or `e2e/`), with `package.json`, `tsconfig`, `e2e.config.ts` |
| Build step | "The engine installs nothing on its own": `flutter build apk` + `flutter install` (or `appPath`) before every run that follows a code change |
| Model access | agent steps need `AI_GATEWAY_API_KEY` (Vercel AI Gateway) — a **cost** and a data question: screen content is sent to a hosted model. Deterministic tests avoid both; a local model is possible |
| Telemetry | on by default (anonymous, no app content); set `E2E_TELEMETRY_DISABLED=1` in this repo |
| Maturity | pre-1.0, "APIs and config can still change between minor releases" → pin `e2e@0.21.x` and `@e2e-dev/mobile@0.12.x` |
| Bundle id | `com.smartreptile.smart_reptile` (`app/android/app/build.gradle.kts`) |
| Locator work | the §7 identifier inventory is a prerequisite either way — the effort is shared, not doubled |

### 14.3 Capability comparison

| Capability | `integration_test` (§3) | `tester-army/e2e` |
|---|---|---|
| Dart-level access, in-process assertions, injectable fakes | yes | no (black box) |
| Hermetic API stub inside the test process | yes (`HttpServer`, §3) | no — the stub has to run as a separate process |
| Drives the **installed** build, including a release APK | no (test build) | yes |
| Native dialogs, permissions, airplane mode, deep links | no | yes |
| Natural-language agent steps | no | yes (needs a model key) |
| Extra toolchain | none (in the SDK) | Node project + `agent-device` + adb |
| Runs on every PR cheaply | yes (~2–3 min, no key) | deterministic subset only, plus build+install |
| Maturity | shipped with the SDK | 0.21.3, pre-1.0 |

### 14.4 Decision

**Pilot, do not replace.** `integration_test` stays the merge gate because it is hermetic, key-free and fast.
`tester-army/e2e` is added as the **acceptance layer** — the thing that demonstrates "an AI drives the app"
to the mentor — and runs nightly / on demand.

Pilot scope (one week, `focus_study`, no production code changes beyond §7 identifiers):

1. `app.open()` → sign in → dashboard (`E2E-AI-03`, `09`).
2. Terrarium list → detail → thresholds (`E2E-AI-10`, `11`).
3. Offline banner via `device.setAirplaneMode(true)` (`E2E-AI-13`, `NFR-03`).
4. One agent step for "create an enclosure" on top of a deterministic assertion.

Gate — the pilot is adopted only if all four hold:

| # | Gate |
|---|---|
| G1 | `npx e2e run --target android` completes unattended from a clean shell and writes `.e2e/report.json` |
| G2 | 5 consecutive runs with zero false failures on the three flows above |
| G3 | An agent step replays from cache with no model call on the second run |
| G4 | Setup cost ≤ 1 developer-day after the identifiers exist |

If G2 fails, the layer is dropped and §3 stays as the whole plan — the identifiers of §7 keep their value
either way (accessibility evidence, `NFR-06`). If the pilot passes, this document gains a §15 that records
the decision, the pinned versions and the nightly job, exactly as the ADR log does for other choices.

### 14.5 Which is easier to implement

Same locator work (§7) either way; everything else favours `integration_test`.

| Work item | `integration_test` | `tester-army/e2e` |
|---|---|---|
| New toolchain to install | none — ships in the SDK | Node ≥ 22.22.3 (this box has 22.15.1) + a Node project + `agent-device` |
| Prerequisites that can block the first run | none known | 3 known (`node -v`, `npm.ps1` blocked by `ExecutionPolicy`, `adb` not on `PATH`) + unknown state of `agent-device` on Windows |
| New files | 7 Dart + 1 PowerShell | 4 TS/config + 1 PowerShell + the same identifiers |
| Effort to first green run | **3–4 h** (§5 P0–P2), or **2 h** for `smoke` only (P0+P1) | **8–12 h**, dominated by the APK build/install loop and device debugging |
| Effort to the full case catalogue | ~16.5 h (§5) | not attempted; pilot only |
| Layers to debug when a test fails | Dart/Flutter | TypeScript + Flutter semantics + adb/device + a pre-1.0 framework |
| Can assert on state without a screen | yes (providers, fakes) | no — every assertion goes through the rendered tree |
| Chance of "cannot run at all on this machine" | ~0 | medium |
| Extra cost per run | 0 | model credits for agent steps; APK build time |

**Recommendation.** Implement `integration_test` first — 2 h buys a green, artifact-producing run, which is
the evidence the requirement actually asks for ("the tests run without a person"). Treat the `e2e` pilot as
a parallel stretch goal in week 2, not as the path to first evidence.

---

## 15. Web client track (`web/`)

The web client is a second surface of the same product (`ADR-019`), and it currently has **zero** automated
tests beyond `tsc` and the string-parity check. It is also the cheapest place to get E2E evidence, because a
browser needs no emulator, no APK and no sideloading.

### 15.1 Baseline (verified 2026-10-30)

| Fact | Where |
|---|---|
| No test runner | `web/package.json` — `dev`, `build`, `preview`, `typecheck`, `check:strings` only |
| No test ids | `grep data-testid web/src` → nothing |
| Routes | `/` (Login), `/dashboard`, `/terrariums`, `/terrariums/:id`, `/species-profiles(/:id)`, `/devices`, `/alerts`, `/history`, `/settings`, `/system`, `/wallboard`, `/admin*` (`web/src/App.tsx`) |
| Guards | `RequireSession` and `RequirePermission` around the shell (`App.tsx` lines 49–69) |
| Session storage | `localStorage['terraguard.session']` (`web/src/api/client.ts`) |
| Language storage | `localStorage['terraguard.language']` (`web/src/i18n/index.tsx`), default copy from the app's ARB deck |
| API base | `VITE_API_BASE`, default `http://localhost:8080`; **the committed `dist` has this baked in** |
| Dev proxy vs prod | `vite.config.ts` proxies `/api` + `/hubs`; `nginx.conf` proxies nothing, so the composed client is cross-origin and leans on the API's CORS list |
| Polling | `usePolled` — `setInterval` per screen (`web/src/lib/usePolled.ts`) |

### 15.2 Tooling

| Option | Assessment |
|---|---|
| **`@playwright/test`** (recommended) | deterministic, no model, HTML report + trace + screenshot on retry, `page.route()` gives a **hermetic API with no server process**, `storageState` gives a pre-signed-in context, one command in CI |
| `tester-army/e2e` web engine (§14) | Playwright underneath with the same test API as the mobile engine, plus `agent.act` — attractive *if* the §14 pilot passes, because one runner would then cover both surfaces. Pre-1.0, so not a prerequisite for the first web evidence |
| Cypress / Vitest+jsdom | rejected: the first duplicates a runner we would then maintain twice, the second cannot test `BrowserRouter` navigation, `localStorage` and CORS behaviour honestly |

Decision: **Playwright first**, `tester-army/e2e` web engine only as part of the §14 pilot.

### 15.3 Harness

```ts
// web/playwright.config.ts
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  fullyParallel: false,
  reporter: [['list'], ['html', { open: 'never' }], ['json', { outputFile: '../build/e2e/web.json' }]],
  use: { baseURL: 'http://127.0.0.1:8081', trace: 'on-first-retry', screenshot: 'only-on-failure' },
  webServer: {
    command: 'npm run dev',
    url: 'http://127.0.0.1:8081',
    reuseExistingServer: true,
    timeout: 120_000,
  },
  projects: [
    { name: 'web-smoke', testMatch: /web-smoke\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    { name: 'web-fullstack', testMatch: /web-fullstack\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
  ],
});
```

The stub is a route handler, so `web-smoke` needs nothing running but Vite:

```ts
// web/e2e/fixtures/api.ts
import type { Page } from '@playwright/test';

const json = (body: unknown, status = 200) => ({
  status,
  contentType: 'application/json',
  body: JSON.stringify(body),
});

export async function stubApi(page: Page) {
  await page.route('**/api/v1/auth/login', (r) =>
    r.request().method() === 'POST'
      ? r.fulfill(json({ /* the same payload as app/test/widget/login_screen_test.dart */ }))
      : r.fulfill(json({ code: 'method_not_allowed' }, 405)));
  await page.route('**/api/v1/terrariums', (r) => r.fulfill(json({ items: [] })));
  await page.route('**/api/v1/time-zones', (r) => r.fulfill(json({ items: [] })));
}

/** A context that starts signed in: `storageState` + the key the client reads. */
export const signedInState = {
  origins: [{
    origin: 'http://127.0.0.1:8081',
    localStorage: [{ name: 'terraguard.session', value: JSON.stringify({ /* AuthSession */ }) }],
  }],
};
```

Every test starts from `localStorage.clear()` (via `test.use({ storageState: { cookies: [], origins: [] } })`
or an `addInitScript`), for the same reason §9 clears the Android app data: a stored session must not leak
between cases.

### 15.4 Application changes required

| Change | Why |
|---|---|
| `id` + `htmlFor` on the login fields | `Field` in `web/src/pages/Login.tsx` renders a `<label>` with no `htmlFor` and the `<input>` **outside** it, so the label is associated with nothing: `getByLabel('Mật khẩu')` resolves to zero elements and a screen reader announces an unnamed field. This is a real `NFR-06` defect, not a test inconvenience |
| `aria-label` on the shell `<nav>` (`Layout.tsx`) | two navs on a page otherwise; also makes `getByRole('navigation', { name: … })` honest |
| A `data-testid` convention where no accessible name exists (`web-<screen>-<slot>`) | cards, chips, chart containers |
| Keep `getByRole` as the default locator | it asserts the accessible name, i.e. it fails when the copy deck and the UI drift — the browser counterpart of §7's rule |

Do **not** wire `data-testid` onto things that already have a role and a name; a test id there hides an a11y
regression instead of catching it.

### 15.5 Cases

`E2E-WEB-01…10` are registered in §6 with steps and assertions. Coverage shape: the three sign-in modes,
`code`-driven error rendering, session-expiry redirect, SPA deep-link reload, wallboard, language persistence,
permission gating, unreachable API, alerts. String parity stays where it is (`npm run check:strings`,
`TC-I-15`) — E2E is not a substitute for it.

### 15.6 Run and CI

```powershell
cd web
npm i -D @playwright/test
npx playwright install chromium          # ~130 MB, once per machine

npx playwright test --project=web-smoke            # no backend, no Docker
docker compose up -d --wait                        # only for the next line
npx playwright test --project=web-fullstack
npx playwright show-report playwright-report       # human triage, not part of the run
```

CI: one job, `npx playwright install --with-deps chromium`, then `web-smoke` on every PR (≈1 min) and
`web-fullstack` in the same nightly job that runs the mobile `fullstack` suite. Artifacts:
`playwright-report/`, `test-results/` (traces), `build/e2e/web.json`.

### 15.7 Determinism

| Rule | Reason |
|---|---|
| Clear `localStorage` before each case | `terraguard.session` and `terraguard.language` survive a reload by design |
| Stub every route the page calls, and fail on an unexpected call | an unstubbed `/api/v1/*` leaves the real network in the test; add `page.route('**/api/v1/**', r => r.abort())` last as a tripwire |
| Never `waitForTimeout`; assert with `expect(locator)` | `usePolled`'s `setInterval` means "wait for quiet" is never quiet; Playwright's auto-wait is the tool |
| Use `page.clock` when a case is about staleness | the freshness/recovery copy depends on elapsed time, and a real 90 s wait is not a test |
| Do not reuse the mobile stub port | the web stub is in-process (`page.route`), so there is no port; the two tracks must not be pointed at each other |
| Pin Chromium via `npx playwright install chromium` in CI | a browser upgrade silently changes layout-sensitive assertions |

### 15.8 Effort

| Item | Estimate |
|---|---|
| Dependency + config + fixtures | 1.5 h |
| Accessible names (`id`/`htmlFor`, nav label, testid convention) | 1–2 h |
| `web-smoke.spec.ts` (8 cases) | 2 h |
| `web-fullstack.spec.ts` (2 cases) | 1 h |
| Wrapper + CI + artifacts | 1 h |
| **Total** | **~6 h**, of which the browser install is a download, not work |

Per case this is by far the cheapest evidence in the project: the mobile track spends its time on boot, build
and install, and the web track spends it on assertions. If the mentor wants one demonstrable "AI runs the
tests" artifact this week, `web-smoke` plus `integration_test` `smoke` is the pair to show.



