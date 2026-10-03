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
