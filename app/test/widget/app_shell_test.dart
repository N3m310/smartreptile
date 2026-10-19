import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/app.dart';
import 'package:smart_reptile/core/permissions.dart';
import 'package:smart_reptile/data/api_client.dart';
import 'package:smart_reptile/data/token_store.dart';
import 'package:smart_reptile/state/accounts_provider.dart';
import 'package:smart_reptile/state/admin_provider.dart';
import 'package:smart_reptile/state/app_data_provider.dart';
import 'package:smart_reptile/state/roles_provider.dart';
import 'package:smart_reptile/state/session_provider.dart';
import 'package:smart_reptile/state/settings_provider.dart';

/// Widget tests for the app shell (`lib/app.dart`) — the surface a keeper sees.
///
/// Three things are asserted, and all three are honesty rules rather than layout. The shell carries the simulated-data
/// notice from 2026-10-14, when the app stopped opening on the prototype: the keeper's screens are still the M1
/// models, so the app says so on every one of them. An action that changes nothing but this screen says *that* rather
/// than reporting a success — the defect the web client fixed by deleting its mock threshold editor (task 4.20). And
/// the administration tab exists only while the session holds a permission that can open something behind it: the
/// screens are gated on permission codes, never on a role name (FR-18).
///
/// The locale is pinned in every case. The test binding runs under `en_US` and the app deliberately follows the
/// device locale when nothing is stored, so without this the assertions would be about the machine running the
/// suite — and the notice case exists precisely because the copy comes from the shared deck rather than being a
/// literal in a widget.
void main() {
  late SettingsProvider settings;
  late SessionProvider session;

  Widget app() => MultiProvider(
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

  /// A tall surface, so a screen is laid out in full and `find.text` can see the parts below the fold.
  void useTallSurface(WidgetTester tester) {
    tester.view.physicalSize = const Size(1200, 3600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  /// A signed-in session holding exactly [permissions], built through the real sign-in path over a stub transport.
  ///
  /// The stub answers the login call the way the API does, so the codes under test are read off the payload the
  /// shell would read in production — a test-only setter would have proved nothing about the gate.
  Future<SessionProvider> sessionHolding(List<String> permissions) async {
    final api = ApiClient(
      baseUrl: 'http://localhost',
      httpClient: MockClient(
        (_) async => http.Response(
          jsonEncode({
            'user': {
              'id': 'u-admin',
              'username': 'quan-tri',
              'email': 'quan-tri@example.com',
              'role': 'Admin',
              'roleId': 'a0e00000-0000-4000-8000-000000000004',
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
          headers: const {'content-type': 'application/json'},
        ),
      ),
    );

    final signedIn = SessionProvider(
      api: api,
      store: TokenStore.inMemory(),
    );
    expect(
      await signedIn.signIn(usernameOrEmail: 'quan-tri', password: 'Demo1234!'),
      isTrue,
    );
    return signedIn;
  }

  Future<void> useLocale(String languageCode, {List<String>? permissions}) async {
    settings = SettingsProvider();
    await settings.setLocale(Locale(languageCode));
    session = permissions == null
        ? (SessionProvider(
            api: ApiClient(baseUrl: 'http://localhost'),
            store: TokenStore.inMemory(),
          )..enterGuestMode())
        : await sessionHolding(permissions);
  }

  testWidgets('the shell carries the simulated-data notice on the first tab', (tester) async {
    await useLocale('vi');
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    // `findRichText` because the notice is one styled paragraph: its title is a span inside it, not its own widget.
    expect(find.textContaining('Dữ liệu mô phỏng', findRichText: true), findsOneWidget);
    expect(
      find.textContaining('chưa gửi lên máy chủ', findRichText: true),
      findsOneWidget,
      reason: 'the notice has to say the actions do not reach the server',
    );
  });

  testWidgets('the same notice reads English when the app is in English', (tester) async {
    await useLocale('en');
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    expect(
      find.textContaining('Simulated data', findRichText: true),
      findsOneWidget,
      reason: 'the copy is a deck key, not a literal in a widget',
    );
    expect(find.textContaining('Dữ liệu mô phỏng', findRichText: true), findsNothing);
  });

  testWidgets('the notice stays while the tabs change', (tester) async {
    await useLocale('vi');
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Cài đặt'));
    await tester.pumpAndSettle();

    expect(
      find.textContaining('Dữ liệu mô phỏng', findRichText: true),
      findsOneWidget,
      reason: 'every keeper tab is still on the mock models, so the notice is about them rather than one screen',
    );
  });

  testWidgets('saving the mock thresholds says what happened instead of reporting a success', (tester) async {
    await useLocale('vi');
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Cài đặt'));
    await tester.pumpAndSettle();

    // The scrollable is named explicitly: every text field on this screen contains one of its own, so the default
    // "the only Scrollable in the tree" finder is ambiguous (and always was — the screen has six text fields).
    await tester.scrollUntilVisible(
      find.text('Lưu cấu hình ngưỡng'),
      400,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.text('Lưu cấu hình ngưỡng'));
    await tester.pumpAndSettle();

    expect(
      find.textContaining('chỉ có trên màn hình này', findRichText: true),
      findsOneWidget,
      reason: 'the numbers changed on screen; nothing was sent, and the sentence has to say which',
    );
    expect(
      find.textContaining('thành công', findRichText: true),
      findsNothing,
      reason: 'no action on this surface may claim a success it did not perform',
    );
  });

  testWidgets('a session without an administrative permission gets no administration tab', (tester) async {
    await useLocale('vi');
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    expect(
      find.text('Quản trị'),
      findsNothing,
      reason: 'a signed-out session holds no code, so the tab would lead to a dead screen',
    );
  });

  testWidgets('holding data.read.any adds the administration tab and drops the mock notice there', (tester) async {
    await useLocale('vi', permissions: [Permissions.dataReadAny]);
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    expect(find.text('Quản trị'), findsOneWidget);

    await tester.tap(find.text('Quản trị'));
    await tester.pumpAndSettle();

    expect(
      find.textContaining('Dữ liệu mô phỏng', findRichText: true),
      findsNothing,
      reason: 'the administration screens read the API, so the simulated-data notice would be untrue over them',
    );
    expect(
      find.text('Toàn cảnh hệ thống'),
      findsOneWidget,
      reason: 'the overview is behind data.read.any and available',
    );
    expect(
      find.text('Toàn bộ dữ liệu'),
      findsOneWidget,
      reason: 'the get-all lists is behind data.read.any and available',
    );
    expect(
      find.text('Tài khoản'),
      findsNothing,
      reason: 'accounts needs user.read, which this session does not hold',
    );
    expect(
      find.text('Vai trò'),
      findsNothing,
      reason: 'roles needs user.read, which this session does not hold',
    );
  });

  testWidgets('holding user.read alone offers accounts and roles and nothing cross-account', (tester) async {
    await useLocale('vi', permissions: [Permissions.userRead]);
    useTallSurface(tester);
    await tester.pumpWidget(app());
    await tester.pumpAndSettle();

    await tester.tap(find.text('Quản trị'));
    await tester.pumpAndSettle();

    expect(
      find.text('Tài khoản'),
      findsOneWidget,
      reason: 'the accounts list is behind user.read and available',
    );
    expect(
      find.text('Vai trò'),
      findsOneWidget,
      reason: 'the role matrix is behind user.read and available',
    );
    expect(
      find.text('Toàn cảnh hệ thống'),
      findsNothing,
      reason: 'the deployment view needs data.read.any, which this session does not hold',
    );
    expect(
      find.text('Toàn bộ dữ liệu'),
      findsNothing,
      reason: 'the data lists need data.read.any, which this session does not hold',
    );
  });
}
