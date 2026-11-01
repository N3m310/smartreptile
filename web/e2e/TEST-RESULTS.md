# Playwright E2E test results

- Run at (UTC): 2026-10-31T02:40:41.061Z
- Overall status: **failed**
- Runtime: v24.21.0 (win32)
- Tests: 21 total; 18 passed; 1 failed; 0 timed out; 2 skipped; 0 interrupted

| Result | Test | Duration | Retry | Details |
|---|---|---:|---:|---|
| passed | chromium › auth.spec.ts › authentication › changes the sign-in language from the language selector | 2.55s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › redirects an unauthenticated visit to a protected page to sign-in | 2.72s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › registers a disposable account, signs in, and removes its access | 4.20s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › shows a useful error for invalid credentials without leaving the sign-in page | 3.54s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › signs in and signs out through the application UI | 3.87s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › filters alerts and history through their real UI controls | 7.46s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens a species profile from the searchable profile catalogue | 5.39s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens the system diagnosis and wallboard routes | 5.17s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › persists personal notification settings across reload and restores the original value | 6.32s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › registers, claims, configures, and purges a disposable IoT device | 9.81s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › rejects an invalid MAC before attempting to claim a device | 6.89s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › validates an incomplete quiet-hours pair without saving profile changes | 5.22s | 0 |  |
| failed | chromium › owner-workflows.spec.ts › owner workflows › creates a terrarium, updates its species, validates thresholds, and opens its history | 10.06s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › navigates through dashboard, terrariums, detail and linked history | 5.74s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › opens devices, alerts and personal settings from the application navigation | 3.94s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › shows owner-only management actions without changing demo data | 3.46s | 0 |  |
| skipped | chromium › role-permissions.spec.ts › administrator › can open the overview, account and role management screens | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| skipped | chromium › role-permissions.spec.ts › administrator › creates, reads, updates and deletes a custom species profile | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| passed | chromium › role-permissions.spec.ts › technician › can read their surfaces but cannot open admin or provision devices | 4.13s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer › can read their surfaces but cannot open admin or provision devices | 3.83s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer species-profile permissions › cannot open species-profile authoring controls | 3.28s | 0 |  |

Generated automatically by the Playwright test runner. See [README](./README.md) for setup and run instructions.
