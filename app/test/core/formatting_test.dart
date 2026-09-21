import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/core/formatting.dart';
import 'package:smart_reptile/core/status.dart';

/// Formatting and status-parsing rules. Cheap tests that protect the numbers the keeper reads.
void main() {
  group('MetricFormat.value', () {
    test('uses the precision of the metric', () {
      // Cards show temperature with one decimal, humidity and lux as integers, UVI with one.
      expect(MetricFormat.value(28.64, 'tempC'), '28.6');
      expect(MetricFormat.value(28.66, 'tempC'), '28.7');
      expect(MetricFormat.value(41.2, 'humidityPct'), '41');
      expect(MetricFormat.value(0.33, 'uvIndex'), '0.3');
    });

    test('groups thousands for illuminance', () {
      expect(MetricFormat.value(1820.4, 'lightLux'), '1 820');
      expect(MetricFormat.value(45000, 'lightLux'), '45 000');
      expect(MetricFormat.value(320, 'lightLux'), '320');
    });
  });

  group('MetricFormat.relativeAge', () {
    test('switches unit at 60 seconds and 60 minutes', () {
      expect(MetricFormat.relativeAge(const Duration(seconds: 12)), '12 s');
      expect(MetricFormat.relativeAge(const Duration(seconds: 59)), '59 s');
      expect(MetricFormat.relativeAge(const Duration(minutes: 12)), '12 min');
      expect(MetricFormat.relativeAge(const Duration(minutes: 59)), '59 min');
      expect(MetricFormat.relativeAge(const Duration(hours: 3)), '3 h');
    });

    test('reports the bucket and count for the plural messages', () {
      expect(
        MetricFormat.ageBucket(const Duration(seconds: 30)),
        AgeBucket.seconds,
      );
      expect(
        MetricFormat.ageBucket(const Duration(minutes: 5)),
        AgeBucket.minutes,
      );
      expect(MetricFormat.ageBucket(const Duration(hours: 5)), AgeBucket.hours);
      expect(MetricFormat.ageCount(const Duration(minutes: 5)), 5);
    });
  });

  group('MetricStatus', () {
    test('parses API values and degrades unknown strings to noData', () {
      expect(MetricStatus.parse('InRange'), MetricStatus.inRange);
      expect(MetricStatus.parse('Critical'), MetricStatus.critical);
      expect(MetricStatus.parse('something_new'), MetricStatus.noData);
      expect(MetricStatus.parse(null), MetricStatus.noData);
    });

    test('distinguishes "no data" from "sensor unavailable"', () {
      // The two need different user actions (wait vs replace a part), so they must never collapse.
      expect(MetricStatus.noData, isNot(MetricStatus.unavailable));
      expect(MetricStatus.noData.hasValue, isFalse);
      expect(MetricStatus.unavailable.hasValue, isFalse);
      expect(MetricStatus.critical.hasValue, isTrue);
      expect(MetricStatus.critical.isProblem, isTrue);
      expect(MetricStatus.inRange.isProblem, isFalse);
    });
  });

  group('Freshness', () {
    test('treats three missed intervals as the staleness threshold', () {
      expect(
        Freshness.fromAge(const Duration(seconds: 180), 60).isStale,
        isFalse,
      );
      expect(
        Freshness.fromAge(const Duration(seconds: 181), 60).isStale,
        isTrue,
      );
      // A 30 s interval device has a tighter threshold.
      expect(
        Freshness.fromAge(const Duration(seconds: 95), 30).isStale,
        isTrue,
      );
    });
  });
}
