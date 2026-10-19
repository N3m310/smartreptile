/// Models for the species-profile catalogue (/api/v1/species-profiles).
///
/// Contains definitions for SpeciesProfileView, SpeciesProfileBand,
/// and request DTOs for creating, updating, and duplicating profiles.
library;

/// Climate zones recognized by the backend Plausibility hint (BR-10.5).
abstract final class ClimateZones {
  static const tropical = 'Tropical';
  static const semiArid = 'SemiArid';
  static const arid = 'Arid';
  static const temperate = 'Temperate';

  static const all = [tropical, semiArid, arid, temperate];

  /// Vietnamese label for display in UI.
  static String labelOf(String zone) {
    switch (zone) {
      case tropical:
        return 'Nhiệt đới (Tropical)';
      case semiArid:
        return 'Bán hoang mạc (Semi-Arid)';
      case arid:
        return 'Hoang mạc (Arid)';
      case temperate:
        return 'Ôn đới (Temperate)';
      default:
        return zone;
    }
  }
}

/// One band on a species profile (representing environmental ranges).
class SpeciesProfileBand {
  const SpeciesProfileBand({
    required this.metric,
    required this.unit,
    required this.phase,
    required this.targetMin,
    required this.targetMax,
    this.criticalMin,
    this.criticalMax,
    this.dwellWarnMinutes = 5,
    this.dwellCritMinutes = 2,
    this.recoveryMargin = 0.5,
    this.enabled = true,
    this.sourceRef,
    this.sourceUrl,
  });

  /// Metric key from dictionary, e.g. `tempC`, `humidity`, `uv`.
  final String metric;

  /// Display unit, e.g. `°C`, `%`, `UVI`.
  final String unit;

  /// Phase when band applies: `any`, `day`, `night`.
  final String phase;

  /// Target band lower bound.
  final double targetMin;

  /// Target band upper bound.
  final double targetMax;

  /// Lower critical bound. Null when derived from targetMin - recoveryMargin.
  final double? criticalMin;

  /// Upper critical bound. Null when derived from targetMax + recoveryMargin.
  final double? criticalMax;

  /// Minutes out of band before Warning alert. Defaults to 5.
  final int dwellWarnMinutes;

  /// Minutes critically out of band before escalation. Defaults to 2.
  final int dwellCritMinutes;

  /// Hysteresis recovery margin.
  final double recoveryMargin;

  /// Whether evaluator tracks this band.
  final bool enabled;

  /// Reference/citation source for these values.
  final String? sourceRef;

  /// URL link to source citation if available.
  final String? sourceUrl;

  factory SpeciesProfileBand.fromJson(Map<String, dynamic> json) {
    return SpeciesProfileBand(
      metric: (json['metric'] as String?) ?? '',
      unit: (json['unit'] as String?) ?? '',
      phase: (json['phase'] as String?) ?? 'any',
      targetMin: (json['targetMin'] as num?)?.toDouble() ?? 0.0,
      targetMax: (json['targetMax'] as num?)?.toDouble() ?? 0.0,
      criticalMin: (json['criticalMin'] as num?)?.toDouble(),
      criticalMax: (json['criticalMax'] as num?)?.toDouble(),
      dwellWarnMinutes: (json['dwellWarnMinutes'] as num?)?.toInt() ?? 5,
      dwellCritMinutes: (json['dwellCritMinutes'] as num?)?.toInt() ?? 2,
      recoveryMargin: (json['recoveryMargin'] as num?)?.toDouble() ?? 0.5,
      enabled: (json['enabled'] as bool?) ?? true,
      sourceRef: json['sourceRef'] as String?,
      sourceUrl: json['sourceUrl'] as String?,
    );
  }

  Map<String, dynamic> toJson() => {
    'metric': metric,
    'unit': unit,
    'phase': phase,
    'targetMin': targetMin,
    'targetMax': targetMax,
    if (criticalMin != null) 'criticalMin': criticalMin,
    if (criticalMax != null) 'criticalMax': criticalMax,
    'dwellWarnMinutes': dwellWarnMinutes,
    'dwellCritMinutes': dwellCritMinutes,
    'recoveryMargin': recoveryMargin,
    'enabled': enabled,
    if (sourceRef != null) 'sourceRef': sourceRef,
    if (sourceUrl != null) 'sourceUrl': sourceUrl,
  };

  SpeciesProfileBand copyWith({
    String? metric,
    String? unit,
    String? phase,
    double? targetMin,
    double? targetMax,
    double? criticalMin,
    double? criticalMax,
    int? dwellWarnMinutes,
    int? dwellCritMinutes,
    double? recoveryMargin,
    bool? enabled,
    String? sourceRef,
    String? sourceUrl,
  }) {
    return SpeciesProfileBand(
      metric: metric ?? this.metric,
      unit: unit ?? this.unit,
      phase: phase ?? this.phase,
      targetMin: targetMin ?? this.targetMin,
      targetMax: targetMax ?? this.targetMax,
      criticalMin: criticalMin ?? this.criticalMin,
      criticalMax: criticalMax ?? this.criticalMax,
      dwellWarnMinutes: dwellWarnMinutes ?? this.dwellWarnMinutes,
      dwellCritMinutes: dwellCritMinutes ?? this.dwellCritMinutes,
      recoveryMargin: recoveryMargin ?? this.recoveryMargin,
      enabled: enabled ?? this.enabled,
      sourceRef: sourceRef ?? this.sourceRef,
      sourceUrl: sourceUrl ?? this.sourceUrl,
    );
  }
}

/// A species profile view returned by GET /api/v1/species-profiles.
class SpeciesProfileView {
  const SpeciesProfileView({
    required this.id,
    required this.name,
    required this.scientificName,
    required this.climateZone,
    required this.isBuiltIn,
    required this.photoperiodHours,
    required this.lightsOnLocalTime,
    required this.lightThresholdLux,
    required this.minLightHoursPerDay,
    this.notes,
    this.bands = const [],
    this.imageUrl,
    this.imageUpdatedAt,
  });

  /// Unique UUID of the profile.
  final String id;

  /// Common display name (e.g. "Rồng Úc (Bearded Dragon)").
  final String name;

  /// Latin scientific name (e.g. "Pogona vitticeps").
  final String scientificName;

  /// Climate zone: Tropical, SemiArid, Arid, Temperate.
  final String climateZone;

  /// True for pre-seeded built-in profiles (immutable).
  final bool isBuiltIn;

  /// Duration of photoperiod in hours (e.g. 12.0).
  final double photoperiodHours;

  /// Time when photoperiod begins in HH:mm format (e.g. "07:00").
  final String lightsOnLocalTime;

  /// Illuminance threshold above which time counts towards light hours (Lux).
  final double lightThresholdLux;

  /// Minimum light hours per day before deficit alert.
  final double minLightHoursPerDay;

  /// Husbandry summary, care caveats, and notes.
  final String? notes;

  /// List of sensor bands for temperature, humidity, UV, etc.
  final List<SpeciesProfileBand> bands;

  /// Path to profile image relative to API root (e.g. `/api/v1/species-profiles/{id}/image`).
  final String? imageUrl;

  /// Last updated timestamp of image for cache busting.
  final DateTime? imageUpdatedAt;

  factory SpeciesProfileView.fromJson(Map<String, dynamic> json) {
    final rawBands = json['bands'] as List<dynamic>? ?? const [];
    final bandsList = rawBands
        .whereType<Map<String, dynamic>>()
        .map(SpeciesProfileBand.fromJson)
        .toList();

    return SpeciesProfileView(
      id: (json['id'] as String?) ?? '',
      name: (json['name'] as String?) ?? '',
      scientificName: (json['scientificName'] as String?) ?? '',
      climateZone: (json['climateZone'] as String?) ?? ClimateZones.tropical,
      isBuiltIn: (json['isBuiltIn'] as bool?) ?? false,
      photoperiodHours: (json['photoperiodHours'] as num?)?.toDouble() ?? 12.0,
      lightsOnLocalTime: (json['lightsOnLocalTime'] as String?) ?? '07:00',
      lightThresholdLux: (json['lightThresholdLux'] as num?)?.toDouble() ?? 1000.0,
      minLightHoursPerDay: (json['minLightHoursPerDay'] as num?)?.toDouble() ?? 8.0,
      notes: json['notes'] as String?,
      bands: bandsList,
      imageUrl: json['imageUrl'] as String?,
      imageUpdatedAt: json['imageUpdatedAt'] != null
          ? DateTime.tryParse(json['imageUpdatedAt'] as String)?.toUtc()
          : null,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'name': name,
    'scientificName': scientificName,
    'climateZone': climateZone,
    'isBuiltIn': isBuiltIn,
    'photoperiodHours': photoperiodHours,
    'lightsOnLocalTime': lightsOnLocalTime,
    'lightThresholdLux': lightThresholdLux,
    'minLightHoursPerDay': minLightHoursPerDay,
    if (notes != null) 'notes': notes,
    'bands': bands.map((b) => b.toJson()).toList(),
    if (imageUrl != null) 'imageUrl': imageUrl,
    if (imageUpdatedAt != null) 'imageUpdatedAt': imageUpdatedAt!.toIso8601String(),
  };

  SpeciesProfileView copyWith({
    String? id,
    String? name,
    String? scientificName,
    String? climateZone,
    bool? isBuiltIn,
    double? photoperiodHours,
    String? lightsOnLocalTime,
    double? lightThresholdLux,
    double? minLightHoursPerDay,
    String? notes,
    List<SpeciesProfileBand>? bands,
    String? imageUrl,
    DateTime? imageUpdatedAt,
  }) {
    return SpeciesProfileView(
      id: id ?? this.id,
      name: name ?? this.name,
      scientificName: scientificName ?? this.scientificName,
      climateZone: climateZone ?? this.climateZone,
      isBuiltIn: isBuiltIn ?? this.isBuiltIn,
      photoperiodHours: photoperiodHours ?? this.photoperiodHours,
      lightsOnLocalTime: lightsOnLocalTime ?? this.lightsOnLocalTime,
      lightThresholdLux: lightThresholdLux ?? this.lightThresholdLux,
      minLightHoursPerDay: minLightHoursPerDay ?? this.minLightHoursPerDay,
      notes: notes ?? this.notes,
      bands: bands ?? this.bands,
      imageUrl: imageUrl ?? this.imageUrl,
      imageUpdatedAt: imageUpdatedAt ?? this.imageUpdatedAt,
    );
  }
}

/// Input model for a single band in create/update requests.
class SpeciesProfileBandInput {
  const SpeciesProfileBandInput({
    required this.metric,
    this.phase = 'any',
    required this.targetMin,
    required this.targetMax,
    this.criticalMin,
    this.criticalMax,
    this.dwellWarnMinutes = 5,
    this.dwellCritMinutes = 2,
    this.recoveryMargin = 0.5,
    this.enabled = true,
    this.sourceRef,
  });

  final String metric;
  final String phase;
  final double targetMin;
  final double targetMax;
  final double? criticalMin;
  final double? criticalMax;
  final int dwellWarnMinutes;
  final int dwellCritMinutes;
  final double recoveryMargin;
  final bool enabled;
  final String? sourceRef;

  factory SpeciesProfileBandInput.fromBand(SpeciesProfileBand band) {
    return SpeciesProfileBandInput(
      metric: band.metric,
      phase: band.phase,
      targetMin: band.targetMin,
      targetMax: band.targetMax,
      criticalMin: band.criticalMin,
      criticalMax: band.criticalMax,
      dwellWarnMinutes: band.dwellWarnMinutes,
      dwellCritMinutes: band.dwellCritMinutes,
      recoveryMargin: band.recoveryMargin,
      enabled: band.enabled,
      sourceRef: band.sourceRef,
    );
  }

  Map<String, dynamic> toJson() => {
    'metric': metric,
    'phase': phase,
    'targetMin': targetMin,
    'targetMax': targetMax,
    if (criticalMin != null) 'criticalMin': criticalMin,
    if (criticalMax != null) 'criticalMax': criticalMax,
    'dwellWarnMinutes': dwellWarnMinutes,
    'dwellCritMinutes': dwellCritMinutes,
    'recoveryMargin': recoveryMargin,
    'enabled': enabled,
    if (sourceRef != null && sourceRef!.isNotEmpty) 'sourceRef': sourceRef,
  };
}

/// Request body for POST /api/v1/species-profiles.
class CreateSpeciesProfileRequest {
  const CreateSpeciesProfileRequest({
    required this.name,
    this.scientificName,
    required this.climateZone,
    this.photoperiodHours = 12.0,
    this.lightsOnLocalTime = '07:00',
    this.lightThresholdLux = 1000.0,
    this.minLightHoursPerDay = 8.0,
    this.notes,
    this.bands = const [],
  });

  final String name;
  final String? scientificName;
  final String climateZone;
  final double photoperiodHours;
  final String lightsOnLocalTime;
  final double lightThresholdLux;
  final double minLightHoursPerDay;
  final String? notes;
  final List<SpeciesProfileBandInput> bands;

  Map<String, dynamic> toJson() => {
    'name': name,
    if (scientificName != null) 'scientificName': scientificName,
    'climateZone': climateZone,
    'photoperiodHours': photoperiodHours,
    'lightsOnLocalTime': lightsOnLocalTime,
    'lightThresholdLux': lightThresholdLux,
    'minLightHoursPerDay': minLightHoursPerDay,
    if (notes != null && notes!.isNotEmpty) 'notes': notes,
    'bands': bands.map((b) => b.toJson()).toList(),
  };
}

/// Request body for PATCH /api/v1/species-profiles/{id}.
class UpdateSpeciesProfileRequest {
  const UpdateSpeciesProfileRequest({
    this.name,
    this.scientificName,
    this.climateZone,
    this.photoperiodHours,
    this.lightsOnLocalTime,
    this.lightThresholdLux,
    this.minLightHoursPerDay,
    this.notes,
    this.bands,
  });

  final String? name;
  final String? scientificName;
  final String? climateZone;
  final double? photoperiodHours;
  final String? lightsOnLocalTime;
  final double? lightThresholdLux;
  final double? minLightHoursPerDay;
  final String? notes;
  final List<SpeciesProfileBandInput>? bands;

  Map<String, dynamic> toJson() => {
    if (name != null) 'name': name,
    if (scientificName != null) 'scientificName': scientificName,
    if (climateZone != null) 'climateZone': climateZone,
    if (photoperiodHours != null) 'photoperiodHours': photoperiodHours,
    if (lightsOnLocalTime != null) 'lightsOnLocalTime': lightsOnLocalTime,
    if (lightThresholdLux != null) 'lightThresholdLux': lightThresholdLux,
    if (minLightHoursPerDay != null) 'minLightHoursPerDay': minLightHoursPerDay,
    if (notes != null) 'notes': notes,
    if (bands != null) 'bands': bands!.map((b) => b.toJson()).toList(),
  };
}

/// Request body for POST /api/v1/species-profiles/{id}/duplicate.
class DuplicateSpeciesProfileRequest {
  const DuplicateSpeciesProfileRequest({this.name});

  final String? name;

  Map<String, dynamic> toJson() => {
    if (name != null && name!.isNotEmpty) 'name': name,
  };
}
