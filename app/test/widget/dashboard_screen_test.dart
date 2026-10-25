import 'dart:convert';

import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/screens/dashboard_screen.dart';
import 'package:smart_reptile/state/app_data_provider.dart';

void main() {
  testWidgets(
    'DashboardScreen fetches and displays live 24h telemetry chart data for the selected terrarium',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.reset);

      final api = ApiClient(
        baseUrl: 'http://localhost',
        httpClient: MockClient((request) async {
          final path = request.url.path;
          final metric = request.url.queryParameters['metric'];

          if (path == '/api/v1/terrariums') {
            return http.Response(
              jsonEncode({
                'items': [
                  {
                    'id': 'terra-1',
                    'name': 'Chuồng Admin 01',
                    'speciesCommonName': 'Arid (desert)',
                  },
                ],
              }),
              200,
              headers: {'content-type': 'application/json'},
            );
          }

          if (path == '/api/v1/terrariums/terra-1/readings/latest') {
            return http.Response(
              jsonEncode({
                'metrics': [
                  {'code': 'tempC', 'value': 31.4, 'status': 'normal'},
                  {'code': 'humidityPct', 'value': 42.8, 'status': 'normal'},
                  {'code': 'lightLux', 'value': 54612, 'status': 'warning'},
                ],
              }),
              200,
              headers: {'content-type': 'application/json'},
            );
          }

          if (path == '/api/v1/terrariums/terra-1/thresholds') {
            return http.Response(
              jsonEncode({'effectiveThresholds': []}),
              200,
              headers: {'content-type': 'application/json'},
            );
          }

          if (path == '/api/v1/terrariums/terra-1/readings') {
            final points = <Map<String, dynamic>>[];
            if (metric == 'tempC') {
              points.addAll([
                {
                  't': '2026-10-24T01:00:00Z',
                  'avg': 29.5,
                  'min': 29.0,
                  'max': 30.0,
                  'count': 2,
                },
                {
                  't': '2026-10-24T02:00:00Z',
                  'avg': 31.4,
                  'min': 31.0,
                  'max': 31.8,
                  'count': 3,
                },
              ]);
            } else if (metric == 'humidityPct') {
              points.addAll([
                {
                  't': '2026-10-24T01:00:00Z',
                  'avg': 40.2,
                  'min': 40.0,
                  'max': 40.5,
                  'count': 2,
                },
                {
                  't': '2026-10-24T02:00:00Z',
                  'avg': 42.8,
                  'min': 42.0,
                  'max': 43.0,
                  'count': 3,
                },
              ]);
            } else if (metric == 'lightLux') {
              points.addAll([
                {
                  't': '2026-10-24T01:00:00Z',
                  'avg': 12000,
                  'min': 11000,
                  'max': 13000,
                  'count': 2,
                },
                {
                  't': '2026-10-24T02:00:00Z',
                  'avg': 54612,
                  'min': 54000,
                  'max': 55000,
                  'count': 3,
                },
              ]);
            }
            return http.Response(
              jsonEncode({'metric': metric, 'points': points}),
              200,
              headers: {'content-type': 'application/json'},
            );
          }

          return http.Response(
            jsonEncode({'items': []}),
            200,
            headers: {'content-type': 'application/json'},
          );
        }),
      );
      api.accessToken = 'test-token';

      final appData = AppDataProvider(api: api);
      await appData.loadAll();

      await tester.pumpWidget(
        ChangeNotifierProvider<AppDataProvider>.value(
          value: appData,
          child: const MaterialApp(home: DashboardScreen()),
        ),
      );
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      expect(appData.currentHistory.length, 2);
      expect(appData.currentHistory[0].temperature, 29.5);
      expect(appData.currentHistory[1].temperature, 31.4);
      expect(appData.currentHistory[0].humidity, 40.2);
      expect(appData.currentHistory[1].humidity, 42.8);
      expect(appData.currentHistory[1].light, 54612);

      final chart = tester.widget<LineChart>(find.byType(LineChart));
      final bars = chart.data.lineBarsData;
      expect(bars.length, 2); // Temp + Hum active by default
      expect(bars[0].spots.map((s) => s.y).toList(), [29.5, 31.4]);
      expect(bars[1].spots.map((s) => s.y).toList(), [40.2, 42.8]);
    },
  );
}

