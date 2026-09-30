/// The twelve demo scenarios the Rule Lab plays back, each one a claim from the documentation turned into data a
/// reviewer can watch.
///
/// Most of them are transcriptions of worked examples in `03-implementation/06` §3 and rule text in
/// `01-product/03` (FR-06/07/11/12/13). Two of them exist for a different reason: they record where the documents
/// **disagree with themselves**, which is the most useful thing a prototype can produce before code is written.
library;

import 'domain.dart';
import 'events.dart';
import 'notification_policy.dart';
import 'silence.dart';
import 'species_profiles.dart';
import 'threshold_engine.dart';

/// One minute of a scenario: only the metrics it sets are produced.
class ScenarioRow {
  /// Creates a row.
  const ScenarioRow({
    required this.minute,
    this.tempC,
    this.humidityPct,
    this.lightLux,
    this.uvIndex,
    this.surfaceTempC,
    this.qualityFlags = 0,
    this.ingestedLateBy,
  });

  /// Minutes after the scenario start.
  final int minute;

  /// Air temperature, when this row sets it.
  final double? tempC;

  /// Relative humidity, when this row sets it.
  final double? humidityPct;

  /// Illuminance, when this row sets it.
  final double? lightLux;

  /// UV index, when this row sets it.
  final double? uvIndex;

  /// Surface temperature, when this row sets it.
  final double? surfaceTempC;

  /// Quality bitmask, e.g. `QualityBits.implausible`.
  final int qualityFlags;

  /// How late the sample reached the server; drives the back-fill rules (BR-11.8).
  final Duration? ingestedLateBy;
}

/// What the documentation says should happen. The prototype asserts against this rather than against its own
/// behaviour, otherwise it would only prove it is self-consistent.
class ScenarioExpectation {
  /// Creates an expectation.
  const ScenarioExpectation({
    required this.alerts,
    this.triggeredAtMinute,
    this.resolvedAtMinute,
    this.peak,
    this.escalated = false,
    this.notificationsSent,
    this.notificationsSuppressed,
    this.samplesSkipped,
    this.docClaim,
  });

  /// Number of alert rows the run must produce.
  final int alerts;

  /// Minutes after start at which the alert must be timed from (the back-dated `TriggeredAt`).
  final int? triggeredAtMinute;

  /// Minutes after start at which the alert must resolve.
  final int? resolvedAtMinute;

  /// Expected peak value.
  final double? peak;

  /// Whether the alert must reach Critical.
  final bool escalated;

  /// Notifications that must be sent.
  final int? notificationsSent;

  /// Notifications that must be suppressed (for any reason).
  final int? notificationsSuppressed;

  /// Readings that must be skipped by evaluation.
  final int? samplesSkipped;

  /// The sentence in the document this expectation comes from, quoted in the Lab.
  final String? docClaim;
}

/// A playback scenario.
class Scenario {
  /// Creates a scenario.
  const Scenario({
    required this.id,
    required this.title,
    required this.docRef,
    required this.question,
    required this.whyItMatters,
    required this.profile,
    required this.start,
    required this.rows,
    required this.expectation,
    this.config = EngineConfig.standard,
    this.preferences = const NotificationPreferences(),
    this.deviceInMaintenance = false,
    this.silence,
    this.overrides = const [],
    this.samplingInterval = const Duration(seconds: 60),
    this.silenceWatchUntilMinute,
    this.knownDocIssue,
  });

  /// Short id shown on the scenario chip, e.g. `B`.
  final String id;

  /// One-line title.
  final String title;

  /// Document reference, e.g. `03-implementation/06 §3 Example B`.
  final String docRef;

  /// The question this scenario answers.
  final String question;

  /// Why a keeper cares about the answer.
  final String whyItMatters;

  /// Profile the terrarium is assigned to.
  final SpeciesProfile profile;

  /// Wall-clock (local) start instant.
  final DateTime start;

  /// The data.
  final List<ScenarioRow> rows;

  /// What must happen.
  final ScenarioExpectation expectation;

  /// Engine parameters; scenarios that test the rate limit or the recovery mode override them.
  final EngineConfig config;

  /// Notification preferences in force.
  final NotificationPreferences preferences;

  /// Whether the device is in maintenance mode (recorded, never notified).
  final bool deviceInMaintenance;

  /// Silence window in force, if any.
  final SilenceWindow? silence;

  /// Per-terrarium threshold overrides.
  final List<ThresholdOverride> overrides;

  /// Sampling interval.
  final Duration samplingInterval;

  /// Minutes after start at which the silence watchdog stops observing; defaults to the last sample.
  final int? silenceWatchUntilMinute;

  /// Where the documentation and its own worked example disagree, when they do.
  final String? knownDocIssue;

  /// Builds the samples this scenario produces.
  List<MetricSample> samples() {
    final samples = <MetricSample>[];
    for (final row in rows) {
      final at = start.add(Duration(minutes: row.minute));
      final ingestedAt = at.add(
        row.ingestedLateBy ?? const Duration(seconds: 4),
      );

      void add(String metric, double? value) {
        if (value == null) {
          return;
        }
        samples.add(
          MetricSample(
            metric: metric,
            value: value,
            recordedAt: at,
            ingestedAt: ingestedAt,
            qualityFlags: row.qualityFlags,
          ),
        );
      }

      add('tempC', row.tempC);
      add('humidityPct', row.humidityPct);
      add('lightLux', row.lightLux);
      add('uvIndex', row.uvIndex);
      add('surfaceTempC', row.surfaceTempC);
    }
    return samples;
  }

  /// Resolver used by the run: the profile plus this scenario's overrides.
  ThresholdResolver resolver() =>
      ThresholdResolver(profile: profile, overrides: overrides);

  /// The instant the watchdog stops observing.
  DateTime? watchUntil() => silenceWatchUntilMinute == null
      ? null
      : start.add(Duration(minutes: silenceWatchUntilMinute!));
}

/// The outcome of playing one scenario back.
class ScenarioCheck {
  /// Creates a check.
  const ScenarioCheck({
    required this.scenario,
    required this.result,
    required this.failures,
  });

  /// The scenario played.
  final Scenario scenario;

  /// What the engine produced.
  final EngineResult result;

  /// Expectation mismatches, in the keeper's language.
  final List<String> failures;

  /// True when the engine did exactly what the documentation says.
  bool get matchesExpectation => failures.isEmpty;

  /// The first alert, when one was produced.
  AlertEpisode? get firstAlert =>
      result.alerts.isEmpty ? null : result.alerts.first;

  /// Minutes from the scenario start to the first alert's `TriggeredAt`.
  int? get triggeredAtMinute =>
      firstAlert?.triggeredAt.difference(scenario.start).inMinutes;

  /// Minutes from the scenario start to the first alert's resolution.
  int? get resolvedAtMinute =>
      firstAlert?.resolvedAt?.difference(scenario.start).inMinutes;

  /// A one-line summary for the Lab header.
  String get headline {
    if (result.alerts.isEmpty) {
      return '${result.counters.samplesEvaluated} readings evaluated · no alert';
    }
    final alert = firstAlert!;
    final span = alert.resolvedAt == null
        ? 'still open'
        : 'resolved after ${alert.resolvedAt!.difference(alert.triggeredAt).inMinutes} min';
    return '${result.alerts.length} alert row(s) · ${alert.severity.label} · $span';
  }
}

/// Plays scenarios back through the engine and diffs the result against the documented expectation.
class ScenarioRunner {
  const ScenarioRunner._();

  /// Runs one scenario, optionally with different engine parameters (which is how the Lab's toggles work).
  static ScenarioCheck run(Scenario scenario, {EngineConfig? config}) {
    final effective = config ?? scenario.config;
    final engine = ThresholdEngine(config: effective);

    final result = engine.run(
      samples: scenario.samples(),
      resolver: scenario.resolver(),
      timeline: scenario.profile.phaseTimeline,
      terrariumName: 'Linh\'s gecko box',
      samplingInterval: scenario.samplingInterval,
      silenceWatchUntil: scenario.watchUntil(),
      deviceInMaintenance: scenario.deviceInMaintenance,
      silence: scenario.silence,
      preferences: scenario.preferences,
      includeSurfaceMetric: scenario.profile.surfaceProbeFitted,
    );

    return ScenarioCheck(
      scenario: scenario,
      result: result,
      failures: _diff(scenario, result),
    );
  }

  static List<String> _diff(Scenario scenario, EngineResult result) {
    final expected = scenario.expectation;
    final failures = <String>[];

    if (result.alerts.length != expected.alerts) {
      failures.add(
        'expected ${expected.alerts} alert row(s), got ${result.alerts.length}',
      );
    }

    final alert = result.alerts.isEmpty ? null : result.alerts.first;
    final triggeredAtMinute = alert?.triggeredAt
        .difference(scenario.start)
        .inMinutes;
    if (expected.triggeredAtMinute != null &&
        triggeredAtMinute != expected.triggeredAtMinute) {
      failures.add(
        'expected TriggeredAt at minute ${expected.triggeredAtMinute}, '
        'got $triggeredAtMinute',
      );
    }

    final resolvedAt = alert?.resolvedAt;
    final resolvedAtMinute = resolvedAt?.difference(scenario.start).inMinutes;
    if (expected.resolvedAtMinute != resolvedAtMinute) {
      failures.add(
        'expected resolution at minute ${expected.resolvedAtMinute ?? 'never'}, '
        'got ${resolvedAtMinute ?? 'never'}',
      );
    }

    if (expected.peak != null && alert?.peakValue != expected.peak) {
      failures.add('expected peak ${expected.peak}, got ${alert?.peakValue}');
    }

    final escalated = alert?.severity == Severity.critical;
    if (escalated != expected.escalated) {
      failures.add('expected escalated=${expected.escalated}, got $escalated');
    }

    if (expected.notificationsSent != null &&
        result.counters.notificationsSent != expected.notificationsSent) {
      failures.add(
        'expected ${expected.notificationsSent} notification(s) sent, '
        'got ${result.counters.notificationsSent}',
      );
    }

    if (expected.notificationsSuppressed != null &&
        result.counters.notificationsSuppressed !=
            expected.notificationsSuppressed) {
      failures.add(
        'expected ${expected.notificationsSuppressed} suppressed, '
        'got ${result.counters.notificationsSuppressed}',
      );
    }

    if (expected.samplesSkipped != null &&
        result.counters.samplesSkipped != expected.samplesSkipped) {
      failures.add(
        'expected ${expected.samplesSkipped} reading(s) skipped, '
        'got ${result.counters.samplesSkipped}',
      );
    }

    return failures;
  }
}

/// The scenario library.
class Scenarios {
  const Scenarios._();

  static final _day = DateTime(2026, 9, 21);

  /// A — a four-minute spike must produce nothing (`03-implementation/06` §3 Example A).
  static final belowDwellSpike = Scenario(
    id: 'A',
    title: 'A 4-minute spike produces no alert',
    docRef: '03-implementation/06 §3 Example A · BR-11.3',
    question: 'Does opening the lid for four minutes wake anyone up?',
    whyItMatters:
        'This is the most valuable behaviour in the system for a keeper. Without dwell, a monitoring app that '
        'interrupts on every spike gets muted — and a muted app protects nothing.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 31.2),
      ScenarioRow(minute: 1, tempC: 32.4),
      ScenarioRow(minute: 2, tempC: 32.9),
      ScenarioRow(minute: 3, tempC: 32.1),
      ScenarioRow(minute: 4, tempC: 31.0),
      ScenarioRow(minute: 5, tempC: 30.8),
    ],
    expectation: const ScenarioExpectation(
      alerts: 0,
      notificationsSent: 0,
      samplesSkipped: 0,
      docClaim:
          'Sustained time never reaches 5 minutes → no alert (§3 Example A).',
    ),
  );

  /// B — a real excursion opens exactly one alert, escalates once, resolves once (§3 Example B).
  static final realExcursion = Scenario(
    id: 'B',
    title: 'One excursion → one alert, escalated, then resolved',
    docRef: '03-implementation/06 §3 Example B · BR-11.3, BR-11.5, BR-11.6, BR-11.4',
    question: 'Does a sustained excursion create one row with a back-dated start, or sixty rows?',
    whyItMatters:
        'One incident must be one row. If `TriggeredAt` were the moment the dwell timer expired, every alert would '
        'under-report how long the animal was actually out of range — the single number a keeper acts on.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 31.8),
      ScenarioRow(minute: 1, tempC: 32.3),
      ScenarioRow(minute: 2, tempC: 32.4),
      ScenarioRow(minute: 3, tempC: 32.6),
      ScenarioRow(minute: 4, tempC: 32.5),
      ScenarioRow(minute: 5, tempC: 33.0),
      ScenarioRow(minute: 6, tempC: 33.4),
      ScenarioRow(minute: 7, tempC: 33.5),
      ScenarioRow(minute: 8, tempC: 33.6),
      ScenarioRow(minute: 9, tempC: 33.7),
      ScenarioRow(minute: 10, tempC: 33.8),
      ScenarioRow(minute: 11, tempC: 33.9),
      ScenarioRow(minute: 12, tempC: 34.0),
      ScenarioRow(minute: 13, tempC: 34.1),
      ScenarioRow(minute: 14, tempC: 34.2),
      ScenarioRow(minute: 15, tempC: 34.3),
      ScenarioRow(minute: 16, tempC: 34.4),
      ScenarioRow(minute: 17, tempC: 34.5),
      ScenarioRow(minute: 18, tempC: 34.6),
      ScenarioRow(minute: 19, tempC: 34.7),
      ScenarioRow(minute: 20, tempC: 34.8),
      ScenarioRow(minute: 21, tempC: 34.9),
      ScenarioRow(minute: 22, tempC: 34.9),
      ScenarioRow(minute: 23, tempC: 35.0),
      ScenarioRow(minute: 24, tempC: 34.9),
      ScenarioRow(minute: 25, tempC: 34.9),
      ScenarioRow(minute: 26, tempC: 34.9),
      ScenarioRow(minute: 27, tempC: 34.9),
      ScenarioRow(minute: 28, tempC: 34.9),
      ScenarioRow(minute: 29, tempC: 34.9),
      ScenarioRow(minute: 30, tempC: 34.9),
      ScenarioRow(minute: 31, tempC: 34.8),
      ScenarioRow(minute: 32, tempC: 34.7),
      ScenarioRow(minute: 33, tempC: 34.6),
      ScenarioRow(minute: 34, tempC: 34.5),
      ScenarioRow(minute: 35, tempC: 34.4),
      ScenarioRow(minute: 36, tempC: 34.3),
      ScenarioRow(minute: 37, tempC: 34.2),
      ScenarioRow(minute: 38, tempC: 34.1),
      ScenarioRow(minute: 39, tempC: 34.0),
      ScenarioRow(minute: 40, tempC: 33.9),
      ScenarioRow(minute: 41, tempC: 33.9),
      ScenarioRow(minute: 42, tempC: 33.0),
      ScenarioRow(minute: 43, tempC: 32.8),
      ScenarioRow(minute: 44, tempC: 33.2),
      ScenarioRow(minute: 45, tempC: 32.6),
      ScenarioRow(minute: 46, tempC: 33.1),
      ScenarioRow(minute: 47, tempC: 32.9),
      ScenarioRow(minute: 48, tempC: 32.7),
      ScenarioRow(minute: 49, tempC: 33.0),
      ScenarioRow(minute: 50, tempC: 32.4),
      ScenarioRow(minute: 51, tempC: 32.2),
      ScenarioRow(minute: 52, tempC: 31.5),
      ScenarioRow(minute: 53, tempC: 31.4),
      ScenarioRow(minute: 54, tempC: 31.6),
      ScenarioRow(minute: 55, tempC: 31.4),
      ScenarioRow(minute: 56, tempC: 31.3),
      ScenarioRow(minute: 57, tempC: 31.2),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 57,
      peak: 35.0,
      escalated: true,
      notificationsSent: 3,
      notificationsSuppressed: 0,
      docClaim:
          'One alert row, one warning push, one critical push, one recovery notice. TriggeredAt 14:01, '
          'ResolvedAt 14:54, PeakValue 35.0 (§3 Example B).',
    ),
    knownDocIssue:
        'The worked example resolves at 14:54 because it counts a 31.6 °C reading as a recovery minute. '
        '31.6 °C is inside the 26–32 °C target band but only 0.4 °C inside it, which is *less* than the 0.5 °C '
        'recovery margin §4.2 requires. With the documented margin the alert resolves at 14:57. '
        'Switch the Lab\'s recovery mode to "plain in band" and the trace reproduces 14:54 exactly — so the '
        'pseudocode and the worked example contradict each other, and one of them should change.',
  );

  /// C — the night band prevents a false alarm (§3 Example C).
  static final nightPhase = Scenario(
    id: 'C',
    title:
        '24.5 °C at night is fine — against the day band it would be an alert',
    docRef: '03-implementation/06 §3 Example C · BR-11.2, FR-10',
    question: 'Does the phase come from the photoperiod rather than from the light sensor?',
    whyItMatters:
        'A natural night drop is physiology, not a fault. And the phase must not be derived from measured lux, '
        'because a broken light sensor would then silently switch the temperature band.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 20, minutes: 30)),
    rows: [
      for (var minute = 0; minute < 20; minute++)
        ScenarioRow(minute: minute, tempC: 24.5),
    ],
    expectation: const ScenarioExpectation(
      alerts: 0,
      notificationsSent: 0,
      docClaim:
          'At 20:30 the terrarium reads 24.5 °C. Against the day band this would be below target, but the night '
          'band makes it in range (§3 Example C).',
    ),
  );

  /// D — device silence is not a habitat alert (§3 Example D).
  static final deviceSilent = Scenario(
    id: 'D',
    title: 'A silent device raises a silence alert, never a habitat alert',
    docRef: '03-implementation/06 §3 Example D · BR-07.2',
    question: 'What does the system do when the data simply stops?',
    whyItMatters:
        'Silence must never look like safety, and it must never be reported as a habitat fault. "We do not know" '
        'and "the temperature is wrong" need different actions from the keeper.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 30.0),
      ScenarioRow(minute: 70, tempC: 30.0),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 3,
      resolvedAtMinute: 70,
      escalated: true,
      notificationsSent: 3,
      docClaim:
          'At 14:04 the watchdog sees 3 × 60 s without data → DeviceSilent Warning, no metric alerts. At 14:30 it '
          'escalates to Critical. On reconnect the alert auto-resolves (§3 Example D).',
    ),
    knownDocIssue:
        'The watchdog fires on a 1-minute tick, so the Warning is evaluated at 14:04 and its `TriggeredAt` is '
        'back-dated to 14:03 — the exact instant "3 × 60 s" elapsed. The document states the observation time; the '
        'prototype states both.',
  );

  /// E — hysteresis: inside the band is not enough.
  static final hysteresis = Scenario(
    id: 'E',
    title: 'Inside the band is not enough to resolve — the margin is',
    docRef: '03-implementation/06 §4.2 · BR-11.4',
    question: 'A value hovering just inside the target band: does the alert close, or does it stay open?',
    whyItMatters:
        'Without a margin the alert opens and closes all afternoon — the classic flapping failure that teaches '
        'keepers to ignore alerts entirely.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 31.0),
      ScenarioRow(minute: 1, tempC: 32.3),
      ScenarioRow(minute: 2, tempC: 32.4),
      ScenarioRow(minute: 3, tempC: 32.5),
      ScenarioRow(minute: 4, tempC: 32.6),
      ScenarioRow(minute: 5, tempC: 32.5),
      ScenarioRow(minute: 6, tempC: 33.0),
      ScenarioRow(minute: 7, tempC: 31.6),
      ScenarioRow(minute: 8, tempC: 32.1),
      ScenarioRow(minute: 9, tempC: 31.6),
      ScenarioRow(minute: 10, tempC: 31.4),
      ScenarioRow(minute: 11, tempC: 31.3),
      ScenarioRow(minute: 12, tempC: 31.2),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 12,
      escalated: false,
      notificationsSent: 2,
      docClaim:
          'Recovery requires the value back inside the band by `recoveryMargin` for 3 consecutive minutes '
          '(BR-11.4).',
    ),
  );

  /// F — a late back-fill is recorded but never notifies.
  static final lateBackfill = Scenario(
    id: 'F',
    title: 'A 12-hour-late back-fill is recorded but wakes nobody',
    docRef: 'BR-11.8, BR-06.6 · rule V-08',
    question: 'What happens when a device reconnects after a day offline and dumps its buffer?',
    whyItMatters:
        'Without this rule a device returning after a day away would fire a burst of stale alarms for conditions '
        'that ended hours ago. The history must still be complete — the report depends on it.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 8)),
    rows: [
      for (final row in const [
        ScenarioRow(minute: 0, tempC: 31.0),
        ScenarioRow(minute: 1, tempC: 33.0),
        ScenarioRow(minute: 2, tempC: 33.2),
        ScenarioRow(minute: 3, tempC: 33.4),
        ScenarioRow(minute: 4, tempC: 33.6),
        ScenarioRow(minute: 5, tempC: 33.8),
        ScenarioRow(minute: 6, tempC: 34.0),
        ScenarioRow(minute: 7, tempC: 34.2),
        ScenarioRow(minute: 8, tempC: 34.4),
        ScenarioRow(minute: 9, tempC: 34.6),
        ScenarioRow(minute: 10, tempC: 34.8),
        ScenarioRow(minute: 11, tempC: 35.0),
        ScenarioRow(minute: 12, tempC: 33.0),
        ScenarioRow(minute: 13, tempC: 32.5),
        ScenarioRow(minute: 14, tempC: 32.2),
        ScenarioRow(minute: 15, tempC: 31.8),
        ScenarioRow(minute: 16, tempC: 31.4),
        ScenarioRow(minute: 17, tempC: 31.3),
        ScenarioRow(minute: 18, tempC: 31.2),
      ])
        ScenarioRow(
          minute: row.minute,
          tempC: row.tempC,
          ingestedLateBy: const Duration(hours: 12),
        ),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 18,
      peak: 35.0,
      escalated: true,
      notificationsSent: 0,
      notificationsSuppressed: 0,
      docClaim:
          'A back-fill batch MUST NOT trigger retroactive notifications for alerts that would have fired more than '
          '6 h ago (they are still recorded) — BR-06.6/BR-11.8.',
    ),
  );

  /// G — a silence window records everything and sends nothing.
  static final silencedMetric = Scenario(
    id: 'G',
    title: 'A silence window suppresses notifications, never the record',
    docRef: 'BR-12.6, BR-11.7 · 02-design/05 §4',
    question: 'If the keeper silences temperature for an hour, does the alert disappear?',
    whyItMatters:
        'Alerts are recorded even while silenced — silencing suppresses notifications only (BR-11.7). A silence '
        'that deleted evidence would make the history untrustworthy, which is the whole point of having one.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: realExcursion.rows,
    silence: SilenceWindow(
      metric: 'tempC',
      from: _day.add(const Duration(hours: 13)),
      until: _day.add(const Duration(hours: 17)),
      reason: 'Lamp being replaced',
      createdBy: 'Linh',
    ),
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 57,
      peak: 35.0,
      escalated: true,
      notificationsSent: 0,
      notificationsSuppressed: 3,
      docClaim: 'Alerts are recorded even while silenced; silencing suppresses notifications only (BR-11.7).',
    ),
  );

  /// H — quiet hours suppress a Warning and let a Critical through.
  static final quietHours = Scenario(
    id: 'H',
    title: 'Quiet hours suppress a Warning but never a Critical',
    docRef: 'BR-13.2 · 02-design/05 §1 and §4',
    question: 'Does a 23:30 excursion wake the keeper up, and should it?',
    whyItMatters:
        'The 22:00–06:00 default exists so the app is not muted entirely. Critical bypasses it, which is only '
        'defensible because the critical band is genuinely dangerous rather than merely off-target.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 23, minutes: 30)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 26.0),
      ScenarioRow(minute: 1, tempC: 27.4),
      ScenarioRow(minute: 2, tempC: 27.6),
      ScenarioRow(minute: 3, tempC: 27.8),
      ScenarioRow(minute: 4, tempC: 28.0),
      ScenarioRow(minute: 5, tempC: 28.1),
      ScenarioRow(minute: 6, tempC: 28.2),
      ScenarioRow(minute: 7, tempC: 28.3),
      ScenarioRow(minute: 8, tempC: 28.4),
      ScenarioRow(minute: 9, tempC: 28.5),
      ScenarioRow(minute: 10, tempC: 28.6),
      ScenarioRow(
        minute: 11,
        tempC: 28.7,
      ), // 23:41 — 10 min sustained → Warning, suppressed
      ScenarioRow(minute: 12, tempC: 28.8),
      ScenarioRow(minute: 13, tempC: 28.9),
      ScenarioRow(minute: 14, tempC: 29.0),
      ScenarioRow(
        minute: 15,
        tempC: 31.6,
      ), // 23:45 — leaves the critical band (> 31 °C)
      ScenarioRow(minute: 16, tempC: 31.8),
      ScenarioRow(minute: 17, tempC: 32.0),
      ScenarioRow(
        minute: 18,
        tempC: 32.2,
      ), // 23:48 — 3 min critical → escalate, bypasses quiet hours
      ScenarioRow(minute: 19, tempC: 32.0),
      ScenarioRow(minute: 20, tempC: 31.5),
      ScenarioRow(
        minute: 21,
        tempC: 26.8,
      ), // inside the band, but not by the 0.5 °C margin
      ScenarioRow(minute: 22, tempC: 26.4),
      ScenarioRow(minute: 23, tempC: 26.3),
      ScenarioRow(
        minute: 24,
        tempC: 26.2,
      ), // 23:54 — resolves; the recovery notice is suppressed
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 24,
      escalated: true,
      notificationsSent: 1,
      notificationsSuppressed: 2,
      docClaim: 'Critical bypasses quiet hours and the event is logged as bypassed_quiet_hours (BR-13.2).',
    ),
    knownDocIssue:
        'At 23:30 the terrarium is in the **night** phase, so the night band (22–27 °C, critical 18–31 °C, dwell '
        '10/3) is in force — not the day band. A quiet-hours test built on daytime numbers is testing the phase rule '
        'by accident and the notification rule not at all.',
  );

  /// I — the hourly cap turns the third notification into a digest entry.
  static final rateLimited = Scenario(
    id: 'I',
    title: 'The hourly cap turns the extra notifications into a digest',
    docRef: 'BR-13.3 · 02-design/05 §5',
    question: 'What happens on the eleventh notification in an hour?',
    whyItMatters:
        'The alerts are still created and still visible in the inbox; only the interruptions stop. That distinction '
        'is what lets a keeper keep notifications switched on.',
    profile: SpeciesProfiles.semiArid,
    config: EngineConfig(rateCapPerHour: 2),
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 31.0),
      ScenarioRow(minute: 1, tempC: 33.0),
      ScenarioRow(minute: 2, tempC: 33.2),
      ScenarioRow(minute: 3, tempC: 33.4),
      ScenarioRow(minute: 4, tempC: 33.6),
      ScenarioRow(minute: 5, tempC: 33.8),
      ScenarioRow(minute: 6, tempC: 34.0),
      ScenarioRow(minute: 7, tempC: 34.6),
      ScenarioRow(minute: 8, tempC: 34.8),
      ScenarioRow(minute: 9, tempC: 35.0),
      ScenarioRow(minute: 10, tempC: 33.5),
      ScenarioRow(minute: 11, tempC: 33.0),
      ScenarioRow(minute: 12, tempC: 32.5),
      ScenarioRow(minute: 13, tempC: 32.0),
      ScenarioRow(minute: 14, tempC: 31.4),
      ScenarioRow(minute: 15, tempC: 31.3),
      ScenarioRow(minute: 16, tempC: 31.2),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 16,
      escalated: true,
      notificationsSent: 2,
      notificationsSuppressed: 1,
      docClaim:
          'Rate limit: ≤ 10 notifications per terrarium per hour; beyond that, notifications are coalesced into one '
          'digest and the event is logged (BR-13.3).',
    ),
  );

  /// J — maintenance mode records everything and notifies nobody.
  static final maintenanceMode = Scenario(
    id: 'J',
    title: 'Maintenance mode records everything and notifies nobody',
    docRef: '02-design/05 §5 · BR-12.6',
    question:
        'How does a keeper clean the enclosure without lying about the data?',
    whyItMatters:
        'Emptying the enclosure legitimately drops the temperature. Without a maintenance mode the keeper has two '
        'bad options: ignore the alerts, or stop the monitoring.',
    profile: SpeciesProfiles.semiArid,
    deviceInMaintenance: true,
    start: _day.add(const Duration(hours: 14)),
    rows: rateLimited.rows,
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: 16,
      escalated: true,
      notificationsSent: 0,
      notificationsSuppressed: 3,
      docClaim: 'Maintenance mode is device-level; it notifies nobody but records everything (02-design/05 §5).',
    ),
  );

  /// K — an implausible reading is stored, flagged, and never evaluated.
  static final implausibleReading = Scenario(
    id: 'K',
    title: 'A broken sensor reading is stored but never alerted on',
    docRef: 'BR-06.4, BR-11.1, BR-07.1 · rule V-06',
    question:
        'What stops a sensor fault from being reported as a habitat emergency?',
    whyItMatters:
        'The system has to distinguish "the animal is fine" from "the sensor is lying". A humidity probe reading '
        '200 %RH is evidence for a calibration review, not a reason to wake anyone up.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: const [
      ScenarioRow(minute: 0, tempC: 29.0),
      // Flagged by the ingest pipeline because it is outside −10…60 °C.
      ScenarioRow(
        minute: 1,
        tempC: 85.0,
        qualityFlags: QualityBits.implausible,
      ),
      // Not flagged: the evaluator applies the metric dictionary itself, so a new firmware cannot smuggle one in.
      ScenarioRow(minute: 2, tempC: 90.0),
      ScenarioRow(minute: 3, tempC: 29.5),
      ScenarioRow(minute: 4, tempC: 29.0),
    ],
    expectation: const ScenarioExpectation(
      alerts: 0,
      notificationsSent: 0,
      samplesSkipped: 2,
      docClaim:
          'Given a sample with TempC = 85, when it is ingested, then it is persisted with quality bit 2 set and it '
          'does not produce an alert (FR-06 acceptance criteria).',
    ),
  );

  /// L — a flapping excursion stays one alert.
  static final dedupe = Scenario(
    id: 'L',
    title: 'Twenty flapping excursions, one alert row',
    docRef: 'BR-11.5 · §4.2 note 2',
    question: 'A value that crosses the band twenty times in forty minutes: twenty alerts?',
    whyItMatters:
        'Dedupe is what makes the alert list readable six months later. Without it the history is a wall of rows '
        'and the keeper can no longer see the one incident that mattered.',
    profile: SpeciesProfiles.semiArid,
    start: _day.add(const Duration(hours: 14)),
    rows: [
      const ScenarioRow(minute: 0, tempC: 31.0),
      for (var minute = 1; minute <= 40; minute++)
        ScenarioRow(minute: minute, tempC: minute.isOdd ? 32.4 : 31.8),
    ],
    expectation: const ScenarioExpectation(
      alerts: 1,
      triggeredAtMinute: 1,
      resolvedAtMinute: null,
      peak: 32.4,
      escalated: false,
      notificationsSent: 1,
      notificationsSuppressed: 0,
      docClaim:
          'Dedupe: at most one open alert per {terrariumId, metric, severity, phase}. Repeated excursions extend '
          'the same alert (BR-11.5).',
    ),
  );

  /// Every scenario, in the order the Lab lists them.
  static final all = <Scenario>[
    belowDwellSpike,
    realExcursion,
    nightPhase,
    deviceSilent,
    hysteresis,
    lateBackfill,
    silencedMetric,
    quietHours,
    rateLimited,
    maintenanceMode,
    implausibleReading,
    dedupe,
  ];

  /// Looks up a scenario by id.
  static Scenario? byId(String id) {
    for (final scenario in all) {
      if (scenario.id == id) {
        return scenario;
      }
    }
    return null;
  }
}
