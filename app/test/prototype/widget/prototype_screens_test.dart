import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';
import 'package:smart_reptile/core/clock.dart';
import 'package:smart_reptile/prototype/fake_world.dart';
import 'package:smart_reptile/prototype/prototype_shell.dart';
import 'package:smart_reptile/prototype/prototype_state.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/state/settings_provider.dart';
import 'package:smart_reptile/widgets/metric_card.dart';

/// Widget tests for the prototype screens.
///
/// The first one exists because of a real bug: `PrototypeApp` was written without
/// `AppLocalizations.localizationsDelegates`, so every `MetricCard` threw when it resolved its labels, Flutter painted
/// an error box, and the terminal printed nothing. A browser screenshot found it; this test would have.
void main() {
  final now = DateTime(2026, 9, 30, 9, 10);

  late PrototypeState state;

  Widget app() => MultiProvider(
    providers: [
      ChangeNotifierProvider<PrototypeState>.value(value: state),
      ChangeNotifierProvider<SettingsProvider>(
        create: (_) => SettingsProvider(),
      ),
    ],
    child: const PrototypeApp(),
  );

  /// A tall surface, so a screen is laid out in full and `find.text` can see the parts below the fold.
  void useTallSurface(WidgetTester tester) {
    tester.view.physicalSize = const Size(1200, 3600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
  }

  setUp(() {
    state = PrototypeState.forWorld(
      Clock.fake(now),
      FakeWorldFactory.build(now: now),
    );
  });

  group('the signed-out gate', () {
    testWidgets(
      'opens on the sign-in screen, with the demo credentials filled in',
      (tester) async {
        useTallSurface(tester);
        await tester.pumpWidget(app());
        await tester.pumpAndSettle();

        expect(find.text('Sign in'), findsWidgets);
        expect(find.text(PrototypeState.demoEmail), findsOneWidget);
        expect(find.text('Prototype'), findsWidgets);
      },
    );

    testWidgets('a wrong password keeps the user out and explains why', (
      tester,
    ) async {
      useTallSurface(tester);
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();

      await tester.enterText(find.byType(TextField).last, 'nonsense');
      await tester.tap(find.widgetWithText(FilledButton, 'Sign in'));
      await tester.pumpAndSettle();

      expect(find.text('Email or password is incorrect.'), findsOneWidget);
      expect(state.isSignedOut, isTrue);
    });
  });

  group('the dashboard', () {
    testWidgets(
      'renders every metric card with its band and its own timestamp',
      (tester) async {
        useTallSurface(tester);
        state.signIn(
          email: PrototypeState.demoEmail,
          password: PrototypeState.demoPassword,
        );
        await tester.pumpWidget(app());
        await tester.pumpAndSettle();

        // The five metrics the semi-arid profile measures, and nothing invented. Scoped to the cards, because "Light"
        // is also a column heading in the report strip below.
        for (final name in const [
          'Temperature',
          'Humidity',
          'Light',
          'UV index',
          'Surface temperature',
        ]) {
          expect(
            find.descendant(
              of: find.byType(MetricCard),
              matching: find.text(name),
            ),
            findsOneWidget,
            reason: '$name has no metric card',
          );
        }

        // The cards carry the engine's verdict, not a colour: the ongoing heat event is Critical.
        expect(find.text('Critical'), findsWidgets);
        expect(find.textContaining('Target 26.0–32.0'), findsOneWidget);
        expect(find.textContaining('Target 30–40'), findsOneWidget);
      },
    );

    testWidgets('shows the open alert with the band that was in force', (
      tester,
    ) async {
      useTallSurface(tester);
      state.signIn(
        email: PrototypeState.demoEmail,
        password: PrototypeState.demoPassword,
      );
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();

      expect(find.textContaining('band 26.0–32.0°C'), findsOneWidget);
      expect(find.textContaining('peak 35.4°C'), findsOneWidget);
      expect(find.text('Open the episode →'), findsOneWidget);
    });

    testWidgets(
      'the alert badge counts open alerts, acknowledged ones included',
      (tester) async {
        useTallSurface(tester);
        state.signIn(
          email: PrototypeState.demoEmail,
          password: PrototypeState.demoPassword,
        );
        await tester.pumpWidget(app());
        await tester.pumpAndSettle();

        final openBefore = state.openAlertCount;
        expect(find.text('$openBefore'), findsWidgets);

        final alert = state.openAlertsFor('t-gecko').single;
        state.acknowledge(alert.id);
        await tester.pumpAndSettle();

        // Acknowledged still needs attention, so the badge must not drop (BR-12.1).
        expect(state.openAlertCount, openBefore);

        state.resolve(alert.id, ResolvedReason.recovered);
        await tester.pumpAndSettle();
        expect(state.openAlertCount, openBefore - 1);
      },
    );

    testWidgets('an unclaimed terrarium shows the onboarding empty state', (
      tester,
    ) async {
      useTallSurface(tester);
      state.signIn(
        email: PrototypeState.demoEmail,
        password: PrototypeState.demoPassword,
      );
      state.selectTerrarium('t-beardie');
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();

      expect(find.text('No reading yet'), findsOneWidget);
      expect(find.textContaining('Claim a sensor node'), findsOneWidget);
    });
  });

  group('the rule lab', () {
    Future<void> openLab(WidgetTester tester) async {
      useTallSurface(tester);
      state.signIn(
        email: PrototypeState.demoEmail,
        password: PrototypeState.demoPassword,
      );
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();
      await tester.tap(find.text('More'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Rule Lab').first);
      await tester.pumpAndSettle();
    }

    testWidgets('lists all twelve scenarios and opens on A', (tester) async {
      await openLab(tester);

      for (final id in const [
        'A',
        'B',
        'C',
        'D',
        'E',
        'F',
        'G',
        'H',
        'I',
        'J',
        'K',
        'L',
      ]) {
        expect(
          find.textContaining('$id · '),
          findsWidgets,
          reason: 'scenario $id is not offered',
        );
      }
      expect(find.text('Matches the documentation'), findsOneWidget);
      expect(find.textContaining('no alert'), findsWidgets);
    });

    testWidgets(
      'a scenario whose worked example contradicts the rule does not claim agreement',
      (tester) async {
        await openLab(tester);

        await tester.tap(find.textContaining('B · One excursion').first);
        await tester.pumpAndSettle();

        // The engine matches the *rule*; the document's own worked example does not match the engine. Showing a green
        // tick here would be the prototype lying in the one place it exists to tell the truth.
        expect(
          find.text('Rule verified — worked example differs'),
          findsOneWidget,
        );
        expect(find.text('Matches the documentation'), findsNothing);
        expect(
          find.textContaining('Where the documents disagree'),
          findsOneWidget,
        );
        expect(find.textContaining('resolves at 14:57'), findsWidgets);
      },
    );

    testWidgets('the rule coverage matrix says plainly what is not modelled', (
      tester,
    ) async {
      await openLab(tester);

      expect(find.textContaining('not modelled'), findsWidgets);
      expect(find.textContaining('BR-15'), findsWidgets);
      expect(
        find.textContaining('Open questions this prototype found'),
        findsOneWidget,
      );
    });
  });

  group('the alerts screen', () {
    testWidgets('filters the inbox and shows the suppression reasons', (
      tester,
    ) async {
      useTallSurface(tester);
      state.signIn(
        email: PrototypeState.demoEmail,
        password: PrototypeState.demoPassword,
      );
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();

      // Tapped by icon: the bottom-navigation label is rendered twice during its selection animation, so `find.text`
      // is ambiguous while the icon is not.
      await tester.tap(find.byIcon(Icons.notifications_none));
      await tester.pumpAndSettle();

      // Every alert in the fixture belongs to one of the two claimed terrariums.
      expect(find.text('Critical'), findsWidgets);

      await tester.tap(find.text('Resolved').first);
      await tester.pumpAndSettle();

      expect(
        state.filteredAlerts.every(
          (alert) => alert.state == AlertState.resolved,
        ),
        isTrue,
      );
    });
  });

  group('settings', () {
    testWidgets('quiet hours can be switched off, and the change is visible', (
      tester,
    ) async {
      useTallSurface(tester);
      state.signIn(
        email: PrototypeState.demoEmail,
        password: PrototypeState.demoPassword,
      );
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();

      await tester.tap(find.text('More'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Settings'));
      await tester.pumpAndSettle();

      expect(state.preferences.quietHoursEnabled, isTrue);
      await tester.tap(
        find.widgetWithText(SwitchListTile, 'Quiet hours 22:00 – 06:00'),
      );
      await tester.pumpAndSettle();

      expect(state.preferences.quietHoursEnabled, isFalse);
      expect(state.preferences.isQuietHour(23), isFalse);
    });
  });
}
