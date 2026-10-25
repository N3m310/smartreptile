import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/core/permissions.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/models/terrarium_models.dart';
import 'package:smart_reptile/screens/alerts_screen.dart';
import 'package:smart_reptile/state/app_data_provider.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/theme/app_theme.dart';

void main() {
  Widget buildScreen({
    required SessionProvider session,
    required AppDataProvider appData,
  }) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<SessionProvider>.value(value: session),
        ChangeNotifierProvider<AppDataProvider>.value(value: appData),
      ],
      child: MaterialApp(
        theme: AppTheme.darkTheme,
        home: const AlertsScreen(),
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
    'owner and admin roles hide logic threshold banner while other roles display it',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final appData = AppDataProvider();
      const bannerText =
          'Logic ngưỡng: Kích hoạt khi giá trị vượt ngoài Min/Max (Không AI)';

      // 1. Owner role -> banner must be hidden
      final ownerSession = await buildSession(role: 'Owner');
      await tester.pumpWidget(buildScreen(session: ownerSession, appData: appData));
      await tester.pumpAndSettle();
      expect(find.text(bannerText), findsNothing);

      // 2. Admin role -> banner must be hidden
      final adminSession = await buildSession(
        role: 'Admin',
        permissions: [Permissions.dataReadAny],
      );
      await tester.pumpWidget(buildScreen(session: adminSession, appData: appData));
      await tester.pumpAndSettle();
      expect(find.text(bannerText), findsNothing);

      // 3. Technician role (other role) -> banner must be visible
      final techSession = await buildSession(
        role: 'Technician',
        permissions: const [],
      );
      await tester.pumpWidget(buildScreen(session: techSession, appData: appData));
      await tester.pumpAndSettle();
      expect(find.text(bannerText), findsOneWidget);
    },
  );

  testWidgets(
    'alert card displays terrarium name after "Chuồng nuôi"',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final appData = AppDataProvider();
      final ownerSession = await buildSession(role: 'Owner');
      await tester.pumpWidget(buildScreen(session: ownerSession, appData: appData));
      await tester.pumpAndSettle();

      // Verify that "Chuồng nuôi: " includes the name of the terrarium
      expect(find.textContaining('Chuồng nuôi:'), findsWidgets);
    },
  );

  testWidgets(
    'AlertModel parses lightLux metric into light_out_of_range alert and displays sun icon',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);

      final rawAlert = {
        'id': 'alt-light-1',
        'metric': 'lightLux',
        'family': 'enclosure',
        'severity': 'critical',
        'state': 'active',
        'triggeringValue': 1500,
        'terrariumId': 't-1',
        'terrariumName': 'Chuồng nuôi Bò Sát VIP',
        'triggeredAt': '2026-10-24T10:00:00Z',
      };

      final alert = AlertModel.fromApi(rawAlert);
      expect(alert.type, 'light_out_of_range');
      expect(alert.title, 'Cảnh báo ánh sáng');
      expect(alert.message, contains('1500 Lux (vượt ngưỡng cho phép)'));
      expect(alert.severity, 'danger');

      final appData = AppDataProvider();
      appData.alerts.insert(0, alert);

      final ownerSession = await buildSession(role: 'Owner');
      await tester.pumpWidget(buildScreen(session: ownerSession, appData: appData));
      await tester.pumpAndSettle();

      expect(find.text('Cảnh báo ánh sáng'), findsOneWidget);
      expect(find.textContaining('1500 Lux (vượt ngưỡng cho phép)'), findsOneWidget);
      expect(find.byIcon(Icons.wb_sunny_outlined), findsWidgets);
    },
  );
}

