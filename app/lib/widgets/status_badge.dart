import 'package:flutter/material.dart';

import '../core/status.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';

/// Small status pill: icon + label + colour. Never colour-only (NFR-06).
class StatusBadge extends StatelessWidget {
  const StatusBadge({super.key, required this.status});

  /// Status to render.
  final MetricStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context);
    final color = status.color(theme);
    final label = status.label(l10n);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(status.icon, size: 14, color: color),
          const SizedBox(width: 4),
          Text(
            label,
            style: theme.textTheme.labelSmall?.copyWith(
              color: color,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
