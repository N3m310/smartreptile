/// Domain vocabulary of the SmartReptile rules, as a pure-Dart library.
///
/// **This is the prototype's shadow of the server domain.** The real engine lives in
/// `backend/src/SmartReptile.Domain` and runs server-side only (BR-11, ADR-005); this file exists so the app can
/// *show* what the server will decide, with fake data, before the endpoints exist. Every type here names the
/// document and rule id it implements, because the point of the prototype is to make the rules reviewable.
///
/// Deliberately free of Flutter imports: the whole rule kernel is unit-testable without a widget tree, and the
/// same functions can be lifted into the backend later if the team wants a shared conformance suite.
library;

/// Day/night split of a threshold band (BR-11.2, `07-appendices/05` §3).
enum Phase {
  /// The metric has a single band that applies at any time of day.
  any('Any'),

  /// Local time is inside the profile's photoperiod window.
  day('Day'),

  /// Local time is outside the photoperiod window.
  night('Night');

  const Phase(this.label);

  /// Short label used in tables and chips.
  final String label;
}

/// Direction of the current excursion (§02-design/03 §4.1).
enum Violation {
  /// The value is inside the target band.
  none('None'),

  /// The value is above the target band.
  hot('Hot'),

  /// The value is below the target band.
  cold('Cold');

  const Violation(this.label);

  /// Short label used in the decision trace.
  final String label;
}

/// Alert severity (`02-design/05` §1). Only two operable severities exist on purpose.
enum Severity {
  /// System events only: never pushed, timeline entry only.
  info('Info', 0),

  /// Outside the target band for `dwellWarnMinutes`.
  warning('Warning', 1),

  /// Outside the critical band for `dwellCritMinutes`, or a silent device for 30 min.
  critical('Critical', 2);

  const Severity(this.label, this.rank);

  /// Display label.
  final String label;

  /// Ordering rank, used for `severity >= minSeverity` comparisons.
  final int rank;
}

/// Alert lifecycle (BR-12.1: `Open → Acknowledged → Resolved`, `Open → Resolved` also allowed).
enum AlertState {
  /// Nothing has been done about it yet.
  open('Open'),

  /// Seen and owned, still unresolved.
  acknowledged('Acknowledged'),

  /// Terminal; a new excursion creates a new row (BR-12.1).
  resolved('Resolved');

  const AlertState(this.label);

  /// Display label.
  final String label;
}

/// Manual resolve reasons (BR-12.3). Two of them are also the v2 training labels (BR-12.5).
enum ResolvedReason {
  /// The environment came back into range on its own — the only reason the engine produces.
  recovered('Recovered', false),

  /// The excursion was not real.
  falsePositive('False positive', true),

  /// The sensor was wrong, not the habitat.
  sensorFault('Sensor fault', true),

  /// The excursion was real and consciously accepted (e.g. a deliberate basking boost).
  accepted('Accepted risk', true);

  const ResolvedReason(this.label, this.isLabelForV2Training);

  /// Display label.
  final String label;

  /// True when the reason is retained as labelled data for the v2 disease model (BR-12.5).
  final bool isLabelForV2Training;
}

/// Where an alert came from (`02-design/05` §2).
enum AlertSource {
  /// Per-metric band violation (FR-11).
  threshold('Threshold'),

  /// No sample for `3 × interval` (BR-07.2).
  deviceSilent('Device silent'),

  /// The device reported consecutive sensor read failures (BR-07.1).
  sensorFault('Sensor fault'),

  /// `|ReceivedAt − RecordedAt| > 120 s` (rule V-09).
  clockSkew('Clock skew'),

  /// `SurfaceTempC − TempC > 12 °C` (`03-implementation/06` §4).
  gradient('Surface gradient'),

  /// Accumulated light-hours below the profile minimum by 21:00 local (`03-implementation/06` §4).
  lightDeficit('Light deficit');

  const AlertSource(this.label);

  /// Display label.
  final String label;
}

/// Where an effective band came from (BR-10.3: override → profile → system default).
enum BandSource {
  /// A per-terrarium row.
  override('override'),

  /// The assigned species profile.
  profile('profile'),

  /// The hard-coded last resort, warned about in the UI.
  systemDefault('default');

  const BandSource(this.label);

  /// Display label.
  final String label;
}

/// Climate zone a species profile belongs to (`07-appendices/05` §4). Drives the non-blocking sanity warning
/// (BR-10.5) — never a blocking validation.
enum ClimateZone {
  /// Humid forest.
  tropical('Tropical'),

  /// Semi-desert.
  semiArid('SemiArid'),

  /// Desert.
  arid('Arid');

  const ClimateZone(this.label);

  /// Display label.
  final String label;
}

/// One metric in the dictionary (§02-design/02 §3.8, verified against the backend `MetricDictionary`).
///
/// Plausibility bounds matter to the prototype because the implausible-value scenario (BR-06.4/BR-11.1) is one of
/// the rules a reviewer most wants to see fail safely.
class MetricDefinition {
  /// Creates a definition.
  const MetricDefinition({
    required this.code,
    required this.displayName,
    required this.unit,
    required this.precision,
    required this.plausibleMin,
    required this.plausibleMax,
    this.isCore = true,
  });

  /// API key, e.g. `tempC`.
  final String code;

  /// Human-readable name (localised in the UI, not stored).
  final String displayName;

  /// Unit string shown next to the value.
  final String unit;

  /// Decimal places kept when displaying.
  final int precision;

  /// Lowest physically plausible value; outside ⇒ quality flag `Implausible`.
  final double plausibleMin;

  /// Highest physically plausible value; outside ⇒ quality flag `Implausible`.
  final double plausibleMax;

  /// Core metrics are required in every species profile.
  final bool isCore;

  /// True when the value lies inside the plausibility bounds (rule V-06).
  bool isPlausible(double value) =>
      value >= plausibleMin && value <= plausibleMax;

  /// Formats a value with this metric's precision.
  String format(double value) => value.toStringAsFixed(precision);
}

/// The metric dictionary, mirroring `backend/src/SmartReptile.Domain/Metrics/MetricDictionary.cs`.
class MetricCatalog {
  const MetricCatalog._();

  /// Air temperature (`tempC`).
  static const airTemperature = MetricDefinition(
    code: 'tempC',
    displayName: 'Air temperature',
    unit: '°C',
    precision: 2,
    plausibleMin: -10,
    plausibleMax: 60,
  );

  /// Relative humidity (`humidityPct`).
  static const humidity = MetricDefinition(
    code: 'humidityPct',
    displayName: 'Relative humidity',
    unit: '%RH',
    precision: 2,
    plausibleMin: 0,
    plausibleMax: 100,
  );

  /// Illuminance (`lightLux`).
  static const light = MetricDefinition(
    code: 'lightLux',
    displayName: 'Illuminance',
    unit: 'lx',
    precision: 1,
    plausibleMin: 0,
    plausibleMax: 200000,
  );

  /// UV index (`uvIndex`).
  static const uvIndex = MetricDefinition(
    code: 'uvIndex',
    displayName: 'UV index',
    unit: 'UVI',
    precision: 2,
    plausibleMin: 0,
    plausibleMax: 15,
  );

  /// Surface temperature (`surfaceTempC`) — optional, not core.
  static const surfaceTemperature = MetricDefinition(
    code: 'surfaceTempC',
    displayName: 'Surface temperature',
    unit: '°C',
    precision: 2,
    plausibleMin: -10,
    plausibleMax: 80,
    isCore: false,
  );

  /// All core metrics, in dashboard order.
  static const core = <MetricDefinition>[
    airTemperature,
    humidity,
    light,
    uvIndex,
  ];

  /// Every metric the prototype knows.
  static const all = <MetricDefinition>[
    airTemperature,
    humidity,
    light,
    uvIndex,
    surfaceTemperature,
  ];

  /// Looks up a definition by API code; returns null for an unknown code rather than throwing, because the
  /// prototype must survive a payload from a newer firmware.
  static MetricDefinition? byCode(String code) {
    for (final definition in all) {
      if (definition.code == code) {
        return definition;
      }
    }
    return null;
  }
}

/// One effective threshold band for one metric and phase (BR-10.1/BR-10.3).
///
/// Dwell and the recovery margin are **team design choices, not literature values** (`07-appendices/05` §5 row 13),
/// which is exactly why they are fields on the band and not constants: the Rule Lab lets a reviewer change them and
/// watch the decision trace change.
class ThresholdBand {
  /// Creates a band.
  const ThresholdBand({
    required this.metric,
    required this.phase,
    required this.targetMin,
    required this.targetMax,
    required this.criticalMin,
    required this.criticalMax,
    required this.dwellWarnMinutes,
    required this.dwellCritMinutes,
    required this.recoveryMargin,
    this.source = BandSource.profile,
    this.citation,
    this.note,
    this.accumulatedOnly = false,
  });

  /// Metric this band applies to, e.g. `tempC`.
  final String metric;

  /// Phase this band applies to.
  final Phase phase;

  /// Lower bound of the target band; below this is `cold`.
  final double targetMin;

  /// Upper bound of the target band; above this is `hot`.
  final double targetMax;

  /// Lower bound of the critical band; below this is critical.
  final double criticalMin;

  /// Upper bound of the critical band; above this is critical.
  final double criticalMax;

  /// Minutes outside the target band before a Warning is opened (BR-11.3).
  final int dwellWarnMinutes;

  /// Minutes outside the critical band before a Warning escalates to Critical (BR-11.3).
  final int dwellCritMinutes;

  /// Margin inside the target band required for a recovery tick (BR-11.4).
  final double recoveryMargin;

  /// Where this band came from (BR-10.3).
  final BandSource source;

  /// Provenance string; every seeded band carries one (`ReferenceDataSeeder`).
  final String? citation;

  /// Free-text note shown in the editor (e.g. the humid-hide caveat for leopard geckos).
  final String? note;

  /// True for a band that exists only to expose a target for the accumulated light rule.
  ///
  /// The three `LightLux` bands the seeder ships are **inert** for per-sample alerting (`03-implementation/06` §4):
  /// a shadow from a plant is not an incident, a failed lamp timer all day is. The evaluator skips them, and the UI
  /// labels the card accordingly instead of pretending light has a per-sample band.
  final bool accumulatedOnly;

  /// True when the value is above the target band.
  bool isHot(double value) => value > targetMax;

  /// True when the value is below the target band.
  bool isCold(double value) => value < targetMin;

  /// True when the value is inside the critical band.
  bool isWithinCritical(double value) =>
      value >= criticalMin && value <= criticalMax;

  /// True when the value is outside the critical band in either direction.
  bool isCritical(double value) => !isWithinCritical(value);

  /// True when the value is inside the target band (inclusive).
  bool isInTargetBand(double value) => value >= targetMin && value <= targetMax;

  /// Returns a copy with the given fields replaced; `clearNote`/`clearCitation` remove them.
  ThresholdBand copyWith({
    String? metric,
    Phase? phase,
    double? targetMin,
    double? targetMax,
    double? criticalMin,
    double? criticalMax,
    int? dwellWarnMinutes,
    int? dwellCritMinutes,
    double? recoveryMargin,
    BandSource? source,
    String? citation,
    String? note,
    bool? accumulatedOnly,
  }) => ThresholdBand(
    metric: metric ?? this.metric,
    phase: phase ?? this.phase,
    targetMin: targetMin ?? this.targetMin,
    targetMax: targetMax ?? this.targetMax,
    criticalMin: criticalMin ?? this.criticalMin,
    criticalMax: criticalMax ?? this.criticalMax,
    dwellWarnMinutes: dwellWarnMinutes ?? this.dwellWarnMinutes,
    dwellCritMinutes: dwellCritMinutes ?? this.dwellCritMinutes,
    recoveryMargin: recoveryMargin ?? this.recoveryMargin,
    source: source ?? this.source,
    citation: citation ?? this.citation,
    note: note ?? this.note,
    accumulatedOnly: accumulatedOnly ?? this.accumulatedOnly,
  );

  @override
  String toString() =>
      'ThresholdBand($metric/${phase.label} target $targetMin–$targetMax, '
      'critical $criticalMin–$criticalMax, dwell $dwellWarnMinutes/$dwellCritMinutes, ${source.label})';
}

/// Climate-zone plausibility envelope used by the non-blocking sanity warning (BR-10.5, `07-appendices/05` §4).
///
/// §4's table lists an **air temperature ceiling** and a **humidity ceiling** per zone, and gives the range each
/// ceiling normally falls in. This type keeps both readings, because they answer different questions: the upper value
/// rejects a target that is hotter than the zone can be, and the lower value rejects BR-10.5's own example — a desert
/// profile with a 19 °C target maximum.
///
/// The ceiling is an **air** ceiling and applies to the **day** band: a night drop is expected, and `SurfaceTempC` is
/// judged against the profile's own seeded surface band instead (`07-appendices/05` §4, clarified 2026-09-29).
class ClimateRange {
  /// Creates a range.
  const ClimateRange({
    required this.zone,
    required this.airMinC,
    required this.airMaxC,
    required this.humidityMinPct,
    required this.humidityMaxPct,
    required this.photoperiodHours,
    required this.lightThresholdLux,
    this.surfaceCriticalMaxC,
  });

  /// Climate zone this envelope describes.
  final ClimateZone zone;

  /// Lower end of the range a normal **day** target maximum falls in for this zone. Used as the plausibility floor.
  final double airMinC;

  /// Upper end of that range — the ceiling a day target maximum must not exceed.
  final double airMaxC;

  /// Lowest humidity target the zone normally uses. Documentation only; §4 lists no humidity floor rule, so none is
  /// enforced (inventing one flagged the seeded SemiArid band's 30 %RH as suspicious, which is correct data).
  final double humidityMinPct;

  /// Humidity ceiling a target maximum must not exceed.
  final double humidityMaxPct;

  /// Typical photoperiod, hours.
  final int photoperiodHours;

  /// Light threshold that the accumulated `LightDeficit` rule uses.
  final double lightThresholdLux;

  /// Ceiling applied to a `SurfaceTempC` band — the seeded surface band's own `criticalMax`. Null when the zone
  /// ships no surface band, in which case no sanity check is applied to that metric.
  final double? surfaceCriticalMaxC;

  /// `07-appendices/05` §4 — day target maximum normally 28–30 °C.
  static const tropical = ClimateRange(
    zone: ClimateZone.tropical,
    airMinC: 28,
    airMaxC: 30,
    humidityMinPct: 70,
    humidityMaxPct: 85,
    photoperiodHours: 12,
    lightThresholdLux: 500,
  );

  /// `07-appendices/05` §4 — day target maximum normally 31–33 °C.
  static const semiArid = ClimateRange(
    zone: ClimateZone.semiArid,
    airMinC: 31,
    airMaxC: 33,
    humidityMinPct: 40,
    humidityMaxPct: 50,
    photoperiodHours: 12,
    lightThresholdLux: 1000,
    surfaceCriticalMaxC: 38,
  );

  /// `07-appendices/05` §4 — basking target maximum normally 40–44 °C.
  static const arid = ClimateRange(
    zone: ClimateZone.arid,
    airMinC: 40,
    airMaxC: 44,
    humidityMinPct: 40,
    humidityMaxPct: 40,
    photoperiodHours: 12,
    lightThresholdLux: 2000,
    surfaceCriticalMaxC: 50,
  );

  /// Envelope for a zone.
  static ClimateRange of(ClimateZone zone) => switch (zone) {
    ClimateZone.tropical => tropical,
    ClimateZone.semiArid => semiArid,
    ClimateZone.arid => arid,
  };
}

/// Quality bits carried on every sample, mirroring the backend `QualityFlags` enum.
class QualityBits {
  const QualityBits._();

  /// Sensor read failed (I²C/1-Wire fault reported by the device).
  static const sensorFault = 1 << 0;

  /// Value outside the metric's plausible range (rule V-06).
  static const implausible = 1 << 1;

  /// First reading after boot; settling time has not elapsed.
  static const firstAfterBoot = 1 << 2;

  /// Back-filled from the device ring buffer after an outage (rule V-08).
  static const backfilled = 1 << 3;

  /// Device clock was not NTP-synced when the sample was produced (rule V-03).
  static const clockUnsynced = 1 << 4;

  /// A calibration offset was applied at ingest (BR-06.7).
  static const calibrationApplied = 1 << 5;

  /// Bits that make a reading unusable for threshold evaluation (BR-11.1).
  static const notEvaluable = sensorFault | implausible;
}

/// One metric reading as the evaluator sees it (a `MetricReading` row joined to its sample).
class MetricSample {
  /// Creates a sample.
  const MetricSample({
    required this.metric,
    required this.value,
    required this.recordedAt,
    required this.ingestedAt,
    this.qualityFlags = 0,
  });

  /// Metric code, e.g. `tempC`.
  final String metric;

  /// Value after calibration offsets (BR-06.7).
  final double value;

  /// Device timestamp (NTP-synced, BR-06.5). All prototype scenarios use local wall-clock times.
  final DateTime recordedAt;

  /// Server timestamp assigned at ingest (BR-06.5). `ingestedAt - recordedAt` is the back-fill age.
  final DateTime ingestedAt;

  /// Quality bitmask (see [QualityBits]).
  final int qualityFlags;

  /// True when the reading may be evaluated at all (BR-11.1).
  bool get isEvaluable =>
      (qualityFlags & QualityBits.notEvaluable) == 0 && _isPlausible;

  /// True when the reading is flagged as impossible rather than merely odd.
  ///
  /// Covers both the explicit quality bit and the metric dictionary's own bounds, so a value like 90 °C is
  /// described as implausible even if a new firmware forgot to set bit 2 — the evaluator applies the dictionary
  /// itself rather than trusting the device to have done it.
  bool get isImplausible =>
      (qualityFlags & QualityBits.implausible) != 0 || !_isPlausible;

  /// True when the reading is a sensor fault.
  bool get hasSensorFault => (qualityFlags & QualityBits.sensorFault) != 0;

  /// True when the reading arrived from the device ring buffer (rule V-08).
  bool get isBackfilled =>
      (qualityFlags & QualityBits.backfilled) != 0 || isLateBackfill;

  /// True when the sample describes the past by more than the notification window (BR-11.8).
  bool get isLateBackfill =>
      ingestedAt.difference(recordedAt) > RuleWindows.backfillNotifyWindow;

  bool get _isPlausible {
    final definition = MetricCatalog.byCode(metric);
    return definition == null || definition.isPlausible(value);
  }

  @override
  String toString() =>
      'MetricSample($metric ${value.toStringAsFixed(2)} @ '
      '${recordedAt.toIso8601String()} flags=$qualityFlags)';
}

/// Fixed windows the rules refer to but that no screen edits.
class RuleWindows {
  const RuleWindows._();

  /// Samples ingested more than this long after they were recorded never notify (BR-11.8, BR-06.6).
  static const backfillNotifyWindow = Duration(hours: 6);

  /// Maximum length of a silence window (BR-12.6).
  static const maxSilence = Duration(hours: 24);

  /// Clock skew beyond which a sample is flagged (rule V-09).
  static const clockSkewTolerance = Duration(seconds: 120);
}

/// Photoperiod of a profile, which is what decides the phase — **not** the measured light
/// (`03-implementation/06` §1; using lux would let a broken light sensor silently switch the temperature band).
class PhaseTimeline {
  /// Creates a timeline.
  const PhaseTimeline({this.lightsOnHour = 7, this.photoperiodHours = 12});

  /// Local hour the lights come on, 0–23.
  final int lightsOnHour;

  /// Hours of light per day.
  final int photoperiodHours;

  /// Hour the lights go off, wrapping past midnight if the photoperiod crosses it.
  int get lightsOffHour => (lightsOnHour + photoperiodHours) % 24;

  /// Phase in force at a local wall-clock instant.
  Phase phaseAt(DateTime localTime) {
    final hour = localTime.hour + localTime.minute / 60;
    final start = lightsOnHour.toDouble();
    final end = start + photoperiodHours;

    if (end <= 24) {
      return hour >= start && hour < end ? Phase.day : Phase.night;
    }

    // Pet photoperiods that wrap past midnight are unusual but not impossible; handle them honestly.
    final wrappedEnd = end - 24;
    return (hour >= start || hour < wrappedEnd) ? Phase.day : Phase.night;
  }

  @override
  String toString() =>
      'PhaseTimeline(lights on ${lightsOnHour.toString().padLeft(2, '0')}:00 '
      'for ${photoperiodHours}h)';
}
