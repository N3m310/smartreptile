# 02 — Test Cases

Catalogue with ids used across this doc set. `P` = priority (1 highest). Every case names *what would break
in the product* if it were not tested. Reference skeletons are given for the cases that carry the most
weight; the rest follow the same shape.

**Naming convention.** `Given<context>_When<action>_Then<expectation>` for backend, and a plain sentence for
widget tests.

---

## A. Firmware native unit tests (`pio test -e native`)

| Id | Case | Input / precondition | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-FW-01 | Median of 5 rejects one glitch | `[41.0, 41.1, 55.0, 41.2, 41.1]` | `41.1`, and quality bit 2 set when the outlier exceeds the noise threshold | 1 | FR-06 |
| TC-U-FW-02 | EMA converges without overshoot | Constant input after a step | Monotonic approach to the step value, no oscillation | 1 | FR-06 |
| TC-U-FW-03 | Plausibility guard flags out-of-range | `tempC = 85` | `SR_PLAUSIBLE` false, quality bit 2 | 1 | FR-06 |
| TC-U-FW-04 | Ring buffer appends and preserves order | 10 pushes | Reads back in insertion order | 1 | FR-07 |
| TC-U-FW-05 | Ring buffer eviction is reported | Push 725 into a 720-slot buffer | 720 retained, `droppedCount = 5`, oldest overwritten | 1 | FR-07 |
| TC-U-FW-06 | Ring buffer survives simulated reset | Serialize/deserialize the backing blob | Identical contents, seq watermark preserved | 1 | FR-07 |
| TC-U-FW-07 | Payload builder emits the documented keys | 3 samples, surface probe present | `t` offsets ascending, `tf/rh/lux/uvi/st` present, `q` omitted when 0, size < 4 KB | 1 | FR-06 |
| TC-U-FW-08 | Payload builder omits the surface key when absent | No DS18B20 | No `st` key at all (not `null`) | 2 | FR-06 |
| TC-U-FW-09 | Plausibility boundary values | `tempC = -10`, `60`, `60.01` | First two accepted, last flagged | 2 | FR-06 |
| TC-U-FW-10 | Transport state machine: MQTT fail → fallback after 60 s | Fake clock, 3 failed connects | State `HttpFallback` after the window, not before | 1 | FR-06 |
| TC-U-FW-11 | Backoff schedule caps and jitters | 8 consecutive failures | Delay sequence 2,4,8,16,32,60,60,60 with ±20% jitter, never exceeding 60 s | 2 | FR-06 |
| TC-U-FW-12 | Clock-unsynced flag when NTP never succeeds | NTP stub fails | Samples carry quality bit 16 and are still produced | 1 | NFR-10 |

```cpp
// firmware/test/test_filters/test_main.cpp
void test_median_rejects_single_glitch(void) {
  const float raw[5] = {41.0f, 41.1f, 55.0f, 41.2f, 41.1f};
  FilterResult r = sr::filter::medianOf5(raw);
  TEST_ASSERT_FLOAT_WITHIN(0.05f, 41.1f, r.value);
  TEST_ASSERT_TRUE(r.outlierDetected);          // → quality bit 2
}
```

---

## B. Backend unit tests (TC-U-01…50)

### B1. Ingest: validation, plausibility, calibration

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-01 | Schema rejects a missing sequence | payload without `seq` | `Result` failure `schema_invalid`, no persistence attempted | 1 | FR-06 |
| TC-U-02 | Schema rejects an unparseable timestamp | `ts = "yesterday"` | failure `schema_invalid` | 1 | FR-06 |
| TC-U-03 | Sample count limits enforced | 121 samples in one batch | failure `payload_too_large` | 2 | FR-06 |
| TC-U-04 | Unknown metric code is dropped, batch survives | sample with `zz: 1.0` plus valid metrics | valid metrics persisted, `unknown_metric` logged, no failure | 2 | FR-06 |
| TC-U-05 | Device authenticator is constant-time and rejects a wrong secret | correct secret, then a secret differing in one char | true then false; comparison uses `FixedTimeEquals` (asserted by no early-exit surrogate: timing variance test with 1 000 iterations under 10% spread) | 1 | FR-05 |
| TC-U-06 | Plausibility flags, never rejects | `TempC = 85` | accepted with quality bit 2, `IsEvaluable = false` | 1 | FR-06 |
| TC-U-07 | Plausibility boundaries per metric | `HumidityPct = 0`, `100`, `100.1` | first two clean, third flagged | 1 | FR-06 |
| TC-U-08 | Negative lux rejected as implausible | `LightLux = -5` | flagged | 2 | FR-06 |
| TC-U-09 | Calibration applies to `Value`, preserves `RawValue` | offset `-0.4 °C`, raw `28.9` | `Value = 28.5`, `RawValue = 28.9` | 1 | FR-07 |

### B2. Threshold decision engine — the highest-value tests in the project

Each test drives `ThresholdDecision.Decide` with an explicit clock and a state, e.g.
`Decide(band(target 26–32, critical 22–34.5, dwell 5/2, margin 0.5), value: 33.0f, at: T+6min, state, now)`
and asserts the returned `DecisionKind` plus the mutated state.

| Id | Case | Scenario | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-10 | Four minutes out of band → nothing | 32.4→33.0 over minutes 1–4, then back in | `None`, no alert, state reset | 1 | FR-11 |
| TC-U-11 | Five minutes out of band → one Open | as TC-U-10 but persisting through minute 5 | `Open(Warning)`, `TriggeredAt` = first out-of-band time (back-dated) | 1 | FR-11 |
| TC-U-12 | Sustained excursion does not open a second alert | open alert + 10 more out-of-band samples | `Touch` only, `PeakValue` updated, one alert id | 1 | FR-11 |
| TC-U-13 | Critical crossing escalates the same alert | open Warning, value 34.9 for 2 min | `Escalate`, severity Critical, alert id unchanged | 1 | FR-11 |
| TC-U-14 | Critical dwell not met → no escalation | value 34.9 for 1 min then 34.0 | no `Escalate` | 1 | FR-11 |
| TC-U-15 | Recovery requires margin inside the band | value 32.2 (inside band but within margin) for 3 min | no `Resolve` | 1 | FR-11 |
| TC-U-16 | Recovery after 3 ticks resolves | value 31.5 for 3 min | `Resolve(Recovered)` | 1 | FR-11 |
| TC-U-17 | Recovery counter resets on a single excursion | in-limit, out-limit, in-limit ×3 | no `Resolve` until 3 *consecutive* ticks | 2 | FR-11 |
| TC-U-18 | Cold excursion uses `TargetMin` | value 24.0 with target 26–32 | `Open(Warning)`, direction = cold, peak tracking minimises | 1 | FR-11 |
| TC-U-19 | Quality flag excludes evaluation | sample with quality bit 2 and value 40 | `None` — no alert from a bad reading | 1 | FR-11 |
| TC-U-20 | Any-phase metric ignores the day/night split | `Phase = Any`, night time | evaluates with the Any band | 2 | FR-11 |

```csharp
[Fact]
public void GivenValueAboveTargetForFourMinutes_ThenNoAlertIsRaised()
{
    var band  = TestBands.LeopardGecko();               // 26–32, crit 22–34.5, dwell 5/2, margin 0.5
    var state = EvaluationState.Empty();
    var start = new DateTimeOffset(2026, 9, 21, 14, 1, 0, TimeSpan.Zero);

    foreach (var (offset, value) in new[] { (0, 32.4m), (1, 32.9m), (2, 32.1m), (3, 31.0m), (4, 30.8m) })
        ThresholdDecision.Decide(band, value, start.AddMinutes(offset), state, start.AddMinutes(offset));

    state.OpenAlertId.Should().BeNull();
    state.Violation.Should().Be(ViolationKind.None);
}

[Fact]
public void GivenValueAboveTargetForFiveMinutes_ThenOneAlertIsOpenedAndBackDated()
{
    var band = TestBands.LeopardGecko();
    var state = EvaluationState.Empty();
    var start = new DateTimeOffset(2026, 9, 21, 14, 1, 0, TimeSpan.Zero);

    for (var m = 0; m <= 5; m++)
        ThresholdDecision.Decide(band, 32.5m + (m * 0.1m), start.AddMinutes(m), state, start.AddMinutes(m));

    state.OpenAlertId.Should().NotBeNull();
    state.FirstOutOfBandAt.Should().Be(start);   // BR-11 / design note 1: triggers are back-dated
}
```

### B3. Threshold resolution, validation, phases, snapshots

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-21 | Band ordering validation | target 40/30 | failure `threshold_ordering_invalid`, nothing persisted | 1 | FR-10 |
| TC-U-22 | Critical must enclose target | target 26–32, critical 28–31 | failure `threshold_critical_invalid` | 1 | FR-10 |
| TC-U-23 | Dwell ordering and recovery-margin fit | crit 6 > warn 5; margin 4 with a 6-wide band | both rejected with field-level errors | 2 | FR-10 |
| TC-U-24 | Precedence: override → profile → default | override for humidity only | humidity `source = override`, others `profile` | 1 | FR-10 |
| TC-U-25 | Phase + local-day bucketing | photoperiod 12 h from 07:00 in `Asia/Ho_Chi_Minh` (UTC+7) | 07:00 local = Day, 19:30 local = Night; a day summary uses local midnight boundaries, not UTC midnight | 1 | FR-10, NFR-10 |
| TC-U-26 | Snapshot on change; open alert keeps its band | change target while an alert is open | new `ThresholdSnapshot` row; alert `BandMin/Max` unchanged; next sample evaluated against the new band | 1 | FR-10, FR-12 |

### B4. Derived signals and device health

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-27 | `DeviceSilent` lifecycle | last sample 4 min ago, interval 60 s → Warning; 31 min → Critical; sample arrives → Resolved | Warning, then Critical on the same alert, then auto-resolve | 1 | FR-07 |
| TC-U-28 | `SensorFault` marks the metric unavailable | `events` payload `sensor_fault{metric: HumidityPct}` | humidity card state `Unavailable`; no humidity alerts raised while faulted | 1 | FR-07 |
| TC-U-29 | Clock skew detection and phase fallback | `RecordedAt` 10 min behind `ReceivedAt` | `ClockSkewSeconds` stored, one Info signal per hour, phase computed from server time, sample flagged | 1 | NFR-10 |
| TC-U-30 | Gradient warning boundary | surface 44.0, air 31.9 (12.1) vs 43.9/32.0 (11.9) | raises at 12.1, silent at 11.9 | 3 | FR-11 |
| TC-U-31 | Light deficit accumulating | 6 h above 1 000 lx with a required 8 h, evaluated at 21:00 local | deficit 2.0 h, one `LightDeficit` signal; no per-sample light alert | 2 | FR-11, FR-14 |

### B5. Auth, tokens and RBAC

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-32 | Password hashing parameters and rehash-on-login | register, then log in with `PasswordIterations` lower than the current target | hash verifies; after login the hash is upgraded and the plaintext is never stored | 1 | FR-01 |
| TC-U-33 | Access token claims and lifetime | issue, then decode | `sub`, `role`, `exp = iat + 15 min`; a token beyond `exp` is rejected | 1 | FR-01 |
| TC-U-34 | Refresh rotation is single-use and families are revoked on reuse | refresh twice with the same token | first succeeds and invalidates; second → `401 token_reused`, whole family revoked, audit entry | 1 | FR-01 |
| TC-U-35 | Login throttling is per-username and generic | 6 failures within 15 min | lock at the 6th, identical error body in every failure, audit entries written | 1 | FR-01 |
| TC-U-36 | RBAC matrix | Owner/Technician/Viewer × 10 actions | exactly the permission matrix in `02-design/06` §3; foreign terrarium → `404`, insufficient role → `403` | 1 | FR-02 |

### B6. Notification policy, retries and content

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-37 | Below minimum severity | severity Warning, user min = Critical | suppressed with reason `below_min_severity` | 1 | FR-13 |
| TC-U-38 | Quiet hours: Warning suppressed, Critical bypasses | 23:30 local, quiet 22:00–06:00 | Warning → `quiet_hours`; Critical → sent with `bypassed_quiet_hours` | 1 | FR-13 |
| TC-U-39 | Silence and maintenance suppress | active silence for humidity; device in Maintenance | both suppressed with their own reasons; the alert row still exists | 1 | FR-12, FR-13 |
| TC-U-40 | Hourly rate limit coalesces into a digest | 11 warnings in one hour | first 10 sent, 11th marked `digest_coalesced`, one digest with counts at hour end | 1 | FR-13 |
| TC-U-41 | Retry schedule | channel always throws | attempts at t, +5 s, +35 s, +155 s; then `Failed` with `LastError`; escalate never blocked | 2 | FR-13 |
| TC-U-42 | Content contract and localisation | critical temperature alert, user language `vi` | title/body contain terrarium, metric, value, unit, band, duration, deep link; Vietnamese text from resources, not stored in DB | 2 | FR-13, NFR-06 |
| TC-U-43 | Escalation timeline stops at acknowledgement | critical alert, ack at 45 min | repeats at 30 min, none after ack at 45 min, none at 60 min | 1 | FR-13 |
| TC-U-44 | Invalid FCM token disables the channel | send returns `UNREGISTERED` | `ChannelsFcm` set false, notification logged, user informed on next app open | 3 | FR-13 |
| TC-U-45 | Digest aggregation correctness | 7 warnings + 1 critical, peak 34.2 | digest states 8 alerts, worst metric and peak value | 3 | FR-13 |

### B7. Summaries, exposure index, coverage

Reference dataset: target 26–32 °C, 40 min at 34.0 °C then 20 min at 33.0 °C, otherwise in range, no gaps.

| Id | Case | Input | Expected | P | FR |
|---|---|---|---|---|---|
| TC-U-46 | Temperature exposure | the reference dataset | `E+ = 1.67 °C·h` (±0.02), `E− = 0` | 1 | FR-14 |
| TC-U-47 | Humidity dry and wet reported separately | 60 min at 25 %RH with target 30–40, and 30 min at 50 %RH | dry 1.0 %-h, wet 0.5 %-h, out-of-range minutes 90 | 1 | FR-14 |
| TC-U-48 | Light hours and deficit | 6 h above 1 000 lx, required 8 h | `LightHours = 6.0`, `LightDeficitHours = 2.0` | 2 | FR-14 |
| TC-U-49 | Coverage and low-confidence flag | 864 expected samples, 600 received | `coverage = 69.4%`, `IsLowConfidence = true`; storage estimate per `02-design/02` §6 is within 20% of the measured value | 1 | FR-07, FR-14, NFR-11 |
| TC-U-50 | Compliance and time-weighted average across a gap | 60 min out of range out of 1 440 min, 6 h gap | `compliance = 95.8%` using minutesWithData (not 1 440); `avg` is time-weighted and unaffected by the gap length | 1 | FR-14 |

```csharp
[Theory]
[InlineData(34.0, 40, 33.0, 20, 1.67)]      // the worked example in 03-implementation/06 §5.1
[InlineData(32.0, 60, 32.0, 60, 0.00)]      // exactly at the bound is not an excursion
[InlineData(36.0, 60, 32.0, 60, 4.00)]
public void GivenOutOfRangeMinutes_ThenExposureMatchesWorkedExample(
    decimal v1, int m1, decimal v2, int m2, decimal expected)
{
    var series = TestSeries.Interpolated(temp1: v1, minutes1: m1, temp2: v2, minutes2: m2);
    ExposureCalculator.Hot(series, targetMax: 32m).Should().BeApproximately(expected, 0.02m);
}
```

---

## C. Backend integration tests (Testcontainers SQL Server + in-process MQTT)

| Id | Case | Setup | Expected | P | FR |
|---|---|---|---|---|---|
| TC-I-01 | Ingest persists sample + readings + updates last seen | 1 valid batch over MQTT | 1 sample, N readings, `LastSeenAt` set, status `online`, `ingest_samples_total` = N | 1 | FR-06 |
| TC-I-02 | Duplicate `(deviceId, seq)` is idempotent | publish the same batch 5× | exactly 1 sample, duplicate counter = 4, second and later responses reported as duplicates (no 5xx) | 1 | FR-06 |
| TC-I-03 | Implausible sample stored but not evaluated | `TempC = 85` | row exists with quality bit 2, zero alerts created | 1 | FR-06 |
| TC-I-04 | Device health + fault events update state | health + `sensor_fault` event | health denorms updated; humidity `Unavailable`; no humidity alert | 1 | FR-07 |
| TC-I-05 | Provisioning end to end | self-register → claim → connect with secret → publish | device bound, status `online`, first sample stored; a second claim of the same code → `404 claim_code_invalid`; a claim for an already-bound terrarium → `409` | 1 | FR-04, FR-05 |
| TC-I-06 | Evaluation through the pipeline | `daily` fixture played back with a fake clock | exactly one alert, correct `TriggeredAt`, correct `PeakValue`; rollups and the daily summary agree with the fixture's hand-computed values | 1 | FR-11, FR-14 |
| TC-I-07 | Alert lifecycle through the API | open → ack (Technician) → resolve (`FalsePositive`) | states and actors stored; a Viewer ack → `403`; a second ack → `409 alert_not_open` | 1 | FR-12, FR-02 |
| TC-I-08 | 30-minute broker outage then back-fill | stop broker, node buffers, restart | every buffered sample stored with quality bit 8, in `RecordedAt` order, zero duplicates, coverage for the window ≥ 98% | 1 | FR-06, FR-07, NFR-03 |
| TC-I-09 | Silence watchdog raises and auto-resolves | stop publishing for 5 min, then resume | `DeviceSilent` Warning then auto-resolve; no metric alerts from missing data | 1 | FR-07 |
| TC-I-10 | Range query bucketing | `daily` + `monthly` fixtures | 1 h → raw; 24 h → 5-min; 30 d → hourly with ≤ 720 points and `bucket` echoed in the response | 1 | FR-09 |
| TC-I-11 | Live push and freshness | subscribe SignalR, ingest a batch | event < 1 s; `readings/latest` p95 ≤ 300 ms over 200 calls on the 30-day dataset; chart payload ≤ 200 KB | 1 | FR-08, NFR-01, NFR-02 |
| TC-I-12 | Notification integration with fakes | WireMock Telegram + fake FCM | one send, correct body/localisation, retries counted, `NotificationLog` rows per attempt, quiet-hours behaviour as in TC-U-38 | 2 | FR-13 |
| TC-I-13 | Security/auth integration | anonymous MQTT publish, wrong secret, revoked device, expired claim code, foreign terrarium read, Viewer mutation, token reuse | each rejected with the documented status; audit rows written; revoked device blocked within 60 s | 1 | FR-01, FR-02, FR-04, FR-05, NFR-04 |
| TC-I-14 | Retention sweep and purge | 91-day-old raw + rollups; snapshot 8 days old; export older than 24 h | raw deleted with count logged, rollups retained, snapshot deleted, export file removed and job `Expired`; a 7-day late back-fill recomputes rollups and the summary | 2 | FR-15, NFR-11 |
| TC-I-15 | Ops endpoints and counters | scrape `/metrics` before and after 1 000 ingests; stop the broker and call `/ready` | counters increase exactly by the expected amounts; `/ready` → 503 with `broker:false` while reads still work; i18n key sets of app and dashboard are identical | 2 | FR-18, NFR-12 |

---

## D. Flutter widget and provider tests (TC-W-01…18)

| Id | Case | Setup | Expected | P | FR |
|---|---|---|---|---|---|
| TC-W-01 | Login success stores tokens and routes home | fake API returns tokens | `AuthProvider.isAuthenticated` true, tokens in secure storage, Home rendered | 1 | FR-01 |
| TC-W-02 | Login failure shows a generic error | fake API → 401 | error text shown, no navigation, password field keeps its value | 1 | FR-01 |
| TC-W-03 | Logout clears state and cache | logged-in provider | tokens removed, `TelemetryProvider` maps emptied, login screen shown | 1 | FR-01 |
| TC-W-04 | Metric card renders value, unit, band, status, timestamp | fixed value 28.6 / InRange / 12 s ago | all five elements present; `Semantics` label = "Temperature 28.6 degrees Celsius, in range" | 1 | FR-08, NFR-06 |
| TC-W-05 | Card dims and warns when stale | last sample 12 min ago (interval 60 s) | staleness chip in warning style, value dimmed, timestamp still shown | 1 | FR-08 |
| TC-W-06 | Layout adapts to width | pump at 360 / 600 / 1 200 dp | 1 / 2 / 4 columns, no overflow errors | 2 | NFR-06 |
| TC-W-07 | `Unavailable` ≠ `NoData` | humidity fault event vs no data at all | distinct labels and icons; no fabricated value in either case | 1 | FR-07, FR-08 |
| TC-W-08 | Live push updates without a manual refresh | fake `LiveClient` emits `readingAdded` | new value rendered after one `pump()`, no `pumpAndSettle` needed | 1 | FR-08 |
| TC-W-09 | History range selection requests the right bucket | tap 30 d | API called with `from/to` for 30 days; bucket label "hourly" shown | 2 | FR-09 |
| TC-W-10 | Chart keeps gaps and caps points | series with a 4 h gap and 900 points | nulls preserved (no interpolation), ≤ 720 points rendered | 1 | FR-09 |
| TC-W-11 | Alerts inbox filters and shows the badge | 3 alerts, mixed severity/state | filter chips work; badge = open count | 1 | FR-12 |
| TC-W-12 | Role gating on alert actions | `Viewer` session | Acknowledge/Resolve absent; for `Technician` present and wired | 1 | FR-02, FR-12 |
| TC-W-13 | Threshold editor validation | `min 40 / max 30` | field error shown, Save disabled, no API call | 1 | FR-10 |
| TC-W-14 | Claim flow: invalid then valid code | fake API `404` then success | error state with guidance, then success state showing the bound terrarium | 1 | FR-04 |
| TC-W-15 | Summary screen always shows coverage | summary with 69% coverage | low-confidence badge visible next to compliance; export button enabled and job status rendered | 2 | FR-14, FR-15 |
| TC-W-16 | Settings: language, quiet hours, severity | change language to `en`, quiet hours, min severity Critical | strings switch, prefs posted to the API, a Warning notification is suppressed in the provider's policy view | 2 | FR-13, NFR-06 |
| TC-W-17 | Camera panel hidden when no camera | no snapshot capability | no empty panel rendered | 3 | FR-17 |
| TC-W-18 | No hard-coded user-facing strings | scan `lib/screens` + `lib/widgets` | every rendered literal comes from `AppLocalizations` | 2 | NFR-06 |

```dart
testWidgets('MetricCard renders value, band, status and its own timestamp', (tester) async {
  await tester.pumpWidget(MaterialApp(
    localizationsDelegates: AppLocalizations.localizationsDelegates,
    home: Scaffold(body: MetricCard(
      metric: MetricCode.tempC, value: 28.6, unit: '°C',
      status: MetricStatus.inRange, capturedAt: DateTime.utc(2026, 9, 21, 8, 15),
      band: const Band(targetMin: 26, targetMax: 32),
      now: DateTime.utc(2026, 9, 21, 8, 15, 12),        // injected, never DateTime.now()
    )),
  ));
  await tester.pump();                                 // no pumpAndSettle: staleness timer runs

  expect(find.text('28.6'), findsOneWidget);
  expect(find.textContaining('26'), findsOneWidget);   // band is always visible
  expect(find.bySemanticsLabel(RegExp('Temperature 28.6')).evaluate(), isNotEmpty);
  expect(find.textContaining('12 s'), findsOneWidget); // its own timestamp
});
```

---

## E. End-to-end and manual scripted tests

| Id | Case | Steps | Expected | P |
|---|---|---|---|---|
| TC-E2E-01 | Cold-start happy path | flash the node → provision → claim in the app → first sample on the dashboard | first live value on screen ≤ 90 s after claim; device `online`; coverage starts at 100% | 1 |
| TC-E2E-02 | 24 h soak with real conditions | run overnight with the terrarium undisturbed | coverage ≥ 99%; ≤ 1 false-positive alert; no watchdog reset; heap stable within 2 KB; measured storage recorded | 1 |
| TC-E2E-03 | Induced excursion alerting | heat the sensor (lamp/hand) past `TargetMax` for > 5 min, then cool it | one Warning push within 90 s of the dwell expiry; escalation to Critical at the critical band; auto-resolve on cooling; recovery notice shown; timings logged | 1 |
| TC-E2E-04 | Sensor accuracy vs reference | compare 10 min of readings against a reference thermometer/hygrometer/lux meter | within ±0.5 °C / ±3 %RH / ±10% lux after offset calibration; numbers recorded in the QA log | 1 |
| TC-E2E-05 | Silent device | unplug the node for 10 min | offline indicator, evaluation paused notice, `DeviceSilent` warning then critical, auto-resolve on restore, back-fill visible in the chart with the correction of the gap | 1 |
| TC-E2E-06 | Release-mode ops check | release APK on a real phone + dashboard + admin page | release build proof captured; `/health`/`/ready`/`/metrics` correct; audit log complete for the session; retention status visible | 2 |

---

## F. Case-count summary

| Suite | Cases | Target |
|---|---|---|
| Firmware native (TC-U-FW-*) | 12 | all green before flashing a release build |
| Backend unit (TC-U-*) | 50 | ≥ 70% line coverage on Domain + Application |
| Backend integration | 15 | all green in CI |
| Widget/provider | 18 | ≥ 70% line coverage on `core` + `state` |
| E2E | 6 | executed per milestone, results recorded |
| **Total** | **101** | |
