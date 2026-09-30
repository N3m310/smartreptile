/// The band bar: one metric's thresholds and where the current value sits inside them.
///
/// It draws the *whole* band, not just the target: the critical limits are what make an alert Critical rather than a
/// warning, and a bar that only showed the target would hide exactly the number a keeper needs. The value marker is
/// the instantaneous reading, so a reviewer can see the difference between "outside the band" (coloured) and
/// "an alert" (needs dwell) — the two are not the same, which is the point of BR-11.3.
library;

import 'package:flutter/material.dart';

import '../labels.dart';
import '../rules/domain.dart';
import 'prototype_ui.dart';

/// A horizontal band with a marker for the current value.
class BandBar extends StatelessWidget {
  /// Creates a band bar.
  const BandBar({
    super.key,
    required this.band,
    required this.value,
    required this.metricCode,
    this.height = 34,
    this.showLimits = true,
  });

  /// The band in force (BR-10.3 — already resolved, including any override).
  final ThresholdBand band;

  /// The value to mark. Null renders the bar without a marker, which is how "no data" looks.
  final double? value;

  /// Metric code, used for units and precision.
  final String metricCode;

  /// Height of the bar.
  final double height;

  /// Whether to print the limit labels under the bar.
  final bool showLimits;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final unit = unitFor(metricCode);

    // The drawn range extends a little past the critical limits so a marker outside them is still visible rather
    // than pinned to the edge, which would understate how far out the value is.
    final span = band.criticalMax - band.criticalMin;
    final padding = span == 0 ? 1.0 : span * 0.15;
    final min = band.criticalMin - padding;
    final max = band.criticalMax + padding;
    final total = max - min;

    double fraction(double raw) => ((raw - min) / total).clamp(0.0, 1.0);

    final markerFraction = value == null ? null : fraction(value!);
    final markerColor = value == null
        ? ProtoColors.unknown
        : band.isCritical(value!)
        ? ProtoColors.critical
        : band.isInTargetBand(value!)
        ? ProtoColors.inRange
        : ProtoColors.warning;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        LayoutBuilder(
          builder: (context, constraints) {
            final width = constraints.maxWidth;

            return SizedBox(
              height: height,
              child: Stack(
                children: [
                  // The critical band: everything inside it is "not critical", including the target.
                  Positioned.fill(
                    child: Container(
                      decoration: BoxDecoration(
                        color: ProtoColors.critical.withValues(alpha: 0.08),
                        borderRadius: BorderRadius.circular(4),
                      ),
                    ),
                  ),
                  // The target band.
                  Positioned(
                    left: fraction(band.targetMin) * width,
                    width:
                        (fraction(band.targetMax) - fraction(band.targetMin)) *
                        width,
                    top: 0,
                    bottom: 0,
                    child: Container(
                      decoration: BoxDecoration(
                        color: ProtoColors.inRange.withValues(alpha: 0.18),
                        border: Border.symmetric(
                          vertical: BorderSide(
                            color: ProtoColors.inRange.withValues(alpha: 0.55),
                          ),
                        ),
                      ),
                    ),
                  ),
                  if (markerFraction != null)
                    Positioned(
                      left: (markerFraction * width - 2).clamp(0.0, width - 4),
                      top: 0,
                      bottom: 0,
                      child: Container(width: 4, color: markerColor),
                    ),
                ],
              ),
            );
          },
        ),
        if (showLimits) ...[
          const SizedBox(height: 4),
          Row(
            children: [
              Text(
                'crit ${formatMetric(band.criticalMin, metricCode)}$unit',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: ProtoColors.critical,
                ),
              ),
              const Spacer(),
              Text(
                'target ${formatMetric(band.targetMin, metricCode)}'
                '–${formatMetric(band.targetMax, metricCode)}$unit',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: ProtoColors.inRange,
                  fontWeight: FontWeight.w600,
                ),
              ),
              const Spacer(),
              Text(
                'crit ${formatMetric(band.criticalMax, metricCode)}$unit',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: ProtoColors.critical,
                ),
              ),
            ],
          ),
        ],
      ],
    );
  }
}

/// The four engine parameters of a band, shown as one dense line.
class BandParameters extends StatelessWidget {
  /// Creates the parameter line.
  const BandParameters({super.key, required this.band});

  /// The band.
  final ThresholdBand band;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = theme.textTheme.labelSmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );

    return Wrap(
      spacing: 10,
      runSpacing: 2,
      children: [
        Text(
          '${Labels.thresholdsDwell} ${band.dwellWarnMinutes}/${band.dwellCritMinutes} '
          '${Labels.unitMinutes}',
          style: style,
        ),
        Text(
          '${Labels.thresholdsRecovery} ${band.recoveryMargin}'
          '${unitFor(band.metric)}',
          style: style,
        ),
        if (band.accumulatedOnly)
          Text(Labels.thresholdsAccumulated, style: style),
      ],
    );
  }
}

/// A chip naming where a band came from, so "why is my limit 32 and not 30?" is answerable in the app (BR-10.3).
class BandSourceChip extends StatelessWidget {
  /// Creates the chip.
  const BandSourceChip({super.key, required this.band, this.inForce = true});

  /// The band.
  final ThresholdBand band;

  /// Whether this band is the one currently in force; the others are marked as not applying now.
  final bool inForce;

  @override
  Widget build(BuildContext context) {
    final label = switch (band.source) {
      BandSource.override => Labels.sourceOverride,
      BandSource.profile => Labels.sourceProfile,
      BandSource.systemDefault => Labels.sourceDefault,
    };

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        StatusPill(
          label: label,
          color: band.source == BandSource.systemDefault
              ? ProtoColors.warning
              : ProtoColors.info,
          icon: band.source == BandSource.override
              ? Icons.person_outline
              : Icons.rule_outlined,
        ),
        const SizedBox(width: 6),
        StatusPill(
          label: band.phase.label,
          color: ProtoColors.info,
          icon: Icons.schedule,
        ),
        if (!inForce) ...[
          const SizedBox(width: 6),
          StatusPill(
            label: Labels.thresholdsNotInForce,
            color: ProtoColors.unknown,
            icon: Icons.remove_circle_outline,
          ),
        ],
      ],
    );
  }
}
