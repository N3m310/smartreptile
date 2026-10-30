import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:smart_reptile/main.dart' as app;
import 'package:smart_reptile/screens/dashboard_screen.dart';
import 'package:smart_reptile/screens/login_screen.dart';
import 'package:smart_reptile/screens/terrarium_detail_screen.dart';

import 'helpers/live_api.dart';
import 'helpers/pump.dart';
import 'helpers/session.dart';

/// Suite `fullstack` â€” `docs/04-quality/05-e2e-ai-testing-plan.md` Â§3 and Â§6 (`E2E-AI-09â€¦12`).
///
/// The app against the **real** backend: `docker compose` (db + api on `:8080`), reached from the emulator as
/// `10.0.2.2:8080`. The fixture â€” one account and one enclosure â€” is created through the public API in `setUpAll`
/// and deleted in `tearDownAll`, so a run neither depends on nor pollutes the data a person is looking at.
///
/// Run it with `--dart-define=API_BASE=http://10.0.2.2:8080`; `scripts/e2e.ps1 -Suite fullstack` brings the stack up
/// first and waits for `/health/ready`.
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  final api = LiveApi();
  final stamp = DateTime.now().millisecondsSinceEpoch;
  final username = 'e2e.keeper.$stamp';
  const password = 'E2e!Reptile1';

  late LiveSession session;
  late String terrariumId;
  late String terrariumName;

  setUpAll(() async {
    session = await api.registerAndSignIn(username, password);
    final speciesProfileId = await api.firstSpeciesProfileId(
      session.accessToken,
    );
    terrariumName = 'E2E enclosure $stamp';
    terrariumId = await api.createTerrarium(
      session.accessToken,
      name: terrariumName,
      speciesProfileId: speciesProfileId,
    );
  });

  tearDownAll(() async {
    // Leaves the deployment as it found it: one account with no enclosures is inert, but an enclosure with
    // telemetry would show up on somebody's dashboard.
    final deleteStatus = await api.deleteTerrarium(
      session.accessToken,
      terrariumId,
    );
    expect(
      deleteStatus,
      204,
      reason: 'teardown could not delete the fixture enclosure',
    );
    final remaining = await api.terrariumNames(session.accessToken);
    expect(
      remaining,
      isEmpty,
      reason: 'the run left enclosures behind: $remaining',
    );
    api.close();
  });

  Future<void> signIn(WidgetTester tester) async {
    await app.main();
    await pumpUntilFound(
      tester,
      find.bySemanticsIdentifier('login.identifier'),
    );
    await tester.enterText(
      find.bySemanticsIdentifier('login.identifier'),
      session.username,
    );
    await tester.enterText(
      find.bySemanticsIdentifier('login.password'),
      session.password,
    );
    await tester.tap(find.bySemanticsIdentifier('login.submit'));
    await pumpUntilGone(tester, find.byType(LoginScreen));
    await pumpUntilFound(tester, find.byType(DashboardScreen));
  }

  testWidgets('E2E-AI-09 a real sign-in shows the account its own enclosure', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    try {
      await signIn(tester);

      // The Terrarium destination, addressed by its icon rather than its label: the label is copy.
      await tapDestination(tester, Icons.pets_outlined);
      await pumpUntilFound(tester, find.text(terrariumName));

      expect(find.text(terrariumName), findsOneWidget);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('E2E-AI-10 the enclosure detail opens on that enclosure', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    try {
      await signIn(tester);

      await tapDestination(tester, Icons.pets_outlined);
      await pumpUntilFound(tester, find.text(terrariumName));

      await tester.tap(find.text(terrariumName));
      await pumpUntilFound(tester, find.byType(TerrariumDetailScreen));

      expect(find.byType(TerrariumDetailScreen), findsOneWidget);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('E2E-AI-11 history opens for an enclosure with no readings', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    try {
      await signIn(tester);

      await tapDestination(tester, Icons.show_chart_outlined);
      // An enclosure with a board and no samples is the ordinary first-run state; it must render, not throw.
      await tester.pump(const Duration(seconds: 2));

      expect(tester.takeException(), isNull);
      expect(find.byType(LoginScreen), findsNothing);
    } finally {
      semantics.dispose();
    }
  });

  testWidgets('E2E-AI-12 signing out lands back on the sign-in form', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    try {
      await signIn(tester);

      await tapDestination(tester, Icons.person_outline);
      await pumpUntilFound(
        tester,
        find.bySemanticsIdentifier('profile.signOut'),
      );
      await tester.tap(find.bySemanticsIdentifier('profile.signOut'));

      await pumpUntilFound(tester, find.byType(LoginScreen));
      expect(await storedRefreshToken(), isNull);
      expect(tester.takeException(), isNull);
    } finally {
      semantics.dispose();
    }
  });

  setUp(clearStoredState);
}
