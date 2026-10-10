# The clickable prototype (fake data, real rules)

A prototype of SmartReptile's main flow, built to **examine the business rules before the backend exists**. It shows
every screen a keeper will use, filled with generated data — and every threshold, dwell, hysteresis, dedupe,
escalation, suppression and coverage decision on those screens is made by a real implementation of the rules in
`docs/`.

Run it:

```bash
cd app
flutter run -d web-server --web-port=8099        # then open http://127.0.0.1:8099
flutter run -d chrome                             # or a Chrome window
flutter test test/prototype                       # 100+ tests, about a second
```

Sign in with the pre-filled demo credentials (`linh@example.com` / `demo1234`).

## What is fake, and what is not

| Fake | Real |
|---|---|
| The measurements, and the three days of history they came from | Every rule in `lib/prototype/rules/` |
| The devices, users, terrariums and their names | The seeded species profiles and their band counts (17 = 5 + 6 + 6) |
| The clock — frozen, and movable with the demo-clock chip | The threshold resolution chain (override → profile → default) |
| The notification *channels* (nothing is sent anywhere) | The notification *decision*: the whole `02-design/05` §4 flow |
| Log-in: the credentials are pre-filled and nothing is stored | The exposures, coverages, light deficits and compliance numbers |

Nothing on this path touches the network or the disk. `--dart-define=PROTOTYPE=false` switches to the M1 live path
(real `ApiClient`, real `TelemetryProvider`), which still shows only honest empty and offline states.

## Why the rules live in their own package

```
lib/prototype/
  rules/            pure Dart, no Flutter imports, unit-testable in a second
    domain.dart            MetricCatalog, ThresholdBand, MetricSample, phases, quality bits
    events.dart            the decision trace, alert episodes, notification records, EngineConfig
    species_profiles.dart  the seeded profiles + ThresholdResolver
    band_validation.dart   BR-10.2 blocking rules and BR-10.5 non-blocking sanity checks
    threshold_engine.dart  the §4.2 decision procedure + the BR-07.2 silence watchdog
    notification_policy.dart  the §4 decision flow
    daily_summary.dart     exposure index, coverage, compliance, gap-aware interpolation
    silence.dart, scenarios.dart
  fake_world.dart   three terrariums, four devices, three days of samples, run through the engine
  prototype_state.dart  the single ChangeNotifier the screens read and mutate
  rule_matrix.dart  which rules are modelled, and which are not
  labels.dart       every user-facing string (see below)
  screens/, widgets/
```

The kernel has no Flutter dependency, which is why the whole rule suite runs in about a second. An earlier attempt to
build a 30-day fixture took tens of seconds because the summary builder rescanned its samples once per minute; the
fix is a two-pointer walk, and the fake world now has a test asserting it stays interactive.

## The Rule Lab is the point

`More → Rule Lab` plays twelve documented scenarios back through the engine and prints the decision trace. Each step
carries the rule id that produced it, so no decision is unattributable, and each scenario carries **what the
documentation claims** will happen, so a disagreement is visible rather than buried.

You can change the warning dwell, the critical dwell, the recovery window and the recovery-margin interpretation, and
watch the same data produce different decisions. Dwell and the recovery margin are described in the docs as *engine
parameters*, not biology — which is exactly why they are editable here.

| # | Scenario | The claim it tests |
|---|---|---|
| A | A 4-minute spike produces no alert | BR-11.3 dwell is a filter, not a delay |
| B | One excursion → one alert, escalated, then resolved | BR-11.3/11.5/11.6/11.4 together |
| C | 24.5 °C at night is fine | BR-11.2 the phase comes from the photoperiod |
| D | A silent device never looks like safety | BR-07.2 |
| E | Inside the band is not enough to resolve | BR-11.4 hysteresis |
| F | A 12-hour-late back-fill wakes nobody | BR-11.8 |
| G | A silence suppresses notifications, never the record | BR-11.7 / BR-12.6 |
| H | Quiet hours suppress a Warning but never a Critical | BR-13.2 |
| I | The hourly cap turns extra notifications into a digest | BR-13.3 |
| J | Maintenance mode records everything, sends nothing | `02-design/05` §5 |
| K | A broken sensor reading is stored, never alerted on | BR-06.4 / BR-11.1 |
| L | Twenty flapping excursions, one alert row | BR-11.5 dedupe |

## Four things the prototype found, which is why it was worth building

1. **`03-implementation/06` contradicts itself about recovery (BR-11.4).** §4.2 requires a value to be back inside the
   target band *by the margin*; its own worked Example B counts a 31.6 °C reading against a 26–32 °C band with a
   0.5 °C margin as a recovery minute. 0.4 is not 0.5. The engine defaults to the pseudocode and resolves at 14:57;
   switch the Lab's recovery mode to "plain in band" and it reproduces the example's 14:54 exactly. **One of the two
   paragraphs has to change.**
2. **A phase rollover with an alert still open is undefined.** State is keyed by `(metric, phase)`, and no document
   says what happens to an alert opened under the previous phase when the photoperiod closes. The engine emits a step
   labelled `OPEN-QUESTION` rather than inventing a resolution.
3. **BR-10.5's climate sanity has no floors in its source table.** `07-appendices/05` §4 lists *ceilings*, and
   BR-10.5's own example needs a floor. Reading the range as "the band a normal day target maximum falls in", and
   judging the **day** band only, is the interpretation that neither flags the seeded profiles nor loses the example.
   A night drop is expected to be cooler than any daytime ceiling. A second exception was needed on 2026-10-07: the
   Arid row's 40–44 °C describes the *basking* zone, so the seeded ambient variant (28–33 °C, `Arid-cool`) is judged
   against its own envelope (`ClimateRange.aridAmbient`) instead of being flagged by the very band §3 gives it.
4. **A recovery notice inherited the alert's escalated severity and bypassed quiet hours.** A resolved alert keeps
   its Critical severity, so "back in range" could wake a keeper at 03:00. The policy now evaluates the *event*, not
   the alert's history — with a regression test named after the bug.

Plus two self-inflicted defects the tests caught: the hourly rate cap existed in **two** places (`EngineConfig` and
`NotificationPolicy`) so setting one silently kept the other, and the first BR-10.5 implementation invented
temperature and humidity *floors* that flag the seeded Tropical night band as suspicious.

## Where the prototype deliberately deviates

- **The metric status is computed in the app.** The design puts that on the server (BR-08.1, "the client never decides
  bands itself"). There is no server yet, so `prototype_state.dart` computes it from the same resolved band the engine
  uses. The day `/readings/latest` lands, that method is deleted and the status comes off the wire. It is marked
  `partly modelled` in the Rule Lab's coverage matrix.
- **History stops at 48 hours.** BR-09.1 gives raw readings to 6 h, 5-minute averages to 48 h, and hourly rollups
  beyond — an M4 pipeline. The 7 d and 30 d chips are shown, disabled, with the reason. Drawing a 30-day line through
  four days of data is the exact dishonesty this project keeps warning itself about.
- **Strings live in `lib/prototype/labels.dart`, not in the ARB files.** The prototype is English-only by decision,
  and adding ~200 strings to `app_en.arb` that will never be translated would make the real catalogue look complete
  when it is not. The convention that matters — no user-facing literals scattered through widgets — is kept, and
  promoting them to ARB later is a mechanical move.
- **The demo clock is frozen** at the instant the fake data ends, and movable from the app bar. A prototype whose
  "now" drifts with the wall clock shows different numbers in every screenshot and cannot be re-checked.

## What is left to build

`rule_matrix.dart` holds the honest list, and the Rule Lab renders it. The short version: duplicate detection
(BR-06.3), clock-skew flagging (rule V-09), channel retries (BR-13.5), retention and export (BR-15), the last mile of
calibration actually moving the readings (BR-07.4), and the two SHOULD-tier derived signals — `GradientWarning` and
`LightDeficit` as an alert rather than a report row.

## Deleting it

The prototype is additive. `lib/prototype/` and `test/prototype/` can be removed together, and the only other edits
are a six-line `Env.prototypeMode` branch in `main.dart`, a `toApiValue()` added to `core/status.dart` (the inverse of
the `parse` that was already there), and `web/flutter_bootstrap.js` — which this machine needs regardless, because the
CanvasKit CDN is unreachable here and the only symptom is a blank white page.
