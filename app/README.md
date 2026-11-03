# smart_reptile

A new Flutter project.

## Tests

```bash
flutter test                      # widget + unit (no device)
flutter gen-l10n                  # the ARB deck is generated, not committed

# E2E: needs an Android emulator or device; the script boots one with a window if none is running.
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite smoke
```

`scripts\e2e.ps1` writes `build/e2e/<suite>.json` + `<suite>.log` and exits `0`/`1`/`2` (passed / failed /
prerequisite missing). Four suites exist: `smoke` (guest shell, no backend), `hermetic` (login, refusal,
registration, language, sign-out — against a stub API inside the test process), `fullstack` (the same journeys
against `docker compose`, fixture created and deleted through the public API) and `alerts` (a claimed board sends an
excursion through the device-facing ingest route and the alert must appear in the app — `TC-E2E-03`'s software
half). The suites and their scope are in `docs/04-quality/05-e2e-ai-testing-plan.md`.

### Watching a run

The mobile run is watchable, the way a Playwright run is: the AVD is booted with a window and the app drives itself
inside it. On top of the app, `lib/core/e2e_hud.dart` paints a banner while a case is running:

```
● E2E RUNNING · HERMETIC                                       0:12
E2E-AI-03 signing in reaches the dashboard
waiting for widget with semantics identifier "login.identifier"
```

The banner names the suite, the case and the step in flight, and turns green (`E2E PASSED`) or red (`E2E FAILED`)
when the case ends. It is inert outside a run — a keeper never sees it — and it paints with a `CustomPainter`
rather than `Text`, so it cannot add matches to the suites' own `find.text` assertions. Suites publish to it through
`integration_test/helpers/hud.dart` (`e2eCase`) and `helpers/pump.dart`, which reports every `pumpUntilFound` /
`pumpUntilGone` / `tapDestination` on its own.

```bash
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1                  # smoke + hermetic, emulator visible
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Suite fullstack # needs Docker Desktop running
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -Headless        # unattended: no emulator window
powershell -ExecutionPolicy Bypass -File ..\scripts\e2e.ps1 -RestartDevice   # a windowless AVD is still up
```

`CI` set to any value makes the run headless, which is what an unattended job wants. `.github/workflows/ci.yml` runs
`flutter test` against its own emulator rather than this script, so nothing in the pipeline depends on the window.

## Getting Started

This project is a starting point for a Flutter application.

A few resources to get you started if this is your first Flutter project:

- [Learn Flutter](https://docs.flutter.dev/get-started/learn-flutter)
- [Write your first Flutter app](https://docs.flutter.dev/get-started/codelab)
- [Flutter learning resources](https://docs.flutter.dev/reference/learning-resources)

For help getting started with Flutter development, view the
[online documentation](https://docs.flutter.dev/), which offers tutorials,
samples, guidance on mobile development, and a full API reference.
