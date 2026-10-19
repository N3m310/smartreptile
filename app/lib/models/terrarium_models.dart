import 'dart:math';

class ThresholdModel {
  ThresholdModel({
    required this.tempMin,
    required this.tempMax,
    required this.humidityMin,
    required this.humidityMax,
    required this.lightMin,
    required this.lightMax,
  });

  double tempMin;
  double tempMax;
  double humidityMin;
  double humidityMax;
  int lightMin;
  int lightMax;

  ThresholdModel copyWith({
    double? tempMin,
    double? tempMax,
    double? humidityMin,
    double? humidityMax,
    int? lightMin,
    int? lightMax,
  }) {
    return ThresholdModel(
      tempMin: tempMin ?? this.tempMin,
      tempMax: tempMax ?? this.tempMax,
      humidityMin: humidityMin ?? this.humidityMin,
      humidityMax: humidityMax ?? this.humidityMax,
      lightMin: lightMin ?? this.lightMin,
      lightMax: lightMax ?? this.lightMax,
    );
  }
}

class TerrariumModel {
  TerrariumModel({
    required this.id,
    required this.name,
    required this.species,
    required this.image,
    required this.status,
    required this.currentTemp,
    required this.currentHumidity,
    required this.currentLight,
    required this.thresholds,
    required this.description,
    required this.deviceCount,
    this.speciesProfileId,
    this.timeZoneId,
    this.location,
  });

  final String id;
  final String name;
  final String species;
  final String image;
  String status; // 'normal', 'warning', 'danger'
  double currentTemp;
  double currentHumidity;
  int currentLight;
  ThresholdModel thresholds;
  final String description;
  final int deviceCount;
  final String? speciesProfileId;
  final String? timeZoneId;
  final String? location;

  static String defaultImageForSpecies(String species) {
    final lower = species.toLowerCase();
    if (lower.contains('rồng') || lower.contains('dragon') || lower.contains('arid') || lower.contains('sa mạc')) {
      return 'https://images.unsplash.com/photo-1574063413132-355dbfd83e25?auto=format&fit=crop&w=800&q=80';
    } else if (lower.contains('trăn') || lower.contains('python') || lower.contains('rắn') || lower.contains('snake') || lower.contains('tropical')) {
      return 'https://images.unsplash.com/photo-1531386151447-fd76ad50012f?auto=format&fit=crop&w=800&q=80';
    } else if (lower.contains('tắc kè') || lower.contains('gecko') || lower.contains('chameleon')) {
      return 'https://images.unsplash.com/photo-1508817628294-5a453fa0b8fb?auto=format&fit=crop&w=800&q=80';
    }
    return 'https://images.unsplash.com/photo-1548767797-d8c844163c4c?auto=format&fit=crop&w=800&q=80';
  }

  factory TerrariumModel.fromApi(
    Map<String, dynamic> json, {
    ThresholdModel? thresholds,
  }) {
    final id = json['id']?.toString() ?? '';
    final name = json['name']?.toString() ?? 'Terrarium';
    final species = json['speciesName']?.toString() ?? 'Chưa xác định';
    final desc = json['description']?.toString() ?? '';
    final dev = json['device'];
    final deviceCount = dev != null ? 1 : 0;
    final openAlertCount = (json['openAlertCount'] as num?)?.toInt() ?? 0;
    final status = openAlertCount > 0 ? 'warning' : 'normal';

    return TerrariumModel(
      id: id,
      name: name,
      species: species,
      image: defaultImageForSpecies(species),
      status: status,
      currentTemp: 28.0,
      currentHumidity: 55.0,
      currentLight: 400,
      thresholds: thresholds ??
          ThresholdModel(
            tempMin: 22.0,
            tempMax: 32.0,
            humidityMin: 40.0,
            humidityMax: 70.0,
            lightMin: 150,
            lightMax: 800,
          ),
      description: desc.isNotEmpty ? desc : 'Chuồng nuôi sinh thái bò sát.',
      deviceCount: deviceCount,
      speciesProfileId: json['speciesProfileId']?.toString(),
      timeZoneId: json['timeZoneId']?.toString(),
      location: json['location']?.toString(),
    );
  }
}

class DeviceModel {
  DeviceModel({
    required this.id,
    required this.name,
    required this.type,
    required this.terrariumId,
    required this.terrariumName,
    required this.isOnline,
    required this.firmware,
    required this.lastSeen,
    required this.ipAddress,
  });

  final String id;
  final String name;
  final String type;
  final String terrariumId;
  final String terrariumName;
  bool isOnline;
  final String firmware;
  final String lastSeen;
  final String ipAddress;

  factory DeviceModel.fromApi(Map<String, dynamic> json) {
    final status = json['status']?.toString().toLowerCase() ?? 'offline';
    final runtime = json['runtime']?.toString().toLowerCase() ?? '';
    final isOnline = runtime == 'online' || status == 'online';
    return DeviceModel(
      id: json['deviceId']?.toString() ?? '',
      name: json['deviceName']?.toString() ?? 'Thiết bị ESP32',
      type: json['protocol']?.toString() ?? 'ESP32 MQTT Node',
      terrariumId: json['terrariumId']?.toString() ?? '',
      terrariumName: json['terrariumName']?.toString() ?? 'Chưa gán chuồng',
      isOnline: isOnline,
      firmware: json['firmwareVersion']?.toString() ?? 'v1.0.0',
      lastSeen: json['lastSeenAt']?.toString() ?? 'Chưa có dữ liệu',
      ipAddress: json['macAddress']?.toString() ?? '00:00:00:00:00:00',
    );
  }
}

class AlertModel {
  AlertModel({
    required this.id,
    required this.title,
    required this.message,
    required this.terrariumId,
    required this.terrariumName,
    required this.type,
    required this.severity, // 'danger' or 'warning'
    required this.isResolved,
    required this.timestamp,
    this.resolvedAt,
  });

  final String id;
  final String title;
  final String message;
  final String terrariumId;
  final String terrariumName;
  final String type;
  final String severity;
  bool isResolved;
  final String timestamp;
  String? resolvedAt;

  factory AlertModel.fromApi(Map<String, dynamic> json) {
    final id = json['id']?.toString() ?? '';
    final metric = json['metric']?.toString() ?? '';
    final family = json['family']?.toString().toLowerCase() ?? '';
    final severityStr = json['severity']?.toString().toLowerCase() ?? 'warning';
    final severity = severityStr == 'critical' ? 'danger' : 'warning';
    final state = json['state']?.toString().toLowerCase() ?? 'active';
    final isResolved = state == 'resolved';

    String title;
    String message;
    String type;

    if (metric.contains('temp')) {
      type = 'temp_high';
      title = 'Cảnh báo nhiệt độ';
      final val = json['triggeringValue'];
      message = val != null
          ? 'Nhiệt độ hiện tại: $val°C (vượt ngưỡng cho phép)'
          : 'Nhiệt độ chuồng lệch ngưỡng an toàn.';
    } else if (metric.contains('hum')) {
      type = 'humidity_high';
      title = 'Cảnh báo độ ẩm';
      final val = json['triggeringValue'];
      message = val != null
          ? 'Độ ẩm hiện tại: $val% (vượt ngưỡng cho phép)'
          : 'Độ ẩm chuồng lệch ngưỡng an toàn.';
    } else if (family == 'device') {
      type = 'device_offline';
      title = 'Mất kết nối thiết bị';
      message = 'Bộ điều khiển IoT không phản hồi tín hiệu.';
    } else {
      type = 'temp_high';
      title = 'Cảnh báo vi khí hậu';
      message = 'Chỉ số đo vượt ngưỡng quy định.';
    }

    return AlertModel(
      id: id,
      title: title,
      message: message,
      terrariumId: json['terrariumId']?.toString() ?? '',
      terrariumName: 'Chuồng nuôi',
      type: type,
      severity: severity,
      isResolved: isResolved,
      timestamp: json['triggeredAt']?.toString() ?? 'Vừa xong',
      resolvedAt: json['resolvedAt']?.toString(),
    );
  }
}

class HistoryPoint {
  HistoryPoint({
    required this.time,
    required this.temperature,
    required this.humidity,
    required this.light,
    required this.status,
  });

  final String time;
  final double temperature;
  final double humidity;
  final int light;
  final String status;
}

/// Initial Terrariums
final List<TerrariumModel> mockTerrariums = [
  TerrariumModel(
    id: 'T01',
    name: 'Terrarium #01',
    species: 'Rồng Úc (Bearded Dragon)',
    image: 'https://images.unsplash.com/photo-1574063413132-355dbfd83e25?auto=format&fit=crop&w=800&q=80',
    status: 'normal',
    currentTemp: 34.5,
    currentHumidity: 38.0,
    currentLight: 850,
    thresholds: ThresholdModel(
      tempMin: 28.0,
      tempMax: 38.0,
      humidityMin: 30.0,
      humidityMax: 45.0,
      lightMin: 500,
      lightMax: 1200,
    ),
    description:
        'Khu vực sa mạc khô nóng, cần nhiệt độ sưởi ấm cao vào ban ngày.',
    deviceCount: 3,
  ),
  TerrariumModel(
    id: 'T02',
    name: 'Terrarium #02',
    species: 'Tắc kè Leopard (Leopard Gecko)',
    image: 'https://images.unsplash.com/photo-1508817628294-5a453fa0b8fb?auto=format&fit=crop&w=800&q=80',
    status: 'warning',
    currentTemp: 29.8,
    currentHumidity: 42.0,
    currentLight: 120,
    thresholds: ThresholdModel(
      tempMin: 26.0,
      tempMax: 32.0,
      humidityMin: 30.0,
      humidityMax: 40.0,
      lightMin: 50,
      lightMax: 400,
    ),
    description: 'Loài hoạt động hoàng hôn/đêm, kiểm soát nhiệt nền ổn định.',
    deviceCount: 3,
  ),
  TerrariumModel(
    id: 'T03',
    name: 'Terrarium #03',
    species: 'Rắn Ngô (Corn Snake)',
    image: 'https://images.unsplash.com/photo-1531386151447-fd76ad50012f?auto=format&fit=crop&w=800&q=80',
    status: 'danger',
    currentTemp: 33.2,
    currentHumidity: 32.0,
    currentLight: 340,
    thresholds: ThresholdModel(
      tempMin: 24.0,
      tempMax: 30.0,
      humidityMin: 45.0,
      humidityMax: 65.0,
      lightMin: 100,
      lightMax: 600,
    ),
    description: 'Cần duy trì độ ẩm vừa phải để hỗ trợ chu kỳ lột xác an toàn.',
    deviceCount: 3,
  ),
];

/// Initial Devices
final List<DeviceModel> mockDevices = [
  DeviceModel(
    id: 'DEV-01-ESP',
    name: 'ESP32 Node 01',
    type: 'ESP32 Controller',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    isOnline: true,
    firmware: 'v1.4.2',
    lastSeen: '1 phút trước',
    ipAddress: '192.168.1.101',
  ),
  DeviceModel(
    id: 'DEV-01-DHT',
    name: 'DHT22 Cảm biến T/H 01',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    isOnline: true,
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  ),
  DeviceModel(
    id: 'DEV-01-LDR',
    name: 'LDR Ánh sáng 01',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    isOnline: true,
    firmware: 'v1.1.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ ADC34',
  ),
  DeviceModel(
    id: 'DEV-02-ESP',
    name: 'ESP32 Node 02',
    type: 'ESP32 Controller',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    isOnline: true,
    firmware: 'v1.4.2',
    lastSeen: '3 phút trước',
    ipAddress: '192.168.1.102',
  ),
  DeviceModel(
    id: 'DEV-02-DHT',
    name: 'DHT22 Cảm biến T/H 02',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    isOnline: true,
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  ),
  DeviceModel(
    id: 'DEV-02-LDR',
    name: 'LDR Ánh sáng 02',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    isOnline: false,
    firmware: 'v1.1.0',
    lastSeen: '45 phút trước',
    ipAddress: 'Nội bộ ADC34',
  ),
  DeviceModel(
    id: 'DEV-03-ESP',
    name: 'ESP32 Node 03',
    type: 'ESP32 Controller',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    isOnline: true,
    firmware: 'v1.4.2',
    lastSeen: 'Vừa xong',
    ipAddress: '192.168.1.103',
  ),
  DeviceModel(
    id: 'DEV-03-DHT',
    name: 'DHT22 Cảm biến T/H 03',
    type: 'DHT22 Cảm biến Nhiệt/Ẩm',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    isOnline: true,
    firmware: 'v1.2.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ GPIO4',
  ),
  DeviceModel(
    id: 'DEV-03-LDR',
    name: 'LDR Ánh sáng 03',
    type: 'LDR Cảm biến Ánh sáng',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    isOnline: true,
    firmware: 'v1.1.0',
    lastSeen: 'Vừa xong',
    ipAddress: 'Nội bộ ADC34',
  ),
];

/// Initial Alerts
final List<AlertModel> mockAlerts = [
  AlertModel(
    id: 'ALT-101',
    title: 'Nhiệt độ vượt ngưỡng an toàn',
    message: 'Nhiệt độ đo được 33.2°C vượt ngưỡng tối đa (30.0°C) cho Rắn Ngô.',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    type: 'temp_high',
    severity: 'danger',
    isResolved: false,
    timestamp: '10 phút trước',
  ),
  AlertModel(
    id: 'ALT-102',
    title: 'Thiết bị mất kết nối ngoại tuyến',
    message: 'Cảm biến LDR Ánh sáng 02 không phản hồi telemetry trong 45 phút.',
    terrariumId: 'T02',
    terrariumName: 'Terrarium #02 (Tắc kè Leopard)',
    type: 'device_offline',
    severity: 'warning',
    isResolved: false,
    timestamp: '45 phút trước',
  ),
  AlertModel(
    id: 'ALT-103',
    title: 'Độ ẩm xuống mức thấp',
    message: 'Độ ẩm 32.0% thấp hơn ngưỡng tối thiểu 45.0% đối với Rắn Ngô.',
    terrariumId: 'T03',
    terrariumName: 'Terrarium #03 (Rắn Ngô)',
    type: 'humidity_low',
    severity: 'danger',
    isResolved: false,
    timestamp: '1 giờ trước',
  ),
  AlertModel(
    id: 'ALT-104',
    title: 'Nhiệt độ ban đêm hạ thấp (Đã xử lý)',
    message:
        'Nhiệt độ giảm xuống 26.5°C tại Terrarium #01. Đã bật sưởi dự phòng.',
    terrariumId: 'T01',
    terrariumName: 'Terrarium #01 (Rồng Úc)',
    type: 'temp_low',
    severity: 'warning',
    isResolved: true,
    timestamp: 'Hôm qua lúc 22:30',
    resolvedAt: 'Hôm qua lúc 22:45',
  ),
];

List<HistoryPoint> generateMockHistory(String terrariumId) {
  final points = <HistoryPoint>[];
  final baseTemp = terrariumId == 'T01'
      ? 34.0
      : terrariumId == 'T02'
      ? 29.0
      : 27.0;
  final baseHum = terrariumId == 'T01'
      ? 38.0
      : terrariumId == 'T02'
      ? 38.0
      : 55.0;
  final baseLight = terrariumId == 'T01'
      ? 800
      : terrariumId == 'T02'
      ? 150
      : 300;

  for (int i = 23; i >= 0; i--) {
    final hour = '${(24 - i).toString().padLeft(2, '0')}:00';
    final tempOffset = sin(i / 3.0) * 3 + ((i % 5) * 0.15 - 0.3);
    final humOffset = -sin(i / 3.0) * 4 + ((i % 4) * 0.2 - 0.4);
    final lightFactor = (i >= 6 && i <= 18) ? 1.0 : 0.05;

    final temp = double.parse((baseTemp + tempOffset).toStringAsFixed(1));
    final hum = double.parse((baseHum + humOffset).toStringAsFixed(1));
    final light = max(0, (baseLight * lightFactor + (i % 7) * 8).round());

    String status = 'normal';
    if (terrariumId == 'T03' && i < 3) {
      status = 'danger';
    } else if (terrariumId == 'T02' && i < 2) {
      status = 'warning';
    }

    points.add(
      HistoryPoint(
        time: hour,
        temperature: temp,
        humidity: hum,
        light: light,
        status: status,
      ),
    );
  }
  return points;
}
