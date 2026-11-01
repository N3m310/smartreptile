import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:integration_test/integration_test.dart';
import 'package:smart_reptile/main.dart' as app;
import 'package:smart_reptile/screens/login_screen.dart';
import 'package:smart_reptile/screens/species_screen.dart';

const _apiBase = String.fromEnvironment(
  'API_BASE',
  defaultValue: 'http://10.0.2.2:8080',
);
const _password = 'E2eKeeper!2026';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  group('SmartReptile mobile E2E', () {
    late _DisposableOwner owner;

    setUpAll(() async {
      try {
        final response = await http
            .get(Uri.parse('$_apiBase/health/ready'))
            .timeout(const Duration(seconds: 5));
        if (response.statusCode != 200) {
          throw StateError(
            'Mobile E2E requires a ready API at $_apiBase '
            '(HTTP ${response.statusCode}).',
          );
        }
      } on SocketException catch (error) {
        throw StateError(
          'Mobile E2E cannot reach the API at $_apiBase. '
          'Start the backend and database first. ${error.message}',
        );
      } on http.ClientException catch (error) {
        throw StateError(
          'Mobile E2E cannot reach the API at $_apiBase. '
          'Start the backend and database first. $error',
        );
      } on TimeoutException {
        throw StateError(
          'Mobile E2E API readiness check timed out for $_apiBase.',
        );
      }
      owner = _DisposableOwner.create();
      await _registerOwnerFixture(owner);
    });

    tearDown(() async {
      await _cleanUpTerrarium(owner);
      owner.terrariumName = null;
    });

    tearDownAll(() => _cleanUpAccount(owner));

    testWidgets('rejects invalid credentials without leaving sign-in', (
      tester,
    ) async {
      await app.main();
      await _waitFor(tester, find.byType(LoginScreen));
      await _useEnglish(tester);

      await tester.enterText(
        find.byType(TextFormField).at(0),
        'mobile_e2e_invalid_${DateTime.now().microsecondsSinceEpoch}',
      );
      await tester.enterText(find.byType(TextFormField).at(1), 'Wrong!Pass1');
      await tester.tap(find.text('Sign in').last);
      await _waitFor(tester, find.text('Wrong username or password.'));
      expect(find.byType(LoginScreen), findsOneWidget);
    });

    testWidgets('changes the sign-in language and restores Vietnamese', (
      tester,
    ) async {
      await app.main();
      await _waitFor(tester, find.byType(LoginScreen));
      await _useEnglish(tester);
      expect(find.text('Sign in'), findsWidgets);

      final vietnamese = find.text('Tiếng Việt');
      await _tapVisible(tester, vietnamese);
      await _waitFor(tester, find.text('Đăng nhập'));
      expect(find.byType(LoginScreen), findsOneWidget);
    });

    testWidgets('registers an Owner, signs in and signs out', (tester) async {
      final registrationOwner = _DisposableOwner.create();
      await app.main();
      await _waitFor(tester, find.byType(LoginScreen));
      await _useEnglish(tester);
      try {
        await _tapVisible(tester, find.text('Create an account'));
        await tester.pumpAndSettle();
        await tester.enterText(
          find.byType(TextFormField).at(0),
          registrationOwner.username,
        );
        await tester.enterText(
          find.byType(TextFormField).at(1),
          registrationOwner.email,
        );
        await tester.enterText(find.byType(TextFormField).at(2), _password);
        await tester.tap(find.text('Register'));
        await _waitFor(tester, find.text('Account created.'));
        registrationOwner.wasRegistered = true;

        expect(find.byType(LoginScreen), findsOneWidget);
        await tester.enterText(find.byType(TextFormField).at(1), _password);
        await _tapVisible(tester, find.text('Sign in').last);
        await _waitFor(tester, find.byType(NavigationBar));
        expect(find.text('Tổng quan'), findsOneWidget);

        await _signOut(tester);
        expect(find.byType(LoginScreen), findsOneWidget);
      } finally {
        await _cleanUpAccount(registrationOwner);
      }
    });

    testWidgets('Owner can navigate to each primary mobile surface', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      expect(find.byType(NavigationBar), findsOneWidget);
      expect(find.text('Tổng quan'), findsOneWidget);

      await tester.tap(find.byIcon(Icons.pets_outlined));
      await _waitFor(tester, find.text('New terrarium'));
      await tester.tap(find.byIcon(Icons.biotech_outlined));
      await _waitFor(tester, find.byType(SpeciesScreen));
      await tester.tap(find.byIcon(Icons.warning_amber_rounded));
      await _waitFor(tester, find.textContaining('Chưa xử lý'));
      await tester.tap(find.byIcon(Icons.show_chart_outlined));
      await _waitFor(tester, find.text('History'));
      await tester.tap(find.byIcon(Icons.person_outline));
      await _waitFor(tester, find.text(owner.username));
    });

    testWidgets('requires a terrarium name before creating', (tester) async {
      await _startOwnerApp(tester, owner);
      await _openTerrariumCreation(tester);
      final createButton = find.widgetWithText(ElevatedButton, 'Create');
      await _waitForEnabled(tester, createButton);
      await tester.tap(createButton);
      await _waitFor(tester, find.text('A name is required.'));
      expect(find.byType(AlertDialog), findsNothing);
    });

    testWidgets('creates a terrarium and shows its detail and empty history', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      final terrariumName = _newTerrariumName();
      owner.terrariumName = terrariumName;
      await _createTerrarium(tester, terrariumName);

      await _waitFor(tester, find.byType(BackButton));
      await _waitFor(tester, find.text('History'));
      await _waitFor(tester, find.textContaining('Data coverage'));
      await _waitFor(tester, find.textContaining('No readings are available'));
      await _waitFor(
        tester,
        find.text('No readings have been received from the device.'),
      );
      expect(find.text('Last 24 hours'), findsOneWidget);
      expect(find.text(terrariumName), findsOneWidget);
    });

    testWidgets('edits a terrarium name from its detail screen', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      final originalName = _newTerrariumName();
      final updatedName = '$originalName updated';
      owner.terrariumName = updatedName;
      await _createTerrarium(tester, originalName);

      await _openTerrariumActions(tester);
      await tester.tap(find.text('Edit terrarium'));
      await tester.pumpAndSettle();
      await _waitFor(tester, find.byType(AlertDialog));
      await tester.enterText(find.byType(TextFormField).first, updatedName);
      await tester.tap(find.text('Save').last);
      await _waitUntilGone(tester, find.byType(AlertDialog));
      await _waitFor(tester, find.text(updatedName));
    });

    testWidgets('cancels terrarium deletion without removing the terrarium', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      final terrariumName = _newTerrariumName();
      owner.terrariumName = terrariumName;
      await _createTerrarium(tester, terrariumName);

      await _openTerrariumActions(tester);
      await tester.tap(find.text('Delete terrarium').last);
      await tester.pumpAndSettle();
      await _waitFor(tester, find.byType(AlertDialog));
      await tester.tap(find.text('Cancel'));
      await _waitUntilGone(tester, find.byType(AlertDialog));
      expect(find.byType(BackButton), findsOneWidget);
      expect(find.text(terrariumName), findsOneWidget);
    });

    testWidgets('deletes a terrarium after confirmation', (tester) async {
      await _startOwnerApp(tester, owner);
      final terrariumName = _newTerrariumName();
      owner.terrariumName = terrariumName;
      await _createTerrarium(tester, terrariumName);

      await _openTerrariumActions(tester);
      await tester.tap(find.text('Delete terrarium').last);
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Delete terrarium'));
      await _waitUntilGone(tester, find.byType(BackButton));
      await _waitFor(tester, find.text('New terrarium'));
      expect(find.text(terrariumName), findsNothing);
    });

    testWidgets('changes history metric and time range', (tester) async {
      await _startOwnerApp(tester, owner);
      final terrariumName = _newTerrariumName();
      owner.terrariumName = terrariumName;
      await _createTerrarium(tester, terrariumName);
      await tester.pageBack();
      await tester.pumpAndSettle();
      await _waitFor(tester, find.text('New terrarium'));
      await tester.tap(find.byIcon(Icons.show_chart_outlined));
      await tester.pumpAndSettle();
      await _waitFor(tester, find.text('History'));
      expect(find.byType(BackButton), findsNothing);
      await _waitFor(tester, find.textContaining(terrariumName));

      final ranges = find.byType(DropdownButton<int>);
      expect(ranges, findsOneWidget);
      await _tapVisible(tester, ranges);
      await tester.tap(find.text('Last 7 days').last);
      await tester.pumpAndSettle();
      expect(find.text('Last 7 days'), findsOneWidget);

      final metrics = find.byWidgetPredicate(
        (widget) => widget is DropdownButton<String> && widget.value == 'tempC',
      );
      expect(metrics, findsOneWidget);
      await _tapVisible(tester, metrics);
      await tester.tap(find.textContaining('Humidity').last);
      await tester.pumpAndSettle();
      expect(find.textContaining('Humidity').first, findsOneWidget);
    });

    testWidgets('filters alerts by pending and resolved state', (tester) async {
      await _startOwnerApp(tester, owner);
      await tester.tap(find.text('Cảnh báo').last);
      final all = find.textContaining('Tất cả (');
      await _waitFor(tester, all);

      final pending = find.textContaining('Chưa xử lý (');
      final pendingChip = find
          .ancestor(of: pending, matching: find.byType(InkWell))
          .first;
      await _tapVisible(tester, pendingChip);
      await tester.pumpAndSettle();
      final resolved = find.textContaining('Đã xử lý (');
      await _waitFor(tester, resolved);
      expect(tester.widget<Text>(pending.first).style?.color, Colors.white);

      final resolvedChip = find
          .ancestor(of: resolved, matching: find.byType(InkWell))
          .first;
      await _tapVisible(tester, resolvedChip);
      await tester.pumpAndSettle();
      expect(tester.widget<Text>(resolved.first).style?.color, Colors.white);
      final allChip = find
          .ancestor(of: all, matching: find.byType(InkWell))
          .first;
      await _tapVisible(tester, allChip);
      await tester.pumpAndSettle();
      expect(tester.widget<Text>(all.first).style?.color, Colors.white);
    });

    testWidgets('searches the species catalogue and opens profile details', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      await tester.tap(find.byIcon(Icons.biotech_outlined));
      await _waitFor(tester, find.byType(SpeciesScreen));
      await _waitFor(tester, find.byType(Card));
      final search = find.byType(TextField).first;
      await tester.enterText(search, 'no_such_mobile_e2e_species');
      await _waitFor(
        tester,
        find.text('Không tìm thấy hồ sơ loài nào phù hợp.'),
      );
      await tester.enterText(search, '');
      await tester.pumpAndSettle();

      final firstProfile = find.byType(Card).first;
      await _tapVisible(tester, firstProfile);
      await _waitFor(tester, find.byType(AlertDialog));
      expect(find.text('Ngưỡng vi khí hậu chi tiết:'), findsOneWidget);
    });

    testWidgets('validates mismatched password confirmation in profile', (
      tester,
    ) async {
      await _startOwnerApp(tester, owner);
      await tester.tap(find.byIcon(Icons.person_outline));
      await _waitFor(tester, find.text(owner.username));
      final currentPassword = find.byType(TextFormField).at(0);
      final newPassword = find.byType(TextFormField).at(1);
      final confirmPassword = find.byType(TextFormField).at(2);
      await tester.ensureVisible(currentPassword);
      await tester.enterText(currentPassword, _password);
      await tester.ensureVisible(newPassword);
      await tester.enterText(newPassword, 'NewPassword!2');
      await tester.ensureVisible(confirmPassword);
      await tester.enterText(confirmPassword, 'Different!2');
      await _tapVisible(tester, find.text('Update password'));
      await _waitFor(
        tester,
        find.text('The repeated password does not match.'),
      );
      expect(find.text(owner.username), findsOneWidget);
    });
  });
}

Future<void> _startOwnerApp(WidgetTester tester, _DisposableOwner owner) async {
  await app.main();
  final restoreDeadline = DateTime.now().add(const Duration(seconds: 5));
  while (find.byType(NavigationBar).evaluate().isEmpty &&
      DateTime.now().isBefore(restoreDeadline)) {
    await tester.pump(const Duration(milliseconds: 250));
  }
  if (find.byType(NavigationBar).evaluate().isNotEmpty) return;

  await _waitFor(tester, find.byType(LoginScreen));
  await _useEnglish(tester);
  await _registerAndSignIn(tester, owner);
}

String _newTerrariumName() =>
    'Mobile E2E ${DateTime.now().microsecondsSinceEpoch}';

Future<void> _openTerrariumCreation(WidgetTester tester) async {
  await tester.tap(find.byIcon(Icons.pets_outlined));
  await _waitFor(tester, find.text('New terrarium'));
  await tester.tap(find.text('New terrarium'));
  await _waitForEnabled(tester, find.widgetWithText(ElevatedButton, 'Create'));
}

Future<void> _createTerrarium(WidgetTester tester, String name) async {
  await _openTerrariumCreation(tester);
  await tester.enterText(find.byType(TextFormField).first, name);
  FocusManager.instance.primaryFocus?.unfocus();
  await SystemChannels.textInput.invokeMethod<void>('TextInput.hide');
  await tester.pump(const Duration(milliseconds: 500));
  await tester.tap(find.text('Create'));
  await _waitFor(tester, find.text(name));
  await _waitFor(tester, find.byType(BackButton));
}

Future<void> _openTerrariumActions(WidgetTester tester) async {
  final actions = find.byType(PopupMenuButton<String>);
  await _waitFor(tester, actions);
  await tester.tap(actions);
  await tester.pumpAndSettle();
}

Future<void> _registerAndSignIn(
  WidgetTester tester,
  _DisposableOwner owner,
) async {
  await tester.enterText(find.byType(TextFormField).first, owner.username);
  await tester.enterText(find.byType(TextFormField).at(1), _password);
  await _tapVisible(tester, find.text('Sign in').last);
  await _waitFor(tester, find.byType(NavigationBar));
  expect(find.text('Tổng quan'), findsOneWidget);
}

Future<void> _waitForEnabled(WidgetTester tester, Finder finder) async {
  final deadline = DateTime.now().add(const Duration(seconds: 30));
  while (DateTime.now().isBefore(deadline)) {
    final buttons = finder.evaluate();
    if (buttons.isNotEmpty &&
        buttons.every(
          (element) => (element.widget as ElevatedButton).onPressed != null,
        )) {
      return;
    }
    await tester.pump(const Duration(milliseconds: 250));
  }
  fail('Timed out waiting for an enabled button: $finder');
}

Future<void> _useEnglish(WidgetTester tester) async {
  final language = find.text('English');
  if (language.evaluate().isNotEmpty) {
    await tester.tap(language);
    await tester.pumpAndSettle();
  }
}

Future<void> _tapVisible(WidgetTester tester, Finder finder) async {
  await tester.ensureVisible(finder);
  await tester.tap(finder);
  await tester.pumpAndSettle();
}

Future<void> _signOut(WidgetTester tester) async {
  await tester.tap(find.byIcon(Icons.person_outline));
  await tester.pumpAndSettle();
  await _tapVisible(tester, find.text('Sign out'));
  await _waitFor(tester, find.byType(LoginScreen));
}

Future<void> _waitFor(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 30),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (finder.evaluate().isEmpty && DateTime.now().isBefore(deadline)) {
    await tester.pump(const Duration(milliseconds: 250));
  }
  expect(finder, findsWidgets);
}

Future<void> _waitUntilGone(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 30),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (finder.evaluate().isNotEmpty && DateTime.now().isBefore(deadline)) {
    await tester.pump(const Duration(milliseconds: 250));
  }
  expect(finder, findsNothing);
}

Future<void> _registerOwnerFixture(_DisposableOwner owner) async {
  final client = http.Client();
  try {
    final base = _apiBase.replaceFirst(RegExp(r'/$'), '');
    final registration = await client.post(
      Uri.parse('$base/api/v1/auth/register'),
      headers: {'content-type': 'application/json'},
      body: jsonEncode({
        'username': owner.username,
        'email': owner.email,
        'password': _password,
        'preferredLanguage': 'en',
      }),
    );
    expect(
      registration.statusCode,
      202,
      reason: 'E2E Owner fixture registration failed.',
    );
    owner.wasRegistered = true;
    owner.accessToken = await _loginForCleanup(client, base, owner);
  } finally {
    client.close();
  }
}

Future<String> _loginForCleanup(
  http.Client client,
  String base,
  _DisposableOwner owner,
) async {
  final login = await client.post(
    Uri.parse('$base/api/v1/auth/login'),
    headers: {'content-type': 'application/json'},
    body: jsonEncode({
      'usernameOrEmail': owner.username,
      'password': _password,
    }),
  );
  expect(login.statusCode, 200, reason: 'E2E cleanup must sign in.');
  final accessToken =
      (jsonDecode(login.body) as Map<String, dynamic>)['accessToken'];
  expect(accessToken, isA<String>());
  return accessToken as String;
}

Future<void> _cleanUpTerrarium(_DisposableOwner owner) async {
  final token = owner.accessToken;
  final terrariumName = owner.terrariumName;
  if (token == null || terrariumName == null) return;

  final client = http.Client();
  try {
    final base = _apiBase.replaceFirst(RegExp(r'/$'), '');
    final headers = {'Authorization': 'Bearer $token'};
    final terrariums = await client.get(
      Uri.parse('$base/api/v1/terrariums'),
      headers: headers,
    );
    expect(terrariums.statusCode, 200, reason: 'E2E cleanup must list data.');
    final rows =
        (jsonDecode(terrariums.body) as Map<String, dynamic>)['items'] as List;
    for (final row in rows.whereType<Map<String, dynamic>>()) {
      if (row['name'] != terrariumName) continue;
      final deleted = await client.delete(
        Uri.parse(
          '$base/api/v1/terrariums/${row['id']}?allowUnboundDevice=true',
        ),
        headers: headers,
      );
      expect(deleted.statusCode, 204, reason: 'E2E terrarium cleanup failed.');
    }
  } finally {
    client.close();
  }
}

Future<void> _cleanUpAccount(_DisposableOwner owner) => _cleanUp(owner);

Future<void> _cleanUp(_DisposableOwner owner) async {
  if (!owner.wasRegistered) return;

  final client = http.Client();
  try {
    final base = _apiBase.replaceFirst(RegExp(r'/$'), '');
    final login = await client.post(
      Uri.parse('$base/api/v1/auth/login'),
      headers: {'content-type': 'application/json'},
      body: jsonEncode({
        'usernameOrEmail': owner.username,
        'password': _password,
      }),
    );
    expect(login.statusCode, 200, reason: 'E2E cleanup must sign in.');
    final accessToken =
        (jsonDecode(login.body) as Map<String, dynamic>)['accessToken'];
    expect(accessToken, isA<String>());
    final headers = {'Authorization': 'Bearer $accessToken'};

    final terrariums = await client.get(
      Uri.parse('$base/api/v1/terrariums'),
      headers: headers,
    );
    expect(terrariums.statusCode, 200, reason: 'E2E cleanup must list data.');
    final rows =
        (jsonDecode(terrariums.body) as Map<String, dynamic>)['items'] as List;
    for (final row in rows.whereType<Map<String, dynamic>>()) {
      if (row['name'] != owner.terrariumName) continue;
      final deleted = await client.delete(
        Uri.parse(
          '$base/api/v1/terrariums/${row['id']}?allowUnboundDevice=true',
        ),
        headers: headers,
      );
      expect(deleted.statusCode, 204, reason: 'E2E terrarium cleanup failed.');
    }

    final disabled = await client.delete(
      Uri.parse('$base/api/v1/auth/me'),
      headers: headers,
    );
    expect(disabled.statusCode, 204, reason: 'E2E account cleanup failed.');
  } finally {
    client.close();
  }
}

class _DisposableOwner {
  _DisposableOwner({required this.username, required this.email});

  factory _DisposableOwner.create() {
    final suffix = DateTime.now().microsecondsSinceEpoch;
    return _DisposableOwner(
      username: 'mobile_e2e_$suffix',
      email: 'mobile-e2e-$suffix@example.com',
    );
  }

  final String username;
  final String email;
  String? terrariumName;
  String? accessToken;
  bool wasRegistered = false;
}
