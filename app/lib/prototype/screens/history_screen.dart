/// History (S5): a range picker, the trend chart, and the alerts that were open inside the range.
///
/// The range list stops at 48 hours on purpose. BR-09.1 says raw readings cover 6 hours, 5-minute averages cover up to
/// 48, and anything longer is served from hourly rollups — an M4 pipeline. Offering a 30-day button that drew a line
/// through four days of data would be the exact dishonesty this project keeps warning itself about, so the longer
/// ranges are shown, disabled, with the reason.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../labels.dart';
import '../prototype_state.dart';
import '../rules/domain.dart';
import '../widgets/band_bar.dart';
import '../widgets/prototype_ui.dart';
import '../widgets/trend_chart.dart';

/// S5: the historical view.
class HistoryScreen extends StatefulWidget {
  /// Creates the screen.
  const HistoryScreen({super.key});

  @override
  State<HistoryScreen> createState() => _HistoryScreenState();
}

class _HistoryScreenState extends State<HistoryScreen> {
  Duration _range = const Duration(hours: 24);
  String _metric = 'tempC';

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final terrariumId = state.activeTerrariumId;
    final view = state.liveView(terrariumId);
    final to = state.demoNow;
    final from = to.subtract(_range);
    final series = state.seriesFor(terrariumId, _metric, from: from, to: to);
    final band = view.bands[_metric]?.band;

    final values = [for (final sample in series) sample.value];
    final alertsInRange = state
        .alertsFor(terrariumId)
        .where(
          (alert) =>
              alert.metric == _metric &&
              !(alert.resolvedAt ?? state.demoNow).isBefore(from),
        )
        .toList();

    return ListView(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 28),
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final option in const [
              (Labels.historyRange1h, Duration(hours: 1), true),
              (Labels.historyRange6h, Duration(hours: 6), true),
              (Labels.historyRange24h, Duration(hours: 24), true),
              (Labels.historyRange48h, Duration(hours: 48), true),
              (Labels.historyRange7d, Duration(days: 7), false),
              (Labels.historyRange30d, Duration(days: 30), false),
            ])
              ChoiceChip(
                label: Text(option.$1),
                selected: _range == option.$2,
                onSelected: option.$3
                    ? (_) => setState(() => _range = option.$2)
                    : null,
              ),
          ],
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final metric in state.measuredMetrics(terrariumId))
              ChoiceChip(
                label: Text(Labels.metricName(metric)),
                selected: _metric == metric,
                onSelected: (_) => setState(() => _metric = metric),
              ),
          ],
        ),
        const SizedBox(height: 14),
        SectionCard(
          title:
              '${state.activeTerrarium.name} · ${Labels.metricName(_metric)}',
          subtitle: Labels.alertTimelineBandNote,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TrendChart(
                points: [
                  for (final sample in series)
                    TrendPoint(sample.recordedAt, sample.value),
                ],
                metricCode: _metric,
                bandMin: band?.targetMin,
                bandMax: band?.targetMax,
                criticalMin: band?.criticalMin,
                criticalMax: band?.criticalMax,
                intervals: [
                  for (final alert in alertsInRange)
                    TrendInterval(
                      from: alert.triggeredAt,
                      to: alert.resolvedAt ?? state.demoNow,
                      color: alert.severity == Severity.critical
                          ? ProtoColors.critical
                          : ProtoColors.warning,
                      label: alert.severity.label,
                    ),
                ],
                height: 200,
              ),
              if (values.isNotEmpty) ...[
                const SizedBox(height: 8),
                Text(
                  Labels.historyStats(
                    formatMetric(
                      values.reduce((a, b) => a < b ? a : b),
                      _metric,
                    ),
                    formatMetric(
                      values.reduce((a, b) => a + b) / values.length,
                      _metric,
                    ),
                    formatMetric(
                      values.reduce((a, b) => a > b ? a : b),
                      _metric,
                    ),
                  ),
                  style: Theme.of(context).textTheme.labelSmall,
                ),
              ],
              const SizedBox(height: 10),
              Row(
                children: [
                  _Legend(
                    color: ProtoColors.inRange,
                    label: Labels.historyTargetBand,
                  ),
                  const SizedBox(width: 12),
                  const _Legend(
                    color: ProtoColors.critical,
                    label: Labels.historyAlertBand,
                  ),
                ],
              ),
              if (band != null) ...[
                const SizedBox(height: 10),
                BandBar(
                  band: band,
                  value: view.metrics[_metric]?.value,
                  metricCode: _metric,
                  height: 24,
                ),
              ],
            ],
          ),
        ),
        if (alertsInRange.isNotEmpty) ...[
          const SizedBox(height: 14),
          SectionCard(
            title: '${Labels.tabAlerts} (${alertsInRange.length})',
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final alert in alertsInRange)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Row(
                      children: [
                        StatusPill(
                          label: alert.severity.label,
                          color: alert.severity == Severity.critical
                              ? ProtoColors.critical
                              : ProtoColors.warning,
                          icon: alert.severity == Severity.critical
                              ? Icons.error_outline
                              : Icons.warning_amber,
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Text(
                            '${formatInstant(alert.triggeredAt)} → '
                            '${alert.resolvedAt == null ? Labels.stateOpen : formatInstant(alert.resolvedAt!)}',
                            style: Theme.of(context).textTheme.labelSmall,
                          ),
                        ),
                        if (alert.peakValue != null)
                          Text(
                            '${Labels.alertPeak} ${formatMetric(alert.peakValue!, _metric)}',
                            style: Theme.of(context).textTheme.labelSmall,
                          ),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 14),
        const Callout(
          message: Labels.historyRollupNote,
          color: ProtoColors.info,
          icon: Icons.stacked_line_chart,
          dense: true,
        ),
      ],
    );
  }
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label});

  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(width: 12, height: 12, color: color.withValues(alpha: 0.3)),
      const SizedBox(width: 6),
      Text(label, style: Theme.of(context).textTheme.labelSmall),
    ],
  );
}
