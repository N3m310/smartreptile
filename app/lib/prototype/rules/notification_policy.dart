/// The notification decision flow of `02-design/05` §4, as a pure function.
///
/// Why it is worth its own file: this is where an alert stops being a database row and becomes something that
/// interrupts a human. The suppression reasons are the evidence the report needs to claim the system is not
/// spammy, and the order of the checks is the whole rule.
library;

import 'domain.dart';
import 'events.dart';

/// Per-user notification preferences (BR-13.2).
class NotificationPreferences {
  /// Creates preferences.
  const NotificationPreferences({
    this.channels = const {Channel.inApp, Channel.telegram},
    this.minSeverity = Severity.warning,
    this.quietHoursEnabled = true,
    this.quietHoursStartHour = 22,
    this.quietHoursEndHour = 6,
    this.recoveryNotices = true,
  });

  /// Channels this user enabled (BR-13.1). The in-app inbox is always on (`02-design/05` §3).
  final Set<Channel> channels;

  /// Lowest severity that may be pushed.
  final Severity minSeverity;

  /// Whether quiet hours are active.
  final bool quietHoursEnabled;

  /// Local hour quiet hours begin.
  final int quietHoursStartHour;

  /// Local hour quiet hours end.
  final int quietHoursEndHour;

  /// Whether the user wants "recovered" notices.
  final bool recoveryNotices;

  /// Returns a copy with the given fields replaced.
  NotificationPreferences copyWith({
    Set<Channel>? channels,
    Severity? minSeverity,
    bool? quietHoursEnabled,
    int? quietHoursStartHour,
    int? quietHoursEndHour,
    bool? recoveryNotices,
  }) => NotificationPreferences(
    channels: channels ?? this.channels,
    minSeverity: minSeverity ?? this.minSeverity,
    quietHoursEnabled: quietHoursEnabled ?? this.quietHoursEnabled,
    quietHoursStartHour: quietHoursStartHour ?? this.quietHoursStartHour,
    quietHoursEndHour: quietHoursEndHour ?? this.quietHoursEndHour,
    recoveryNotices: recoveryNotices ?? this.recoveryNotices,
  );

  /// True when the local hour falls inside quiet hours, including the wrap past midnight (22:00–06:00).
  bool isQuietHour(int localHour) {
    if (!quietHoursEnabled) {
      return false;
    }
    if (quietHoursStartHour == quietHoursEndHour) {
      return false;
    }
    return quietHoursStartHour < quietHoursEndHour
        ? localHour >= quietHoursStartHour && localHour < quietHoursEndHour
        : localHour >= quietHoursStartHour || localHour < quietHoursEndHour;
  }
}

/// The outcome of the notification policy for one event.
class NotificationDecision {
  /// Creates a decision.
  const NotificationDecision({
    required this.send,
    required this.reason,
    required this.ruleId,
    required this.channels,
    required this.detail,
    this.bypassedQuietHours = false,
  });

  /// True when at least one channel will receive the notification.
  final bool send;

  /// Why, when nothing was sent; `none` when it was.
  final SuppressedReason reason;

  /// The rule that decided it, e.g. `BR-13.2`.
  final String ruleId;

  /// Channels to deliver to.
  final Set<Channel> channels;

  /// One sentence explaining the decision (`quiet hours 22:00–06:00`), shown in the Rule Lab.
  final String detail;

  /// True when a Critical notification was deliberately allowed through quiet hours (BR-13.2).
  final bool bypassedQuietHours;

  /// A decision that suppresses everything, used when there is nothing to send.
  static const nothing = NotificationDecision(
    send: false,
    reason: SuppressedReason.preference,
    ruleId: 'BR-13.1',
    channels: {},
    detail: 'No event to notify about.',
  );
}

/// Evaluates the §4 decision flow in the order the document specifies.
class NotificationPolicy {
  /// Creates a policy.
  const NotificationPolicy({
    this.rateCapPerHour = 10,
    this.cooldownMinutes = 60,
  });

  /// Notifications allowed per terrarium per hour (BR-13.3).
  final int rateCapPerHour;

  /// Minimum minutes between repeats for a still-open alert (`02-design/05` §5).
  final int cooldownMinutes;

  /// Evaluates one notification event.
  ///
  /// The order matters and follows the flowchart: recovery preference → silence → maintenance → minimum severity
  /// → quiet hours (Critical bypasses) → hourly cap → send.
  NotificationDecision evaluate({
    required Severity severity,
    required DateTime localTime,
    required NotificationPreferences preferences,
    bool isRecovery = false,
    bool silencedMetric = false,
    bool deviceInMaintenance = false,
    int sentInCurrentHour = 0,
  }) {
    // 1. A recovery notice is a preference, never a duty.
    if (isRecovery && !preferences.recoveryNotices) {
      return const NotificationDecision(
        send: false,
        reason: SuppressedReason.preference,
        ruleId: 'BR-13.2',
        channels: {},
        detail: 'Recovery notices are switched off for this user.',
      );
    }

    // 2. A deliberate, accountable silence window (BR-12.6).
    if (silencedMetric) {
      return const NotificationDecision(
        send: false,
        reason: SuppressedReason.silencedMetric,
        ruleId: 'BR-12.6',
        channels: {},
        detail: 'The metric is inside an active silence window. The alert is still recorded.',
      );
    }

    // 3. Maintenance mode notifies nobody but records everything (`02-design/05` §5).
    if (deviceInMaintenance) {
      return const NotificationDecision(
        send: false,
        reason: SuppressedReason.maintenance,
        ruleId: 'BR-12.6',
        channels: {},
        detail: 'The device is in maintenance mode: cleaning or a lamp change is expected.',
      );
    }

    // 4. The user's own floor (BR-13.2).
    if (severity.rank < preferences.minSeverity.rank) {
      return NotificationDecision(
        send: false,
        reason: SuppressedReason.belowMinSeverity,
        ruleId: 'BR-13.2',
        channels: const {},
        detail:
            '${severity.label} is below this user\'s minimum of ${preferences.minSeverity.label}.',
      );
    }

    // 5. Quiet hours — Critical always gets through, and the bypass is logged.
    final quiet = preferences.isQuietHour(localTime.hour);
    var bypassed = false;
    if (quiet) {
      // A Critical bypasses quiet hours because somebody has to act now. A **recovery** notice is the opposite of
      // that — and because a resolved alert keeps its escalated severity, the naive check let a "back in range"
      // message at 03:00 wake the keeper up. The event, not the alert's history, decides whether it is urgent.
      if (severity == Severity.critical && !isRecovery) {
        bypassed = true;
      } else {
        return NotificationDecision(
          send: false,
          reason: SuppressedReason.quietHours,
          ruleId: 'BR-13.2',
          channels: const {},
          detail:
              'Quiet hours ${preferences.quietHoursStartHour.toString().padLeft(2, '0')}:00–'
              '${preferences.quietHoursEndHour.toString().padLeft(2, '0')}:00 suppress a Warning. '
              'A Critical still gets through.',
        );
      }
    }

    // 6. The hourly cap, which turns a burst into one digest instead of eleven interruptions (BR-13.3).
    if (sentInCurrentHour >= rateCapPerHour) {
      return NotificationDecision(
        send: false,
        reason: SuppressedReason.digestCoalesced,
        ruleId: 'BR-13.3',
        channels: const {},
        detail:
            'The hourly cap of $rateCapPerHour is reached; this event joins the digest. '
            'The inbox still shows it.',
      );
    }

    // 7. Send.
    if (preferences.channels.isEmpty) {
      return const NotificationDecision(
        send: false,
        reason: SuppressedReason.noChannel,
        ruleId: 'BR-13.1',
        channels: {},
        detail: 'No channel is enabled for this user.',
      );
    }

    return NotificationDecision(
      send: true,
      reason: SuppressedReason.none,
      ruleId: 'BR-13.1',
      channels: preferences.channels,
      detail: bypassed
          ? 'Critical bypasses quiet hours and is delivered anyway (logged as bypassed_quiet_hours).'
          : 'Delivered to ${preferences.channels.map((c) => c.label).join(', ')}.',
      bypassedQuietHours: bypassed,
    );
  }

  /// True when a still-open alert should re-notify given the last notification time (§5 repeat cooldown).
  bool mayRepeat(DateTime now, DateTime? lastNotificationAt) {
    if (lastNotificationAt == null) {
      return true;
    }
    return now.difference(lastNotificationAt).inMinutes >= cooldownMinutes;
  }

  /// The notification content contract of `02-design/05` §7.
  ///
  /// Rules enforced here: always include the band, always include how long it has been out, never use the words
  /// "dwell" or "hysteresis".
  static String title({
    required String terrariumName,
    required String metricName,
    required bool isHot,
    required bool isCritical,
  }) {
    final direction = isHot ? 'high' : 'low';
    final prefix = isCritical ? 'Critical' : 'Warning';
    return '$terrariumName · $metricName $direction ($prefix)';
  }

  /// The body half of the content contract.
  static String body({
    required double value,
    required String unit,
    required double targetMin,
    required double targetMax,
    required int minutesOutOfRange,
    required bool isRecovery,
    double? criticalMax,
    double? criticalMin,
  }) {
    if (isRecovery) {
      return 'Back in range at $value$unit (band '
          '${_trim(targetMin)}–${_trim(targetMax)}$unit) after $minutesOutOfRange min.';
    }

    final buffer = StringBuffer()
      ..write('$value$unit (band ${_trim(targetMin)}–${_trim(targetMax)}$unit)')
      ..write(', out of range $minutesOutOfRange min');
    if (criticalMax != null && value > criticalMax) {
      buffer.write('\nCritical band exceeded: > ${_trim(criticalMax)}$unit');
    } else if (criticalMin != null && value < criticalMin) {
      buffer.write('\nCritical band exceeded: < ${_trim(criticalMin)}$unit');
    }
    return buffer.toString();
  }

  static String _trim(double value) => value == value.roundToDouble()
      ? value.toStringAsFixed(0)
      : value.toStringAsFixed(1);
}
