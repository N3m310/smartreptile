import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/rules/band_validation.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/species_profiles.dart';

/// BR-10.2 and `03-implementation/06` §2, including the per-metric climate check that was clarified on 2026-09-29
/// (a basking surface must not be measured against the zone's **air** ceiling).
void main() {
  const valid = ThresholdBand(
    metric: 'tempC',
    phase: Phase.day,
    targetMin: 26,
    targetMax: 32,
    criticalMin: 22,
    criticalMax: 34.5,
    dwellWarnMinutes: 5,
    dwellCritMinutes: 2,
    recoveryMargin: 0.5,
  );

  group('BR-10.2 blocking validation', () {
    test('a correctly shaped band produces no blocking issue', () {
      final issues = BandValidation.validate(valid);

      expect(BandValidation.canSave(issues), isTrue);
      expect(issues.where((issue) => issue.isBlocking), isEmpty);
    });

    test('the acceptance-criteria example 40 / 30 is rejected', () {
      final issues = BandValidation.validate(
        valid.copyWith(targetMin: 40, targetMax: 30),
      );

      expect(BandValidation.canSave(issues), isFalse);
      expect(
        issues.firstWhere((issue) => issue.isBlocking).message,
        contains('Target minimum must be lower'),
      );
    });

    test('a critical band that does not enclose the target is rejected', () {
      final issues = BandValidation.validate(
        valid.copyWith(criticalMin: 28, criticalMax: 31),
      );

      expect(BandValidation.canSave(issues), isFalse);
      expect(
        issues.any(
          (issue) => issue.message.contains('must enclose the target band'),
        ),
        isTrue,
      );
    });

    test('a critical dwell longer than the warning dwell is rejected', () {
      final issues = BandValidation.validate(
        valid.copyWith(dwellCritMinutes: 9),
      );

      expect(BandValidation.canSave(issues), isFalse);
      expect(issues.any((issue) => issue.ruleId == 'BR-11.3'), isTrue);
    });

    test('a recovery margin larger than half the band is rejected', () {
      final issues = BandValidation.validate(
        valid.copyWith(recoveryMargin: 3.5),
      );

      expect(BandValidation.canSave(issues), isFalse);
      expect(issues.any((issue) => issue.ruleId == 'BR-11.4'), isTrue);
    });
  });

  group('§2 warnings that must never block a save', () {
    test('a warmer night than day is a warning, not an error', () {
      final issues = BandValidation.validate(
        valid.copyWith(
          phase: Phase.night,
          targetMin: 33,
          targetMax: 36,
          criticalMax: 40,
        ),
        siblings: [valid],
      );

      expect(BandValidation.canSave(issues), isTrue);
      expect(
        issues
            .singleWhere((issue) => issue.severity == IssueSeverity.warning)
            .message,
        contains('usually a typing mistake'),
      );
    });
  });

  group('BR-10.5 climate-zone plausibility is non-blocking', () {
    test(
      'an arid profile with a target maximum of 19 °C warns and still saves',
      () {
        final issues = BandValidation.validate(
          valid.copyWith(
            targetMin: 14,
            targetMax: 19,
            criticalMin: 5,
            criticalMax: 25,
          ),
          climateRange: ClimateRange.arid,
        );

        expect(BandValidation.canSave(issues), isTrue);
        expect(
          issues.single.message,
          contains('day target maximum of 40–44 °C, which is far above 19 °C'),
        );
      },
    );

    test(
      'a night band is exempt: a night drop is expected, not a typing mistake',
      () {
        final night = SpeciesProfiles.tropical
            .bandsFor('tempC')
            .singleWhere((band) => band.phase == Phase.night);

        // 20–24 °C sits well below the zone's 28–30 °C day ceiling, and that is the entire point of a night band.
        expect(
          BandValidation.validate(night, climateRange: ClimateRange.tropical),
          isEmpty,
        );
      },
    );

    test(
      'the seeded Arid basking surface band does not trip its own sanity rule',
      () {
        final surface = SpeciesProfiles.arid.bandsFor('surfaceTempC').single;

        final issues = BandValidation.validate(
          surface,
          climateRange: ClimateRange.arid,
        );

        // 45 °C target max against a 44 °C *air* ceiling would warn — which is exactly the false positive the
        // per-metric split exists to prevent.
        expect(issues, isEmpty);
        expect(ClimateRange.arid.surfaceCriticalMaxC, 50);
      },
    );

    test('a surface band above the seeded surface ceiling does warn', () {
      final issues = BandValidation.validate(
        valid.copyWith(
          metric: 'surfaceTempC',
          targetMin: 38,
          targetMax: 52,
          criticalMin: 28,
          criticalMax: 56,
          recoveryMargin: 1,
        ),
        climateRange: ClimateRange.arid,
      );

      expect(
        issues.single.message,
        contains('basking surface tops out at 50 °C'),
      );
    });

    test('the seeded SemiArid air band sits inside its zone envelope', () {
      final airBand = SpeciesProfiles.semiArid.bandsFor('tempC').first;

      expect(
        BandValidation.validate(airBand, climateRange: ClimateRange.semiArid),
        isEmpty,
      );
    });

    test(
      'light and UV have no climate envelope, so no warning is invented',
      () {
        final lightBand = SpeciesProfiles.semiArid.bandsFor('uvIndex').single;

        expect(
          BandValidation.validate(lightBand, climateRange: ClimateRange.arid),
          isEmpty,
        );
      },
    );
  });

  test('every seeded band passes its own validation', () {
    for (final profile in [
      SpeciesProfiles.tropical,
      SpeciesProfiles.semiArid,
      SpeciesProfiles.arid,
    ]) {
      for (final band in profile.bands) {
        final issues = BandValidation.validate(
          band,
          siblings: profile.bands,
          climateRange: profile.climateRange,
        );
        expect(
          issues,
          isEmpty,
          reason:
              '${profile.id} / ${band.metric} ${band.phase.label} failed: '
              '${issues.join('; ')}',
        );
      }
    }
  });
}
