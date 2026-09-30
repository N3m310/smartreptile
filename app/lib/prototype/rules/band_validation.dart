/// Band validation: the rules that reject a threshold before it can reach the engine (BR-10.2 and
/// `03-implementation/06` §2).
///
/// Two severities exist on purpose. **Blocking** issues make a band incoherent and the server returns `400`;
/// **warnings** are the helpful-but-paternalistic checks (BR-10.5) that must never stop a keeper from configuring
/// something unusual, because unusual enclosures are the norm in this hobby.
library;

import 'domain.dart';

/// How bad a validation finding is.
enum IssueSeverity {
  /// The band is incoherent; saving it must fail with a field-level error.
  blocking('Blocking'),

  /// The band is unusual; the UI says so and saves it anyway.
  warning('Warning');

  const IssueSeverity(this.label);

  /// Display label.
  final String label;

  /// True when this issue prevents the band from being saved.
  bool get isBlocking => this == IssueSeverity.blocking;
}

/// One validation finding.
class ValidationIssue {
  /// Creates a finding.
  const ValidationIssue({
    required this.ruleId,
    required this.message,
    required this.severity,
  });

  /// Rule that produced it, e.g. `BR-10.2` or `§2 recovery margin`.
  final String ruleId;

  /// What is wrong, in the keeper's language.
  final String message;

  /// Whether it blocks the save.
  final IssueSeverity severity;

  /// True when the band cannot be saved.
  bool get isBlocking => severity.isBlocking;

  @override
  String toString() => '${severity.label} $ruleId: $message';
}

/// Validates a band against BR-10.2, the arithmetic rules in `03-implementation/06` §2 and the climate envelope.
class BandValidation {
  const BandValidation._();

  /// Validates one band.
  ///
  /// [siblings] are the other bands of the same profile — the night-vs-day temperature sanity check needs the day
  /// band to compare against. [climateRange] drives the non-blocking plausibility warning.
  static List<ValidationIssue> validate(
    ThresholdBand band, {
    List<ThresholdBand> siblings = const [],
    ClimateRange? climateRange,
  }) {
    final issues = <ValidationIssue>[];

    // BR-10.2 — ordering.
    if (band.targetMin >= band.targetMax) {
      issues.add(
        const ValidationIssue(
          ruleId: 'BR-10.2',
          message: 'Target minimum must be lower than the target maximum.',
          severity: IssueSeverity.blocking,
        ),
      );
    }

    // BR-10.2 — the critical band must enclose the target band.
    if (band.criticalMin > band.targetMin ||
        band.criticalMax < band.targetMax) {
      issues.add(
        const ValidationIssue(
          ruleId: 'BR-10.2',
          message: 'The critical band must enclose the target band.',
          severity: IssueSeverity.blocking,
        ),
      );
    }

    // BR-10.2 — critical ordering.
    if (band.criticalMin >= band.criticalMax) {
      issues.add(
        const ValidationIssue(
          ruleId: 'BR-10.2',
          message: 'Critical minimum must be lower than the critical maximum.',
          severity: IssueSeverity.blocking,
        ),
      );
    }

    // §2 — a Warning that fires *after* its Critical escalation would be incoherent.
    if (band.dwellCritMinutes > band.dwellWarnMinutes) {
      issues.add(
        const ValidationIssue(
          ruleId: 'BR-11.3',
          message:
              'The critical dwell must not be longer than the warning dwell, '
              'otherwise the warning could never precede the escalation.',
          severity: IssueSeverity.blocking,
        ),
      );
    }

    // §2 — a margin larger than half the band makes recovery impossible.
    final halfBand = (band.targetMax - band.targetMin) / 2;
    if (band.recoveryMargin >= halfBand) {
      issues.add(
        ValidationIssue(
          ruleId: 'BR-11.4',
          message:
              'The recovery margin (${band.recoveryMargin}) must be smaller than half the target band '
              '(${halfBand.toStringAsFixed(2)}), otherwise the alert can never resolve.',
          severity: IssueSeverity.blocking,
        ),
      );
    }

    issues.addAll(_phaseSanity(band, siblings));
    if (climateRange != null) {
      issues.addAll(_climateSanity(band, climateRange));
    }

    return issues;
  }

  /// §2 phase sanity: a **warmer night than day** is almost always a data-entry mistake.
  static List<ValidationIssue> _phaseSanity(
    ThresholdBand band,
    List<ThresholdBand> siblings,
  ) {
    if (band.metric != 'tempC' || band.phase != Phase.night) {
      return const [];
    }

    final dayBand = siblings
        .where((other) => other.metric == 'tempC' && other.phase == Phase.day)
        .firstOrNull;
    if (dayBand == null || band.targetMax <= dayBand.targetMax) {
      return const [];
    }

    return const [
      ValidationIssue(
        ruleId: 'BR-10.5',
        message:
            'The night target is warmer than the day target. That is usually a typing mistake — '
            'a night drop is normal physiology.',
        severity: IssueSeverity.warning,
      ),
    ];
  }

  /// BR-10.5 — non-blocking climate-zone plausibility, judged **per metric** on the **day** band.
  ///
  /// Two readings matter here, and both come from the documentation rather than from taste:
  ///
  /// * §4's table gives an *air temperature ceiling* and a *humidity ceiling* — a target maximum the zone should not
  ///   exceed. It also gives the range that ceiling normally falls in (Tropical 28–30, SemiArid 31–33, Arid
  ///   40–44), which is what makes BR-10.5's own example work: a "desert profile with `TargetMax < 20 °C`" is
  ///   implausible because it is far below 40.
  /// * Only the **day** band is judged. A night band is *expected* to be cooler than any daytime ceiling — the
  ///   Tropical night maximum of 24 °C is not a data-entry mistake. Applying the ceiling to it was the first version
  ///   of this function, and it flagged the seeded profiles; the test that asserts every seeded band validates clean
  ///   is what caught it.
  ///
  /// `SurfaceTempC` is judged separately, against the profile's own seeded surface ceiling, because a basking rock
  /// is legitimately hotter than the air above it (`07-appendices/05` §4, clarified 2026-09-29).
  static List<ValidationIssue> _climateSanity(
    ThresholdBand band,
    ClimateRange range,
  ) {
    switch (band.metric) {
      case 'tempC':
        if (band.phase != Phase.day) {
          return const [];
        }
        if (band.targetMax > range.airMaxC) {
          return [
            ValidationIssue(
              ruleId: 'BR-10.5',
              message:
                  'A ${range.zone.label} profile normally has a day target maximum of '
                  '${range.airMinC.toStringAsFixed(0)}–${range.airMaxC.toStringAsFixed(0)} °C.',
              severity: IssueSeverity.warning,
            ),
          ];
        }
        if (band.targetMax < range.airMinC) {
          return [
            ValidationIssue(
              ruleId: 'BR-10.5',
              message:
                  'A ${range.zone.label} profile normally has a day target maximum of '
                  '${range.airMinC.toStringAsFixed(0)}–${range.airMaxC.toStringAsFixed(0)} °C, '
                  'which is far above ${band.targetMax.toStringAsFixed(0)} °C.',
              severity: IssueSeverity.warning,
            ),
          ];
        }
        return const [];

      case 'surfaceTempC':
        final ceiling = range.surfaceCriticalMaxC;
        if (ceiling != null && band.targetMax > ceiling) {
          return [
            ValidationIssue(
              ruleId: 'BR-10.5',
              message:
                  'The seeded ${range.zone.label} basking surface tops out at '
                  '${ceiling.toStringAsFixed(0)} °C.',
              severity: IssueSeverity.warning,
            ),
          ];
        }
        return const [];

      case 'humidityPct':
        if (band.targetMax > range.humidityMaxPct) {
          return [
            ValidationIssue(
              ruleId: 'BR-10.5',
              message:
                  'A ${range.zone.label} profile normally keeps humidity below '
                  '${range.humidityMaxPct.toStringAsFixed(0)} %RH.',
              severity: IssueSeverity.warning,
            ),
          ];
        }
        return const [];

      default:
        // Light and UV have no climate-zone envelope in `07-appendices/05` §4; inventing one would be a rule with
        // no source, which is exactly what this doc set refuses to do.
        return const [];
    }
  }

  /// True when nothing blocking was found.
  static bool canSave(List<ValidationIssue> issues) =>
      !issues.any((issue) => issue.isBlocking);
}

extension _FirstOrNull<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
