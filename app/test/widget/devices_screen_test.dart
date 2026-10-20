import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/l10n/generated/app_localizations.dart';
import 'package:smart_reptile/screens/devices_screen.dart';
import 'package:smart_reptile/state/device_provider.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/state/terrarium_provider.dart';
import 'package:smart_reptile/theme/app_theme.dart';

void main() {
  final testDevicesJson = [
    {
      'deviceId': 'dev-esp32-01',
      'deviceName': 'Node Cảm Biến Bể 1',
      'macAddress': 'AA:BB:CC:DD:EE:01',
      'hardwareChipId': 'ESP32-S3-WROOM-1',
      'protocol': 'MQTT_JSON_V1',
      'runtime': 'Online',
      'runtimeStatus': 'Online',
      'firmwareVersion': '1.4.2',
      'samplingIntervalSec': 15,
      'publishIntervalSec': 30,
      'terrariumId': 't-01',
      'terrariumName': 'Bể Rồng Úc #1',
      'signalDbm': -55,
      'batteryPercent': 98,
      'lastSeenAtUtc': '2026-10-20T10:00:00Z',
    },
    {
      'deviceId': 'dev-esp32-02',
      'deviceName': 'Node Đo Dự Phòng',
      'macAddress': 'AA:BB:CC:DD:EE:02',
      'hardwareChipId': 'ESP32-WROOM-32D',
      'protocol': 'MQTT_JSON_V1',
      'runtime': 'Offline',
      'runtimeStatus': 'Offline',
      'firmwareVersion': '1.3.0',
      'samplingIntervalSec': 30,
      'publishIntervalSec': 60,
      'terrariumId': null,
      'terrariumName': null,
      'signalDbm': null,
      'batteryPercent': null,
      'lastSeenAtUtc': '2026-10-19T08:00:00Z',
    },
  ];

  late ApiClient api;
  late DeviceProvider deviceProvider;
  late TerrariumProvider terrariumProvider;
  late SessionProvider sessionProvider;

  setUp(() {
    api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/devices') {
          return http.Response(
            jsonEncode({'items': testDevicesJson}),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        if (request.url.path == '/api/v1/terrariums') {
          return http.Response(
            jsonEncode({
              'items': [
                {
                  'id': 't-01',
                  'name': 'Bể Rồng Úc #1',
                  'description': 'Mô tả',
                  'speciesProfileId': 'sp-01',
                  'speciesName': 'Bearded Dragon',
                  'timeZoneId': 'Asia/Ho_Chi_Minh',
                }
              ]
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response('{}', 200, headers: {'content-type': 'application/json'});
      }),
    );

    deviceProvider = DeviceProvider(api: api);
    terrariumProvider = TerrariumProvider(api);
    sessionProvider = SessionProvider(api: api, store: TokenStore.inMemory())
      ..enterGuestMode();
  });

  Widget buildScreen() {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<DeviceProvider>.value(value: deviceProvider),
        ChangeNotifierProvider<TerrariumProvider>.value(value: terrariumProvider),
        ChangeNotifierProvider<SessionProvider>.value(value: sessionProvider),
      ],
      child: MaterialApp(
        locale: const Locale('vi'),
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        theme: AppTheme.darkTheme,
        home: const DevicesScreen(),
      ),
    );
  }

  testWidgets('renders device list and summary tiles', (tester) async {
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(buildScreen());
    await tester.pumpAndSettle();

    expect(find.text('Thiết bị IoT'), findsOneWidget);
    expect(find.text('Node Cảm Biến Bể 1'), findsOneWidget);
    expect(find.text('Node Đo Dự Phòng'), findsOneWidget);
    expect(find.text('AA:BB:CC:DD:EE:01'), findsOneWidget);
    expect(find.text('Bể Rồng Úc #1'), findsOneWidget);
    expect(find.text('Chưa gán'), findsOneWidget);
  });

  testWidgets('filters devices by status and search text', (tester) async {
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(buildScreen());
    await tester.pumpAndSettle();

    // Tap Offline filter chip
    final offlineChip = find.ancestor(
      of: find.text('Ngoại tuyến'),
      matching: find.byType(InkWell),
    ).first;
    await tester.tap(offlineChip);
    await tester.pumpAndSettle();

    expect(find.text('Node Đo Dự Phòng'), findsOneWidget);
    expect(find.text('Node Cảm Biến Bể 1'), findsNothing);

    // Search query
    final searchField = find.byType(TextField).first;
    await tester.enterText(searchField, 'Dự Phòng');
    await tester.pumpAndSettle();

    expect(find.text('Node Đo Dự Phòng'), findsOneWidget);
  });

  testWidgets('shows device technical details dialog', (tester) async {
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(buildScreen());
    await tester.pumpAndSettle();

    // Tap on the first card to open details
    await tester.tap(find.text('Node Cảm Biến Bể 1'));
    await tester.pumpAndSettle();

    expect(find.text('Mã thiết bị (ID)'), findsOneWidget);
    expect(find.text('ESP32-S3-WROOM-1'), findsOneWidget);
    expect(find.text('MQTT_JSON_V1'), findsOneWidget);
    expect(find.text('Đóng'), findsOneWidget);

    await tester.tap(find.text('Đóng'));
    await tester.pumpAndSettle();

    expect(find.text('Mã thiết bị (ID)'), findsNothing);
  });
}
