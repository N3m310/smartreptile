import 'package:flutter/foundation.dart';

import '../core/clock.dart';
import '../core/env.dart';
import '../core/problem.dart';
import '../core/status.dart';
import '../data/api_client.dart';
import '../models/metric_value.dart';

/// Owns the latest values per terrarium and the device state behind them.
///
/// Three decisions worth defending (`03-implementation/05` §2):
///  1. freshness is *derived* at render time, never stored as a flag a timer has to flip;
///  2. a push batch produces exactly one notification, so a 12 h back-fill burst cannot cause a frame storm;
///  3. the clock is injected, so staleness is testable without waiting.
class TelemetryProvider extends ChangeNotifier {
  TelemetryProvider({required this._api, required this.clock});

  final ApiClient _api;

  /// Injected time source; every age-related decision goes through it (`03-implementation/05` §2).
  final Clock clock;

  final Map<String, Map<String, MetricValue>> _latest = {};
  final Map<String, DeviceSnapshot> _devices = {};

  bool _loading = false;
  AppFailure? _lastFailure;

  /// Number of notifications emitted, used by tests to assert "one notify per batch".
  int notifyCount = 0;

  /// True while a request is in flight.
  bool get isLoading => _loading;

  /// Last failure, if any; the UI shows cached values alongside this.
  AppFailure? get lastFailure => _lastFailure;

  /// True when we are showing cached data because the server is unreachable.
  bool get isOffline => _lastFailure?.isNetworkFailure ?? false;

  /// Latest known values for a terrarium, keyed by metric code.
  Map<String, MetricValue> valuesFor(String terrariumId) =>
      _latest[terrariumId] ?? const {};

  /// Device state for a terrarium.
  DeviceSnapshot? deviceFor(String terrariumId) => _devices[terrariumId];

  /// Freshness of one metric, derived from the injected clock.
  Freshness freshnessFor(String terrariumId, String metricCode) {
    final value = _latest[terrariumId]?[metricCode];
    if (value == null) {
      return const Freshness.noData();
    }

    final interval =
        _devices[terrariumId]?.samplingIntervalSec ??
        Env.defaultSamplingIntervalSec;
    return Freshness.fromAge(
      clock.nowUtc().difference(value.capturedAt),
      interval,
    );
  }

  /// Loads `readings/latest` for one terrarium. On failure the previous values are kept (never cleared):
  /// a dashboard that empties itself during a network blip loses the keeper's trust.
  Future<void> loadLatest(String terrariumId) async {
    _loading = true;
    notifyListeners();

    try {
      final payload = await _api.getJson(
        '/api/v1/terrariums/$terrariumId/readings/latest',
      );
      _applyLatest(payload, terrariumId);
      _lastFailure = null;
    } on AppFailure catch (failure) {
      _lastFailure = failure;
    } finally {
      _loading = false;
      notifyListeners();
    }
  }

  /// Applies a SignalR `readingAdded` payload (same shape as one entry of `readings/latest`).
  ///
  /// Batches are merged into the map first and notified once: this is what keeps a back-fill burst cheap.
  void applyPush(Map<String, dynamic> payload, String terrariumId) {
    final metrics = payload['metrics'];
    if (metrics is! List || metrics.isEmpty) {
      return;
    }

    final merged = Map<String, MetricValue>.from(
      _latest[terrariumId] ?? const {},
    );
    for (final entry in metrics) {
      if (entry is Map<String, dynamic>) {
        final value = MetricValue.fromJson(entry);
        merged[value.code] = value;
      }
    }

    _latest[terrariumId] = merged;
    _lastFailure = null;
    notifyListeners();
  }

  /// Applies a SignalR `statusChanged` payload.
  void applyStatusPush(Map<String, dynamic> payload, String terrariumId) {
    _devices[terrariumId] = DeviceSnapshot.fromJson(payload);
    notifyListeners();
  }

  /// Clears everything (used on logout, so one user cannot see another's values).
  void clear() {
    _latest.clear();
    _devices.clear();
    _lastFailure = null;
    notifyListeners();
  }

  void _applyLatest(Map<String, dynamic> payload, String terrariumId) {
    final metrics = payload['metrics'];
    if (metrics is List) {
      final merged = <String, MetricValue>{};
      for (final entry in metrics) {
        if (entry is Map<String, dynamic>) {
          final value = MetricValue.fromJson(entry);
          merged[value.code] = value;
        }
      }
      _latest[terrariumId] = merged;
    }

    final device = payload['device'];
    if (device is Map<String, dynamic>) {
      _devices[terrariumId] = DeviceSnapshot.fromJson(device);
    }
  }

  @override
  void notifyListeners() {
    notifyCount++;
    super.notifyListeners();
  }
}
