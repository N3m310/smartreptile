import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/e2e_hud.dart';

/// Pumps real frames until [finder] matches, instead of blocking on a fixed delay.
///
/// `pumpAndSettle` cannot be used for this: `main()` fires `unawaited(session.restore())` and
/// `unawaited(settings.load())`, and a live (on-device) test has no fake clock to settle them — it would
/// either return on the first frame or never settle at all.
///
/// The wait is also what the on-device banner reports, so the emulator says what the case is waiting for instead of
/// showing a screen that sits still.
Future<void> pumpUntilFound(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 30),
  Duration step = const Duration(milliseconds: 100),
}) async {
  E2eHud.step('waiting for ${finder.describeMatch(Plurality.one)}');
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    await tester.pump(step);
    if (finder.evaluate().isNotEmpty) return;
  }
  fail('Timed out after $timeout waiting for $finder');
}

/// Pumps frames until [finder] matches nothing, for the transitions the shell animates away.
Future<void> pumpUntilGone(
  WidgetTester tester,
  Finder finder, {
  Duration timeout = const Duration(seconds: 30),
  Duration step = const Duration(milliseconds: 100),
}) async {
  E2eHud.step('waiting for ${finder.describeMatch(Plurality.one)} to go away');
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    await tester.pump(step);
    if (finder.evaluate().isEmpty) return;
  }
  fail('Timed out after $timeout waiting for $finder to disappear');
}

/// Names the next action on the banner, for the phases a finder cannot describe ("signing in", "signing out").
///
/// The pump helpers above report themselves; this is for the gaps between them.
void hudStep(String text) => E2eHud.step(text);

/// Taps a shell destination by its icon, scoped to the `NavigationBar`.
///
/// Scoping matters: an empty state on the open screen draws the same `IconData` as the destination that leads to it
/// (`Icons.show_chart_outlined` at 48 px, for instance), and `find.byIcon` then matches two widgets and fails. The
/// destination is addressed inside the bar, which is also what a keeper taps.
Future<void> tapDestination(WidgetTester tester, IconData icon) async {
  final destination = find.descendant(
    of: find.byType(NavigationBar),
    matching: find.byIcon(icon),
  );
  expect(destination, findsWidgets, reason: 'no destination $icon in the bar');
  E2eHud.step('tapping a navigation destination');
  await tester.tap(destination.first);
  await tester.pump(const Duration(milliseconds: 600));
}
