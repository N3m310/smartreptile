import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/core/permissions.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/screens/species_screen.dart';
import 'package:smart_reptile/services/species_profile_service.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/state/species_profile_provider.dart';
import 'package:smart_reptile/theme/app_theme.dart';

void main() {
  final testProfiles = {
    'items': [
      {
        'id': 'sp-1',
        'name': 'Arid (desert)',
        'scientificName': 'Pogona vitticeps',
        'climateZone': 'Arid',
        'photoperiodHours': 12,
        'isBuiltIn': true,
        'bands': [],
      },
    ],
  };

  Widget buildScreen({
    required SessionProvider session,
    required SpeciesProfileProvider provider,
  }) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<SessionProvider>.value(value: session),
        ChangeNotifierProvider<SpeciesProfileProvider>.value(value: provider),
      ],
      child: MaterialApp(
        theme: AppTheme.darkTheme,
        home: const SpeciesScreen(),
      ),
    );
  }

  Future<SessionProvider> buildSession({
    required String role,
    List<String> permissions = const [],
  }) async {
    final authApi = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/auth/login') {
          return http.Response(
            jsonEncode({
              'user': {
                'id': 'u-1',
                'username': 'test_user',
                'email': 'test@example.com',
                'role': role,
                'permissions': permissions,
                'preferredLanguage': 'vi',
                'timeZoneId': 'Asia/Ho_Chi_Minh',
              },
              'accessToken': 'access',
              'refreshToken': 'refresh',
              'accessTokenExpiresAtUtc': '2030-01-01T00:00:00Z',
              'refreshTokenExpiresAtUtc': '2030-02-01T00:00:00Z',
            }),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response('{}', 200, headers: {'content-type': 'application/json'});
      }),
    );
    final session = SessionProvider(api: authApi, store: TokenStore.inMemory());
    await session.signIn(usernameOrEmail: 'test_user', password: 'password');
    return session;
  }

  testWidgets(
    'owner role hides top header/banner (ảnh 2) and refresh button (ảnh 3) on SpeciesScreen',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final api = ApiClient(
        baseUrl: 'http://localhost',
        httpClient: MockClient((request) async {
          if (request.url.path == '/api/v1/species-profiles') {
            return http.Response(
              jsonEncode(testProfiles),
              200,
              headers: {'content-type': 'application/json'},
            );
          }
          return http.Response('{}', 200, headers: {'content-type': 'application/json'});
        }),
      );

      final service = SpeciesProfileService(api: api);
      final provider = SpeciesProfileProvider(service: service);
      await provider.fetchProfiles();

      // 1. Owner role
      final ownerSession = await buildSession(role: 'Owner');
      await tester.pumpWidget(buildScreen(session: ownerSession, provider: provider));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      // Header and guide banner (Ảnh 2) must be hidden
      expect(find.text('Hồ sơ loài & Vi khí hậu'), findsNothing);
      expect(find.textContaining('Chế độ Người dùng: Tra cứu thông số vi khí hậu'), findsNothing);

      // Refresh button (Ảnh 3) must be hidden
      expect(find.byIcon(Icons.refresh), findsNothing);

      // Search input remains visible
      expect(find.text('Tìm loài...'), findsOneWidget);

      // 2. Technician role (non-admin, non-owner)
      final techSession = await buildSession(
        role: 'Technician',
        permissions: [Permissions.speciesRead],
      );
      await tester.pumpWidget(buildScreen(session: techSession, provider: provider));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      // Header and guide banner must be visible for Technician
      expect(find.text('Hồ sơ loài & Vi khí hậu'), findsOneWidget);
      expect(find.textContaining('Chế độ Người dùng: Tra cứu thông số vi khí hậu'), findsOneWidget);

      // Refresh button must be visible for Technician
      expect(find.byIcon(Icons.refresh), findsOneWidget);
    },
  );

  testWidgets(
    'admin role can open create species dialog with lightLux min/max fields and submit successfully',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      Map<String, dynamic>? capturedBody;
      final api = ApiClient(
        baseUrl: 'http://localhost',
        httpClient: MockClient((request) async {
          if (request.url.path == '/api/v1/species-profiles' && request.method == 'GET') {
            return http.Response(
              jsonEncode(testProfiles),
              200,
              headers: {'content-type': 'application/json'},
            );
          }
          if (request.url.path == '/api/v1/species-profiles' && request.method == 'POST') {
            capturedBody = jsonDecode(request.body) as Map<String, dynamic>;
            return http.Response(
              jsonEncode({
                'id': 'sp-created',
                'name': capturedBody!['name'],
                'scientificName': capturedBody!['scientificName'],
                'climateZone': capturedBody!['climateZone'],
                'photoperiodHours': capturedBody!['photoperiodHours'],
                'lightThresholdLux': capturedBody!['lightThresholdLux'],
                'bands': capturedBody!['bands'],
              }),
              201,
              headers: {'content-type': 'application/json'},
            );
          }
          return http.Response('{}', 200, headers: {'content-type': 'application/json'});
        }),
      );

      final service = SpeciesProfileService(api: api);
      final provider = SpeciesProfileProvider(service: service);
      await provider.fetchProfiles();

      final adminSession = await buildSession(
        role: 'Administrator',
        permissions: [Permissions.speciesWrite, Permissions.speciesRead],
      );

      await tester.pumpWidget(buildScreen(session: adminSession, provider: provider));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 200));

      // 'Thêm loài' button must be present for Admin
      final addBtn = find.text('Thêm loài');
      expect(addBtn, findsOneWidget);
      await tester.tap(addBtn);
      await tester.pumpAndSettle();

      // Dialog should be open
      expect(find.text('Thêm hồ sơ loài mới'), findsOneWidget);
      expect(find.text('Độ sáng min (Lux)'), findsOneWidget);
      expect(find.text('Độ sáng max (Lux)'), findsOneWidget);

      // Enter name
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Tên loài bò sát *'),
        'Kỳ đà hoa',
      );

      // Enter light min & max
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Độ sáng min (Lux)'),
        '300',
      );
      await tester.enterText(
        find.widgetWithText(TextFormField, 'Độ sáng max (Lux)'),
        '1200',
      );

      // Submit
      final submitBtn = find.widgetWithText(ElevatedButton, 'Tạo hồ sơ loài');
      await tester.ensureVisible(submitBtn);
      await tester.tap(submitBtn);
      await tester.pumpAndSettle();

      // Check captured POST body
      expect(capturedBody, isNotNull);
      expect(capturedBody!['name'], 'Kỳ đà hoa');
      expect(capturedBody!['lightThresholdLux'], 300.0);

      final bands = capturedBody!['bands'] as List<dynamic>;
      final lightBand = bands.firstWhere((b) => b['metric'] == 'lightLux');
      expect(lightBand, isNotNull);
      expect(lightBand['targetMin'], 300.0);
      expect(lightBand['targetMax'], 1200.0);
    },
  );
}

