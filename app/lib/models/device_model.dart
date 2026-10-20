/// IoT Device models matching backend contracts (`/api/v1/devices`).
class DeviceItem {
  final String deviceId;
  final String deviceName;
  final String chipId;
  final String macAddress;
  final String? firmwareVersion;
  final String status;
  final String runtime;
  final String protocol;
  final String? terrariumId;
  final String? terrariumName;
  final String? lastSeenAt;
  final int samplingIntervalSec;
  final int publishIntervalSec;
  final String? provisionedAt;
  final String? revokedAt;
  final String createdAt;
  final double? signalStrengthDbm;
  final double? batteryPct;

  DeviceItem({
    required this.deviceId,
    required this.deviceName,
    required this.chipId,
    required this.macAddress,
    this.firmwareVersion,
    required this.status,
    required this.runtime,
    required this.protocol,
    this.terrariumId,
    this.terrariumName,
    this.lastSeenAt,
    required this.samplingIntervalSec,
    required this.publishIntervalSec,
    this.provisionedAt,
    this.revokedAt,
    required this.createdAt,
    this.signalStrengthDbm,
    this.batteryPct,
  });

  bool get isOnline => runtime.toLowerCase() == 'online';
  bool get isOffline => runtime.toLowerCase() == 'offline';

  factory DeviceItem.fromJson(Map<String, dynamic> json) {
    return DeviceItem(
      deviceId: json['deviceId']?.toString() ?? '',
      deviceName: json['deviceName']?.toString() ?? '',
      chipId: json['chipId']?.toString() ?? json['hardwareChipId']?.toString() ?? '',
      macAddress: json['macAddress']?.toString() ?? '',
      firmwareVersion: json['firmwareVersion']?.toString(),
      status: json['status']?.toString() ?? 'offline',
      runtime: json['runtime']?.toString() ?? json['runtimeStatus']?.toString() ?? 'offline',
      protocol: json['protocol']?.toString() ?? 'mqtt',
      terrariumId: json['terrariumId']?.toString(),
      terrariumName: json['terrariumName']?.toString(),
      lastSeenAt: json['lastSeenAt']?.toString() ?? json['lastSeenAtUtc']?.toString(),
      samplingIntervalSec: (json['samplingIntervalSec'] as num?)?.toInt() ?? 60,
      publishIntervalSec: (json['publishIntervalSec'] as num?)?.toInt() ?? 60,
      provisionedAt: json['provisionedAt']?.toString(),
      revokedAt: json['revokedAt']?.toString(),
      createdAt: json['createdAt']?.toString() ?? '',
      signalStrengthDbm: (json['signalStrengthDbm'] as num?)?.toDouble() ??
          (json['signalDbm'] as num?)?.toDouble(),
      batteryPct: (json['batteryPct'] as num?)?.toDouble() ??
          (json['batteryPercent'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() => {
        'deviceId': deviceId,
        'deviceName': deviceName,
        'chipId': chipId,
        'macAddress': macAddress,
        'firmwareVersion': firmwareVersion,
        'status': status,
        'runtime': runtime,
        'protocol': protocol,
        'terrariumId': terrariumId,
        'terrariumName': terrariumName,
        'lastSeenAt': lastSeenAt,
        'samplingIntervalSec': samplingIntervalSec,
        'publishIntervalSec': publishIntervalSec,
        'provisionedAt': provisionedAt,
        'revokedAt': revokedAt,
        'createdAt': createdAt,
        'signalStrengthDbm': signalStrengthDbm,
        'batteryPct': batteryPct,
      };
}

/// Result returned once when a device is claimed for a terrarium.
class ClaimResult {
  final String deviceId;
  final String secret;
  final String terrariumId;
  final String boundAtUtc;

  ClaimResult({
    required this.deviceId,
    required this.secret,
    required this.terrariumId,
    required this.boundAtUtc,
  });

  factory ClaimResult.fromJson(Map<String, dynamic> json) {
    return ClaimResult(
      deviceId: json['deviceId']?.toString() ?? '',
      secret: json['secret']?.toString() ?? '',
      terrariumId: json['terrariumId']?.toString() ?? '',
      boundAtUtc: json['boundAtUtc']?.toString() ?? '',
    );
  }
}
