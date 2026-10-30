import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:smart_reptile/main.dart' as app;
import 'package:smart_reptile/screens/login_screen.dart';

import 'helpers/pump.dart';

/// Suite `smoke` (docs/04-quality/05-e2e-ai-testing-plan.md §3, §6): the app boots, a keeper can enter the
/// demo shell without a server, and every destination opens without throwing.
///
/// No backend, no `--dart-define`, no Firebase: `main()` skips FCM when the four defines are absent, and guest
/// mode never reaches the API (`AppDataProvider.loadAll` returns early without an access token).
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('a guest reaches the shell and opens every destination', (
    tester,
  ) async {
    // Identifiers live in the semantics tree, which a test only builds on request. The handle must be disposed
    // inside the test body: the binding verifies that before `addTearDown` callbacks run.
    final semantics = tester.ensureSemantics();
    try {
      await app.main();

      await pumpUntilFound(tester, find.bySemanticsIdentifier('login.guest'));
      expect(find.byType(LoginScreen), findsOneWidget);

      await tester.tap(find.bySemanticsIdentifier('login.guest'));
      await pumpUntilGone(tester, find.byType(LoginScreen));
      await pumpUntilFound(tester, find.byType(NavigationBar));

      final destinations = find.byType(NavigationDestination);
      final count = destinations.evaluate().length;
      expect(
        count,
        greaterThanOrEqualTo(5),
        reason:
            'the signed-out guest shell should still show the navigation bar',
      );

      for (var index = 0; index < count; index++) {
        await tester.tap(destinations.at(index));
        await tester.pump(const Duration(milliseconds: 600));
        expect(
          tester.takeException(),
          isNull,
          reason: 'destination $index threw while opening',
        );
      }
    } finally {
      semantics.dispose();
    }
  });
}
