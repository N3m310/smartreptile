# smart_reptile

A new Flutter project.

## Tests

```bash
flutter test                      # widget + unit (no device)
flutter gen-l10n                  # the ARB deck is generated, not committed

# E2E: needs an Android emulator or device; the script boots one headless if none is running.
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite smoke
```

`scripts\e2e.ps1` writes `build/e2e/<suite>.json` + `<suite>.log` and exits `0`/`1`/`2` (passed / failed /
prerequisite missing). Four suites exist: `smoke` (guest shell, no backend), `hermetic` (login, refusal,
registration, language, sign-out — against a stub API inside the test process), `fullstack` (the same journeys
against `docker compose`, fixture created and deleted through the public API) and `alerts` (a claimed board sends an
excursion through the device-facing ingest route and the alert must appear in the app — `TC-E2E-03`'s software
half). The suites and their scope are in `docs/04-quality/05-e2e-ai-testing-plan.md`.

```bash
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1                  # smoke + hermetic
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite fullstack # needs Docker Desktop running
```

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.
