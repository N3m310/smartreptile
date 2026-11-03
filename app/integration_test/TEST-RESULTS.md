# Mobile integration-test report

## Latest run — 2026-11-01

- **Status:** Passed; 13 of 13 E2E scenarios.
- **Target:** Pixel 9a Android emulator (Android 17 / API 37, `emulator-5554`).
- **Command:** `flutter test integration_test --device-id emulator-5554 --reporter expanded`
- **Backend:** Local .NET API at `http://localhost:8080`; the suite's `/health/ready` check passed.
- **App build/install:** Passed.
- **Dart analysis:** `flutter analyze integration_test/app_e2e_test.dart` passed.
- **FCM:** Disabled for this build; unrelated to the tested scenarios.

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
