/// The dashboard (S4): what the terrarium looks like right now.
///
/// The screen shows four things and refuses to show a fifth:
///  1. the device's honesty — online/offline/maintenance, last seen, and whether evaluation is paused;
///  2. each metric with its value, its *resolved* band and where that band came from;
///  3. the open alert, if any, with a link to its episode;
///  4. today's summary — out-of-range minutes, exposure, light hours — each rendered next to its coverage.
///
/// The fifth thing it must never show is a stale value that looks current (BR-08.5), which is why the age of every
/// reading travels with it and the whole grid dims when the device goes quiet.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../models/metric_value.dart';
import '../../widgets/metric_card.dart';
import '../fake_world.dart';
import '../labels.dart';
import '../prototype_state.dart';
import '../rules/domain.dart';
import '../widgets/band_bar.dart';
import '../widgets/prototype_ui.dart';
import 'alerts_screen.dart';
import 'thresholds_screen.dart';

/// S4: the live dashboard for the selected terrarium.
class DashboardScreen extends StatelessWidget {
  /// Creates the dashboard.
  const DashboardScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final view = state.liveView(state.activeTerrariumId);

    return ListView(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 24),
      children: [
        _DeviceHeader(view: view),
        const SizedBox(height: 10),
        if (view.silences.isNotEmpty) ...[
          Callout(
            title: Labels.dashboardSilenceBanner,
            message: view.silences
                .map(
                  (silence) => silence.metric == null
                      ? 'All metrics · ${silence.reason}'
                      : '${Labels.metricName(silence.metric!)} · ${silence.reason}',
                )
                .join('\n'),
            color: ProtoColors.info,
            icon: Icons.notifications_off_outlined,
            dense: true,
          ),
          const SizedBox(height: 8),
        ],
        if (view.isDeviceOffline) ...[
          const Callout(
            message: Labels.dashboardOfflineBanner,
            color: ProtoColors.warning,
            icon: Icons.cloud_off,
            dense: true,
          ),
          const SizedBox(height: 8),
        ],
        if (view.device?.status == DeviceStatus.maintenance) ...[
          const Callout(
            message: Labels.dashboardMaintenanceBanner,
            color: ProtoColors.unknown,
            icon: Icons.build_circle_outlined,
            dense: true,
          ),
          const SizedBox(height: 8),
        ],
        _AlertBanner(view: view),
        const SizedBox(height: 10),
        if (view.hasNoData)
          _EmptyState(view: view)
        else ...[
          _MetricGrid(view: view),
          const SizedBox(height: 14),
          _TodayStrip(view: view),
        ],
        const SizedBox(height: 14),
        const _PrototypeNote(),
      ],
    );
  }
}

class _DeviceHeader extends StatelessWidget {
  const _DeviceHeader({required this.view});

  final LiveView view;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final device = view.device;
    final age = view.newestAge;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          view.terrarium.name,
          style: theme.textTheme.titleLarge?.copyWith(
            fontWeight: FontWeight.w700,
          ),
        ),
        const SizedBox(height: 2),
        Text(
          '${view.terrarium.profile.species} · ${view.terrarium.timeZoneId}',
          style: theme.textTheme.bodySmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 6),
        Wrap(
          spacing: 8,
          runSpacing: 6,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            StatusPill(
              label: device?.status.label ?? 'no device',
              color: device == null
                  ? ProtoColors.unknown
                  : device.status.isReporting
                  ? ProtoColors.inRange
                  : ProtoColors.unknown,
              icon: device?.status.isReporting == true
                  ? Icons.circle
                  : Icons.circle_outlined,
            ),
            if (device != null)
              Text(
                age == null
                    ? Labels.dashboardNeverReceived
                    : '${Labels.devicesLastSeen} '
                          '${formatShortDuration(age)} ago',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: view.isStale ? ProtoColors.warning : null,
                  fontWeight: view.isStale ? FontWeight.w700 : null,
                ),
              ),
            if (device != null)
              Text(
                '${Labels.devicesFirmware} ${device.firmwareVersion}',
                style: theme.textTheme.labelSmall,
              ),
          ],
        ),
      ],
    );
  }
}

class _AlertBanner extends StatelessWidget {
  const _AlertBanner({required this.view});

  final LiveView view;

  @override
  Widget build(BuildContext context) {
    final alert = view.bannerAlert;

    if (alert == null) {
      return const Callout(
        message: Labels.dashboardAllClear,
        color: ProtoColors.inRange,
        icon: Icons.check_circle_outline,
        dense: true,
      );
    }

    final band = alert.bandAtOpen;
    final peak = alert.peakValue;
    final summary = [
      if (alert.metric != null) Labels.metricName(alert.metric!),
      if (band != null)
        'band ${formatMetric(band.targetMin, alert.metric ?? '')}'
            '–${formatMetric(band.targetMax, alert.metric ?? '')}'
            '${unitFor(alert.metric ?? '')}',
      if (peak != null)
        'peak ${formatMetric(peak, alert.metric ?? '')}'
            '${unitFor(alert.metric ?? '')}',
    ].join(' · ');

    return Callout(
      title: alert.severity == Severity.critical
          ? Labels.severityCritical
          : Labels.severityWarning,
      message: view.openAlerts.length == 1
          ? summary
          : '${view.openAlerts.length} open — worst: $summary',
      color: alert.severity == Severity.critical
          ? ProtoColors.critical
          : ProtoColors.warning,
      icon: alert.severity == Severity.critical
          ? Icons.error_outline
          : Icons.warning_amber,
      actionLabel: 'Open the episode →',
      onAction: () => Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => AlertDetailScreen(alertId: alert.id),
        ),
      ),
    );
  }
}

class _MetricGrid extends StatelessWidget {
  const _MetricGrid({required this.view});

  final LiveView view;

  @override
  Widget build(BuildContext context) {
    final ordered = <String>[
      for (final code in const [
        'tempC',
        'humidityPct',
        'lightLux',
        'uvIndex',
        'surfaceTempC',
      ])
        if (view.metrics.containsKey(code)) code,
    ];

    return LayoutBuilder(
      builder: (context, constraints) {
        // The design's responsive rule: one column under 420 dp, two to 900, four above.
        final columns = constraints.maxWidth < 420
            ? 1
            : constraints.maxWidth < 900
            ? 2
            : 4;
        final width = (constraints.maxWidth - (columns - 1) * 12) / columns;

        return Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [
            for (final code in ordered)
              SizedBox(
                width: width,
                child: _MetricTile(view: view, code: code),
              ),
          ],
        );
      },
    );
  }
}

class _MetricTile extends StatelessWidget {
  const _MetricTile({required this.view, required this.code});

  final LiveView view;
  final String code;

  @override
  Widget build(BuildContext context) {
    final metric = view.metrics[code]!;
    final effective = view.bands[code];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        MetricCard(
          metricCode: metric.code,
          displayName: Labels.metricName(metric.code),
          value: metric.value,
          unit: metric.unit,
          status: metric.displayStatus,
          capturedAt: metric.capturedAt,
          now: view.now,
          band: effective?.band == null
              ? null
              : Band(
                  min: effective!.band!.targetMin,
                  max: effective.band!.targetMax,
                ),
          sourceLabel: effective?.source?.label,
          samplingIntervalSec: view.samplingIntervalSec,
        ),
        if (effective?.band != null) ...[
          const SizedBox(height: 6),
          BandBar(
            band: effective!.band!,
            value: metric.value,
            metricCode: code,
            height: 22,
            showLimits: false,
          ),
          const SizedBox(height: 4),
          TextButton(
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) =>
                    ThresholdsScreen(terrariumId: view.terrarium.id),
              ),
            ),
            style: TextButton.styleFrom(
              padding: EdgeInsets.zero,
              minimumSize: const Size(0, 28),
              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
            ),
            child: const Text(
              Labels.dashboardWhyThisNumber,
              style: TextStyle(fontSize: 11),
            ),
          ),
        ],
      ],
    );
  }
}

class _TodayStrip extends StatelessWidget {
  const _TodayStrip({required this.view});

  final LiveView view;

  @override
  Widget build(BuildContext context) {
    final today = view.today;
    if (today == null) {
      return const SizedBox.shrink();
    }

    return SectionCard(
      title: Labels.dashboardToday,
      subtitle: today.isLowConfidence
          ? '${Labels.reportLowConfidence} · ${Labels.reportCoverageRule}'
          : Labels.reportCoverageRule,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 16,
            runSpacing: 6,
            children: [
              _Stat(
                value: '${today.temperatureOutOfRangeMinutes}',
                label: Labels.reportOutOfRange,
                unit: Labels.unitMinutes,
                color: today.temperatureOutOfRangeMinutes > 0
                    ? ProtoColors.warning
                    : ProtoColors.inRange,
              ),
              _Stat(
                value: today.temperatureExposureDegCHours.toStringAsFixed(2),
                label: Labels.reportExposure,
                unit: '°C·h',
                color: ProtoColors.info,
              ),
              _Stat(
                value: formatMetric(today.lightHours, 'lightLux'),
                label: Labels.reportLight,
                unit: 'h',
                color: today.lightDeficitHours > 0
                    ? ProtoColors.warning
                    : ProtoColors.inRange,
              ),
              _Stat(
                value: '${today.criticalAlertCount}/${today.alertCount}',
                label: Labels.reportAlerts,
                unit: 'crit',
                color: today.criticalAlertCount > 0
                    ? ProtoColors.critical
                    : ProtoColors.unknown,
              ),
              _Stat(
                value: today.coveragePct.toStringAsFixed(0),
                label: Labels.reportCoverage,
                unit: '%',
                color: today.isLowConfidence
                    ? ProtoColors.warning
                    : ProtoColors.unknown,
              ),
            ],
          ),
          if (today.lightDeficitHours > 0) ...[
            const SizedBox(height: 8),
            Text(
              '${Labels.reportLightNote} Deficit today: '
              '${today.lightDeficitHours.toStringAsFixed(1)} h of '
              '${today.requiredLightHours} h required.',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ],
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({
    required this.value,
    required this.label,
    required this.unit,
    required this.color,
  });

  final String value;
  final String label;
  final String unit;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Text(
              value,
              style: theme.textTheme.titleMedium?.copyWith(
                fontWeight: FontWeight.w700,
                color: color,
              ),
            ),
            const SizedBox(width: 3),
            Text(unit, style: theme.textTheme.labelSmall),
          ],
        ),
        Text(label, style: theme.textTheme.labelSmall),
      ],
    );
  }
}

class _EmptyState extends StatelessWidget {
  const _EmptyState({required this.view});

  final LiveView view;

  @override
  Widget build(BuildContext context) {
    final hasDevice = view.device != null;

    return SectionCard(
      child: Column(
        children: [
          Icon(
            hasDevice ? Icons.hourglass_empty : Icons.terrain_outlined,
            size: 36,
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
          const SizedBox(height: 10),
          Text(
            hasDevice
                ? Labels.dashboardWaitingTitle
                : Labels.dashboardEmptyTitle,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: 4),
          Text(
            hasDevice ? Labels.dashboardWaitingBody : Labels.dashboardEmptyBody,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _PrototypeNote extends StatelessWidget {
  const _PrototypeNote();

  @override
  Widget build(BuildContext context) => const Callout(
    title: Labels.prototypeTitle,
    message: Labels.prototypeBody,
    color: ProtoColors.prototype,
    icon: Icons.science_outlined,
    dense: true,
  );
}
