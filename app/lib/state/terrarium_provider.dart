import 'package:flutter/foundation.dart';

import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/api_terrarium_models.dart';

class TerrariumProvider extends ChangeNotifier {
  TerrariumProvider(this._api);

  final ApiClient _api;
  final Map<String, Future<Uint8List?>> _profileImages = {};
  List<ApiTerrarium> _terrariums = [];
  bool _loading = false;
  AppFailure? _lastFailure;

  List<ApiTerrarium> get terrariums => List.unmodifiable(_terrariums);
  bool get isLoading => _loading;
  AppFailure? get lastFailure => _lastFailure;

  Future<void> loadTerrariums() async {
    _loading = true;
    notifyListeners();
    try {
      final payload = await _api.getJson('/api/v1/terrariums');
      final rows = payload['items'];
      if (rows is! List) {
        throw const FormatException(
          'Terrarium list response has no items array.',
        );
      }
      _terrariums = [
        for (final row in rows)
          if (row is Map<String, dynamic>) ApiTerrarium.fromJson(row),
      ];
      _lastFailure = null;
    } on AppFailure catch (failure) {
      _lastFailure = failure;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  Future<List<ApiSpeciesProfile>> loadSpeciesProfiles() async {
    final payload = await _api.getJson('/api/v1/species-profiles');
    final rows = payload['items'];
    if (rows is! List) {
      throw const FormatException(
        'Species profile response has no items array.',
      );
    }

    return [
      for (final row in rows)
        if (row is Map<String, dynamic>) ApiSpeciesProfile.fromJson(row),
    ];
  }

  Future<Uint8List?> loadProfileImage(ApiSpeciesProfile profile) {
    if (profile.imageUrl == null) return Future.value(null);
    final version = profile.imageUpdatedAt;
    final path = version == null
        ? profile.imageUrl!
        : '${profile.imageUrl!}?v=${Uri.encodeQueryComponent(version)}';
    return _profileImages.putIfAbsent(
      '${profile.id}:$version',
      () => _api.getBytes(path),
    );
  }

  Future<ApiTerrarium> loadTerrarium(String id) async {
    final payload = await _api.getJson('/api/v1/terrariums/$id');
    return ApiTerrarium.fromJson(payload);
  }

  Future<Map<String, dynamic>> loadLatest(String id) =>
      _api.getJson('/api/v1/terrariums/$id/readings/latest');

  Future<Map<String, dynamic>> loadReadings({
    required String id,
    required String metric,
    required DateTime from,
    required DateTime to,
  }) {
    final query = ApiClient.query({
      'metric': metric,
      'from': from.toUtc().toIso8601String(),
      'to': to.toUtc().toIso8601String(),
    });
    return _api.getJson('/api/v1/terrariums/$id/readings$query');
  }

  Future<Map<String, dynamic>> loadCoverage({
    required String id,
    required DateTime from,
    required DateTime to,
  }) {
    final query = ApiClient.query({
      'from': from.toUtc().toIso8601String(),
      'to': to.toUtc().toIso8601String(),
    });
    return _api.getJson('/api/v1/terrariums/$id/coverage$query');
  }

  Future<ApiTerrarium> createTerrarium({
    required String name,
    required String speciesProfileId,
    String? location,
  }) async {
    final payload = await _api.postJson(
      '/api/v1/terrariums',
      body: {
        'name': name.trim(),
        'speciesProfileId': speciesProfileId,
        if (location != null && location.trim().isNotEmpty)
          'location': location.trim(),
      },
    );
    final created = ApiTerrarium.fromJson(payload);
    _terrariums = [..._terrariums, created];
    notifyListeners();
    return created;
  }

  Future<ApiTerrarium> updateTerrarium(
    ApiTerrarium current, {
    required String name,
    String? location,
    String? description,
    String? ownerUserId,
  }) async {
    final rowVersion = current.rowVersion;
    if (rowVersion == null || rowVersion.isEmpty) {
      throw const AppFailure(code: 'precondition_required', statusCode: 428);
    }
    final payload = await _api.patchJson(
      '/api/v1/terrariums/${current.id}',
      headers: {'If-Match': rowVersion},
      body: {
        'name': name.trim(),
        'location': location?.trim() ?? '',
        'description': description?.trim() ?? '',
        // Only when it changed: the server reads an omitted member as "leave it", and an assignment is an
        // administrator-only change that also carries the bound board across (2026-10-23).
        'ownerUserId': ?ownerUserId,
      },
    );
    final updated = ApiTerrarium.fromJson(payload);
    _replace(updated);
    return updated;
  }

  Future<void> deleteTerrarium(ApiTerrarium terrarium) async {
    await _api.deleteJson(
      '/api/v1/terrariums/${terrarium.id}?allowUnboundDevice=true',
    );
    _terrariums = _terrariums.where((item) => item.id != terrarium.id).toList();
    notifyListeners();
  }

  void _replace(ApiTerrarium updated) {
    _terrariums = [
      for (final item in _terrariums)
        if (item.id == updated.id) updated else item,
    ];
    notifyListeners();
  }

  void clear() {
    _terrariums = [];
    _profileImages.clear();
    _lastFailure = null;
    _loading = false;
    notifyListeners();
  }
}
