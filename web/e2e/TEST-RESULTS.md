# Playwright E2E test results

- Run at (UTC): 2026-10-30T11:43:31.996Z
- Overall status: **passed**
- Runtime: v24.21.0 (win32)
- Tests: 21 total; 19 passed; 0 failed; 0 timed out; 2 skipped; 0 interrupted

| Result | Test | Duration | Retry | Details |
|---|---|---:|---:|---|
| passed | chromium › auth.spec.ts › authentication › changes the sign-in language from the language selector | 1.15s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › redirects an unauthenticated visit to a protected page to sign-in | 1.22s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › registers a disposable account, signs in, and removes its access | 2.78s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › shows a useful error for invalid credentials without leaving the sign-in page | 1.54s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › signs in and signs out through the application UI | 1.42s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › filters alerts and history through their real UI controls | 2.24s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens a species profile from the searchable profile catalogue | 2.01s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens the system diagnosis and wallboard routes | 2.61s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › persists personal notification settings across reload and restores the original value | 2.72s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › registers, claims, configures, and purges a disposable IoT device | 3.75s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › rejects an invalid MAC before attempting to claim a device | 2.61s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › validates an incomplete quiet-hours pair without saving profile changes | 1.82s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › creates a terrarium, updates its species, validates thresholds, and opens its history | 2.95s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › navigates through dashboard, terrariums, detail and linked history | 1.52s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › opens devices, alerts and personal settings from the application navigation | 1.84s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › shows owner-only management actions without changing demo data | 1.57s | 0 |  |
| skipped | chromium › role-permissions.spec.ts › administrator › can open the overview, account and role management screens | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| skipped | chromium › role-permissions.spec.ts › administrator › creates, reads, updates and deletes a custom species profile | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| passed | chromium › role-permissions.spec.ts › technician › can read their surfaces but cannot open admin or provision devices | 1.66s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer › can read their surfaces but cannot open admin or provision devices | 1.64s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer species-profile permissions › cannot open species-profile authoring controls | 1.51s | 0 |  |

Generated automatically by the Playwright test runner. See [README](./README.md) for setup and run instructions.
