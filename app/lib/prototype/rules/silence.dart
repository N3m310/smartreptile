/// Silence windows (BR-12.6): deliberate, accountable, always visible suppression.
///
/// A silence is not a mute button. It needs a reason, it expires on its own within 24 hours, and the dashboard shows
/// it — because "the app was quiet" and "the app was silenced" must never look the same to a keeper.
library;

/// A silence applied to one metric of one terrarium.
class SilenceWindow {
  /// Creates a silence window.
  const SilenceWindow({
    required this.metric,
    required this.from,
    required this.until,
    required this.reason,
    required this.createdBy,
    this.terrariumId,
  });

  /// Terrarium it applies to. Null means "the run in progress", which is how the Rule Lab uses a silence without
  /// introducing a terrarium into a pure function.
  final String? terrariumId;

  /// Metric silenced, or null to silence every metric of the terrarium.
  final String? metric;

  /// Start instant.
  final DateTime from;

  /// End instant; the maximum allowed length is [maxDuration].
  final DateTime until;

  /// Why, recorded for the audit trail.
  final String reason;

  /// Who created it.
  final String createdBy;

  /// Hard cap imposed by BR-12.6.
  static const maxDuration = Duration(hours: 24);

  /// True when this window suppresses notifications for [metric] at [at].
  bool covers(String? metric, DateTime at) {
    if (at.isBefore(from) || at.isAfter(until)) {
      return false;
    }
    return this.metric == null || this.metric == metric;
  }

  /// Remaining time, for the dashboard banner.
  Duration remaining(DateTime now) =>
      until.isAfter(now) ? until.difference(now) : Duration.zero;

  /// True when the window has already expired.
  bool isExpired(DateTime now) => !until.isAfter(now);

  /// Creates a window clamped to the 24-hour maximum, which is what the API would do.
  factory SilenceWindow.create({
    required String? metric,
    required DateTime now,
    required Duration duration,
    required String reason,
    required String createdBy,
    String? terrariumId,
  }) => SilenceWindow(
    metric: metric,
    from: now,
    until: now.add(duration > maxDuration ? maxDuration : duration),
    reason: reason,
    createdBy: createdBy,
    terrariumId: terrariumId,
  );

  @override
  String toString() =>
      'SilenceWindow(${metric ?? 'all metrics'} until ${until.toIso8601String()}: $reason)';
}
