# 05 — Mobile E2E testing

How the app's end-to-end coverage is run, and the rules that keep it honest. `01-test-strategy-and-plan.md` places
this layer in the overall plan; `02-test-cases.md` §E holds the cases it answers to.

## 1. What runs today

**One suite:** [`app/integration_test/app_e2e_test.dart`](../../app/integration_test/app_e2e_test.dart).

It drives the real app (`app.main()`) against the real API — no stub, no fixture server — on an Android emulator or a
USB-connected phone. The 13 scenarios cover invalid login, language selection, registration and session handling,
primary navigation, terrarium validation and CRUD, history controls, alert filters, species search and details, and
password validation. Scenario ids are in
[`app/integration_test/README.md`](../../app/integration_test/README.md); the last run is
[`app/integration_test/TEST-RESULTS.md`](../../app/integration_test/TEST-RESULTS.md), which the suite writes itself
(§2.1).

### 1.1 What this layer does not cover

| Not covered | Where it is covered instead |
|---|---|
| Guest mode, an empty catalogue, request accounting | `app/test/widget/` — a widget test can assert on a fake client cheaply |
| Keychain contents after sign-out | `app/test/` unit tests over the token store |
| An API outage, and silence presented as silence | manual checklist (`03-manual-qa-checklist.md`); a 3-interval dwell is minutes of wall clock |
| The web client | `web/e2e/` — Playwright specs, run by `npm run test:e2e` |
| Real hardware, camera and sensor paths | `03-manual-qa-checklist.md` |

## 2. Run

```powershell
# 1. Backend (separate terminal), then wait for readiness
dotnet run --project backend/src/SmartReptile.Api
curl.exe http://localhost:8080/health/ready

# 2. The suite, from the repository root — runs it and writes both reports
e2e.bat            # opens the dashboard when it finishes; -NoOpen to skip
```

`API_BASE` defaults to `http://10.0.2.2:8080`, which is the host as seen from an emulator. A phone on USB needs
`adb reverse tcp:8080 tcp:8080` and `--dart-define=API_BASE=http://127.0.0.1:8080`; the whole procedure, TURN
included, is in [`app/README.md`](../../app/README.md).

### 2.1 The report

`flutter test` has no `show-report`: the JSON reporter (`--reporter json`, `--file-reporter json:<file>`) is its only
machine-readable output, and `--reporter github` only turns failures into annotations. The Markdown report the web
suite gets from its Playwright reporter is therefore generated from that JSON —
[`app/tool/e2e_report.dart`](../../app/tool/e2e_report.dart) — in the same shape as `web/e2e/TEST-RESULTS.md`: one
row per scenario with status and duration, written even when the suite fails.

`e2e.bat` (root) and `app\tool\run_e2e.cmd` both do it, and both pass `-ExecutionPolicy Bypass` — Windows
blocks a bare `.ps1` under the default policy, which is why the implementation
[`app/tool/run_e2e.ps1`](../../app/tool/run_e2e.ps1) is reached through them. Besides the Markdown they write
`build/e2e/report.html` — the dashboard, and the closest thing Flutter has to `playwright show-report`: status,
counts, wall time, per-scenario durations and All/Passed/Failed/Skipped filters, in one self-contained file that opens
offline (it opens by itself when the run ends; `-NoOpen` skips that). In CI the same conversion runs after the emulator
step, appends the table to the job summary and leaves both files in the `e2e-reports` artifact. An `xunit-viewer` page
would need JSON → JUnit → HTML; nothing in the repository needs that.

The run is gated in CI on a nightly schedule and on demand (`.github/workflows/ci.yml`, job `app-e2e`): it needs an
emulator and the compose stack, so it does not run on every push. `flutter analyze` and `flutter test` gate pushes.

## 3. Rules that keep it honest

| Rule | Reason |
|---|---|
| Clear app data before a run (`adb shell pm clear com.smartreptile.smart_reptile`) | the refresh token lives in the keychain (`app/lib/data/token_store.dart`); otherwise "the app opens on sign-in" fails on the second run of the day |
| One device, one run at a time | two runs on one backend see each other's fixtures, and the empty-state cases lie |
| A run must leave the backend as it found it | the suite creates a unique Owner fixture and unique terrarium names, and deletes only what it created; teardown matches on the exact generated name |
| Never `find.byIcon` at the top level | an empty state draws the same `IconData` as the destination that leads to it |
| Never `pumpAndSettle` in the prototype Rule Lab | `rule_lab_screen.dart` runs a 260 ms `Timer.periodic`; it never settles |
| Never assert a raw `find.text` on a copy string where an identifier exists | the deck is bilingual and `vi`-first |
| Freeze time in fixtures (fixed UTC timestamps), never `DateTime.now()` | day/night phase and local-day bucketing would otherwise depend on when the run happens |
| Assert values, not formatting, where `intl` is involved | locale-sensitive number formatting is `TC-W-*`'s job |
| A screen may not call a notifying provider method from `initState` | `TerrariumProvider.loadTerrariums` notifies before its first `await`, so the call marks the provider scope dirty while the parent builds — defer with `addPostFrameCallback` |

## 4. Locators — `Semantics(identifier:)`, not `ValueKey`

Text finders in a `vi`-first app break on every copy change. The fix is **not** a `ValueKey`: a key is visible to
`flutter_test` only. One `Semantics(identifier: …)` is visible to three consumers at once:

| Consumer | Reads it as |
|---|---|
| `integration_test` | `find.bySemanticsIdentifier('login.username')` |
| an external driver, if ever adopted | `screen.getByTestId('login.username')` — the Android resource id / iOS accessibility id |
| TalkBack, VoiceOver | the element identity — i.e. part of `NFR-06`'s accessibility evidence |

Implemented: the login form (`login.identifier`, `login.username`, `login.email`, `login.password`, `login.code`,
`login.resetCode`, `login.newPassword`), the login actions (`login.submit`, `login.guest`, `login.error`), the
language pills (`login.lang.vi`, `login.lang.en`) and `profile.signOut`.

Three rules that cost an hour each if missed:

1. **A tappable control needs `MergeSemantics`.** `Semantics(identifier: …, child: ElevatedButton(…))` produces two
   nodes — the id on a wrapper, the `button` role on the child — and a coordinate-based tap can land between them.
2. **A `TextFormField` needs the identifier plus a label.** Without a label it reads as an unnamed textbox.
3. **A plain `testWidgets` must enable semantics** (`tester.ensureSemantics()` … `handle.dispose()`);
   `integration_test` on a device gets the tree from the platform bridge. Dispose the handle inside the test body,
   never in `addTearDown`, or the binding fails the test with "All SemanticsHandle instances must be disposed".

Identifiers name **slots**, not copy: never assert on the text of an identified widget; assert on the widget or the
value behind it.

## 5. History — the four suites that used to exist

Until 2026-11-05 the layer was a harness of four suites (`smoke`, `hermetic`, `fullstack`, `alerts`) driven by
`scripts/e2e.ps1`, plus a stub API, fixture helpers and an on-device progress banner (`lib/core/e2e_hud.dart`).

It was removed on request: four overlapping suites, a PowerShell wrapper, a stub server and a banner in the
production widget tree were more machinery than the coverage justified, and only `app_e2e_test.dart` was being run.
The scenarios worth keeping moved into that suite; the ones that need a stub or minutes of wall clock moved to widget
tests or the manual checklist (§1.1). The retired case ids `E2E-AI-*` are no longer maintained as a separate
registry — `02-test-cases.md` §E and the traceability matrix remain the source for what a case must prove.
