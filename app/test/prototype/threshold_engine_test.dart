import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/events.dart';
import 'package:smart_reptile/prototype/rules/notification_policy.dart';
import 'package:smart_reptile/prototype/rules/silence.dart';
import 'package:smart_reptile/prototype/rules/species_profiles.dart';
import 'package:smart_reptile/prototype/rules/threshold_engine.dart';

/// Unit tests for the rule kernel, written against the rule ids rather than against the implementation, so a failure
/// names the rule that broke rather than a line number.
///
/// The leopard-gecko day band is 26–32 °C with critical 22–34.5 °C, dwell 5/2 min and a 0.5 °C recovery margin, and
/// the photoperiod runs 07:00–19:00 — so a series starting at 14:00 is evaluated in the `Day` phase.
void main() {
  final day = DateTime(2026, 9, 21);
  final start = day.add(const Duration(hours: 14));

  ThresholdResolver resolver({
    SpeciesProfile? profile,
    List<ThresholdOverride> overrides = const [],
  }) => ThresholdResolver(
    profile: profile ?? SpeciesProfiles.semiArid,
    overrides: overrides,
  );

  /// Runs a temperature series, one reading per minute, starting at [from].
  EngineResult runTemps(
    List<double> temps, {
    EngineConfig config = EngineConfig.standard,
    NotificationPreferences preferences = const NotificationPreferences(),
    SilenceWindow? silence,
    bool deviceInMaintenance = false,
    List<ThresholdOverride> overrides = const [],
    SpeciesProfile? profile,
    DateTime? from,
    Duration lateBy = Duration.zero,
  }) {
    final origin = from ?? start;
    final samples = <MetricSample>[
      for (var i = 0; i < temps.length; i++)
        MetricSample(
          metric: 'tempC',
          value: temps[i],
          recordedAt: origin.add(Duration(minutes: i)),
          ingestedAt: origin.add(Duration(minutes: i)).add(lateBy),
        ),
    ];

    return ThresholdEngine(config: config).run(
      samples: samples,
      resolver: resolver(profile: profile, overrides: overrides),
      timeline: (profile ?? SpeciesProfiles.semiArid).phaseTimeline,
      terrariumName: 'Test box',
      preferences: preferences,
      silence: silence,
      deviceInMaintenance: deviceInMaintenance,
    );
  }

  /// A series that opens exactly one Warning at minute 6 and resolves at minute 9.
  const openThenRecover = <double>[
    31.0,
    32.4,
    32.6,
    32.8,
    33.0,
    33.2,
    33.4,
    31.4,
    31.3,
    31.2,
  ];

  group('BR-11.3 dwell is a filter, not a delay', () {
    test('four minutes outside the band produce no alert', () {
      final result = runTemps([31.2, 32.4, 32.9, 32.1, 31.0, 30.8]);

      expect(result.alerts, isEmpty);
      expect(result.counters.warningsOpened, 0);
      // The excursion was still tracked internally; it simply never reached the dwell threshold.
      expect(
        result.steps.where((s) => s.kind == DecisionKind.belowDwell),
        isNotEmpty,
      );
    });

    test(
      'the fifth minute opens exactly one alert, back-dated to the start',
      () {
        final result = runTemps(openThenRecover);

        final alert = result.alerts.single;
        expect(alert.triggeredAt, start.add(const Duration(minutes: 1)));
        expect(alert.severity, Severity.warning);
        // The keeper sees the truth: out of range for 5 minutes, not "for 0 minutes".
        expect(
          result.steps
              .firstWhere((s) => s.kind == DecisionKind.openedWarning)
              .explanation,
          contains('for 5 min'),
        );
      },
    );

    test('a cold excursion is the mirror image of a hot one', () {
      final result = runTemps([26.0, 25.0, 24.5, 24.0, 23.5, 23.0, 22.5]);

      expect(result.alerts, hasLength(1));
      expect(
        result.steps.any(
          (s) => s.explanation.contains('below the target band'),
        ),
        isTrue,
      );
    });
  });

  group('BR-11.4 recovery needs the margin, held long enough', () {
    test('two consecutive recovery minutes do not resolve the alert', () {
      final result = runTemps([...openThenRecover.take(9), 32.5]);

      final alert = result.alerts.single;
      expect(alert.resolvedAt, isNull);
      expect(alert.isOpen, isTrue);
      // Going back outside the band resets the counter silently — the reset step belongs to the in-band branch, so
      // what proves the reset here is the alert still being open after two ticks and a fresh excursion.
      expect(result.steps.last.kind, DecisionKind.touched);
    });

    test(
      'three consecutive recovery minutes resolve it with reason Recovered',
      () {
        final result = runTemps(openThenRecover);

        final alert = result.alerts.single;
        expect(alert.resolvedAt, start.add(const Duration(minutes: 9)));
        expect(alert.resolvedReason, ResolvedReason.recovered);
        expect(alert.state, AlertState.resolved);
        expect(result.counters.resolutions, 1);
      },
    );

    test(
      'an in-band reading that misses the margin is not a recovery minute',
      () {
        // 31.6 °C is inside the 26–32 °C target band, but only 0.4 °C inside it — less than the 0.5 °C margin.
        final result = runTemps([
          ...openThenRecover.take(7),
          31.6,
          31.6,
          31.6,
          31.6,
        ]);

        expect(result.alerts.single.resolvedAt, isNull);
        expect(
          result.steps.where((s) => s.kind == DecisionKind.recoveryReset),
          hasLength(4),
        );
      },
    );
  });

  group('BR-11.5 dedupe and BR-11.6 escalation', () {
    test('a repeated excursion extends the same alert instead of opening a second one', () {
      final result = runTemps([
        ...openThenRecover.take(7),
        31.4,
        32.5,
        32.7,
        33.0,
        31.6,
        32.9,
      ]);

      expect(result.alerts, hasLength(1));
      expect(result.counters.warningsOpened, 1);
      expect(result.counters.resolutions, 0);
    });

    test('crossing the critical band upgrades the open alert, once', () {
      final result = runTemps([
        31.0,
        32.4,
        32.6,
        32.8,
        33.0,
        33.2,
        34.6, // opens a Warning; the critical timer starts here
        34.8,
        35.0, // two minutes of critical → escalate
        34.9,
        34.9, // still critical, but the alert is already Critical
      ]);

      expect(result.alerts, hasLength(1));
      final alert = result.alerts.single;
      expect(alert.severity, Severity.critical);
      expect(alert.peakValue, 35.0);
      expect(result.counters.escalations, 1);
    });

    test('a resolved alert is terminal: the next excursion is a new row', () {
      final result = runTemps([
        ...openThenRecover,
        32.4,
        32.6,
        32.8,
        33.0,
        33.2,
        33.4,
      ]);

      expect(result.alerts, hasLength(2));
      expect(result.alerts.first.state, AlertState.resolved);
      expect(result.alerts.last.isOpen, isTrue);
      expect(result.alerts.first.id, isNot(result.alerts.last.id));
      expect(result.counters.warningsOpened, 2);
    });
  });

  group('BR-11.1 and BR-06.4 flagged readings never influence alerts', () {
    test(
      'an implausible value is skipped even when the device forgot to flag it',
      () {
        final result = runTemps([29.0, 90.0, 29.5]);

        expect(result.alerts, isEmpty);
        expect(result.counters.samplesEvaluated, 2);
        expect(result.counters.samplesSkipped, 1);
        final skipped = result.steps.firstWhere(
          (s) => s.kind == DecisionKind.skippedNotEvaluable,
        );
        expect(skipped.ruleId, 'BR-11.1');
        expect(skipped.explanation, contains('plausible range'));
      },
    );

    test('a sensor fault is skipped and explained as a broken sensor', () {
      final samples = [
        MetricSample(
          metric: 'tempC',
          value: 29.0,
          recordedAt: start,
          ingestedAt: start,
        ),
        MetricSample(
          metric: 'tempC',
          value: 29.0,
          recordedAt: start.add(const Duration(minutes: 1)),
          ingestedAt: start.add(const Duration(minutes: 1)),
          qualityFlags: QualityBits.sensorFault,
        ),
      ];

      final result = ThresholdEngine().run(
        samples: samples,
        resolver: resolver(),
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        terrariumName: 'Test box',
      );

      expect(result.alerts, isEmpty);
      expect(result.counters.samplesSkipped, 1);
      expect(
        result.steps.any((s) => s.explanation.contains('read failure')),
        isTrue,
      );
    });
  });

  group('BR-11.2 phase selection comes from the photoperiod', () {
    test('24.5 °C is in range at night and below target during the day', () {
      final atNight = runTemps(
        List.filled(10, 24.5),
        from: day.add(const Duration(hours: 20, minutes: 30)),
      );
      expect(atNight.alerts, isEmpty);

      final atNoon = runTemps(
        List.filled(7, 24.5),
        from: day.add(const Duration(hours: 12)),
      );
      expect(atNoon.alerts, hasLength(1));
      expect(atNoon.alerts.single.severity, Severity.warning);
    });

    test('the photoperiod window decides the phase', () {
      const timeline = PhaseTimeline(lightsOnHour: 7, photoperiodHours: 12);
      expect(timeline.phaseAt(DateTime(2026, 9, 21, 6, 59)), Phase.night);
      expect(timeline.phaseAt(DateTime(2026, 9, 21, 7, 0)), Phase.day);
      expect(timeline.phaseAt(DateTime(2026, 9, 21, 18, 59)), Phase.day);
      expect(timeline.phaseAt(DateTime(2026, 9, 21, 19, 0)), Phase.night);
    });
  });

  group('BR-07.2 device silence is never safety', () {
    test('a silence alert escalates and auto-resolves on reconnect', () {
      final samples = [
        MetricSample(
          metric: 'tempC',
          value: 30.0,
          recordedAt: start,
          ingestedAt: start,
        ),
        MetricSample(
          metric: 'tempC',
          value: 30.0,
          recordedAt: start.add(const Duration(minutes: 70)),
          ingestedAt: start.add(const Duration(minutes: 70)),
        ),
      ];

      final result = ThresholdEngine().run(
        samples: samples,
        resolver: resolver(),
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        terrariumName: 'Test box',
      );

      expect(result.alerts, hasLength(1));
      final alert = result.alerts.single;
      expect(alert.source, AlertSource.deviceSilent);
      expect(alert.triggeredAt, start.add(const Duration(minutes: 3)));
      expect(alert.severity, Severity.critical);
      expect(alert.resolvedAt, start.add(const Duration(minutes: 70)));
      // No metric alert may be produced from data that does not exist.
      expect(alert.metric, isNull);
    });
  });

  group('BR-11.8 a late back-fill is recorded but never notifies', () {
    test('an excursion ingested 12 hours late produces a row and zero notifications', () {
      final result = runTemps([
        31.0,
        33.0,
        33.2,
        33.4,
        33.6,
        33.8,
        34.0,
        31.4,
        31.3,
        31.2,
      ], lateBy: const Duration(hours: 12));

      expect(result.alerts, hasLength(1));
      expect(result.counters.notificationsSent, 0);
      expect(result.counters.backfilledSamples, 10);
      expect(result.steps.where((s) => s.ruleId == 'BR-11.8'), isNotEmpty);
    });
  });

  group('BR-10.3 threshold resolution', () {
    test('an override wins over the profile band and is labelled as such', () {
      final override = ThresholdOverride(
        terrariumId: 't1',
        band: const ThresholdBand(
          metric: 'tempC',
          phase: Phase.day,
          targetMin: 26,
          targetMax: 30,
          criticalMin: 22,
          criticalMax: 33,
          dwellWarnMinutes: 5,
          dwellCritMinutes: 2,
          recoveryMargin: 0.5,
          source: BandSource.override,
        ),
        editedBy: 'Linh',
        editedAt: day,
      );

      final effective = resolver(overrides: [override]).resolve('tempC', start);

      expect(effective.source, BandSource.override);
      expect(effective.band?.targetMax, 30);
    });

    test('the override changes the engine, not just the label', () {
      final override = ThresholdOverride(
        terrariumId: 't1',
        band: SpeciesProfiles.semiArid
            .bandsFor('tempC')
            .first
            .copyWith(
              targetMax: 30,
              criticalMax: 33,
              source: BandSource.override,
            ),
        editedBy: 'Linh',
        editedAt: day,
      );

      // 32.5 °C is inside the seeded band but outside the override, so the same data must now alert.
      final result = runTemps(
        [31.0, 32.5, 32.6, 32.7, 32.8, 32.9, 33.0],
        overrides: [override],
      );

      expect(result.alerts, hasLength(1));
    });

    test('a metric with an Any-phase row ignores the day/night split', () {
      final night = resolver().resolve(
        'humidityPct',
        day.add(const Duration(hours: 23)),
      );
      final noon = resolver().resolve('humidityPct', start);

      expect(night.band?.phase, Phase.any);
      expect(night.band?.targetMin, noon.band?.targetMin);
    });

    test('a terrarium with no profile falls back to the system default, and says so', () {
      const bare = ThresholdResolver(profile: null);
      final effective = bare.resolve('tempC', start);

      expect(effective.source, BandSource.systemDefault);
      expect(effective.band, isNotNull);
      expect(bare.resolve('uvIndex', start).hasBand, isFalse);
    });

    test('the seeded band counts match the backend seeder: 5 + 6 + 6 + 2 = 19', () {
      expect(SpeciesProfiles.tropical.bands, hasLength(5));
      expect(SpeciesProfiles.semiArid.bands, hasLength(6));
      expect(SpeciesProfiles.arid.bands, hasLength(6));
      expect(SpeciesProfiles.aridCool.bands, hasLength(2));
    });

    test('the three light bands are inert, which is why 19 bands means 16 to verify', () {
      final all = SpeciesProfiles.all
          .expand((profile) => profile.bands)
          .toList();
      final inert = all.where(
        (band) => band.accumulatedOnly && band.metric == 'lightLux',
      );

      expect(inert, hasLength(3));
      expect(all, hasLength(19));
      expect(19 - inert.length, 16);
    });

    test('26 °C at 20:00 is inside the night band and raises nothing', () {
      final result = runTemps(
        List.filled(7, 26.0),
        from: day.add(const Duration(hours: 20)),
      );

      expect(result.alerts, isEmpty);
    });
  });

  group('BR-12.6 / BR-13.x notification suppression', () {
    test('a silence window records the alert and sends nothing', () {
      final result = runTemps(
        openThenRecover,
        silence: SilenceWindow(
          metric: 'tempC',
          from: start.subtract(const Duration(hours: 1)),
          until: start.add(const Duration(hours: 3)),
          reason: 'Lamp swap',
          createdBy: 'Linh',
        ),
      );

      expect(result.alerts, hasLength(1));
      expect(result.counters.notificationsSent, 0);
      expect(result.counters.notificationsSuppressed, 2);
      expect(
        result.steps.where(
          (s) => s.suppressedReason == SuppressedReason.silencedMetric,
        ),
        hasLength(2),
      );
    });

    test('maintenance mode records the alert and notifies nobody', () {
      final result = runTemps(openThenRecover, deviceInMaintenance: true);

      expect(result.alerts, hasLength(1));
      expect(result.counters.notificationsSent, 0);
      expect(
        result.steps.where(
          (s) => s.suppressedReason == SuppressedReason.maintenance,
        ),
        hasLength(2),
      );
    });

    test('a Critical at 23:48 bypasses quiet hours while the 23:41 Warning does not', () {
      // At 23:30 the night band is in force (target 22–27 °C, critical 18–31 °C, dwell 10/3), so the numbers differ
      // from their daytime equivalents on purpose.
      final result = runTemps([
        26.0,
        27.4,
        27.6,
        27.8,
        28.0,
        28.1,
        28.2,
        28.3,
        28.4,
        28.5,
        28.6,
        28.7, // 23:41 — Warning opens inside quiet hours → suppressed
        28.8,
        28.9,
        29.0,
        31.6, // 23:45 — leaves the critical band
        31.8,
        32.0,
        32.2, // 23:48 — Critical sustained → bypasses quiet hours
        32.0,
        31.5,
        26.8, // in band, but not by the margin
        26.4,
        26.3,
        26.2, // 23:54 — resolves; a recovery notice must NOT bypass quiet hours
      ], from: day.add(const Duration(hours: 23, minutes: 30)));

      expect(result.counters.notificationsSent, 1);
      expect(result.counters.notificationsSuppressed, 2);
      expect(
        result.steps.any(
          (s) => s.suppressedReason == SuppressedReason.quietHours,
        ),
        isTrue,
      );
      expect(result.alerts.single.severity, Severity.critical);
    });

    test('the hourly cap turns the next notification into a digest entry', () {
      final result = runTemps([
        31.0,
        32.4,
        32.6,
        32.8,
        33.0,
        33.2,
        34.6,
        34.8,
        35.0,
        34.9,
        31.4,
        31.3,
        31.2,
      ], config: const EngineConfig(rateCapPerHour: 2));

      expect(result.counters.notificationsSent, 2);
      expect(
        result.steps.where(
          (s) => s.suppressedReason == SuppressedReason.digestCoalesced,
        ),
        isNotEmpty,
      );
    });
  });

  group('open questions the prototype deliberately exposes', () {
    test(
      'a phase rollover with an alert open is reported, not silently resolved',
      () {
        // 33 °C for five hours: the excursion opens in the Day phase and is still open when the lights go off at 19:00,
        // where the night band (22–27 °C) is exceeded as well.
        final result = runTemps(List.filled(301, 33.0));

        expect(
          result.steps.where((s) => s.ruleId == 'OPEN-QUESTION'),
          isNotEmpty,
          reason: 'the documents do not define what happens to an alert open across a phase change',
        );
      },
    );
  });
}
