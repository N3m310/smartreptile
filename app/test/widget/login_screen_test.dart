import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/app.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/screens/login_screen.dart';
import 'package:smart_reptile/state/accounts_provider.dart';
import 'package:smart_reptile/state/admin_provider.dart';
import 'package:smart_reptile/state/app_data_provider.dart';
import 'package:smart_reptile/state/roles_provider.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/state/settings_provider.dart';

void main() {
  late SettingsProvider settings;
  late SessionProvider session;

  Future<void> setupEnv(String lang, {http.Client? client}) async {
    final apiClient = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: client ??
          MockClient((_) async => http.Response('{}', 200)),
    );

    settings = SettingsProvider();
    await settings.setLocale(Locale(lang));
    session = SessionProvider(
      api: apiClient,
      store: TokenStore.inMemory(),
    );
  }

  Widget buildApp() {
    return MultiProvider(
      providers: [
        ChangeNotifierProvider<AppDataProvider>(create: (_) => AppDataProvider()),
        ChangeNotifierProvider<SettingsProvider>.value(value: settings),
        ChangeNotifierProvider<SessionProvider>.value(value: session),
        ChangeNotifierProvider<AdminProvider>(
          create: (_) => AdminProvider(api: ApiClient(baseUrl: 'http://localhost')),
        ),
        ChangeNotifierProvider<AccountsProvider>(
          create: (_) => AccountsProvider(api: ApiClient(baseUrl: 'http://localhost')),
        ),
        ChangeNotifierProvider<RolesProvider>(
          create: (_) => RolesProvider(api: ApiClient(baseUrl: 'http://localhost')),
        ),
      ],
      child: const SmartReptileApp(),
    );
  }

  void useTallSurface(WidgetTester tester) {
    tester.view.physicalSize = const Size(1200, 3600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  testWidgets('launches into LoginScreen when not signed in', (tester) async {
    useTallSurface(tester);
    await setupEnv('vi');
    await tester.pumpWidget(buildApp());
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsOneWidget);
    expect(find.textContaining('VIVARIUM', findRichText: true), findsOneWidget);
    expect(find.textContaining('GUARD', findRichText: true), findsOneWidget);
    expect(find.text('Đăng nhập hệ thống'), findsOneWidget);
    expect(find.text('Đăng nhập'), findsOneWidget);
    expect(find.text('Tạo tài khoản'), findsOneWidget);
    expect(find.text('Quên mật khẩu?'), findsOneWidget);
    expect(find.text('Khám phá bản thử nghiệm (Khách)'), findsOneWidget);
  });

  testWidgets('can switch between sign-in, register and recovery modes', (tester) async {
    useTallSurface(tester);
    await setupEnv('vi');
    await tester.pumpWidget(buildApp());
    await tester.pumpAndSettle();

    // Switch to Register
    await tester.tap(find.text('Tạo tài khoản'));
    await tester.pumpAndSettle();

    expect(find.text('Đăng ký'), findsOneWidget);
    expect(find.text('Quay lại đăng nhập'), findsOneWidget);

    // Switch back to Sign In
    await tester.tap(find.text('Quay lại đăng nhập'));
    await tester.pumpAndSettle();

    expect(find.text('Đăng nhập hệ thống'), findsOneWidget);

    // Switch to Recover
    await tester.tap(find.text('Quên mật khẩu?'));
    await tester.pumpAndSettle();

    expect(find.text('Gửi mã đặt lại'), findsOneWidget);
    expect(find.text('Quay lại đăng nhập'), findsOneWidget);
  });

  testWidgets('switching language updates copy to English', (tester) async {
    useTallSurface(tester);
    await setupEnv('vi');
    await tester.pumpWidget(buildApp());
    await tester.pumpAndSettle();

    // Switch to English
    await tester.tap(find.text('English'));
    await tester.pumpAndSettle();

    expect(find.text('Sign in'), findsWidgets);
    expect(find.text('Create an account'), findsOneWidget);
    expect(find.text('Forgot your password?'), findsOneWidget);
  });

  testWidgets('tapping guest mode enters the shell directly', (tester) async {
    useTallSurface(tester);
    await setupEnv('vi');
    await tester.pumpWidget(buildApp());
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsOneWidget);

    await tester.tap(find.text('Khám phá bản thử nghiệm (Khách)'));
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsNothing);
    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.text('Tổng quan'), findsOneWidget);
  });

  testWidgets('successful login transitions from LoginScreen to the shell', (tester) async {
    useTallSurface(tester);

    final mockClient = MockClient((request) async {
      if (request.url.path.contains('/api/v1/auth/login')) {
        return http.Response(
          jsonEncode({
            'user': {
              'id': 'u1',
              'username': 'keeper',
              'email': 'keeper@vivariumguard.vn',
              'role': 'Owner',
              'roleId': 'a0e00000-0000-4000-8000-000000000001',
              'permissions': ['terrarium.read', 'terrarium.write'],
              'preferredLanguage': 'vi',
              'timeZoneId': 'Asia/Ho_Chi_Minh',
            },
            'accessToken': 'jwt_access',
            'refreshToken': 'jwt_refresh',
            'accessTokenExpiresAtUtc': '2030-01-01T00:00:00Z',
            'refreshTokenExpiresAtUtc': '2030-02-01T00:00:00Z',
          }),
          200,
          headers: {'content-type': 'application/json'},
        );
      }
      return http.Response('{}', 200);
    });

    await setupEnv('vi', client: mockClient);
    await tester.pumpWidget(buildApp());
    await tester.pumpAndSettle();

    // Enter username and password
    final fields = find.byType(TextFormField);
    await tester.enterText(fields.at(0), 'keeper');
    await tester.enterText(fields.at(1), 'Password123!');
    await tester.pumpAndSettle();

    // Tap Sign In
    await tester.tap(find.text('Đăng nhập'));
    await tester.pumpAndSettle();

    expect(find.byType(LoginScreen), findsNothing);
    expect(find.byType(NavigationBar), findsOneWidget);
    expect(find.text('Tổng quan'), findsOneWidget);
  });
}
