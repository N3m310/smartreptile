import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:smart_reptile/main.dart' as app;
import 'package:smart_reptile/screens/dashboard_screen.dart';
import 'package:smart_reptile/screens/login_screen.dart';

import 'helpers/fake_api.dart';
import 'helpers/fixtures.dart';
import 'helpers/pump.dart';
import 'helpers/session.dart';

/// Suite `hermetic` — `docs/04-quality/05-e2e-ai-testing-plan.md` §3 and §6 (`E2E-AI-02…08`).
///
/// The app is the published build; the backend is [FakeApi], a loopback `HttpServer` in this process. Nothing but
/// `127.0.0.1:18999` is contacted, so the suite needs no Docker, no database and no network, and every response is
/// byte-identical between runs.
///
/// Run it with `--dart-define=API_BASE=http://127.0.0.1:18999` — `scripts/e2e.ps1 -Suite hermetic` does.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  late FakeApi api;

  setUpAll(() async {
    api = await FakeApi.start();
    // The five reads `AppDataProvider.loadAll()` and the species service fan out to. Stubbed as empty lists so a
    // signed-in shell renders its empty state instead of an error.
    for (final path in shellReadPaths) {
      api.onEmptyList(path);
    }
    // A stored token is rotated on launch; refusing it makes "restore" behave like a first run when a case wants
    // that. Cases that sign in stub `POST /auth/login` themselves.
    api.on(
      'POST',
      '/api/v1/auth/refresh',
      (_) => const StubReply(401, {
        'code': 'refresh_token_spent',
        'detail': 'The refresh token was already used.',
      }),
    );
    // Sign-out revokes the refresh token server-side before dropping it locally. A 200 here lets `E2E-AI-08` test
    // the local half, which is the half that can leak a session onto the next launch.
    api.on('POST', '/api/v1/auth/logout', (_) => <String, dynamic>{});
  });

  tearDownAll(() async {
    // A request the stub did not know about is a route this suite has silently stopped covering.
    expect(
      api.unmatched,
      isEmpty,
      reason: 'unstubbed requests reached the stub: ${api.unmatched}',
    );
    await api.stop();
  });

  setUp(clearStoredState);

  testWidgets('E2E-AI-02 a guest reaches the shell without a single API call', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    try {
      await app.main();
      await pumpUntilFound(tester, find.bySemanticsIdentifier('login.guest'));

      await tester.tap(find.bySemanticsIdentifier('login.guest'));
      await pumpUntilGone(tester, find.byType(LoginScreen));
      await pumpUntilFound(tester, find.byType(NavigationBar));

      expect(find.byType(NavigationBar), findsOneWidget);
      // Guest mode is offline by construction: no access token, so `loadAll` returns before it builds a request.
      expect(api.callsTo('GET', '/api/v1/terrariums'), 0);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('E2E-AI-03 signing in reaches the dashboard', (tester) async {
    api.on('POST', '/api/v1/auth/login', (_) => loginPayload());
    final semantics = tester.ensureSemantics();
    try {
      await app.main();
      await pumpUntilFound(
        tester,
        find.bySemanticsIdentifier('login.identifier'),
      );

      await tester.enterText(
        find.bySemanticsIdentifier('login.identifier'),
        'keeper',
      );
      await tester.enterText(
        find.bySemanticsIdentifier('login.password'),
        'Demo!Reptile1',
      );
      await tester.tap(find.bySemanticsIdentifier('login.submit'));

      await pumpUntilGone(tester, find.byType(LoginScreen));
      await pumpUntilFound(tester, find.byType(DashboardScreen));

      expect(find.byType(NavigationBar), findsOneWidget);
      expect(api.callsTo('POST', '/api/v1/auth/login'), 1);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('E2E-AI-04 a refused credential says so and stays on the form', (
    tester,
  ) async {
    api.on(
      'POST',
      '/api/v1/auth/login',
      (_) => StubReply(401, invalidCredentialsBody()),
    );
    final semantics = tester.ensureSemantics();
    try {
      await app.main();
      await pumpUntilFound(
        tester,
        find.bySemanticsIdentifier('login.identifier'),
      );

      await tester.enterText(
        find.bySemanticsIdentifier('login.identifier'),
        'keeper',
      );
      await tester.enterText(
        find.bySemanticsIdentifier('login.password'),
        'wrong-password',
      );
      await tester.tap(find.bySemanticsIdentifier('login.submit'));

      await pumpUntilFound(tester, find.bySemanticsIdentifier('login.error'));

      expect(find.byType(LoginScreen), findsOneWidget);
      expect(find.byType(NavigationBar), findsNothing);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets(
    'E2E-AI-05 registration issues a recovery code and returns to sign-in',
    (tester) async {
      api.on('POST', '/api/v1/auth/register', (_) => registerPayload());
      final semantics = tester.ensureSemantics();
      try {
        await app.main();
        await pumpUntilFound(tester, find.bySemanticsIdentifier('login.guest'));

        await tester.tap(find.text('Tạo tài khoản'));
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('login.username'),
        );

        await tester.enterText(
          find.bySemanticsIdentifier('login.username'),
          'new.keeper',
        );
        await tester.enterText(
          find.bySemanticsIdentifier('login.email'),
          'new.keeper@example.test',
        );
        await tester.enterText(
          find.bySemanticsIdentifier('login.password'),
          'Demo!Reptile1',
        );
        await tester.tap(find.bySemanticsIdentifier('login.submit'));

        // Back on the sign-in form, with the code shown once.
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('login.identifier'),
        );
        expect(find.text('E2E0-0000-0000-0001'), findsOneWidget);
        expect(api.callsTo('POST', '/api/v1/auth/register'), 1);
        expect(tester.takeException(), isNull);
      } finally {
        semantics.dispose();
      }
    },
  );

  testWidgets('E2E-AI-06 an empty catalogue renders the shell, not an error', (
    tester,
  ) async {
    api.on('POST', '/api/v1/auth/login', (_) => loginPayload());
    final semantics = tester.ensureSemantics();
    try {
      await app.main();
      await pumpUntilFound(
        tester,
        find.bySemanticsIdentifier('login.identifier'),
      );

      await tester.enterText(
        find.bySemanticsIdentifier('login.identifier'),
        'keeper',
      );
      await tester.enterText(
        find.bySemanticsIdentifier('login.password'),
        'Demo!Reptile1',
      );
      await tester.tap(find.bySemanticsIdentifier('login.submit'));
      await pumpUntilFound(tester, find.byType(DashboardScreen));

      // Every one of the five reads was answered with an empty list, and none of them was an error.
      for (final path in shellReadPaths) {
        expect(
          api.callsTo('GET', path),
          greaterThanOrEqualTo(1),
          reason: '$path was never requested after sign-in',
        );
      }
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets(
    'E2E-AI-07 the language pill switches the whole form to English',
    (tester) async {
      final semantics = tester.ensureSemantics();
      try {
        await app.main();
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('login.lang.en'),
        );

        await tester.tap(find.bySemanticsIdentifier('login.lang.en'));
        await tester.pump(const Duration(milliseconds: 600));

        // The one case that asserts copy: the copy *is* the outcome here, and the deck (`app/lib/l10n/app_en.arb`)
        // is the contract.
        expect(find.text('Sign in'), findsWidgets);
        expect(find.text('Create an account'), findsOneWidget);
        expect(tester.takeException(), isNull);
      } finally {
        semantics.dispose();
      }
    },
  );

  testWidgets(
    'E2E-AI-08 signing out clears the keychain, not just the screen',
    (tester) async {
      api.on('POST', '/api/v1/auth/login', (_) => loginPayload());
      final semantics = tester.ensureSemantics();
      try {
        await app.main();
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('login.identifier'),
        );

        await tester.enterText(
          find.bySemanticsIdentifier('login.identifier'),
          'keeper',
        );
        await tester.enterText(
          find.bySemanticsIdentifier('login.password'),
          'Demo!Reptile1',
        );
        await tester.tap(find.bySemanticsIdentifier('login.submit'));
        await pumpUntilFound(tester, find.byType(DashboardScreen));

        expect(
          await storedRefreshToken(),
          isNotNull,
          reason: 'sign-in should have persisted the refresh token',
        );

        // An ordinary keeper is not an administrator, so the last destination is Profile.
        await tester.tap(find.byIcon(Icons.person_outline));
        await pumpUntilFound(
          tester,
          find.bySemanticsIdentifier('profile.signOut'),
        );

        await tester.tap(find.bySemanticsIdentifier('profile.signOut'));
        await pumpUntilFound(tester, find.byType(LoginScreen));

        expect(
          await storedRefreshToken(),
          isNull,
          reason: 'sign-out must remove the token from the keychain, not just leave the shell',
        );
        expect(tester.takeException(), isNull);
      } finally {
        semantics.dispose();
      }
    },
  );
}
