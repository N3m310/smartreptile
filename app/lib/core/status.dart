import 'package:flutter/material.dart';

/// The single vocabulary shared by the API, the app and the dashboard (`02-design/04` §5).
///
/// Rule: the status-to-colour/icon mapping exists exactly once, here. A second place that decides red/green is
/// a bug waiting to happen, and colour alone is never the signal (NFR-06 accessibility).
enum MetricStatus {
  inRange,
  outOfRange,
  critical,
  noData,
  unavailable,
  maintenance;

  /// Parses the API value; anything unknown degrades to [MetricStatus.noData] rather than crashing.
  static MetricStatus parse(String? value) {
    switch (value) {
      case 'InRange':
        return MetricStatus.inRange;
      case 'OutOfRange':
        return MetricStatus.outOfRange;
      case 'Critical':
        return MetricStatus.critical;
      case 'Unavailable':
        return MetricStatus.unavailable;
      case 'Maintenance':
        return MetricStatus.maintenance;
      case 'NoData':
      default:
        return MetricStatus.noData;
    }
  }

  /// True when the keeper should look at this value now.
  bool get isProblem =>
      this == MetricStatus.outOfRange || this == MetricStatus.critical;

  /// True when we genuinely have a current value to show.
  bool get hasValue =>
      this == MetricStatus.inRange ||
      this == MetricStatus.outOfRange ||
      this == MetricStatus.critical;

  Color color(ThemeData theme) {
    switch (this) {
      case MetricStatus.inRange:
        return const Color(0xFF2E7D32); // token color.inRange
      case MetricStatus.outOfRange:
        return const Color(0xFFED6C02); // token color.warning
      case MetricStatus.critical:
        return const Color(0xFFC62828); // token color.critical
      case MetricStatus.noData:
      case MetricStatus.unavailable:
      case MetricStatus.maintenance:
        return const Color(0xFF616161); // token color.unknown
    }
  }

  IconData get icon {
    switch (this) {
      case MetricStatus.inRange:
        return Icons.check_circle;
      case MetricStatus.outOfRange:
        return Icons.warning_amber;
      case MetricStatus.critical:
        return Icons.error;
      case MetricStatus.maintenance:
        return Icons.build_circle_outlined;
      case MetricStatus.unavailable:
      case MetricStatus.noData:
        return Icons.help_outline;
    }
  }

  /// Localisation key resolved by the widget layer.
  String get localizationKey {
    switch (this) {
      case MetricStatus.inRange:
        return 'statusInRange';
      case MetricStatus.outOfRange:
        return 'statusOutOfRange';
      case MetricStatus.critical:
        return 'statusCritical';
      case MetricStatus.noData:
        return 'statusNoData';
      case MetricStatus.unavailable:
        return 'statusUnavailable';
      case MetricStatus.maintenance:
        return 'statusMaintenance';
    }
  }
}

/// Freshness of a value, derived from its timestamp — never stored as a boolean flag that a timer flips
/// (`03-implementation/05` §2).
enum FreshnessKind { noData, fresh, stale }

/// Freshness plus the age it was derived from, so the UI never shows a value without saying how old it is.
class Freshness {
  const Freshness(this.kind, this.age);

  const Freshness.noData() : kind = FreshnessKind.noData, age = Duration.zero;

  final FreshnessKind kind;
  final Duration age;

  bool get isStale => kind == FreshnessKind.stale;
  bool get hasData => kind != FreshnessKind.noData;

  /// Derives freshness from the sample age and the device's sampling interval.
  static Freshness fromAge(Duration age, int samplingIntervalSec) {
    final threshold = Duration(seconds: samplingIntervalSec * 3);
    return Freshness(
      age > threshold ? FreshnessKind.stale : FreshnessKind.fresh,
      age,
    );
  }
}
