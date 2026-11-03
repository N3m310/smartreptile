import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/e2e_hud.dart';

/// Runs one E2E case with the on-device banner (`app/lib/core/e2e_hud.dart`) naming the suite and the case.
///
/// This replaces `testWidgets` in the four suites. A plain `testWidgets` leaves the emulator with no indication of
/// what is executing — a screen that moves on its own — which is what this wrapper fixes: the banner shows
/// `E2E RUNNING · <suite>`, the case name, an elapsed clock, and the step that `pump.dart` publishes as the case
/// taps and waits. When the case ends, the banner turns green or red.
///
/// The banner is a courtesy, so its own failures are swallowed: nothing here may turn a green case red or hide the
/// real failure of a red one.
void e2eCase(
  String suite,
  String name,
  Future<void> Function(WidgetTester tester) body,
) {
  testWidgets(name, (tester) async {
    E2eHud.begin(suite: suite, testCase: name);
    var passed = false;
    try {
      await body(tester);
      passed = true;
    } finally {
      E2eHud.finish(passed: passed);
      try {
        await tester.pump(const Duration(milliseconds: 32));
      } on Object {
        // Nothing to do: the verdict above is already the test's own.
      }
    }
  });
}
