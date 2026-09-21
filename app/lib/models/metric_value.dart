import '../core/status.dart';

/// One metric value as delivered by `GET /api/v1/terrariums/{id}/readings/latest`.
class MetricValue {
  const MetricValue({
    required this.code,
    required this.value,
    required this.unit,
    required this.capturedAt,
    this.status,
    this.qualityFlags = 0,
  });

  /// Metric code, e.g. `tempC`.
  final String code;

  /// Value in the metric's own unit.
  final double value;

  /// Unit string from the server's metric dictionary.
  final String unit;

  /// Device timestamp of the sample — mandatory, so a value can never be rendered without its time.
  final DateTime capturedAt;

  /// Status evaluated by the server (the client never decides bands itself).
  final String? status;

  /// Quality bitmask (see the backend `QualityFlags`).
  final int qualityFlags;

  /// Parsed status for display decisions.
  MetricStatus get displayStatus =>
      status == null ? MetricStatus.noData : MetricStatus.parse(status);

  /// Builds a value from a REST payload entry.
  factory MetricValue.fromJson(Map<String, dynamic> json) => MetricValue(
    code: json['code'] as String? ?? 'unknown',
    value: (json['value'] as num?)?.toDouble() ?? 0,
    unit: json['unit'] as String? ?? '',
    capturedAt: DateTime.parse(json['capturedAt'] as String).toUtc(),
    status: json['status'] as String?,
    qualityFlags: (json['qualityFlags'] as num?)?.toInt() ?? 0,
  );
}

/// Device-level state shown in the dashboard header.
class DeviceSnapshot {
  const DeviceSnapshot({
    required this.deviceId,
    required this.status,
    this.lastSeenAt,
    this.samplingIntervalSec = 60,
    this.firmwareVersion,
  });

  /// Short device id (`sr-xxxxxx`).
  final String deviceId;

  /// `online` / `offline` / `maintenance` / `provisioning` / `revoked`.
  final String status;

  /// Last time the server heard from the device.
  final DateTime? lastSeenAt;

  /// Interval the device is expected to report at; drives the staleness threshold.
  final int samplingIntervalSec;

  /// Firmware version, shown in the fleet view (FR-16).
  final String? firmwareVersion;

  /// True when the device is expected to be producing data.
  bool get isOnline => status == 'online';

  /// True when the device is in maintenance mode: alerts are recorded but not notified (BR-12.6).
  bool get isMaintenance => status == 'maintenance';

  /// Builds a snapshot from a REST payload entry.
  factory DeviceSnapshot.fromJson(Map<String, dynamic> json) => DeviceSnapshot(
    deviceId: json['deviceId'] as String? ?? '',
    status: json['status'] as String? ?? 'offline',
    lastSeenAt: json['lastSeenAt'] == null
        ? null
        : DateTime.parse(json['lastSeenAt'] as String).toUtc(),
    samplingIntervalSec: (json['samplingIntervalSec'] as num?)?.toInt() ?? 60,
    firmwareVersion: json['firmwareVersion'] as String?,
  );
}

/// A band as returned by the server (target bounds only; critical bounds are not needed on the card).
class Band {
  const Band({required this.min, required this.max});

  /// Lower bound of the target band.
  final double min;

  /// Upper bound of the target band.
  final double max;
}
