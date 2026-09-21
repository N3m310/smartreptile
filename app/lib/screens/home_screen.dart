import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../core/clock.dart';
import '../l10n/generated/app_localizations.dart';
import '../l10n/labels.dart';
import '../models/metric_value.dart';
import '../state/telemetry_provider.dart';
import '../widgets/metric_card.dart';

/// Dashboard (S4 in `02-design/04` §1.1): what the terrarium looks like right now.
///
/// M1 shows the live/cached state and the honest empty/offline states. Terrarium selection, the alert banner
/// and the chart range selector arrive in M2/M4.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key, this.terrariumId, this.terrariumName});

  /// Terrarium to display; null renders the onboarding empty state.
  final String? terrariumId;

  /// Name shown in the header.
  final String? terrariumName;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final telemetry = context.watch<TelemetryProvider>();
    final clock = context.read<Clock>();

    if (terrariumId == null) {
      return _EmptyState(
        title: l10n.emptyStateTitle,
        body: l10n.emptyStateBody,
      );
    }

    final values = telemetry.valuesFor(terrariumId!);
    final device = telemetry.deviceFor(terrariumId!);

    return RefreshIndicator(
      onRefresh: () => telemetry.loadLatest(terrariumId!),
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _Header(
            name: terrariumName ?? terrariumId!,
            device: device,
            isOffline: telemetry.isOffline,
          ),
          if (telemetry.isOffline) ...[
            const SizedBox(height: 8),
            _Banner(
              icon: Icons.cloud_off,
              text: l10n.errorBackendUnreachable,
              color: const Color(0xFFED6C02),
            ),
          ],
          if (device?.isMaintenance ?? false) ...[
            const SizedBox(height: 8),
            _Banner(
              icon: Icons.build_circle_outlined,
              text: l10n.maintenanceNotice,
              color: const Color(0xFF616161),
            ),
          ],
          const SizedBox(height: 12),
          if (values.isEmpty)
            _EmptyState(title: l10n.statusNoData, body: l10n.emptyStateBody)
          else
            Wrap(
              spacing: 12,
              runSpacing: 12,
              children: [
                for (final entry in values.entries)
                  SizedBox(
                    width: 190,
                    child: MetricCard(
                      metricCode: entry.key,
                      displayName: metricDisplayName(l10n, entry.value.code),
                      value: entry.value.value,
                      unit: entry.value.unit,
                      status: entry.value.displayStatus,
                      capturedAt: entry.value.capturedAt,
                      now: clock.nowUtc(),
                      samplingIntervalSec: device?.samplingIntervalSec ?? 60,
                    ),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({
    required this.name,
    required this.device,
    required this.isOffline,
  });

  final String name;
  final DeviceSnapshot? device;
  final bool isOffline;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final l10n = AppLocalizations.of(context);
    final status = device?.status ?? 'offline';

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(name, style: theme.textTheme.titleLarge),
        const SizedBox(height: 4),
        Row(
          children: [
            Icon(
              deviceStatusIcon(status),
              size: 10,
              color: deviceStatusColor(status),
            ),
            const SizedBox(width: 6),
            Text(
              device?.isOnline == true ? l10n.deviceOnline : l10n.deviceOffline,
              style: theme.textTheme.bodySmall,
            ),
            if (device?.firmwareVersion != null) ...[
              const SizedBox(width: 8),
              Text(
                'fw ${device!.firmwareVersion}',
                style: theme.textTheme.bodySmall,
              ),
            ],
          ],
        ),
      ],
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.icon, required this.text, required this.color});

  final IconData icon;
  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Icon(icon, color: color, size: 18),
          const SizedBox(width: 8),
          Expanded(
            child: Text(text, style: Theme.of(context).textTheme.bodySmall),
          ),
        ],
      ),
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.title, required this.body});

  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(
              Icons.terrain_outlined,
              size: 42,
              color: theme.colorScheme.onSurfaceVariant,
            ),
            const SizedBox(height: 12),
            Text(
              title,
              style: theme.textTheme.titleMedium,
              textAlign: TextAlign.center,
            ),
            const SizedBox(height: 8),
            Text(
              body,
              style: theme.textTheme.bodySmall,
              textAlign: TextAlign.center,
            ),
          ],
        ),
      ),
    );
  }
}
