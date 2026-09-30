import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/fake_world.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/events.dart';

/// The fake world is a fixture with a job: it must exercise the interesting rules on screen.
///
/// These tests exist because a fixture that quietly stops demonstrating something is worse than a failing test — the
/// demo goes ahead, the screen looks fine, and the rule it was supposed to show is never actually exercised. Every
/// assertion here is a promise the demo makes.
void main() {
  // 20:41 on a fixed date, because the injected events are absolute-time and the assertions below have to hold
  // whatever the wall clock happens to be when CI runs.
  final now = DateTime(2026, 9, 30, 20, 41);
  late FakeWorld world;
  late Stopwatch stopwatch;

  setUpAll(() {
    stopwatch = Stopwatch()..start();
    world = FakeWorldFactory.build(now: now);
    stopwatch.stop();
  });

  group('the world builds at a sane cost', () {
    test('generating three days of history and evaluating it stays interactive', () {
      // A guard against an accidental O(minutes × samples) regression, not a benchmark: the first version of the
      // summary builder took tens of seconds on exactly this input.
      expect(
        stopwatch.elapsedMilliseconds,
        lessThan(15000),
        reason:
            'the fake world took ${stopwatch.elapsedMilliseconds} ms to build',
      );
    });

    test('four terrariums-wide fixtures would have been worse, so the history is three days', () {
      expect(FakeWorldFactory.historyDays, 3);
      expect(world.historyInterval, const Duration(seconds: 60));
    });
  });

  group('t-gecko — the terrarium that demonstrates everything', () {
    test('has one open alert, and it is Critical on air temperature', () {
      final open = world.openAlertsFor('t-gecko');

      expect(open, hasLength(1));
      expect(open.single.severity, Severity.critical);
      expect(open.single.metric, 'tempC');
      expect(open.single.source, AlertSource.threshold);
    });

    test('the open alert is genuine, not a two-minute blip', () {
      final alert = world.openAlertsFor('t-gecko').single;
      final lasted = world.anchor.difference(alert.triggeredAt);

      // Comfortably past the 5-minute dwell, so the dashboard shows a real duration rather than "0 min".
      expect(lasted.inMinutes, greaterThan(10));
      expect(alert.peakValue, greaterThan(34.5));
    });

    test('its history has the resolved incidents the Alerts screen needs', () {
      final resolved = world
          .alertsFor('t-gecko')
          .where((alert) => alert.state == AlertState.resolved)
          .toList();

      expect(resolved.length, greaterThanOrEqualTo(2));
      expect(
        resolved.every(
          (alert) => alert.resolvedReason == ResolvedReason.recovered,
        ),
        isTrue,
      );
      // Both severities are represented, so the inbox is not a wall of one colour.
      expect(resolved.any((a) => a.severity == Severity.warning), isTrue);
      expect(resolved.any((a) => a.severity == Severity.critical), isTrue);
    });

    test('every metric has a fresh reading, so nothing on the dashboard is dimmed', () {
      final latest = world.latest['t-gecko']!;

      expect(
        latest.keys,
        containsAll(['tempC', 'humidityPct', 'lightLux', 'uvIndex']),
      );
      for (final entry in latest.entries) {
        final age = world.anchor.difference(entry.value.recordedAt);
        expect(
          age.inSeconds,
          lessThan(60),
          reason:
              '${entry.key} is ${age.inSeconds}s old and would render as stale',
        );
      }
    });

    test('three days of 1-minute samples for five metrics', () {
      final samples = world.history['t-gecko']!;
      final metrics = samples.map((s) => s.metric).toSet();

      expect(
        metrics.length,
        5,
        reason: 'the semi-arid profile fits a surface probe',
      );
      // The last day is partial — the fixture runs to "now", not to midnight — so the count is derived rather than
      // hard-coded. Assuming three whole days is what a future 04:00 test run would break.
      final minutes = world.anchor.difference(world.historyFrom).inMinutes + 1;
      expect(samples.length, minutes * metrics.length);
      expect(
        samples.first.recordedAt.isBefore(samples.last.recordedAt),
        isTrue,
      );
    });
  });

  group('t-quarantine — the silent device, which must not look like safety', () {
    test(
      'raises DeviceSilent rather than a habitat alert, and it is Critical',
      () {
        // Its historic excursions have resolved; the only thing still open is the silence itself.
        final open = world.openAlertsFor('t-quarantine');

        expect(open, hasLength(1));
        expect(open.single.source, AlertSource.deviceSilent);
        expect(open.single.metric, isNull);
        expect(open.single.severity, Severity.critical);
      },
    );

    test(
      'its data stops 42 minutes before now, which is what makes it stale',
      () {
        final latest = world.latest['t-quarantine']!;
        final age = world.anchor.difference(latest['tempC']!.recordedAt);

        expect(age.inMinutes, 42);
        final device = world.deviceFor('t-quarantine')!;
        expect(device.status, DeviceStatus.offline);
        expect(
          device.lastSeenAt,
          world.anchor.subtract(const Duration(minutes: 42)),
        );
      },
    );

    test(
      'an ordinary day in the tub is genuinely in range, not quietly marginal',
      () {
        // Two days ago has a humidity event and a dead lamp, but the temperature band must be clean — otherwise the
        // fixture would show a warning-coloured dashboard for a terrarium that is supposed to be the quiet one.
        final summary = world.summaries['t-quarantine']!.first;

        expect(summary.perMetric['tempC']!.outOfRangeMinutes, 0);
        expect(
          summary.perMetric['lightLux']!.hoursAboveLightThreshold,
          lessThan(1),
        );
        expect(summary.lightDeficitHours, greaterThan(5));
        expect(summary.hasLightDeficit, isTrue);
      },
    );
  });

  group('t-beardie — the empty state, by design', () {
    test('has no device, no history and no alerts', () {
      expect(world.deviceFor('t-beardie'), isNull);
      expect(world.history.containsKey('t-beardie'), isFalse);
      expect(world.alertsFor('t-beardie'), isEmpty);
    });

    test('an unclaimed node exists in the fleet, showing a pairing code', () {
      final unclaimed = world.devices
          .where((device) => !device.isClaimed)
          .toList();

      expect(unclaimed, hasLength(1));
      expect(unclaimed.single.status, DeviceStatus.provisioning);
      expect(unclaimed.single.claimCode, 'K7M2QP4T');
      expect(
        unclaimed.single.claimCodeExpiresAt!.isAfter(world.anchor),
        isTrue,
        reason: 'a code shown as expired would be a bug in the fixture, not in the app',
      );
    });
  });

  group('the fixture does not contradict itself', () {
    test('every alert belongs to a terrarium that exists and has a device', () {
      for (final alert in world.alerts) {
        expect(world.terrariumById(alert.terrariumId), isNotNull);
        expect(world.deviceFor(alert.terrariumId), isNotNull);
      }
    });

    test(
      'the notification log has both deliveries and reasoned suppressions',
      () {
        expect(world.notifications.any((n) => n.isSent), isTrue);
        expect(
          world.notifications.any((n) => n.outcome.isSent == false),
          isTrue,
          reason: 'the suppression table in the report needs rows to count',
        );
        // Every suppressed notification must say why: an unexplained silence is the thing this system refuses to do.
        for (final record in world.notifications.where((n) => !n.isSent)) {
          expect(record.outcome, isNot(SuppressedReason.none));
        }
      },
    );

    test('alert ids are unique across terrariums', () {
      final ids = world.alerts.map((alert) => alert.id).toSet();
      expect(ids.length, world.alerts.length);
    });

    test('every summarised day carries coverage, because a summary without it is a lie', () {
      for (final entry in world.summaries.entries) {
        expect(entry.value, isNotEmpty);
        for (final summary in entry.value) {
          expect(summary.expectedSamples, greaterThan(0));
          expect(summary.coveragePct, greaterThan(0));
          expect(summary.coveragePct, lessThanOrEqualTo(100));
          expect(summary.timeZoneLabel, 'Asia/Ho_Chi_Minh');
        }
      }
    });
  });
}
