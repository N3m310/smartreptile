/// Seeded species profiles and the override → profile → default resolution (FR-10, BR-10.1/BR-10.3).
///
/// The numbers are transcribed from `docs/07-appendices/05-species-threshold-reference.md` §1–§3, including the
/// band counts the backend seeder ships (**19 bands**: Tropical 5, SemiArid 6, Arid 6, Arid-cool 2 — of which the
/// three `LightLux` bands are inert per `03-implementation/06` §4).
///
/// **Honesty note carried from the docs:** these are *typical published husbandry ranges*, not measured physiology,
/// and every row still owes a page reference (`07-appendices/05` §5). The prototype shows the citation on every band
/// card so nobody demos a number without its provenance.
library;

import 'domain.dart';

/// A seeded species profile: bands plus the profile-level data the accumulated rules need (FR-10).
class SpeciesProfile {
  /// Creates a profile.
  const SpeciesProfile({
    required this.id,
    required this.name,
    required this.species,
    required this.climateZone,
    required this.bands,
    required this.phaseTimeline,
    required this.lightThresholdLux,
    required this.minLightHoursPerDay,
    this.uvFitted = true,
    this.surfaceProbeFitted = false,
    this.climateRangeOverride,
    this.note,
  });

  /// Stable id used in the fake API payloads, e.g. `semi-arid-leopard-gecko`.
  final String id;

  /// Display name, e.g. "Semi-arid (leopard gecko)".
  final String name;

  /// Species the numbers describe.
  final String species;

  /// Climate zone, which picks the plausibility envelope (BR-10.5).
  final ClimateZone climateZone;

  /// All bands, including night variants and inert light rows.
  final List<ThresholdBand> bands;

  /// Photoperiod that decides the phase (BR-11.2).
  final PhaseTimeline phaseTimeline;

  /// Lux threshold used by the accumulated `LightDeficit` rule.
  final double lightThresholdLux;

  /// Light-hours a day the species needs; below this, `LightDeficit` fires at 21:00 local.
  final int minLightHoursPerDay;

  /// False when the LTR390 is not fitted, in which case no UV alert exists at all.
  final bool uvFitted;

  /// True when a surface probe is part of the build (enables `GradientWarning`).
  final bool surfaceProbeFitted;

  /// Profile-level caveat shown in the thresholds editor.
  final String? note;

  /// Zone envelope to use when this profile does not monitor the zone's usual side — an ambient/cool-side variant of
  /// a zone whose §4 numbers describe the basking zone. Null means the plain zone envelope.
  final ClimateRange? climateRangeOverride;

  /// Plausibility envelope for this profile: the override when one is declared, otherwise its climate zone.
  ClimateRange get climateRange =>
      climateRangeOverride ?? ClimateRange.of(climateZone);

  /// Bands for one metric, in declaration order.
  List<ThresholdBand> bandsFor(String metric) =>
      bands.where((band) => band.metric == metric).toList(growable: false);

  /// True when the profile has no band for a metric, i.e. the metric is not measured for this species.
  bool measuresNothingFor(String metric) => bandsFor(metric).isEmpty;
}

/// The four seeded profiles (BR-10.1). Counts match the backend seeder: 5 + 6 + 6 + 2 = 19 bands.
class SpeciesProfiles {
  const SpeciesProfiles._();

  /// Tropical / humid forest — crested gecko (`07-appendices/05` §1).
  static const tropical = SpeciesProfile(
    id: 'tropical-crested-gecko',
    name: 'Tropical (humid forest)',
    species: 'Crested gecko (Correlophus ciliatus)',
    climateZone: ClimateZone.tropical,
    phaseTimeline: PhaseTimeline(),
    lightThresholdLux: 500,
    minLightHoursPerDay: 10,
    bands: [
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.day,
        targetMin: 24,
        targetMax: 28,
        criticalMin: 18,
        criticalMax: 31,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 0.5,
        citation: 'typical published husbandry range — verify edition/page (§5 row 9)',
        note: 'Cool-adapted for a gecko; sustained heat, not cold, is the common killer indoors.',
      ),
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.night,
        targetMin: 20,
        targetMax: 24,
        criticalMin: 16,
        criticalMax: 27,
        dwellWarnMinutes: 10,
        dwellCritMinutes: 3,
        recoveryMargin: 0.5,
        citation: 'typical published husbandry range — verify edition/page (§5 row 3 analogue)',
        note: 'A natural night drop is expected; a warm night is the anomaly.',
      ),
      ThresholdBand(
        metric: 'humidityPct',
        phase: Phase.any,
        targetMin: 60,
        targetMax: 80,
        criticalMin: 40,
        criticalMax: 95,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 3,
        citation: 'typical published husbandry range — verify edition/page (§5 row 10)',
      ),
      ThresholdBand(
        metric: 'lightLux',
        phase: Phase.day,
        targetMin: 500,
        targetMax: 200000,
        criticalMin: 0,
        criticalMax: 200000,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 25,
        citation: 'husbandry practice, not physiology — verify (§5 row 12)',
        note: 'Inert band: light is judged on accumulated hours, never per sample (§4).',
        accumulatedOnly: true,
      ),
      ThresholdBand(
        metric: 'uvIndex',
        phase: Phase.day,
        targetMin: 0,
        targetMax: 1,
        criticalMin: 0,
        criticalMax: 2,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 0.1,
        citation: 'UV-Tool guidance — verify (§5 row 8 analogue)',
        note: 'Only exists when the LTR390 is fitted.',
      ),
    ],
  );

  /// Semi-arid / semi-desert — **the demo species**: leopard gecko (`07-appendices/05` §2).
  static const semiArid = SpeciesProfile(
    id: 'semi-arid-leopard-gecko',
    name: 'Semi-arid (leopard gecko)',
    species: 'Leopard gecko (Eublepharis macularius)',
    climateZone: ClimateZone.semiArid,
    phaseTimeline: PhaseTimeline(),
    lightThresholdLux: 1000,
    minLightHoursPerDay: 8,
    surfaceProbeFitted: true,
    note:
        'The humidity band describes **air** humidity. The humid hide runs at 70–80 %RH during shedding and a '
        'single sensor cannot represent both — a known v1 limitation, not a bug.',
    bands: [
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.day,
        targetMin: 26,
        targetMax: 32,
        criticalMin: 22,
        criticalMax: 34.5,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 0.5,
        citation: 'typical published husbandry range — verify edition/page (§5 rows 1–2)',
        note: '34.5 °C is where an unmonitored heat mat becomes dangerous.',
      ),
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.night,
        targetMin: 22,
        targetMax: 27,
        criticalMin: 18,
        criticalMax: 31,
        dwellWarnMinutes: 10,
        dwellCritMinutes: 3,
        recoveryMargin: 0.5,
        citation: 'typical published husbandry range — verify edition/page (§5 row 3)',
        note: 'A 3–5 °C night drop is physiologically beneficial, not a fault.',
      ),
      ThresholdBand(
        metric: 'surfaceTempC',
        phase: Phase.day,
        targetMin: 30,
        targetMax: 34,
        criticalMin: 22,
        criticalMax: 38,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 1,
        citation: 'typical published husbandry range — verify edition/page (§5 row 14)',
        note: 'Basking surface; judging it against the *air* ceiling would be wrong (§4).',
      ),
      ThresholdBand(
        metric: 'humidityPct',
        phase: Phase.any,
        targetMin: 30,
        targetMax: 40,
        criticalMin: 20,
        criticalMax: 60,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 3,
        citation: 'typical published husbandry range — verify edition/page (§5 row 4)',
      ),
      ThresholdBand(
        metric: 'lightLux',
        phase: Phase.day,
        targetMin: 1000,
        targetMax: 200000,
        criticalMin: 0,
        criticalMax: 200000,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 50,
        citation: 'husbandry practice, not physiology — verify (§5 row 12)',
        note: 'Inert band: light is judged on accumulated hours, never per sample (§4).',
        accumulatedOnly: true,
      ),
      ThresholdBand(
        metric: 'uvIndex',
        phase: Phase.day,
        targetMin: 0,
        targetMax: 1.5,
        criticalMin: 0,
        criticalMax: 2.5,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 0.1,
        citation: 'UV-Tool guidance — verify (§5 row 8 analogue)',
        note: 'Strong UVB is not required for this species; only exists when the LTR390 is fitted.',
      ),
    ],
  );

  /// Arid / desert — bearded dragon (`07-appendices/05` §3).
  ///
  /// The docs describe an `Arid-cool` ambient 28–33 °C variant, but **the seeder never creates it** (open decision
  /// 2026-09-29). The prototype therefore ships the seeded six bands only, and the Rule Lab scenario `N` uses
  /// [aridCool] — a profile variant that exists to make that gap visible rather than to hide it.
  static const arid = SpeciesProfile(
    id: 'arid-bearded-dragon',
    name: 'Arid (bearded dragon, basking)',
    species: 'Bearded dragon (Pogona vitticeps)',
    climateZone: ClimateZone.arid,
    phaseTimeline: PhaseTimeline(),
    lightThresholdLux: 2000,
    minLightHoursPerDay: 10,
    surfaceProbeFitted: true,
    note:
        'This profile describes the **basking** zone. The air on the cool side is 10 °C cooler and is not measured '
        'by this band — see the `Arid-cool` variant, which the backend seeder does not ship yet.',
    bands: [
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.day,
        targetMin: 38,
        targetMax: 42,
        criticalMin: 30,
        criticalMax: 45,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 1,
        citation: 'typical published husbandry range — verify edition/page (§5 row 6)',
        note: 'A true heliotherm needs a hot spot; air temperature alone understates its needs.',
      ),
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.night,
        targetMin: 24,
        targetMax: 28,
        criticalMin: 18,
        criticalMax: 32,
        dwellWarnMinutes: 10,
        dwellCritMinutes: 3,
        recoveryMargin: 1,
        citation: 'typical published husbandry range — verify edition/page (§5 row 7 analogue)',
      ),
      ThresholdBand(
        metric: 'surfaceTempC',
        phase: Phase.day,
        targetMin: 38,
        targetMax: 45,
        criticalMin: 28,
        criticalMax: 50,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 1,
        citation: 'typical published husbandry range — verify edition/page (§5 row 14)',
        note: 'The probe sits on the basking rock, not in the air above it.',
      ),
      ThresholdBand(
        metric: 'humidityPct',
        phase: Phase.any,
        targetMin: 30,
        targetMax: 40,
        criticalMin: 20,
        criticalMax: 55,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 3,
        citation: 'typical published husbandry range — verify edition/page (§5 row 4 analogue)',
      ),
      ThresholdBand(
        metric: 'lightLux',
        phase: Phase.day,
        targetMin: 2000,
        targetMax: 200000,
        criticalMin: 0,
        criticalMax: 200000,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 100,
        citation: 'husbandry practice, not physiology — verify (§5 row 12)',
        note: 'Inert band: light is judged on accumulated hours, never per sample (§4).',
        accumulatedOnly: true,
      ),
      ThresholdBand(
        metric: 'uvIndex',
        phase: Phase.day,
        targetMin: 1,
        targetMax: 3.5,
        criticalMin: 0,
        criticalMax: 5,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 0.2,
        citation: 'UV-Tool guidance — verify (§5 row 8)',
        note: 'Without a UVB lamp the lower bound cannot be met; the app says "below target" rather than "fine".',
      ),
    ],
  );

  /// An `Arid-cool` ambient variant that the docs describe and the seeder creates (seeded 2026-10-07).
  ///
  /// Only the ambient air band differs; the other bands are shared with [arid] so the two profiles cannot drift.
  static const aridCool = SpeciesProfile(
    id: 'arid-cool-bearded-dragon',
    name: 'Arid-cool (bearded dragon, ambient)',
    species: 'Bearded dragon (Pogona vitticeps)',
    climateZone: ClimateZone.arid,
    phaseTimeline: PhaseTimeline(),
    lightThresholdLux: 2000,
    minLightHoursPerDay: 10,
    climateRangeOverride: ClimateRange.aridAmbient,
    note:
        'Described in `07-appendices/05` §3 and verified by row 7 of the checklist; seeded by the backend on '
        '2026-10-07, so both sides carry the same 19 bands. No surface or UV band ships with it: both belong to '
        'the basking zone this variant does not describe.',
    bands: [
      ThresholdBand(
        metric: 'tempC',
        phase: Phase.day,
        targetMin: 28,
        targetMax: 33,
        criticalMin: 24,
        criticalMax: 36,
        dwellWarnMinutes: 5,
        dwellCritMinutes: 2,
        recoveryMargin: 1,
        citation: 'typical published husbandry range — verify edition/page (§5 row 7)',
        note: 'Cool-side air. The 45 °C critical maximum would be fatal here, which is why the two zones differ.',
      ),
      ThresholdBand(
        metric: 'humidityPct',
        phase: Phase.any,
        targetMin: 30,
        targetMax: 40,
        criticalMin: 20,
        criticalMax: 55,
        dwellWarnMinutes: 15,
        dwellCritMinutes: 5,
        recoveryMargin: 3,
        citation: 'typical published husbandry range — verify edition/page (§5 row 4 analogue)',
      ),
    ],
  );

  /// All profiles a keeper can pick, in the order the app lists them.
  static const all = <SpeciesProfile>[semiArid, tropical, arid, aridCool];

  /// Looks up a profile by id; null when unknown.
  static SpeciesProfile? byId(String id) {
    for (final profile in all) {
      if (profile.id == id) {
        return profile;
      }
    }
    return null;
  }
}

/// The hard-coded last resort in the resolution chain (BR-10.3 step 3).
///
/// It exists so a terrarium with no profile still shows *something* — and it is deliberately wide, because a
/// default that fires alerts is worse than a default that says "I do not know this species".
class SystemDefaults {
  const SystemDefaults._();

  /// Wide fallback bands, one per core metric.
  static const bands = <ThresholdBand>[
    ThresholdBand(
      metric: 'tempC',
      phase: Phase.any,
      targetMin: 18,
      targetMax: 35,
      criticalMin: 5,
      criticalMax: 45,
      dwellWarnMinutes: 15,
      dwellCritMinutes: 5,
      recoveryMargin: 1,
      source: BandSource.systemDefault,
      note: 'No species profile assigned: these are survival limits, not husbandry targets.',
    ),
    ThresholdBand(
      metric: 'humidityPct',
      phase: Phase.any,
      targetMin: 20,
      targetMax: 90,
      criticalMin: 5,
      criticalMax: 100,
      dwellWarnMinutes: 15,
      dwellCritMinutes: 5,
      recoveryMargin: 3,
      source: BandSource.systemDefault,
      note: 'No species profile assigned.',
    ),
  ];

  /// Default band for a metric, if one exists.
  static ThresholdBand? forMetric(String metric) {
    for (final band in bands) {
      if (band.metric == metric) {
        return band;
      }
    }
    return null;
  }
}

/// A per-terrarium override row (FR-10, BR-10.3 step 1).
class ThresholdOverride {
  /// Creates an override.
  const ThresholdOverride({
    required this.terrariumId,
    required this.band,
    required this.editedBy,
    required this.editedAt,
    this.reason,
  });

  /// Terrarium the override belongs to.
  final String terrariumId;

  /// The overriding band; its `source` is always [BandSource.override].
  final ThresholdBand band;

  /// Who edited it (BR-10.4: edits are audited).
  final String editedBy;

  /// When it was edited.
  final DateTime editedAt;

  /// Why, shown in the audit trail.
  final String? reason;
}

/// The result of resolving one metric: the band plus where it came from (BR-10.3).
class EffectiveBand {
  /// Creates an effective band.
  const EffectiveBand({required this.metric, required this.band});

  /// Metric resolved.
  final String metric;

  /// The band in force, or null when nothing applies (the metric is not measured for this species).
  final ThresholdBand? band;

  /// True when a band was found.
  bool get hasBand => band != null;

  /// Source of the band, or `null` when there is none.
  BandSource? get source => band?.source;

  /// True when the band is inert for per-sample alerting (light; §4).
  bool get isAccumulatedOnly => band?.accumulatedOnly ?? false;
}

/// Resolves effective bands for one terrarium (BR-10.3): override → profile → system default.
///
/// Precedence inside a layer: an exact-phase row beats an `Any`-phase row; an override beats a profile row even when
/// the profile row is phase-specific — the editor states this out loud because it surprises people
/// (`03-implementation/06` §1).
class ThresholdResolver {
  /// Creates a resolver.
  const ThresholdResolver({
    required this.profile,
    this.overrides = const [],
    this.timeline,
  });

  /// Assigned species profile, or null for the system default only.
  final SpeciesProfile? profile;

  /// Per-terrarium overrides.
  final List<ThresholdOverride> overrides;

  /// Photoperiod; defaults to the profile's.
  final PhaseTimeline? timeline;

  /// Resolves the band for a metric at a local wall-clock instant.
  EffectiveBand resolve(String metric, DateTime localTime) {
    final effectiveTimeline =
        timeline ?? profile?.phaseTimeline ?? const PhaseTimeline();
    final candidates = <ThresholdBand>[
      for (final override in overrides)
        if (override.band.metric == metric) override.band,
      ...?profile?.bandsFor(metric),
    ];

    // BR-11.2 / `03-implementation/06` §1: a metric with an `Any` row never splits by phase.
    final hasAnyRow = candidates.any((band) => band.phase == Phase.any);
    final phase = hasAnyRow ? Phase.any : effectiveTimeline.phaseAt(localTime);

    final band =
        _firstWithPhase(candidates, phase, BandSource.override) ??
        _firstWithPhase(candidates, Phase.any, BandSource.override) ??
        _firstWithPhase(candidates, phase, BandSource.profile) ??
        _firstWithPhase(candidates, Phase.any, BandSource.profile) ??
        SystemDefaults.forMetric(metric);

    return EffectiveBand(metric: metric, band: band);
  }

  /// Resolves every core metric plus the optional surface probe.
  List<EffectiveBand> resolveAll(
    DateTime localTime, {
    bool includeSurface = true,
  }) {
    final metrics = <String>[
      for (final definition in MetricCatalog.all)
        if (includeSurface || definition.code != 'surfaceTempC')
          definition.code,
    ];
    return [for (final metric in metrics) resolve(metric, localTime)];
  }

  ThresholdBand? _firstWithPhase(
    List<ThresholdBand> bands,
    Phase phase,
    BandSource source,
  ) {
    for (final band in bands) {
      if (band.source == source && band.phase == phase) {
        return band;
      }
    }
    return null;
  }

  /// Returns a resolver with one more override, replacing any existing override for the same metric and phase.
  ThresholdResolver withOverride(ThresholdOverride override) {
    final kept = overrides
        .where(
          (existing) =>
              existing.band.metric != override.band.metric ||
              existing.band.phase != override.band.phase,
        )
        .toList();
    return ThresholdResolver(
      profile: profile,
      overrides: [...kept, override],
      timeline: timeline,
    );
  }

  /// Returns a resolver without the override for a metric and phase.
  ThresholdResolver withoutOverride(String metric, Phase phase) =>
      ThresholdResolver(
        profile: profile,
        overrides: overrides
            .where(
              (existing) =>
                  existing.band.metric != metric ||
                  existing.band.phase != phase,
            )
            .toList(),
        timeline: timeline,
      );
}
