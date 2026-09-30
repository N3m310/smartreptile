/// Small building blocks shared by every prototype screen.
///
/// They are deliberately dumb: none of them knows about a rule. A widget that decides something would be a second
/// place where the product logic lives, and this prototype exists to have exactly one.
library;

import 'package:flutter/material.dart';

import '../../core/status.dart' as status;
import '../labels.dart';

/// Colours, named once so a screen cannot invent its own red.
///
/// The values are the tokens in `02-design/04` §3, which the real app already uses in `core/status.dart`; these
/// constants exist only for the cases a `MetricStatus` does not cover (an info note, a prototype banner).
class ProtoColors {
  const ProtoColors._();

  /// Within target band.
  static const inRange = Color(0xFF2E7D32);

  /// Outside the target band.
  static const warning = Color(0xFFED6C02);

  /// Outside the critical band.
  static const critical = Color(0xFFC62828);

  /// Unknown, unavailable, offline.
  static const unknown = Color(0xFF616161);

  /// A note that is neither good nor bad.
  static const info = Color(0xFF0277BD);

  /// The prototype-only accent, used so it is obvious what is not part of the product.
  static const prototype = Color(0xFF6A1B9A);
}

/// A tinted message block with an icon, and optionally an action.
class Callout extends StatelessWidget {
  /// Creates a callout.
  const Callout({
    super.key,
    required this.message,
    required this.color,
    this.icon = Icons.info_outline,
    this.title,
    this.actionLabel,
    this.onAction,
    this.dense = false,
  });

  /// Body text.
  final String message;

  /// Accent colour.
  final Color color;

  /// Leading icon.
  final IconData icon;

  /// Optional bold first line.
  final String? title;

  /// Optional action label.
  final String? actionLabel;

  /// Optional action callback.
  final VoidCallback? onAction;

  /// Tighter padding for inline use.
  final bool dense;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final padding = dense ? 10.0 : 14.0;

    return Container(
      padding: EdgeInsets.all(padding),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: color.withValues(alpha: 0.30)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: color, size: 18),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (title != null) ...[
                  Text(
                    title!,
                    style: theme.textTheme.labelLarge?.copyWith(
                      color: color,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  const SizedBox(height: 2),
                ],
                Text(
                  message,
                  style: theme.textTheme.bodySmall?.copyWith(height: 1.35),
                ),
                if (actionLabel != null && onAction != null) ...[
                  const SizedBox(height: 6),
                  TextButton(
                    onPressed: onAction,
                    style: TextButton.styleFrom(
                      padding: EdgeInsets.zero,
                      minimumSize: const Size(0, 32),
                      tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                      foregroundColor: color,
                    ),
                    child: Text(actionLabel!),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A titled card, the standard container of the prototype's screens.
class SectionCard extends StatelessWidget {
  /// Creates a section card.
  const SectionCard({
    super.key,
    this.title,
    this.subtitle,
    this.trailing,
    this.child,
    this.padding = const EdgeInsets.all(14),
  });

  /// Heading.
  final String? title;

  /// Secondary line under the heading.
  final String? subtitle;

  /// Widget on the right of the heading row.
  final Widget? trailing;

  /// Body.
  final Widget? child;

  /// Inner padding.
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Card(
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(8),
        side: BorderSide(color: theme.dividerColor.withValues(alpha: 0.5)),
      ),
      child: Padding(
        padding: padding,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            if (title != null)
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          title!,
                          style: theme.textTheme.titleSmall?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        if (subtitle != null) ...[
                          const SizedBox(height: 2),
                          Text(
                            subtitle!,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: theme.colorScheme.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  if (trailing != null) ?trailing,
                ],
              ),
            if (title != null && child != null) const SizedBox(height: 10),
            ?child,
          ],
        ),
      ),
    );
  }
}

/// A label/value row, used by every detail screen.
class KeyValueRow extends StatelessWidget {
  /// Creates a row.
  const KeyValueRow({
    super.key,
    required this.label,
    required this.value,
    this.valueColor,
    this.monospace = false,
    this.icon,
  });

  /// Left-hand label.
  final String label;

  /// Right-hand value.
  final String value;

  /// Colour for the value.
  final Color? valueColor;

  /// Render the value in a monospace style, for ids and codes.
  final bool monospace;

  /// Optional leading icon.
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = theme.textTheme.bodySmall;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 14, color: theme.colorScheme.onSurfaceVariant),
            const SizedBox(width: 6),
          ],
          SizedBox(
            width: 132,
            child: Text(
              label,
              style: style?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ),
          Expanded(
            child: Text(
              value,
              style: monospace
                  ? style?.copyWith(
                      fontFamily: 'monospace',
                      fontWeight: FontWeight.w600,
                    )
                  : style?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: valueColor,
                    ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A status pill: icon, label and colour, never colour alone (NFR-06).
class StatusPill extends StatelessWidget {
  /// Creates a pill.
  const StatusPill({
    super.key,
    required this.label,
    required this.color,
    this.icon,
  });

  /// Text.
  final String label;

  /// Accent colour.
  final Color color;

  /// Leading icon.
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 12, color: color),
            const SizedBox(width: 4),
          ],
          Text(
            label,
            style: theme.textTheme.labelSmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

/// Heading used between sections on a long screen.
class SectionHeading extends StatelessWidget {
  /// Creates a heading.
  const SectionHeading(this.text, {super.key, this.trailing});

  /// Heading text.
  final String text;

  /// Optional widget on the right.
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 8, top: 4),
      child: Row(
        children: [
          Expanded(
            child: Text(
              text.toUpperCase(),
              style: theme.textTheme.labelSmall?.copyWith(
                letterSpacing: 0.8,
                fontWeight: FontWeight.w700,
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ),
          ?trailing,
        ],
      ),
    );
  }
}

/// Maps the prototype's status vocabulary onto the shared colours.
extension ProtoStatusColor on status.MetricStatus {
  /// Display colour for a status.
  Color get protoColor {
    switch (this) {
      case status.MetricStatus.inRange:
        return ProtoColors.inRange;
      case status.MetricStatus.outOfRange:
        return ProtoColors.warning;
      case status.MetricStatus.critical:
        return ProtoColors.critical;
      case status.MetricStatus.noData:
      case status.MetricStatus.unavailable:
      case status.MetricStatus.maintenance:
        return ProtoColors.unknown;
    }
  }

  /// Display label for a status.
  String get protoLabel {
    switch (this) {
      case status.MetricStatus.inRange:
        return 'in range';
      case status.MetricStatus.outOfRange:
        return 'out of range';
      case status.MetricStatus.critical:
        return 'critical';
      case status.MetricStatus.noData:
        return 'no data';
      case status.MetricStatus.unavailable:
        return 'sensor unavailable';
      case status.MetricStatus.maintenance:
        return 'maintenance';
    }
  }
}

/// Formats a duration the way the alert screens need it: "9 min", "1 h 20 min".
String formatShortDuration(Duration duration) {
  if (duration.inMinutes < 60) {
    return '${duration.inMinutes} ${Labels.unitMinutes}';
  }
  final hours = duration.inHours;
  final minutes = duration.inMinutes - hours * 60;
  return minutes == 0 ? '$hours h' : '$hours h $minutes min';
}

/// Pads a number to two digits for the `HH:mm` and `dd` formats.
String _pad2(int value) => value.toString().padLeft(2, '0');

/// Formats a wall-clock instant as `dd MMM HH:mm`, which is the en format in `02-design/04` §7.
String formatInstant(DateTime instant) {
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${_pad2(instant.day)} ${months[instant.month - 1]} '
      '${_pad2(instant.hour)}:${_pad2(instant.minute)}';
}

/// Formats a time of day as `HH:mm`.
String formatTime(DateTime instant) =>
    '${instant.hour.toString().padLeft(2, '0')}:'
    '${instant.minute.toString().padLeft(2, '0')}';

/// Formats a metric value with the metric's own precision and thousands separators.
String formatMetric(double value, String metricCode) {
  final digits = switch (metricCode) {
    'tempC' || 'surfaceTempC' => 1,
    'humidityPct' || 'lightLux' => 0,
    'uvIndex' => 2,
    _ => 1,
  };
  final text = value.toStringAsFixed(digits);
  if (metricCode != 'lightLux') {
    return text;
  }
  final parts = text.split('.');
  final buffer = StringBuffer();
  for (var i = 0; i < parts.first.length; i++) {
    if (i > 0 && (parts.first.length - i) % 3 == 0) {
      buffer.write(' ');
    }
    buffer.write(parts.first[i]);
  }
  return parts.length > 1
      ? '${buffer.toString()}.${parts[1]}'
      : buffer.toString();
}

/// The unit for a metric code, from the metric dictionary.
String unitFor(String metricCode) {
  switch (metricCode) {
    case 'tempC':
    case 'surfaceTempC':
      return '°C';
    case 'humidityPct':
      return '%RH';
    case 'lightLux':
      return 'lx';
    case 'uvIndex':
      return 'UVI';
    default:
      return '';
  }
}
