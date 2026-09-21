import 'package:flutter/material.dart';

import '../core/formatting.dart';
import '../core/status.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../models/metric_value.dart';
import 'status_badge.dart';

/// One metric card on the dashboard.
///
/// The API of this widget enforces two product rules:
///  * `capturedAt` is required, so a value can never be rendered without its timestamp (`02-design/04` §6);
///  * the band and its source are always shown, so a number is never unexplained.
class MetricCard extends StatelessWidget {
  const MetricCard({
    super.key,
    required this.metricCode,
    required this.displayName,
    required this.value,
    required this.unit,
    required this.status,
    required this.capturedAt,
    required this.now,
    this.band,
    this.sourceLabel,
    this.samplingIntervalSec = 60,
  });

  /// Metric code, e.g. `tempC`.
  final String metricCode;

  /// Localised metric name, e.g. "Temperature".
  final String displayName;

  /// Latest value.
  final double value;

  /// Unit string from the metric dictionary.
  final String unit;

  /// Server-evaluated status.
  final MetricStatus status;

  /// Timestamp of the sample the value came from.
  final DateTime capturedAt;

  /// Injected "now" so the rendered age is deterministic in tests.
  final DateTime now;

  /// Target band, shown next to the value when known.
  final Band? band;

  /// Where the band came from (`profile` / `override` / `default`).
  final String? sourceLabel;

  /// Sampling interval used to decide staleness.
  final int samplingIntervalSec;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context);
    final freshness = Freshness.fromAge(
      now.difference(capturedAt),
      samplingIntervalSec,
    );
    final isStale = freshness.isStale;
    final statusColor = status.color(theme);
    final formatted = MetricFormat.value(value, metricCode);
    final ageText = MetricFormat.relativeAge(freshness.age);

    return Semantics(
      label: '$displayName $formatted $unit, ${status.label(l10n)}',
      child: Opacity(
        // Stale readings are dimmed and labelled: a monitoring app must never show an old value as if it were
        // current, that is the single most dangerous UX failure in this domain.
        opacity: isStale ? 0.55 : 1,
        child: Card(
          elevation: 0,
          margin: EdgeInsets.zero,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(8),
            side: BorderSide(color: statusColor.withValues(alpha: 0.35)),
          ),
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  displayName,
                  style: theme.textTheme.labelMedium?.copyWith(
                    letterSpacing: 0.4,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.baseline,
                  textBaseline: TextBaseline.alphabetic,
                  children: [
                    Text(
                      formatted,
                      style: theme.textTheme.headlineSmall?.copyWith(
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const SizedBox(width: 4),
                    Text(unit, style: theme.textTheme.bodySmall),
                  ],
                ),
                const SizedBox(height: 8),
                StatusBadge(status: status),
                if (band != null) ...[
                  const SizedBox(height: 6),
                  Text(
                    l10n.targetBandLabel(
                      MetricFormat.value(band!.min, metricCode),
                      MetricFormat.value(band!.max, metricCode),
                      unit,
                    ),
                    style: theme.textTheme.bodySmall,
                  ),
                ],
                const SizedBox(height: 4),
                Row(
                  children: [
                    Icon(
                      isStale ? Icons.history_toggle_off : Icons.schedule,
                      size: 12,
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                    const SizedBox(width: 4),
                    Expanded(
                      child: Text(
                        ageText,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: isStale
                              ? const Color(0xFFED6C02)
                              : theme.colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
