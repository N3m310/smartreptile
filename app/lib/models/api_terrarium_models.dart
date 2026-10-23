class ApiTerrarium {
  const ApiTerrarium({
    required this.id,
    required this.name,
    required this.speciesProfileId,
    required this.speciesName,
    required this.location,
    required this.description,
    required this.timeZoneId,
    required this.rowVersion,
    required this.device,
    required this.latestSampleAt,
    required this.openAlertCount,
    this.ownerEmail,
    this.ownerUserId,
  });

  final String id;
  final String name;
  final String speciesProfileId;
  final String speciesName;
  final String? location;
  final String? description;
  final String timeZoneId;
  final String? rowVersion;
  final Map<String, dynamic>? device;
  final DateTime? latestSampleAt;
  final int openAlertCount;

  /// Email of the owning account. Present only on an administrator's answers (`data.read.any`), whose list can hold
  /// enclosures belonging to accounts they did not name (2026-10-23).
  final String? ownerEmail;

  /// The owning account's id, present on the same answers as [ownerEmail]. Null for an enclosure an administrator
  /// created and has not assigned yet.
  final String? ownerUserId;

  factory ApiTerrarium.fromJson(Map<String, dynamic> json) => ApiTerrarium(
    id: json['id'] as String,
    name: json['name'] as String,
    speciesProfileId: json['speciesProfileId'] as String,
    speciesName: json['speciesName'] as String,
    location: json['location'] as String?,
    description: json['description'] as String?,
    timeZoneId: json['timeZoneId'] as String? ?? '',
    rowVersion: json['rowVersion'] as String?,
    device: json['device'] as Map<String, dynamic>?,
    latestSampleAt: json['latestSampleAt'] == null
        ? null
        : DateTime.parse(json['latestSampleAt'] as String).toLocal(),
    openAlertCount: (json['openAlertCount'] as num?)?.toInt() ?? 0,
    ownerEmail: json['ownerEmail'] as String?,
    ownerUserId: json['ownerUserId'] as String?,
  );
}

class ApiSpeciesProfile {
  const ApiSpeciesProfile({
    required this.id,
    required this.name,
    required this.scientificName,
    required this.isBuiltIn,
    required this.photoperiodHours,
    required this.lightsOnLocalTime,
    required this.lightThresholdLux,
    required this.minLightHoursPerDay,
    required this.notes,
    required this.imageUrl,
    required this.imageUpdatedAt,
    required this.bands,
  });

  final String id;
  final String name;
  final String scientificName;
  final bool isBuiltIn;
  final double photoperiodHours;
  final String lightsOnLocalTime;
  final double lightThresholdLux;
  final double minLightHoursPerDay;
  final String? notes;
  final String? imageUrl;
  final String? imageUpdatedAt;
  final List<Map<String, dynamic>> bands;

  factory ApiSpeciesProfile.fromJson(Map<String, dynamic> json) =>
      ApiSpeciesProfile(
        id: json['id'] as String,
        name: json['name'] as String,
        scientificName: json['scientificName'] as String? ?? '',
        isBuiltIn: json['isBuiltIn'] as bool? ?? false,
        photoperiodHours:
            (json['photoperiodHours'] as num?)?.toDouble() ?? 0,
        lightsOnLocalTime: json['lightsOnLocalTime'] as String? ?? '',
        lightThresholdLux:
            (json['lightThresholdLux'] as num?)?.toDouble() ?? 0,
        minLightHoursPerDay:
            (json['minLightHoursPerDay'] as num?)?.toDouble() ?? 0,
        notes: json['notes'] as String?,
        imageUrl: json['imageUrl'] as String?,
        imageUpdatedAt: json['imageUpdatedAt'] as String?,
        bands: [
          for (final band in (json['bands'] as List?) ?? const [])
            if (band is Map<String, dynamic>) band,
        ],
      );
}
