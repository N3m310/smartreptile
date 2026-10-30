# Playwright end-to-end tests

These tests run the real React app against the real SmartReptile API and database. They do not mock API responses.
Locally, `npm run test:e2e` opens a visible Chromium window and runs one worker so you can watch the automation.
No browser is opened by setup, test listing, or type-checking. In CI, Playwright switches to headless mode.
The global setup obtains one short-lived signed-in session per configured demo role and writes Playwright storage
states under the ignored `test-results/.auth/` directory. Protected-page tests reuse those states, avoiding repeated
password logins against the backend's 10-requests-per-minute auth limit; the dedicated authentication tests still
exercise the actual login and logout UI. Allow the one-minute auth limit window to reset before immediately rerunning
the whole suite, because each run prepares three demo-role sessions plus the explicit login scenarios.

## What is covered

The suite uses the real browser app, API and database; API responses are not mocked. Its current end-to-end coverage is:

| Area | Role | Scenarios |
|---|---|---|
| Authentication | Public / disposable account | Invalid and valid sign-in, sign-out, language selection, protected-route redirect, registration, backup-code recovery, sign-in with the recovered password, and account disablement in cleanup |
| Terrariums | Owner | Navigation, create a unique terrarium, open its detail, update species, reject an invalid threshold, open linked history, and soft-delete the fixture |
| Devices | Owner | Reject an invalid MAC without an API call; claim a simulated device, edit its name and sampling settings, permanently delete it, and purge any remaining fixture |
| Settings | Owner | Reject incomplete quiet hours without saving; save a notification preference, verify it survives reload, then restore the original value |
| History and alerts | Owner | Change history range/metric and alert state/severity filters |
| Species profiles | Viewer / Admin | Verify Viewer cannot author profiles; Admin creates, reads, updates and deletes a custom profile |
| Permissions | Technician / Viewer | Read allowed surfaces, verify restricted controls are absent, and verify administrator routes redirect |
| System surfaces | Owner | Open the system diagnosis and wallboard routes |
| Administrator | Admin | Open the overview, account management and role management screens |

This is broad UI-to-API E2E coverage, not a claim that every backend rule or every destructive action is covered.
In particular, the current suite does not generate telemetry to exercise alert evaluation and acknowledge/resolve/silence
transitions, test email-issued password-reset codes, or mutate Admin accounts/roles. Those scenarios need dedicated
test data and cleanup policies. The admin CRUD test is skipped unless optional Admin credentials are configured.

Terrarium fixtures are soft-deleted through the API in `finally` cleanup. The registration workflow disables its
disposable account; the account row remains in the database. The device workflow creates a simulated board through
the UI's MAC-claim path and purges it after the edit/delete path, without claiming physical hardware. The Admin profile
is permanently deleted in the UI and has an API cleanup fallback. Settings are restored to their pre-test value.
Use a dedicated test database for repeated or CI runs. The suite does not delete demo data or change demo-account
passwords.

## Prerequisites

1. Node.js and npm.
2. The SmartReptile API and its SQL database running at `http://127.0.0.1:8080`.
3. Development demo data enabled and seeded. By default, the tests use the demo accounts created by
   `DemoDataSeeder`: `demo.owner`, `demo.technician`, and `demo.viewer`, all with password `Demo!Reptile1`.
4. Playwright's Chromium browser installed once:

   ```powershell
   cd web
   npm ci
   npx playwright install chromium
   ```

Start the backend using the repository's normal development setup (see the root README). The web dev server is started
automatically by Playwright on port 8081. If port 8081 is already serving the app, it is reused outside CI.

## Run

From `web/`:

```powershell
npm run test:e2e
```

Type-check the application and test code without launching a browser:

```powershell
npm run typecheck
```

List the cases without launching a browser:

```powershell
npm run test:e2e:list
```

Open the saved HTML report after a run:

```powershell
npm run test:e2e:report
```

The tests save traces, screenshots, and video only on failure in `test-results/`; the HTML report is written to
`playwright-report/`. Every run also replaces [`TEST-RESULTS.md`](./TEST-RESULTS.md) with a Markdown summary containing
the timestamp, runner status, counts, and per-test result/duration.

## Configuration

The defaults are suitable for the local Compose/development setup. Override account values in PowerShell before running
the suite when using a dedicated test database:

```powershell
$env:E2E_OWNER_USERNAME = "demo.owner"
$env:E2E_OWNER_PASSWORD = "Demo!Reptile1"
$env:E2E_TECHNICIAN_USERNAME = "demo.technician"
$env:E2E_TECHNICIAN_PASSWORD = "Demo!Reptile1"
$env:E2E_VIEWER_USERNAME = "demo.viewer"
$env:E2E_VIEWER_PASSWORD = "Demo!Reptile1"
npm run test:e2e
```

Administrator coverage is skipped unless both optional values are set:

```powershell
$env:E2E_ADMIN_USERNAME = "your-test-admin"
$env:E2E_ADMIN_PASSWORD = "your-test-admin-password"
```

Use an Admin account dedicated to testing. Do not copy credentials into test code, reports, or committed files.

Additional settings:

| Variable | Default | Purpose |
|---|---|---|
| `PLAYWRIGHT_BASE_URL` | `http://127.0.0.1:8081` | Web app base URL |
| `PLAYWRIGHT_START_WEB_SERVER` | `1` | Set to `0` to use an already-running/remote web app |
| `E2E_API_BASE_URL` | `http://127.0.0.1:8080` | Backend base URL used for readiness and test-session setup |
| `E2E_API_HEALTH_URL` | `http://127.0.0.1:8080/health/ready` | API readiness endpoint checked before tests |
| `E2E_SKIP_API_HEALTH_CHECK` | `0` | Set to `1` only when intentionally testing an environment where the readiness endpoint is unavailable |

The Vite development proxy in `vite.config.ts` currently forwards `/api` and `/hubs` to `http://localhost:8080`.
Changing only `E2E_API_HEALTH_URL` does not change that proxy target. For another API host, update the Vite proxy or run
the tests against a deployment whose web app already points at that API.

## CI

Install the browser once in the CI image with `npx playwright install --with-deps chromium`, configure a dedicated
database with seeded test accounts, then run `npm run test:e2e`. `CI` enables one retry, switches Chromium to headless
mode, limits workers to two, and disables reusing an existing web server.
