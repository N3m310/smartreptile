# Mobile end-to-end tests

These tests drive the Flutter app on an Android emulator and use the local .NET API. The 13 end-to-end scenarios
cover invalid login, language selection, registration and session handling, primary navigation, terrarium validation
and CRUD, history controls, alert filters, species search and details, and password validation. They are separate
from the unit and widget tests under `test/`.

## Run

1. Start the local API using the project's .NET workflow. From the repository root:

   ```powershell
   dotnet run --project backend/src/SmartReptile.Api
   ```

   Ensure the API's configured database and other local dependencies are available. In a separate terminal, wait
   for readiness:

   ```powershell
   curl.exe http://localhost:8080/health/ready
   ```

2. Start an Android emulator, then from `app` run:

   ```powershell
   flutter test integration_test --device-id emulator-5554 --reporter expanded
   ```

   The default API URL is `http://10.0.2.2:8080`, which reaches the host machine from the Android emulator. For a
   different API address, pass `--dart-define=API_BASE=http://<host>:<port>`.

The suite creates a unique Owner fixture through the API and reuses it across scenarios; the registration scenario
also verifies registration through the app UI. Terrarium operations are performed through the UI. Teardown deletes
any remaining terrarium with that exact generated name. Test accounts are identifiable by the `mobile_e2e_`
username prefix.

The suite clears the stored refresh token in `setUpAll`, so it starts from a signed-out device whatever the emulator
was left in. Without that, the app restores a session before the first frame — a phone left signed in by a manual
camera-demo run opens on the dashboard and every sign-in scenario fails on a `LoginScreen` that never appears.

The Flutter test runner prints each scenario and the final pass/fail summary, and the suite writes
[`TEST-RESULTS.md`](./TEST-RESULTS.md) on every run — see **Report** below.

## Report

`flutter test` has no `show-report`, so both reports are generated from the JSON reporter (`--file-reporter
json:<file>`) — the same idea as the web suite's Playwright reporter:

```powershell
# from the repository root — works from any working directory
e2e.bat                          # suite + both reports, then opens the dashboard
e2e.bat -NoOpen                  # reports only, do not open a browser
e2e.bat -Device gqyxmvzdyd4piffq # a USB phone instead of the emulator

# from app/ — same thing, same options
.\tool\run_e2e.cmd -NoOpen
```

`tool/run_e2e.ps1` is the implementation. Windows blocks `.ps1` files under the default ExecutionPolicy, so call
it through the `.bat`/`.cmd` (they pass `-ExecutionPolicy Bypass`), or run
`powershell -ExecutionPolicy Bypass -File .\tool\run_e2e.ps1` yourself.

One run writes two things:

| File | What it is |
|---|---|
| `integration_test/TEST-RESULTS.md` | The committed log, same shape as [`web/e2e/TEST-RESULTS.md`](../../web/e2e/TEST-RESULTS.md) — the table a report quotes |
| `build/e2e/report.html` | The dashboard: status, counts, wall time, per-scenario durations, All/Passed/Failed/Skipped filters. Self-contained (no CDN, no assets), so it opens offline by double-click; `build/` is gitignored |

The two steps, if you want them apart:

```powershell
flutter test integration_test/app_e2e_test.dart -d emulator-5554 --file-reporter json:build/e2e-report.json
dart run tool/e2e_report.dart build/e2e-report.json --out integration_test/TEST-RESULTS.md --html build/e2e/report.html
```

Both are written even when the suite fails — that is when they matter. `xunit-viewer` (JSON → JUnit → HTML) would
give the same page with more moving parts; nothing in the repository needs it.

## Covered scenarios

| ID | Scenario | Assertions |
|---|---|---|
| MOB-E2E-01 | Invalid login | Invalid credentials show an error and keep the user on sign-in. |
| MOB-E2E-02 | Language selection | Switches sign-in to English and restores Vietnamese. |
| MOB-E2E-03 | Registration and session | Registers an Owner through the UI, signs in, and signs out. |
| MOB-E2E-04 | Primary navigation | Owner can navigate to each primary mobile surface. |
| MOB-E2E-05 | Terrarium validation | A terrarium cannot be created without a name. |
| MOB-E2E-06 | Create and view terrarium | Creates a terrarium and opens its detail and empty-history states. |
| MOB-E2E-07 | Edit terrarium | Updates a terrarium name from its detail screen. |
| MOB-E2E-08 | Cancel deletion | Cancels confirmation and verifies the terrarium remains. |
| MOB-E2E-09 | Confirm deletion | Confirms deletion and verifies the terrarium is removed. |
| MOB-E2E-10 | History controls | Changes the history metric and time range. |
| MOB-E2E-11 | Alert filters | Selects pending, resolved, and all alert states. |
| MOB-E2E-12 | Species catalogue | Searches the catalogue and opens a species profile. |
| MOB-E2E-13 | Password validation | Mismatched password confirmation is rejected in profile. |

## Cleanup and data safety

The suite creates a unique Owner fixture and unique terrarium names for the run. Teardown deletes only a
terrarium whose name exactly matches the current run. Test accounts use the `mobile_e2e_` prefix.
