import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/mac_address.dart';

/// The MAC rules the claim-by-MAC form applies, mirroring the API's `MacAddress.Inspect` so the two cannot disagree
/// about what a board's address looks like (2026-10-23).
void main() {
  group('accepted spellings normalise to one form', () {
    const cases = {
      'AA:BB:CC:DD:EE:FF': 'AA:BB:CC:DD:EE:FF',
      'aa:bb:cc:dd:ee:ff': 'AA:BB:CC:DD:EE:FF',
      'AA-BB-CC-DD-EE-FF': 'AA:BB:CC:DD:EE:FF',
      'aabbccddeeff': 'AA:BB:CC:DD:EE:FF',
      'aabb.ccdd.eeff': 'AA:BB:CC:DD:EE:FF',
      '  AA:BB:CC:DD:EE:FF  ': 'AA:BB:CC:DD:EE:FF',
      'B0:CB:D8:CE:01:E0': 'B0:CB:D8:CE:01:E0',
    };

    cases.forEach((typed, expected) {
      test('"$typed"', () {
        final inspection = inspectMac(typed);
        expect(inspection.isUsable, isTrue);
        expect(inspection.normalised, expected);
        expect(macProblemMessage(inspection.problem), isNull);
      });
    });
  });

  group('an address no single board can own is refused with its reason', () {
    const cases = {
      'FF:FF:FF:FF:FF:FF': MacProblem.broadcast,
      'ff-ff-ff-ff-ff-ff': MacProblem.broadcast,
      '00:00:00:00:00:00': MacProblem.allZero,
      '01:00:5E:00:00:01': MacProblem.multicast,
      '33:33:00:00:00:01': MacProblem.multicast,
      'AA:BB:CC:DD:EE': MacProblem.malformed,
      'GG:BB:CC:DD:EE:FF': MacProblem.malformed,
      '': MacProblem.malformed,
    };

    cases.forEach((typed, expected) {
      test('"$typed"', () {
        final inspection = inspectMac(typed);
        expect(inspection.isUsable, isFalse);
        expect(inspection.problem, expected);
        expect(macProblemMessage(inspection.problem), isNotNull);
      });
    });
  });

  test('a null value is malformed rather than an exception', () {
    expect(inspectMac(null).problem, MacProblem.malformed);
  });
}
