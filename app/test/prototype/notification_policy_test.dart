import 'package:flutter_test/flutter_test.dart';
import 'package:smart_reptile/prototype/rules/domain.dart';
import 'package:smart_reptile/prototype/rules/events.dart';
import 'package:smart_reptile/prototype/rules/notification_policy.dart';

/// The policy matrix promised by `02-design/05` §8: *"one test per cell of the matrix (12 cells)"*.
///
/// The cells exist so the report can show a real suppression table instead of hand-waving about alert fatigue. Each
/// test names the rule it pins.
void main() {
  const policy = NotificationPolicy();
  final night = DateTime(2026, 9, 21, 23, 30);
  final noon = DateTime(2026, 9, 21, 12);

  NotificationDecision decide({
    Severity severity = Severity.warning,
    DateTime? at,
    NotificationPreferences preferences = const NotificationPreferences(),
    bool isRecovery = false,
    bool silencedMetric = false,
    bool deviceInMaintenance = false,
    int sentInCurrentHour = 0,
  }) => policy.evaluate(
    severity: severity,
    localTime: at ?? noon,
    preferences: preferences,
    isRecovery: isRecovery,
    silencedMetric: silencedMetric,
    deviceInMaintenance: deviceInMaintenance,
    sentInCurrentHour: sentInCurrentHour,
  );

  group('BR-13.1 channels', () {
    test('a Warning at noon is delivered to every enabled channel', () {
      final decision = decide();

      expect(decision.send, isTrue);
      expect(decision.reason, SuppressedReason.none);
      expect(decision.channels, contains(Channel.inApp));
      expect(decision.channels, contains(Channel.telegram));
      expect(decision.ruleId, 'BR-13.1');
    });

    test('no enabled channel suppresses with noChannel', () {
      final decision = decide(
        preferences: const NotificationPreferences(channels: {}),
      );

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.noChannel);
    });
  });

  group('BR-13.2 preferences, quiet hours and the Critical bypass', () {
    test('a Warning below the user minimum is suppressed', () {
      final decision = decide(
        preferences: const NotificationPreferences(
          minSeverity: Severity.critical,
        ),
      );

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.belowMinSeverity);
    });

    test('a Critical at 23:30 bypasses quiet hours and says so', () {
      final decision = decide(severity: Severity.critical, at: night);

      expect(decision.send, isTrue);
      expect(decision.bypassedQuietHours, isTrue);
      expect(decision.detail, contains('bypassed_quiet_hours'));
    });

    test('a warning at 23:30 is suppressed by quiet hours', () {
      final decision = decide(at: night);

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.quietHours);
    });

    test(
      'a recovery notice never bypasses quiet hours, even after an escalation',
      () {
        // A resolved alert keeps its escalated Critical severity, so evaluating the bypass on severity alone let
        // "back in range" wake the keeper at 03:00. This is the regression test for that.
        final decision = decide(
          severity: Severity.critical,
          at: night,
          isRecovery: true,
        );

        expect(decision.send, isFalse);
        expect(decision.reason, SuppressedReason.quietHours);
        expect(decision.bypassedQuietHours, isFalse);
      },
    );

    test('quiet hours wrapping midnight do not suppress a 05:00 Warning by accident', () {
      final decision = decide(at: DateTime(2026, 9, 21, 5, 30));
      expect(decision.reason, SuppressedReason.quietHours);

      final outside = decide(at: DateTime(2026, 9, 21, 6, 30));
      expect(outside.send, isTrue);
    });

    test('a recovery notice is suppressed when the user switched them off', () {
      final decision = decide(
        isRecovery: true,
        preferences: const NotificationPreferences(recoveryNotices: false),
      );

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.preference);
    });

    test('recovery preference is checked before quiet hours, and Critical does not excuse it', () {
      final decision = decide(
        severity: Severity.critical,
        at: night,
        isRecovery: true,
        preferences: const NotificationPreferences(recoveryNotices: false),
      );

      expect(decision.reason, SuppressedReason.preference);
    });
  });

  group('BR-12.6 silence and maintenance', () {
    test('a silenced metric is suppressed even for a Critical', () {
      final decision = decide(
        severity: Severity.critical,
        silencedMetric: true,
      );

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.silencedMetric);
      expect(decision.ruleId, 'BR-12.6');
    });

    test('maintenance mode suppresses a Critical', () {
      final decision = decide(
        severity: Severity.critical,
        deviceInMaintenance: true,
      );

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.maintenance);
    });
  });

  group('BR-13.3 hourly cap', () {
    test('the eleventh notification in an hour is coalesced into a digest', () {
      final decision = decide(sentInCurrentHour: 10);

      expect(decision.send, isFalse);
      expect(decision.reason, SuppressedReason.digestCoalesced);
      expect(decision.ruleId, 'BR-13.3');
      expect(decision.detail, contains('inbox still shows it'));
    });

    test('the tenth notification still goes out', () {
      expect(decide(sentInCurrentHour: 9).send, isTrue);
    });
  });

  group('§5 repeat cooldown', () {
    test('a still-open alert does not re-notify within the cooldown', () {
      final last = DateTime(2026, 9, 21, 14, 0);

      expect(
        policy.mayRepeat(last.add(const Duration(minutes: 20)), last),
        isFalse,
      );
      expect(
        policy.mayRepeat(last.add(const Duration(minutes: 60)), last),
        isTrue,
      );
      expect(policy.mayRepeat(last, null), isTrue);
    });
  });

  group('§7 notification content contract', () {
    test('the title carries the terrarium, metric and direction', () {
      expect(
        NotificationPolicy.title(
          terrariumName: "Linh's gecko box",
          metricName: 'Temperature',
          isHot: true,
          isCritical: false,
        ),
        "Linh's gecko box · Temperature high (Warning)",
      );
    });

    test('the body always carries the band and the duration out of range', () {
      final body = NotificationPolicy.body(
        value: 34.2,
        unit: '°C',
        targetMin: 26,
        targetMax: 32,
        minutesOutOfRange: 9,
        isRecovery: false,
      );

      expect(body, contains('band 26–32°C'));
      expect(body, contains('out of range 9 min'));
      // The words the contract forbids.
      expect(body.toLowerCase(), isNot(contains('dwell')));
      expect(body.toLowerCase(), isNot(contains('hysteresis')));
    });

    test('a critical body states the critical bound that was exceeded', () {
      final body = NotificationPolicy.body(
        value: 35,
        unit: '°C',
        targetMin: 26,
        targetMax: 32,
        minutesOutOfRange: 6,
        isRecovery: false,
        criticalMax: 34.5,
      );

      expect(body, contains('Critical band exceeded: > 34.5°C'));
    });

    test('a recovery body reads as good news, with the band', () {
      final body = NotificationPolicy.body(
        value: 31.2,
        unit: '°C',
        targetMin: 26,
        targetMax: 32,
        minutesOutOfRange: 35,
        isRecovery: true,
      );

      expect(body, startsWith('Back in range'));
      expect(body, contains('after 35 min'));
    });
  });
}
