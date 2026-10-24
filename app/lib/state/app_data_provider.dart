import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/terrarium_models.dart';

class AppDataProvider extends ChangeNotifier {
  AppDataProvider({ApiClient? api}) {
    _api = api;
    _terrariums = List.from(mockTerrariums);
    _devices = List.from(mockDevices);
    _alerts = List.from(mockAlerts);
    _selectedTerrariumId = _terrariums.first.id;
  }

  ApiClient? _api;

  late List<TerrariumModel> _terrariums;
  late List<DeviceModel> _devices;
  late List<AlertModel> _alerts;
  late String _selectedTerrariumId;

  bool _isLive = false;
  bool _isLoading = false;
  String? _errorMessage;

  List<Map<String, dynamic>> _speciesProfiles = [];
  List<Map<String, dynamic>> _timeZones = [];
  final Map<String, List<HistoryPoint>> _historyByTerrarium = {};

  List<TerrariumModel> get terrariums => _terrariums;
  List<DeviceModel> get devices => _devices;
  List<AlertModel> get alerts => _alerts;
  String get selectedTerrariumId => _selectedTerrariumId;

  bool get isLive => _isLive;
  bool get isLoading => _isLoading;
  String? get errorMessage => _errorMessage;

  List<Map<String, dynamic>> get speciesProfiles => _speciesProfiles;
  List<Map<String, dynamic>> get timeZones => _timeZones;

  void updateApi(ApiClient api) {
    _api = api;
  }

  TerrariumModel get selectedTerrarium {
    return _terrariums.firstWhere(
      (t) => t.id == _selectedTerrariumId,
      orElse: () => _terrariums.first,
    );
  }

  int get pendingAlertsCount => _alerts.where((a) => !a.isResolved).length;

  int get onlineDevicesCount => _devices.where((d) => d.isOnline).length;

  List<HistoryPoint> get currentHistory => historyFor(_selectedTerrariumId);

  List<HistoryPoint> historyFor(String terrariumId) {
    if (_isLive) {
      final stored = _historyByTerrarium[terrariumId];
      if (stored != null && stored.isNotEmpty) {
        return stored;
      }
      return _fallbackLiveHistory(terrariumId);
    }
    return generateMockHistory(terrariumId);
  }

  String _pointStatus(
    TerrariumModel terrarium,
    double temp,
    double hum,
    int light,
  ) {
    final th = terrarium.thresholds;
    if (temp < th.tempMin || temp > th.tempMax) return 'danger';
    if (hum < th.humidityMin || hum > th.humidityMax) return 'warning';
    if (light < th.lightMin || light > th.lightMax) return 'warning';
    return 'normal';
  }

  List<HistoryPoint> _fallbackLiveHistory(String terrariumId) {
    final terrarium = _terrariums.firstWhere(
      (t) => t.id == terrariumId,
      orElse: () => _terrariums.first,
    );
    final now = DateTime.now();
    final prev = now.subtract(const Duration(hours: 1));
    final prevLabel =
        '${prev.hour.toString().padLeft(2, '0')}:${prev.minute.toString().padLeft(2, '0')}';
    final nowLabel =
        '${now.hour.toString().padLeft(2, '0')}:${now.minute.toString().padLeft(2, '0')}';
    final st = _pointStatus(
      terrarium,
      terrarium.currentTemp,
      terrarium.currentHumidity,
      terrarium.currentLight,
    );
    return [
      HistoryPoint(
        time: prevLabel,
        temperature: terrarium.currentTemp,
        humidity: terrarium.currentHumidity,
        light: terrarium.currentLight,
        status: st,
      ),
      HistoryPoint(
        time: nowLabel,
        temperature: terrarium.currentTemp,
        humidity: terrarium.currentHumidity,
        light: terrarium.currentLight,
        status: st,
      ),
    ];
  }

  void selectTerrarium(String id) {
    if (_selectedTerrariumId != id) {
      _selectedTerrariumId = id;
      if (_isLive) {
        fetchLatestReadings(id).then((_) => fetchHistory(id));
        fetchThresholds(id);
      }
      notifyListeners();
    }
  }

  /// Reset to simulated/mock models (e.g. on sign-out or offline test)
  void resetToMocks() {
    _terrariums = List.from(mockTerrariums);
    _devices = List.from(mockDevices);
    _alerts = List.from(mockAlerts);
    _historyByTerrarium.clear();
    _selectedTerrariumId = _terrariums.first.id;
    _isLive = false;
    _errorMessage = null;
    notifyListeners();
  }

  /// Loads all live data from the backend API: terrariums, devices, alerts, profiles
  Future<void> loadAll() async {
    final client = _api;
    if (client == null || client.accessToken == null) {
      return;
    }

    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      await Future.wait([
        fetchTerrariums(),
        fetchDevices(),
        fetchAlerts(),
        fetchSpeciesProfiles(),
        fetchTimeZones(),
      ]);
      _isLive = true;
      _errorMessage = null;
    } on AppFailure catch (failure) {
      _errorMessage = failure.detail ?? failure.code;
    } catch (e) {
      _errorMessage = e.toString();
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// GET /api/v1/terrariums
  Future<void> fetchTerrariums() async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/terrariums');
      final items = payload['items'] as List<dynamic>? ?? [];

      if (items.isNotEmpty) {
        final List<TerrariumModel> list = [];
        for (final item in items) {
          if (item is Map<String, dynamic>) {
            final t = TerrariumModel.fromApi(item);
            list.add(t);
          }
        }

        if (list.isNotEmpty) {
          _terrariums = list;
          if (!_terrariums.any((t) => t.id == _selectedTerrariumId)) {
            _selectedTerrariumId = _terrariums.first.id;
          }
          _isLive = true;

          // Hydrate readings, thresholds, and 24h history for all loaded terrariums
          final hydration = <Future<void>>[];
          for (final t in _terrariums) {
            hydration.add(fetchLatestReadings(t.id).then((_) => fetchHistory(t.id)));
            hydration.add(fetchThresholds(t.id));
          }
          await Future.wait(hydration);
        }
      }
    } catch (_) {
      // Retain existing state on failure
    }
  }

  /// GET /api/v1/terrariums/{id}/readings (24h history for tempC, humidityPct, lightLux)
  Future<void> fetchHistory(String terrariumId) async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final to = DateTime.now().toUtc();
      final from = to.subtract(const Duration(hours: 24));
      final fromStr = from.toIso8601String();
      final toStr = to.toIso8601String();

      Future<List<Map<String, dynamic>>> loadMetric(String metric) async {
        try {
          final query = ApiClient.query({
            'metric': metric,
            'from': fromStr,
            'to': toStr,
          });
          final res = await client.getJson('/api/v1/terrariums/$terrariumId/readings$query');
          final pts = res['points'];
          if (pts is List) {
            return pts.whereType<Map<String, dynamic>>().toList();
          }
        } catch (_) {}
        return const [];
      }

      final results = await Future.wait([
        loadMetric('tempC'),
        loadMetric('humidityPct'),
        loadMetric('lightLux'),
      ]);

      final tempPts = results[0];
      final humPts = results[1];
      final lightPts = results[2];

      final maxLen = [tempPts.length, humPts.length, lightPts.length]
          .reduce((a, b) => a > b ? a : b);

      final terrarium = _terrariums.firstWhere(
        (t) => t.id == terrariumId,
        orElse: () => _terrariums.first,
      );

      double lastTemp = terrarium.currentTemp;
      double lastHum = terrarium.currentHumidity;
      int lastLight = terrarium.currentLight;

      for (final p in tempPts) {
        if (p['avg'] is num) {
          lastTemp = double.parse((p['avg'] as num).toStringAsFixed(1));
          break;
        }
      }
      for (final p in humPts) {
        if (p['avg'] is num) {
          lastHum = double.parse((p['avg'] as num).toStringAsFixed(1));
          break;
        }
      }
      for (final p in lightPts) {
        if (p['avg'] is num) {
          lastLight = (p['avg'] as num).round();
          break;
        }
      }

      final validPoints = <HistoryPoint>[];
      for (int i = 0; i < maxLen; i++) {
        final tVal = (i < tempPts.length && tempPts[i]['avg'] is num)
            ? (tempPts[i]['avg'] as num).toDouble()
            : null;
        final hVal = (i < humPts.length && humPts[i]['avg'] is num)
            ? (humPts[i]['avg'] as num).toDouble()
            : null;
        final lVal = (i < lightPts.length && lightPts[i]['avg'] is num)
            ? (lightPts[i]['avg'] as num).round()
            : null;

        if (tVal != null || hVal != null || lVal != null) {
          if (tVal != null) lastTemp = double.parse(tVal.toStringAsFixed(1));
          if (hVal != null) lastHum = double.parse(hVal.toStringAsFixed(1));
          if (lVal != null) lastLight = lVal;

          final rawTime = (i < tempPts.length ? tempPts[i]['t'] : null) ??
              (i < humPts.length ? humPts[i]['t'] : null) ??
              (i < lightPts.length ? lightPts[i]['t'] : null);
          final dt = rawTime != null ? DateTime.tryParse(rawTime.toString())?.toLocal() : null;
          final timeLabel = dt != null
              ? '${dt.hour.toString().padLeft(2, '0')}:${dt.minute.toString().padLeft(2, '0')}'
              : '${i.toString().padLeft(2, '0')}:00';

          validPoints.add(
            HistoryPoint(
              time: timeLabel,
              temperature: lastTemp,
              humidity: lastHum,
              light: lastLight,
              status: _pointStatus(terrarium, lastTemp, lastHum, lastLight),
            ),
          );
        }
      }

      if (validPoints.isEmpty) {
        _historyByTerrarium.remove(terrariumId);
      } else if (validPoints.length == 1) {
        final only = validPoints.first;
        _historyByTerrarium[terrariumId] = [
          HistoryPoint(
            time: 'Trước',
            temperature: only.temperature,
            humidity: only.humidity,
            light: only.light,
            status: only.status,
          ),
          only,
        ];
      } else if (validPoints.length > 24) {
        final sampled = <HistoryPoint>[];
        final step = (validPoints.length - 1) / 23.0;
        for (int i = 0; i < 24; i++) {
          final idx = (i * step).round().clamp(0, validPoints.length - 1);
          sampled.add(validPoints[idx]);
        }
        _historyByTerrarium[terrariumId] = sampled;
      } else {
        _historyByTerrarium[terrariumId] = validPoints;
      }

      notifyListeners();
    } catch (_) {}
  }

  /// GET /api/v1/terrariums/{id}/thresholds
  Future<void> fetchThresholds(String terrariumId) async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/terrariums/$terrariumId/thresholds');
      final effective = payload['effectiveThresholds'] as List<dynamic>? ?? [];

      double? tempMin, tempMax;
      double? humMin, humMax;
      int? lightMin, lightMax;

      for (final band in effective) {
        if (band is Map<String, dynamic>) {
          final metric = band['metric']?.toString();
          final tMin = (band['targetMin'] as num?)?.toDouble();
          final tMax = (band['targetMax'] as num?)?.toDouble();

          if (metric == 'tempC') {
            tempMin = tMin;
            tempMax = tMax;
          } else if (metric == 'humidityPct') {
            humMin = tMin;
            humMax = tMax;
          } else if (metric == 'lightLux') {
            lightMin = tMin?.toInt();
            lightMax = tMax?.toInt();
          }
        }
      }

      final index = _terrariums.indexWhere((t) => t.id == terrariumId);
      if (index != -1 && (tempMin != null || humMin != null || lightMin != null)) {
        _terrariums[index].thresholds = _terrariums[index].thresholds.copyWith(
          tempMin: tempMin,
          tempMax: tempMax,
          humidityMin: humMin,
          humidityMax: humMax,
          lightMin: lightMin,
          lightMax: lightMax,
        );
        notifyListeners();
      }
    } catch (_) {}
  }

  /// GET /api/v1/terrariums/{id}/readings/latest
  Future<void> fetchLatestReadings(String terrariumId) async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/terrariums/$terrariumId/readings/latest');
      final metrics = payload['metrics'] as List<dynamic>? ?? [];

      final index = _terrariums.indexWhere((t) => t.id == terrariumId);
      if (index == -1) return;

      bool hasCritical = false;
      bool hasWarning = false;

      for (final m in metrics) {
        if (m is Map<String, dynamic>) {
          final code = m['code']?.toString();
          final val = (m['value'] as num?)?.toDouble();
          final status = m['status']?.toString().toLowerCase();

          if (status == 'critical') hasCritical = true;
          if (status == 'warning') hasWarning = true;

          if (val != null) {
            if (code == 'tempC') {
              _terrariums[index].currentTemp = val;
            } else if (code == 'humidityPct') {
              _terrariums[index].currentHumidity = val;
            } else if (code == 'lightLux') {
              _terrariums[index].currentLight = val.toInt();
            }
          }
        }
      }

      if (hasCritical) {
        _terrariums[index].status = 'danger';
      } else if (hasWarning) {
        _terrariums[index].status = 'warning';
      } else {
        _terrariums[index].status = 'normal';
      }

      notifyListeners();
    } catch (_) {}
  }

  /// GET /api/v1/devices
  Future<void> fetchDevices() async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/devices');
      final items = payload['items'] as List<dynamic>? ?? [];

      if (items.isNotEmpty) {
        final list = <DeviceModel>[];
        for (final item in items) {
          if (item is Map<String, dynamic>) {
            list.add(DeviceModel.fromApi(item));
          }
        }
        _devices = list;
      }
    } catch (_) {}
  }

  /// GET /api/v1/alerts
  Future<void> fetchAlerts() async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/alerts');
      final items = payload['items'] as List<dynamic>? ?? [];

      if (items.isNotEmpty) {
        final list = <AlertModel>[];
        for (final item in items) {
          if (item is Map<String, dynamic>) {
            list.add(AlertModel.fromApi(item));
          }
        }
        _alerts = list;
      }
    } catch (_) {}
  }

  /// GET /api/v1/species-profiles
  Future<void> fetchSpeciesProfiles() async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/species-profiles');
      final items = payload['items'] as List<dynamic>? ?? [];
      _speciesProfiles = items.whereType<Map<String, dynamic>>().toList();
    } catch (_) {}
  }

  /// GET /api/v1/time-zones
  Future<void> fetchTimeZones() async {
    final client = _api;
    if (client == null || client.accessToken == null) return;

    try {
      final payload = await client.getJson('/api/v1/time-zones');
      final items = payload['items'] as List<dynamic>? ?? [];
      _timeZones = items.whereType<Map<String, dynamic>>().toList();
    } catch (_) {}
  }

  /// POST /api/v1/terrariums
  Future<bool> createTerrarium({
    required String name,
    required String speciesProfileId,
    String? location,
    String? description,
    String? timeZoneId,
  }) async {
    final client = _api;
    if (client != null && client.accessToken != null) {
      try {
        await client.postJson(
          '/api/v1/terrariums',
          body: {
            'name': name.trim(),
            'speciesProfileId': speciesProfileId,
            'location': (location != null && location.trim().isNotEmpty) ? location.trim() : null,
            'description': (description != null && description.trim().isNotEmpty) ? description.trim() : null,
            'timeZoneId': (timeZoneId != null && timeZoneId.trim().isNotEmpty) ? timeZoneId.trim() : 'Asia/Ho_Chi_Minh',
          },
        );
        await fetchTerrariums();
        return true;
      } catch (e) {
        _errorMessage = e.toString();
        notifyListeners();
        return false;
      }
    }

    // Local fallback when offline
    addTerrarium(
      name: name,
      species: 'Loài bò sát mới',
      description: description ?? '',
    );
    return true;
  }

  /// PUT /api/v1/terrariums/{id}/thresholds
  Future<bool> updateThresholds(String terrariumId, ThresholdModel newThresholds) async {
    final index = _terrariums.indexWhere((t) => t.id == terrariumId);
    if (index != -1) {
      _terrariums[index].thresholds = newThresholds;
      notifyListeners();
    }

    final client = _api;
    if (client != null && client.accessToken != null) {
      try {
        await client.putJson(
          '/api/v1/terrariums/$terrariumId/thresholds',
          body: {
            'overrides': [
              {
                'metric': 'tempC',
                'targetMin': newThresholds.tempMin,
                'targetMax': newThresholds.tempMax,
              },
              {
                'metric': 'humidityPct',
                'targetMin': newThresholds.humidityMin,
                'targetMax': newThresholds.humidityMax,
              },
              {
                'metric': 'lightLux',
                'targetMin': newThresholds.lightMin,
                'targetMax': newThresholds.lightMax,
              },
            ],
          },
        );
        return true;
      } catch (_) {
        return false;
      }
    }
    return true;
  }

  /// POST /api/v1/alerts/{id}/resolve
  Future<bool> resolveAlert(String alertId) async {
    final index = _alerts.indexWhere((a) => a.id == alertId);
    if (index != -1) {
      _alerts[index].isResolved = true;
      _alerts[index].resolvedAt = 'Vừa xong';
      notifyListeners();
    }

    final client = _api;
    if (client != null && client.accessToken != null) {
      final numericId = int.tryParse(alertId);
      if (numericId != null) {
        try {
          await client.postJson(
            '/api/v1/alerts/$numericId/resolve',
            body: {'note': 'Đã kiểm tra và xử lý qua ứng dụng di động'},
          );
          return true;
        } catch (_) {
          return false;
        }
      }
    }
    return true;
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
      image: TerrariumModel.defaultImageForSpecies(species),
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
      deviceCount: 1,
    );

    _terrariums.add(added);
    notifyListeners();
  }
}
