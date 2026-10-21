import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/device_model.dart';

/// Provider for managing IoT device fleet state, claiming, and configuration.
class DeviceProvider extends ChangeNotifier {
  DeviceProvider({required this.api});

  final ApiClient api;

  List<DeviceItem> _devices = [];
  bool _isLoading = false;
  AppFailure? _lastFailure;
  String _searchQuery = '';
  String _statusFilter = 'all'; // 'all', 'online', 'offline'

  List<DeviceItem> get devices => List.unmodifiable(_devices);
  bool get isLoading => _isLoading;
  AppFailure? get lastFailure => _lastFailure;
  String get searchQuery => _searchQuery;
  String get statusFilter => _statusFilter;

  List<DeviceItem> get filteredDevices {
    return _devices.where((d) {
      if (_statusFilter != 'all') {
        if (d.runtime.toLowerCase() != _statusFilter.toLowerCase()) {
          return false;
        }
      }
      if (_searchQuery.trim().isNotEmpty) {
        final query = _searchQuery.trim().toLowerCase();
        final matches = d.deviceId.toLowerCase().contains(query) ||
            d.deviceName.toLowerCase().contains(query) ||
            d.macAddress.toLowerCase().contains(query) ||
            (d.terrariumName?.toLowerCase().contains(query) ?? false);
        if (!matches) return false;
      }
      return true;
    }).toList();
  }

  int get offlineCount =>
      _devices.where((d) => d.runtime.toLowerCase() == 'offline').length;

  void setSearchQuery(String query) {
    _searchQuery = query;
    notifyListeners();
  }

  void setStatusFilter(String filter) {
    _statusFilter = filter;
    notifyListeners();
  }

  void clear() {
    _devices = [];
    _isLoading = false;
    _lastFailure = null;
    _searchQuery = '';
    _statusFilter = 'all';
    notifyListeners();
  }

  Future<void> loadDevices() async {
    _isLoading = true;
    _lastFailure = null;
    notifyListeners();

    try {
      final payload = await api.getJson('/api/v1/devices');
      final rows = payload['items'];
      if (rows is List) {
        _devices = [
          for (final row in rows)
            if (row is Map<String, dynamic>) DeviceItem.fromJson(row),
        ];
      }
    } on AppFailure catch (e) {
      _lastFailure = e;
    } catch (e) {
      _lastFailure = AppFailure(
        code: 'unknown',
        statusCode: 0,
        detail: e.toString(),
      );
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  Future<ClaimResult> claimDevice({
    required String claimCode,
    required String terrariumId,
  }) async {
    final payload = await api.postJson(
      '/api/v1/devices/claim',
      body: {
        'claimCode': claimCode.trim(),
        'terrariumId': terrariumId,
      },
    );
    final result = ClaimResult.fromJson(payload);
    await loadDevices();
    return result;
  }

  Future<ClaimResult> claimByMac({
    required String macAddress,
    required String terrariumId,
  }) async {
    final payload = await api.postJson(
      '/api/v1/devices/claim-by-mac',
      body: {
        'macAddress': macAddress.trim(),
        'terrariumId': terrariumId,
      },
    );
    final result = ClaimResult.fromJson(payload);
    await loadDevices();
    return result;
  }

  Future<DeviceItem> getDevice(String deviceId) async {
    final payload = await api.getJson(
      '/api/v1/devices/${Uri.encodeComponent(deviceId)}',
    );
    return DeviceItem.fromJson(payload);
  }

  Future<DeviceItem> updateDevice(
    String deviceId, {
    String? deviceName,
    int? samplingIntervalSec,
    int? publishIntervalSec,
  }) async {
    final body = <String, dynamic>{};
    if (deviceName != null) body['deviceName'] = deviceName;
    if (samplingIntervalSec != null) {
      body['samplingIntervalSec'] = samplingIntervalSec;
    }
    if (publishIntervalSec != null) {
      body['publishIntervalSec'] = publishIntervalSec;
    }

    final payload = await api.patchJson(
      '/api/v1/devices/${Uri.encodeComponent(deviceId)}',
      body: body,
    );
    final updated = DeviceItem.fromJson(payload);
    await loadDevices();
    return updated;
  }

  Future<String> rotateSecret(String deviceId) async {
    final payload = await api.postJson(
      '/api/v1/devices/${Uri.encodeComponent(deviceId)}/rotate-secret',
    );
    final secret = payload['secret']?.toString() ?? '';
    await loadDevices();
    return secret;
  }

  Future<void> revokeDevice(String deviceId) async {
    await api.postJson(
      '/api/v1/devices/${Uri.encodeComponent(deviceId)}/revoke',
    );
    await loadDevices();
  }

  Future<void> deleteDevice(String deviceId) async {
    await api.deleteJson(
      '/api/v1/devices/${Uri.encodeComponent(deviceId)}',
    );
    await loadDevices();
  }
}
