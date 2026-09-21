import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:smart_reptile/core/clock.dart';
import 'package:smart_reptile/core/status.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/state/telemetry_provider.dart';

/// TC-W-08 and the freshness rules from `03-implementation/05` §2.
///
/// The clock is injected, so nothing here waits for real time — the rule from `04-quality/01` §4 that keeps the
/// suite fast and deterministic.
void main() {
  const terrariumId = 'terrarium-1';
  final start = DateTime.utc(2026, 9, 21, 8, 15);

  late FakeClock clock;

  setUp(() => clock = Clock.fake(start));

  TelemetryProvider providerWith(http.Client client) => TelemetryProvider(
    api: ApiClient(baseUrl: 'http://test', httpClient: client),
    clock: clock,
  );

  Map<String, dynamic> readingPayload(
    double temperature, {
    int secondsAgo = 0,
  }) => {
    'metrics': [
      {
        'code': 'tempC',
        'value': temperature,
        'unit': '°C',
        'status': 'InRange',
        'capturedAt': start
            .subtract(Duration(seconds: secondsAgo))
            .toIso8601String(),
        'qualityFlags': 0,
      },
    ],
    'device': {
      'deviceId': 'sr-3f9a2c',
      'status': 'online',
      'lastSeenAt': start.toIso8601String(),
      'samplingIntervalSec': 60,
    },
  };

  test('merges a push batch and notifies exactly once', () {
    final provider = providerWith(
      MockClient((_) async => http.Response('{}', 200)),
    );
    var notifications = 0;
    provider.addListener(() => notifications++);

    provider.applyPush({
      'metrics': [
        {
          'code': 'tempC',
          'value': 28.6,
          'unit': '°C',
          'status': 'InRange',
          'capturedAt': start.toIso8601String(),
        },
        {
          'code': 'humidityPct',
          'value': 41.2,
          'unit': '%RH',
          'status': 'InRange',
          'capturedAt': start.toIso8601String(),
        },
        {
          'code': 'lightLux',
          'value': 1820,
          'unit': 'lx',
          'status': 'InRange',
          'capturedAt': start.toIso8601String(),
        },
      ],
    }, terrariumId);

    // Three metrics, one rebuild: a 12 h back-fill burst must not become a frame storm.
    expect(notifications, 1);
    expect(provider.valuesFor(terrariumId).length, 3);
    expect(provider.valuesFor(terrariumId)['humidityPct']!.value, 41.2);
  });

  test('keeps previous values when a newer sample arrives without them', () {
    final provider = providerWith(
      MockClient((_) async => http.Response('{}', 200)),
    );
    provider.applyPush({
      'metrics': [
        {
          'code': 'tempC',
          'value': 28.6,
          'unit': '°C',
          'capturedAt': start.toIso8601String(),
        },
        {
          'code': 'humidityPct',
          'value': 41.2,
          'unit': '%RH',
          'capturedAt': start.toIso8601String(),
        },
      ],
    }, terrariumId);

    provider.applyPush({
      'metrics': [
        {
          'code': 'tempC',
          'value': 29.1,
          'unit': '°C',
          'capturedAt': start.toIso8601String(),
        },
      ],
    }, terrariumId);

    expect(provider.valuesFor(terrariumId)['tempC']!.value, 29.1);
    expect(provider.valuesFor(terrariumId)['humidityPct']!.value, 41.2);
  });

  test('derives freshness from the clock rather than storing it', () {
    final provider = providerWith(
      MockClient((_) async => http.Response('{}', 200)),
    );
    provider.applyStatusPush({
      'deviceId': 'sr-3f9a2c',
      'status': 'online',
      'samplingIntervalSec': 60,
    }, terrariumId);
    provider.applyPush({
      'metrics': [
        {
          'code': 'tempC',
          'value': 28.6,
          'unit': '°C',
          'capturedAt': start.toIso8601String(),
        },
      ],
    }, terrariumId);

    expect(
      provider.freshnessFor(terrariumId, 'tempC').kind,
      FreshnessKind.fresh,
    );

    // 3 minutes = the staleness threshold for a 60 s interval; past it the value must be flagged stale.
    clock.advance(const Duration(minutes: 2));
    expect(
      provider.freshnessFor(terrariumId, 'tempC').kind,
      FreshnessKind.fresh,
    );

    clock.advance(const Duration(minutes: 5));
    expect(
      provider.freshnessFor(terrariumId, 'tempC').kind,
      FreshnessKind.stale,
    );
    expect(
      provider.freshnessFor(terrariumId, 'tempC').age,
      const Duration(minutes: 7),
    );
  });

  test('reports no data for a metric that never arrived', () {
    final provider = providerWith(
      MockClient((_) async => http.Response('{}', 200)),
    );

    expect(
      provider.freshnessFor(terrariumId, 'uvIndex').kind,
      FreshnessKind.noData,
    );
    expect(provider.valuesFor(terrariumId), isEmpty);
  });

  test('loads latest readings and device state from the API', () async {
    final client = MockClient((request) async {
      expect(
        request.url.path,
        '/api/v1/terrariums/$terrariumId/readings/latest',
      );
      return http.Response(jsonEncode(readingPayload(28.6)), 200);
    });

    final provider = providerWith(client);
    await provider.loadLatest(terrariumId);

    expect(provider.lastFailure, isNull);
    expect(provider.valuesFor(terrariumId)['tempC']!.value, 28.6);
    expect(provider.deviceFor(terrariumId)!.isOnline, isTrue);
    expect(provider.deviceFor(terrariumId)!.samplingIntervalSec, 60);
  });

  test(
    'keeps cached values and flags offline when the server is unreachable',
    () async {
      var fail = false;
      final client = MockClient((_) async {
        if (fail) {
          throw http.ClientException('connection refused');
        }
        return http.Response(jsonEncode(readingPayload(28.6)), 200);
      });

      final provider = providerWith(client);
      await provider.loadLatest(terrariumId);
      expect(provider.valuesFor(terrariumId)['tempC']!.value, 28.6);

      fail = true;
      await provider.loadLatest(terrariumId);

      // The dashboard must not empty itself during a network blip: it keeps the value and says it is offline.
      expect(provider.isOffline, isTrue);
      expect(provider.lastFailure!.isNetworkFailure, isTrue);
      expect(provider.valuesFor(terrariumId)['tempC']!.value, 28.6);
    },
  );

  test(
    'clear() empties state so a logout cannot leak values to the next user',
    () {
      final provider = providerWith(
        MockClient((_) async => http.Response('{}', 200)),
      );
      provider.applyPush({
        'metrics': [
          {
            'code': 'tempC',
            'value': 28.6,
            'unit': '°C',
            'capturedAt': start.toIso8601String(),
          },
        ],
      }, terrariumId);

      provider.clear();

      expect(provider.valuesFor(terrariumId), isEmpty);
      expect(provider.deviceFor(terrariumId), isNull);
    },
  );
}
