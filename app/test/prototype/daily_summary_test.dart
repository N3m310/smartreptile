import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/rules/daily_summary.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/species_profiles.dart';

/// The summary maths of `03-implementation/06` §5, including the worked exposure example the document states.
void main() {
  final day = DateTime(2026, 9, 21);

  const resolver = ThresholdResolver(profile: SpeciesProfiles.semiArid);
  const builder = DailySummaryBuilder(
    // Zero tolerance so the worked examples count exactly the minutes they say they do.
    gapTolerance: Duration.zero,
  );

  MetricSample sample(String metric, DateTime at, double value) => MetricSample(
    metric: metric,
    value: value,
    recordedAt: at,
    ingestedAt: at.add(const Duration(seconds: 4)),
  );

  group('§5.1 temperature exposure in degree-hours', () {
    test(
      'the documented worked example yields 1.67 °C·h, not just 60 minutes',
      () {
        // Target 26–32 °C; 40 minutes at 34.0 °C and 20 minutes at 33.0 °C.
        final samples = <MetricSample>[
          for (var minute = 0; minute < 40; minute++)
            sample(
              'tempC',
              day.add(Duration(hours: 12, minutes: minute)),
              34.0,
            ),
          for (var minute = 40; minute < 60; minute++)
            sample(
              'tempC',
              day.add(Duration(hours: 12, minutes: minute)),
              33.0,
            ),
        ];

        final summary = builder.build(
          localDate: day,
          samples: samples,
          resolver: resolver,
          timeline: SpeciesProfiles.semiArid.phaseTimeline,
          requiredLightHours: 8,
        );

        final stats = summary.perMetric['tempC']!;
        expect(stats.outOfRangeMinutes, 60);
        expect(stats.hotExposureDegCHours, closeTo(1.67, 0.01));
        expect(stats.coldExposureDegCHours, 0);
        expect(stats.totalExposureDegCHours, closeTo(1.67, 0.01));
        // 60 out-of-range minutes out of 60 minutes with data.
        expect(stats.compliancePct, 0);
      },
    );

    test('two days with equal out-of-range minutes can differ in exposure', () {
      List<MetricSample> minutesAt(double value, int count) => [
        for (var minute = 0; minute < count; minute++)
          sample('tempC', day.add(Duration(hours: 12, minutes: minute)), value),
      ];

      final mild = builder.build(
        localDate: day,
        samples: minutesAt(33.0, 60),
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
      );
      final harsh = builder.build(
        localDate: day,
        samples: minutesAt(38.0, 60),
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
      );

      expect(
        mild.perMetric['tempC']!.outOfRangeMinutes,
        harsh.perMetric['tempC']!.outOfRangeMinutes,
      );
      expect(
        harsh.perMetric['tempC']!.hotExposureDegCHours,
        greaterThan(mild.perMetric['tempC']!.hotExposureDegCHours * 2),
      );
    });

    test('a cold excursion accumulates separately from a hot one', () {
      // Midday, so the *day* band (26–32 °C) is the one in force: 24.0 °C is 2 °C cold for 30 minutes.
      // The first version of this test put the excursion at 02:00, where the night band (22–27 °C) makes 24 °C
      // perfectly normal — so it measured the phase rule rather than the exposure rule.
      final samples = <MetricSample>[
        for (var minute = 0; minute < 30; minute++)
          sample('tempC', day.add(Duration(hours: 12, minutes: minute)), 24.0),
      ];

      final summary = builder.build(
        localDate: day,
        samples: samples,
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
      );

      final stats = summary.perMetric['tempC']!;
      expect(stats.coldExposureDegCHours, closeTo(1.0, 0.01));
      expect(stats.hotExposureDegCHours, 0);
    });

    test('the day band is used at noon and the night band at night', () {
      // 24.5 °C: inside the 22–27 °C night band, below the 26–32 °C day band.
      final atNight = builder.build(
        localDate: day,
        samples: [
          for (var minute = 0; minute < 30; minute++)
            sample(
              'tempC',
              day.add(Duration(hours: 21, minutes: minute)),
              24.5,
            ),
        ],
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
      );

      expect(atNight.perMetric['tempC']!.outOfRangeMinutes, 0);
    });
  });

  group('§5.3 light hours', () {
    test('light hours accumulate only while the lux threshold is met', () {
      final samples = <MetricSample>[
        // Eight hours above the 1 000 lx threshold, then sixteen below it.
        for (var minute = 0; minute < 8 * 60; minute++)
          sample('lightLux', day.add(Duration(minutes: 7 * 60 + minute)), 1500),
        for (var minute = 0; minute < 16 * 60; minute++)
          sample('lightLux', day.add(Duration(minutes: 15 * 60 + minute)), 40),
      ];

      final summary = builder.build(
        localDate: day,
        samples: samples,
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
        lightThresholdLux: SpeciesProfiles.semiArid.lightThresholdLux,
      );

      expect(summary.lightHours, closeTo(8, 0.01));
      expect(summary.lightDeficitHours, closeTo(0, 0.01));
      expect(summary.hasLightDeficit, isFalse);
    });

    test(
      'a short photoperiod produces a deficit rather than a per-sample alert',
      () {
        final samples = <MetricSample>[
          for (var minute = 0; minute < 2 * 60; minute++)
            sample(
              'lightLux',
              day.add(Duration(minutes: 9 * 60 + minute)),
              1500,
            ),
        ];

        final summary = builder.build(
          localDate: day,
          samples: samples,
          resolver: resolver,
          timeline: SpeciesProfiles.semiArid.phaseTimeline,
          requiredLightHours: 8,
          lightThresholdLux: SpeciesProfiles.semiArid.lightThresholdLux,
        );

        expect(summary.lightHours, closeTo(2, 0.01));
        expect(summary.lightDeficitHours, closeTo(6, 0.01));
        expect(summary.hasLightDeficit, isTrue);
      },
    );
  });

  group('BR-09.5 and §5.5 gaps are null, coverage is stated', () {
    test('a gap longer than the tolerance is not interpolated across', () {
      final samples = [
        sample('tempC', day.add(const Duration(hours: 12)), 30.0),
        sample('tempC', day.add(const Duration(hours: 12, minutes: 30)), 30.0),
      ];

      final summary = builder.build(
        localDate: day,
        samples: samples,
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
      );

      // Two minutes of data — the two readings themselves — and no invented values between them.
      expect(summary.perMetric['tempC']!.minutesWithData, 2);
      expect(summary.perMetric['tempC']!.min, 30.0);
      expect(summary.perMetric['tempC']!.max, 30.0);
    });

    test('coverage below 80 % is flagged low-confidence', () {
      final summary = builder.build(
        localDate: day,
        samples: [
          for (var minute = 0; minute < 30; minute++)
            sample('tempC', day.add(Duration(minutes: minute)), 30.0),
        ],
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
        expectedSamples: 60,
      );

      expect(summary.coveragePct, 50);
      expect(summary.isLowConfidence, isTrue);
      expect(summary.expectedSamples, 60);
    });

    test('a fully covered day is not low-confidence', () {
      // Midday again: 30.0 °C is inside the day band, so the compliance number measures coverage, not the phase.
      final summary = builder.build(
        localDate: day,
        samples: [
          for (var minute = 0; minute < 60; minute++)
            sample(
              'tempC',
              day.add(Duration(hours: 12, minutes: minute)),
              30.0,
            ),
        ],
        resolver: resolver,
        timeline: SpeciesProfiles.semiArid.phaseTimeline,
        requiredLightHours: 8,
        expectedSamples: 60,
      );

      expect(summary.coveragePct, 100);
      expect(summary.isLowConfidence, isFalse);
      expect(summary.overallCompliancePct, 100);
    });
  });

  test('the synthetic day generator is deterministic', () {
    List<MetricSample> build() => DailySummaryBuilder.syntheticDay(
      localDay: day,
      metric: 'tempC',
      valueAt: (minute) => 30 + minute % 7,
      intervalMinutes: 30,
    );

    final first = build();
    final second = build();

    expect(first.length, 48);
    expect(
      first.map((s) => s.value).toList(),
      second.map((s) => s.value).toList(),
    );
    // Deterministic by construction: a prototype whose screenshots change every build is not evidence.
    expect(first.first.recordedAt, day);
  });
}
