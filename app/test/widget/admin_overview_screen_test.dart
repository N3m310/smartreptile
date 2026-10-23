import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/core/permissions.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/l10n/generated/app_localizations.dart';
import 'package:smart_reptile/screens/admin/admin_overview_screen.dart';
import 'package:smart_reptile/state/admin_provider.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/theme/app_theme.dart';

void main() {
  final overviewJson = {
    'counts': {
      'accounts': 5,
      'accountsActive': 4,
      'terrariums': 3,
      'terrariumsWithDevice': 2,
      'devices': 4,
      'devicesOnline': 1,
      'alertsOpen': 10,
      'alertsCritical': 3,
      'speciesProfiles': 6,
    },
    'families': [
      {
        'key': 'terrarium',
        'metrics': ['batteryPct', 'humidity', 'illuminance', 'rssiDbm'],
        'alertsOpen': 10,
      },
      {
        'key': 'species',
        'metrics': ['tempSurfaceC', 'tempAmbientC', 'uvIndex'],
        'alertsOpen': 0,
      },
    ],
    'accounts': [],
  };

  Widget buildScreen({required SessionProvider session, required AdminProvider admin}) {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<SessionProvider>.value(value: session),
        ChangeNotifierProvider<AdminProvider>.value(value: admin),
      ],
      child: MaterialApp(
        locale: const Locale('vi'),
        theme: AppTheme.darkTheme,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
        home: const AdminOverviewScreen(),
      ),
    );
  }

  testWidgets('admin role hides the two alert families section while other roles display it', (tester) async {
    tester.view.physicalSize = const Size(1200, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/admin/overview') {
          return http.Response(
            jsonEncode(overviewJson),
            200,
            headers: {'content-type': 'application/json'},
          );
        }
        return http.Response('{}', 200, headers: {'content-type': 'application/json'});
      }),
    );

    // 1. Non-admin role (Technician with data.read.any permission)
    final techApi = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/auth/login') {
          return http.Response(
            jsonEncode({
              'user': {
                'id': 'u-tech',
                'username': 'tech_user',
                'email': 'tech@example.com',
                'role': 'technician',
                'permissions': [Permissions.dataReadAny],
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
    final techSession = SessionProvider(api: techApi, store: TokenStore.inMemory());
    await techSession.signIn(usernameOrEmail: 'tech_user', password: 'password');
    final techAdmin = AdminProvider(api: api);
    await techAdmin.loadOverview();

    await tester.pumpWidget(buildScreen(session: techSession, admin: techAdmin));
    await tester.pump();

    expect(find.text('Hai nhóm cảnh báo'), findsOneWidget);

    // 2. Admin role
    final adminAuthApi = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient((request) async {
        if (request.url.path == '/api/v1/auth/login') {
          return http.Response(
            jsonEncode({
              'user': {
                'id': 'u-admin',
                'username': 'admin_user',
                'email': 'admin@example.com',
                'role': 'administrator',
                'permissions': [Permissions.dataReadAny, Permissions.userRead],
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
    final adminSession = SessionProvider(api: adminAuthApi, store: TokenStore.inMemory());
    await adminSession.signIn(usernameOrEmail: 'admin_user', password: 'password');
    final adminProv = AdminProvider(api: api);
    await adminProv.loadOverview();

    await tester.pumpWidget(buildScreen(session: adminSession, admin: adminProv));
    await tester.pump();

    expect(find.text('Hai nhóm cảnh báo'), findsNothing);
  });
}
