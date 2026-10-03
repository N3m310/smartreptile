import 'package:flutter/foundation.dart';
import '../models/terrarium_models.dart';

class AppDataProvider extends ChangeNotifier {
  AppDataProvider() {
    _terrariums = List.from(mockTerrariums);
    _devices = List.from(mockDevices);
    _alerts = List.from(mockAlerts);
    _selectedTerrariumId = 'T01';
  }

  late List<TerrariumModel> _terrariums;
  late List<DeviceModel> _devices;
  late List<AlertModel> _alerts;
  late String _selectedTerrariumId;

  List<TerrariumModel> get terrariums => _terrariums;
  List<DeviceModel> get devices => _devices;
  List<AlertModel> get alerts => _alerts;
  String get selectedTerrariumId => _selectedTerrariumId;

  TerrariumModel get selectedTerrarium {
    return _terrariums.firstWhere(
      (t) => t.id == _selectedTerrariumId,
      orElse: () => _terrariums.first,
    );
  }

  int get pendingAlertsCount =>
      _alerts.where((a) => !a.isResolved).length;

  int get onlineDevicesCount =>
      _devices.where((d) => d.isOnline).length;

  List<HistoryPoint> get currentHistory =>
      generateMockHistory(_selectedTerrariumId);

  List<HistoryPoint> historyFor(String terrariumId) =>
      generateMockHistory(terrariumId);

  void selectTerrarium(String id) {
    if (_selectedTerrariumId != id) {
      _selectedTerrariumId = id;
      notifyListeners();
    }
  }

  void resolveAlert(String alertId) {
    final index = _alerts.indexWhere((a) => a.id == alertId);
    if (index != -1) {
      _alerts[index].isResolved = true;
      _alerts[index].resolvedAt = 'Vừa xong';
      notifyListeners();
    }
  }

  void updateThresholds(String terrariumId, ThresholdModel newThresholds) {
    final index = _terrariums.indexWhere((t) => t.id == terrariumId);
    if (index != -1) {
      _terrariums[index].thresholds = newThresholds;
      notifyListeners();
    }
  }

  void addTerrarium({
    required String name,
    required String species,
    required String description,
  }) {
    final newId = 'T0${_terrariums.length + 1}';
    final added = TerrariumModel(
      id: newId,
      name: name,
      species: species,
      image:
          'https://images.unsplash.com/photo-1548767797-d8c844163c4c?auto=format&fit=crop&w=800&q=80',
      status: 'normal',
      currentTemp: 28.5,
      currentHumidity: 50.0,
      currentLight: 400,
      thresholds: ThresholdModel(
        tempMin: 24.0,
        tempMax: 32.0,
        humidityMin: 40.0,
        humidityMax: 60.0,
        lightMin: 200,
        lightMax: 800,
      ),
      description: description.isEmpty
          ? 'Chuồng nuôi sinh thái mới được cấu hình.'
          : description,
      deviceCount: 3,
    );

    _terrariums.add(added);
    notifyListeners();
  }
}

