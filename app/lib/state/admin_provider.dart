import 'package:flutter/foundation.dart';

import '../core/paging.dart';
import '../core/problem.dart';
import '../data/api_client.dart';
import '../models/admin_models.dart';

/// The administrator's get-all surface: `GET /api/v1/admin/overview` and the three cross-account lists.
///
/// One provider rather than four, because the screens share a lifetime: they are reachable only while the session
/// holds `data.read.any`, and they are all cleared together when it ends. Everything here reads *other accounts'*
/// rows, so [clear] is not a nicety — a signed-out app that still held the deployment's accounts in memory would be
/// showing one account another's data after a sign-out.
class AdminProvider extends ChangeNotifier {
  /// Creates the provider.
  AdminProvider({required this._api});

  final ApiClient _api;

  AdminOverview? _overview;
  bool _isLoadingOverview = false;
  AppFailure? _overviewFailure;

  PagedState<AdminTerrarium>? _terrariums;
  PagedState<AdminDevice>? _devices;
  PagedState<AdminAlert>? _alerts;
  PagedState<AdminSpeciesProfile>? _speciesProfiles;

  String? _alertFamily;
  String? _alertState;

  /// The last overview read, or null while it has never succeeded.
  AdminOverview? get overview => _overview;

  /// True while the overview is being read.
  bool get isLoadingOverview => _isLoadingOverview;

  /// Why the last overview read failed.
  AppFailure? get overviewFailure => _overviewFailure;

  /// The enclosures page, or null while it has never been read.
  PagedState<AdminTerrarium>? get terrariums => _terrariums;

  /// The boards page, or null while it has never been read.
  PagedState<AdminDevice>? get devices => _devices;

  /// The alerts page, or null while it has never been read.
  PagedState<AdminAlert>? get alerts => _alerts;

  /// The species profiles page, or null while it has never been read.
  PagedState<AdminSpeciesProfile>? get speciesProfiles => _speciesProfiles;

  /// The family the alert list is filtered by, or null for both.
  String? get alertFamily => _alertFamily;

  /// The state the alert list is filtered by, or null for any.
  String? get alertState => _alertState;

  /// Reads the overview.
  Future<void> loadOverview() async {
    _isLoadingOverview = true;
    _overviewFailure = null;
    notifyListeners();

    try {
      final payload = await _api.getJson('/api/v1/admin/overview');
      _overview = AdminOverview.fromJson(payload);
    } on AppFailure catch (failure) {
      _overviewFailure = failure;
    } finally {
      _isLoadingOverview = false;
      notifyListeners();
    }
  }

  /// Reads one page of every account's enclosures.
  Future<void> loadTerrariums({String? search, int skip = 0}) async {
    final filter = search ?? _terrariums?.search ?? '';
    _terrariums = (_terrariums ?? _initial<AdminTerrarium>(filter))
        .copyWith(search: filter, isLoading: true, clearFailure: true);
    notifyListeners();

    _terrariums = await _loadPage(
      path: '/api/v1/admin/terrariums',
      query: {'search': filter, 'skip': skip, 'take': adminPageSize},
      parse: AdminTerrarium.fromJson,
      previous: _terrariums!,
    );
    notifyListeners();
  }

  /// Reads one page of every board in the deployment.
  Future<void> loadDevices({String? search, int skip = 0}) async {
    final filter = search ?? _devices?.search ?? '';
    _devices = (_devices ?? _initial<AdminDevice>(filter)).copyWith(
      search: filter,
      isLoading: true,
      clearFailure: true,
    );
    notifyListeners();

    _devices = await _loadPage(
      path: '/api/v1/admin/devices',
      query: {'search': filter, 'skip': skip, 'take': adminPageSize},
      parse: AdminDevice.fromJson,
      previous: _devices!,
    );
    notifyListeners();
  }

  /// Reads one page of every account's alerts under the current filters.
  Future<void> loadAlerts({int skip = 0}) async {
    _alerts = (_alerts ?? _initial<AdminAlert>('')).copyWith(
      isLoading: true,
      clearFailure: true,
    );
    notifyListeners();

    _alerts = await _loadPage(
      path: '/api/v1/admin/alerts',
      query: {
        'family': _alertFamily,
        'state': _alertState,
        'skip': skip,
        'take': adminPageSize,
      },
      parse: AdminAlert.fromJson,
      previous: _alerts!,
    );
    notifyListeners();
  }

  /// Applies an alert filter and goes back to the first page, because page 3 of the old filter is meaningless here.
  Future<void> filterAlerts({String? family, String? state}) async {
    _alertFamily = family;
    _alertState = state;
    await loadAlerts();
  }

  /// Reads one page of species profiles.
  Future<void> loadSpeciesProfiles({String? search, int skip = 0}) async {
    final filter = search ?? _speciesProfiles?.search ?? '';
    _speciesProfiles = (_speciesProfiles ?? _initial<AdminSpeciesProfile>(filter))
        .copyWith(search: filter, isLoading: true, clearFailure: true);
    notifyListeners();

    try {
      final payload = await _api.getJson('/api/v1/species-profiles');
      final list = (payload['items'] as List?) ?? const [];
      final allProfiles = [
        for (final item in list)
          if (item is Map<String, dynamic>) AdminSpeciesProfile.fromJson(item),
      ];

      // Client-side filtering and slicing since GET /species-profiles returns the full list
      final filtered = filter.trim().isEmpty
          ? allProfiles
          : allProfiles.where((p) {
              final q = filter.toLowerCase();
              return p.name.toLowerCase().contains(q) ||
                  p.scientificName.toLowerCase().contains(q) ||
                  p.climateZone.toLowerCase().contains(q);
            }).toList();

      final pagedItems = filtered.skip(skip).take(adminPageSize).toList();

      _speciesProfiles = _speciesProfiles!.copyWith(
        items: pagedItems,
        total: filtered.length,
        skip: skip,
        take: adminPageSize,
        isLoading: false,
        clearFailure: true,
      );
    } on AppFailure catch (failure) {
      _speciesProfiles = _speciesProfiles!.copyWith(
        isLoading: false,
        failure: failure,
      );
    } finally {
      notifyListeners();
    }
  }

  /// Drops everything, so a sign-out cannot leave another account's data on screen.
  void clear() {
    _overview = null;
    _overviewFailure = null;
    _isLoadingOverview = false;
    _terrariums = null;
    _devices = null;
    _alerts = null;
    _speciesProfiles = null;
    _alertFamily = null;
    _alertState = null;
    notifyListeners();
  }

  PagedState<T> _initial<T>(String search) => PagedState<T>(
    items: const [],
    total: 0,
    skip: 0,
    take: adminPageSize,
    search: search,
  );

  Future<PagedState<T>> _loadPage<T>({
    required String path,
    required Map<String, Object?> query,
    required T Function(Map<String, dynamic>) parse,
    required PagedState<T> previous,
  }) async {
    try {
      final payload = await _api.getJson('$path${ApiClient.query(query)}');
      final page = ApiPage.fromJson(payload, parse);

      return previous.copyWith(
        items: page.items,
        total: page.total,
        skip: page.skip,
        take: page.take,
        isLoading: false,
        clearFailure: true,
      );
    } on AppFailure catch (failure) {
      // The rows stay: a failed page turn shows the page that was there plus the reason, rather than an empty list
      // that reads as "there is nothing".
      return previous.copyWith(isLoading: false, failure: failure);
    }
  }
}
