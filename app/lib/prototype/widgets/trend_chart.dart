/// A hand-drawn trend chart.
///
/// **Why not a chart package?** Because the one rule this chart has to obey is BR-09.5: *"series are gap-aware:
/// intervals with no data are `null`, never interpolated"*. A `CustomPainter` draws a segment per consecutive pair of
/// real readings, so a gap is a gap; most chart libraries insist on a continuous series and quietly draw a straight
/// line across the hole — which is the single most misleading thing a monitoring chart can do, because a straight line
/// across missing data looks exactly like a measurement.
library;

import 'package:flutter/material.dart';

import 'prototype_ui.dart';

/// One point on a trend. A null value is a gap, not a zero.
class TrendPoint {
  /// Creates a point.
  const TrendPoint(this.at, this.value);

  /// When the reading was taken.
  final DateTime at;

  /// The reading, or null when there is no data at this instant.
  final double? value;
}

/// An interval to shade behind the line, used for alert overlays (BR-09.3).
class TrendInterval {
  /// Creates an interval.
  const TrendInterval({
    required this.from,
    required this.to,
    required this.color,
    this.label,
  });

  /// Start instant.
  final DateTime from;

  /// End instant.
  final DateTime to;

  /// Shade colour.
  final Color color;

  /// Optional label for the legend.
  final String? label;
}

/// A trend chart with a target band, alert overlays and honest gaps.
class TrendChart extends StatelessWidget {
  /// Creates a chart.
  const TrendChart({
    super.key,
    required this.points,
    required this.metricCode,
    this.bandMin,
    this.bandMax,
    this.criticalMin,
    this.criticalMax,
    this.intervals = const [],
    this.height = 170,
    this.showAxisLabels = true,
  });

  /// The series.
  final List<TrendPoint> points;

  /// Metric code, for units and precision.
  final String metricCode;

  /// Target band lower bound, when a band is known.
  final double? bandMin;

  /// Target band upper bound.
  final double? bandMax;

  /// Critical lower bound.
  final double? criticalMin;

  /// Critical upper bound.
  final double? criticalMax;

  /// Intervals to shade, e.g. alerts.
  final List<TrendInterval> intervals;

  /// Chart height.
  final double height;

  /// Whether to draw the min/max and time labels.
  final bool showAxisLabels;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final values = [
      for (final point in points)
        if (point.value != null) point.value!,
    ];

    if (points.isEmpty || values.isEmpty) {
      return SizedBox(
        height: height,
        child: Center(
          child: Text(
            'No samples in this range.',
            style: theme.textTheme.bodySmall,
          ),
        ),
      );
    }

    var min = values.reduce((a, b) => a < b ? a : b);
    var max = values.reduce((a, b) => a > b ? a : b);
    if (bandMin != null) {
      min = min < bandMin! ? min : bandMin!;
    }
    if (bandMax != null) {
      max = max > bandMax! ? max : bandMax!;
    }
    if (criticalMin != null) {
      min = min < criticalMin! ? min : criticalMin!;
    }
    if (criticalMax != null) {
      max = max > criticalMax! ? max : criticalMax!;
    }
    // A flat series would divide by zero; give it a band of ±1 so the line sits in the middle.
    if (max - min < 0.001) {
      min -= 1;
      max += 1;
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: height,
          child: CustomPaint(
            painter: _TrendPainter(
              points: points,
              min: min,
              max: max,
              bandMin: bandMin,
              bandMax: bandMax,
              criticalMin: criticalMin,
              criticalMax: criticalMax,
              intervals: intervals,
              lineColor: theme.colorScheme.primary,
              gridColor: theme.dividerColor.withValues(alpha: 0.4),
            ),
            size: Size.infinite,
          ),
        ),
        if (showAxisLabels) ...[
          const SizedBox(height: 4),
          Row(
            children: [
              Text(
                formatMetric(max, metricCode),
                style: theme.textTheme.labelSmall,
              ),
              const Spacer(),
              Text(
                formatInstant(points.first.at),
                style: theme.textTheme.labelSmall,
              ),
              const SizedBox(width: 8),
              Text('→', style: theme.textTheme.labelSmall),
              const SizedBox(width: 8),
              Text(
                formatInstant(points.last.at),
                style: theme.textTheme.labelSmall,
              ),
              const Spacer(),
              Text(
                formatMetric(min, metricCode),
                style: theme.textTheme.labelSmall,
              ),
            ],
          ),
        ],
      ],
    );
  }
}

class _TrendPainter extends CustomPainter {
  _TrendPainter({
    required this.points,
    required this.min,
    required this.max,
    required this.bandMin,
    required this.bandMax,
    required this.criticalMin,
    required this.criticalMax,
    required this.intervals,
    required this.lineColor,
    required this.gridColor,
  });

  final List<TrendPoint> points;
  final double min;
  final double max;
  final double? bandMin;
  final double? bandMax;
  final double? criticalMin;
  final double? criticalMax;
  final List<TrendInterval> intervals;
  final Color lineColor;
  final Color gridColor;

  @override
  void paint(Canvas canvas, Size size) {
    final span = max - min;
    final firstAt = points.first.at;
    final lastAt = points.last.at;
    final totalMs = lastAt.difference(firstAt).inMilliseconds;

    double y(double value) =>
        size.height - ((value - min) / span) * size.height;
    double x(DateTime at) {
      if (totalMs == 0) {
        return size.width / 2;
      }
      final offset = at.difference(firstAt).inMilliseconds;
      return (offset / totalMs) * size.width;
    }

    // Alert overlays go behind everything else (BR-09.3).
    for (final interval in intervals) {
      final left = x(interval.from).clamp(0.0, size.width);
      final right = x(interval.to).clamp(0.0, size.width);
      if (right <= left) {
        continue;
      }
      canvas.drawRect(
        Rect.fromLTRB(left, 0, right, size.height),
        Paint()..color = interval.color.withValues(alpha: 0.18),
      );
    }

    // The critical zones, so an excursion is visibly out of the *allowed* region, not just off a green line.
    if (criticalMax != null && criticalMax! > min) {
      final top = y(max);
      final bottom = y(criticalMax!);
      if (bottom > top) {
        canvas.drawRect(
          Rect.fromLTRB(0, top, size.width, bottom),
          Paint()..color = ProtoColors.critical.withValues(alpha: 0.10),
        );
      }
    }
    if (criticalMin != null && criticalMin! < max) {
      final top = y(criticalMin!);
      final bottom = y(min);
      if (bottom > top) {
        canvas.drawRect(
          Rect.fromLTRB(0, top, size.width, bottom),
          Paint()..color = ProtoColors.critical.withValues(alpha: 0.10),
        );
      }
    }

    // The target band.
    if (bandMin != null && bandMax != null) {
      final top = y(bandMax!);
      final bottom = y(bandMin!);
      canvas.drawRect(
        Rect.fromLTRB(0, top, size.width, bottom),
        Paint()..color = ProtoColors.inRange.withValues(alpha: 0.16),
      );
    }

    // The series: one polyline per run of consecutive non-null values. A run of one draws a dot, because a single
    // isolated reading is data and must not vanish.
    final line = Paint()
      ..color = lineColor
      ..strokeWidth = 1.8
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;
    final dot = Paint()..color = lineColor;

    var run = <Offset>[];
    var runSize = 0;

    void flush() {
      if (runSize == 1) {
        canvas.drawCircle(run.first, 2.2, dot);
      } else if (run.length > 1) {
        final path = Path()..moveTo(run.first.dx, run.first.dy);
        for (final point in run.skip(1)) {
          path.lineTo(point.dx, point.dy);
        }
        canvas.drawPath(path, line);
      }
      run = <Offset>[];
      runSize = 0;
    }

    for (final point in points) {
      if (point.value == null) {
        flush();
        continue;
      }
      run.add(Offset(x(point.at), y(point.value!)));
      runSize++;
    }
    flush();

    // A faint baseline so an empty chart still reads as a chart.
    canvas.drawLine(
      Offset(0, size.height),
      Offset(size.width, size.height),
      Paint()
        ..color = gridColor
        ..strokeWidth = 1,
    );
  }

  @override
  bool shouldRepaint(_TrendPainter oldDelegate) =>
      oldDelegate.points != points ||
      oldDelegate.min != min ||
      oldDelegate.max != max ||
      oldDelegate.intervals != intervals;
}
