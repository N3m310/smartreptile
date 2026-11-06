# Playwright E2E test results

- Run at (UTC): 2026-11-05T12:49:14.882Z
- Overall status: **passed**
- Runtime: v22.15.1 (win32)
- Tests: 21 total; 19 passed; 0 failed; 0 timed out; 2 skipped; 0 interrupted

| Result | Test | Duration | Retry | Details |
|---|---|---:|---:|---|
| passed | chromium › auth.spec.ts › authentication › changes the sign-in language from the language selector | 1.53s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › redirects an unauthenticated visit to a protected page to sign-in | 1.76s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › registers a disposable account, signs in, and removes its access | 3.83s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › shows a useful error for invalid credentials without leaving the sign-in page | 2.03s | 0 |  |
| passed | chromium › auth.spec.ts › authentication › signs in and signs out through the application UI | 1.95s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › filters alerts and history through their real UI controls | 1.63s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens a species profile from the searchable profile catalogue | 1.41s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › opens the system diagnosis and wallboard routes | 2.27s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › persists personal notification settings across reload and restores the original value | 6.99s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › registers, claims, configures, and purges a disposable IoT device | 2.21s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › rejects an invalid MAC before attempting to claim a device | 1.48s | 0 |  |
| passed | chromium › feature-surfaces.spec.ts › owner feature surfaces › validates an incomplete quiet-hours pair without saving profile changes | 1.91s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › creates a terrarium, updates its species, validates thresholds, and opens its history | 16.80s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › navigates through dashboard, terrariums, detail and linked history | 15.77s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › opens devices, alerts and personal settings from the application navigation | 16.35s | 0 |  |
| passed | chromium › owner-workflows.spec.ts › owner workflows › shows owner-only management actions without changing demo data | 15.57s | 0 |  |
| skipped | chromium › role-permissions.spec.ts › administrator › can open the overview, account and role management screens | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| skipped | chromium › role-permissions.spec.ts › administrator › creates, reads, updates and deletes a custom species profile | 0.00s | 0 | Set E2E_ADMIN_USERNAME and E2E_ADMIN_PASSWORD to run administrator scenarios. |
| passed | chromium › role-permissions.spec.ts › technician › can read their surfaces but cannot open admin or provision devices | 15.43s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer › can read their surfaces but cannot open admin or provision devices | 15.47s | 0 |  |
| passed | chromium › role-permissions.spec.ts › viewer species-profile permissions › cannot open species-profile authoring controls | 15.21s | 0 |  |

Generated automatically by the Playwright test runner. See [README](./README.md) for setup and run instructions.
