/// The alert inbox (S6) and the alert episode (S7).
///
/// These two screens are where the rule kernel's output becomes something a person acts on, so they show the numbers
/// the engine computed rather than a summary of them: the back-dated start, the peak, the band that was in force at
/// the time, and *why* a notification was or was not sent. `FalsePositive` and `SensorFault` are offered as resolve
/// reasons because they are the v2 model's label source (BR-12.5) — offering them is the whole point of the button.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../labels.dart';
import '../prototype_state.dart';
import '../rules/domain.dart';
import '../rules/events.dart';
import '../widgets/band_bar.dart';
import '../widgets/prototype_ui.dart';
import '../widgets/trend_chart.dart';

/// S6: the filterable alert inbox.
class AlertsScreen extends StatelessWidget {
  /// Creates the inbox.
  const AlertsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final alerts = state.filteredAlerts;

    return Column(
      children: [
        _FilterBar(state: state),
        const Divider(height: 1),
        Expanded(
          child: alerts.isEmpty
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Text(
                      Labels.alertsEmpty,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ),
                )
              : ListView.separated(
                  padding: const EdgeInsets.fromLTRB(12, 12, 12, 24),
                  itemCount: alerts.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (context, index) => _AlertTile(
                    alert: alerts[index],
                    terrariumName: state.alertById(alerts[index].id) == null
                        ? ''
                        : _terrariumName(state, alerts[index].terrariumId),
                  ),
                ),
        ),
      ],
    );
  }

  String _terrariumName(PrototypeState state, String terrariumId) {
    for (final terrarium in state.terrariums) {
      if (terrarium.id == terrariumId) {
        return terrarium.name;
      }
    }
    return terrariumId;
  }
}

class _FilterBar extends StatelessWidget {
  const _FilterBar({required this.state});

  final PrototypeState state;

  @override
  Widget build(BuildContext context) {
    final filter = state.alertFilter;

    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
      child: Wrap(
        spacing: 8,
        runSpacing: 8,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          ChoiceChip(
            label: const Text(Labels.alertsFilterAll),
            selected: filter.severity == null && filter.state == null,
            onSelected: (_) => state.setAlertFilter(const AlertFilter()),
          ),
          ChoiceChip(
            label: const Text(Labels.severityCritical),
            selected: filter.severity == Severity.critical,
            onSelected: (selected) => state.setAlertFilter(
              selected
                  ? filter.copyWith(severity: Severity.critical)
                  : filter.copyWith(clearSeverity: true),
            ),
          ),
          ChoiceChip(
            label: const Text(Labels.severityWarning),
            selected: filter.severity == Severity.warning,
            onSelected: (selected) => state.setAlertFilter(
              selected
                  ? filter.copyWith(severity: Severity.warning)
                  : filter.copyWith(clearSeverity: true),
            ),
          ),
          ChoiceChip(
            label: const Text(Labels.stateOpen),
            selected: filter.state == AlertState.open,
            onSelected: (selected) => state.setAlertFilter(
              selected
                  ? filter.copyWith(state: AlertState.open)
                  : filter.copyWith(clearState: true),
            ),
          ),
          ChoiceChip(
            label: const Text(Labels.stateResolved),
            selected: filter.state == AlertState.resolved,
            onSelected: (selected) => state.setAlertFilter(
              selected
                  ? filter.copyWith(state: AlertState.resolved)
                  : filter.copyWith(clearState: true),
            ),
          ),
          if (!filter.isEmpty)
            TextButton(
              onPressed: () => state.setAlertFilter(const AlertFilter()),
              child: const Text(Labels.alertsClearFilter),
            ),
        ],
      ),
    );
  }
}

class _AlertTile extends StatelessWidget {
  const _AlertTile({required this.alert, required this.terrariumName});

  final AlertEpisode alert;
  final String terrariumName;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = switch (alert.severity) {
      Severity.critical => ProtoColors.critical,
      Severity.warning => ProtoColors.warning,
      Severity.info => ProtoColors.info,
    };
    final metricName = alert.metric == null
        ? alert.source.label
        : Labels.metricName(alert.metric!);

    return InkWell(
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => AlertDetailScreen(alertId: alert.id),
        ),
      ),
      child: SectionCard(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              width: 4,
              height: 46,
              decoration: BoxDecoration(
                color: color,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          metricName,
                          style: theme.textTheme.titleSmall?.copyWith(
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ),
                      StatusPill(
                        label: alert.severity.label,
                        color: color,
                        icon: alert.severity == Severity.critical
                            ? Icons.error_outline
                            : Icons.warning_amber,
                      ),
                      const SizedBox(width: 6),
                      StatusPill(
                        label: alert.state.label,
                        color: alert.isOpen
                            ? ProtoColors.unknown
                            : ProtoColors.inRange,
                        icon: alert.isOpen
                            ? Icons.radio_button_unchecked
                            : Icons.check_circle_outline,
                      ),
                    ],
                  ),
                  const SizedBox(height: 4),
                  Text(
                    terrariumName,
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: theme.colorScheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 4),
                  Wrap(
                    spacing: 12,
                    children: [
                      Text(
                        '${Labels.alertTriggered} ${formatInstant(alert.triggeredAt)}',
                        style: theme.textTheme.labelSmall,
                      ),
                      Text(
                        alert.resolvedAt == null
                            ? '${Labels.alertDuration} '
                                  '${formatShortDuration(alert.lastObservedAt?.difference(alert.triggeredAt) ?? Duration.zero)}'
                            : '${Labels.alertDuration} '
                                  '${formatShortDuration(alert.durationAsOf(alert.resolvedAt!))}',
                        style: theme.textTheme.labelSmall,
                      ),
                      if (alert.peakValue != null)
                        Text(
                          '${Labels.alertPeak} '
                          '${formatMetric(alert.peakValue!, alert.metric ?? 'tempC')}'
                          '${unitFor(alert.metric ?? '')}',
                          style: theme.textTheme.labelSmall,
                        ),
                    ],
                  ),
                  if (alert.acknowledgedBy != null && alert.isOpen)
                    Padding(
                      padding: const EdgeInsets.only(top: 2),
                      child: Text(
                        Labels.alertAcknowledgedBy(alert.acknowledgedBy!),
                        style: theme.textTheme.labelSmall,
                      ),
                    ),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, size: 20),
          ],
        ),
      ),
    );
  }
}

/// S7: one alert episode, its timeline, its actions and its notification history.
class AlertDetailScreen extends StatefulWidget {
  /// Creates the detail screen for one alert.
  const AlertDetailScreen({super.key, required this.alertId});

  /// The alert to show.
  final int alertId;

  @override
  State<AlertDetailScreen> createState() => _AlertDetailScreenState();
}

class _AlertDetailScreenState extends State<AlertDetailScreen> {
  ResolvedReason _reason = ResolvedReason.recovered;
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final alert = state.alertById(widget.alertId);

    if (alert == null) {
      return Scaffold(
        appBar: AppBar(title: const Text(Labels.alert)),
        body: const Center(child: Text('This alert no longer exists.')),
      );
    }

    final theme = Theme.of(context);
    final color = alert.severity == Severity.critical
        ? ProtoColors.critical
        : ProtoColors.warning;
    final band = alert.bandAtOpen;
    final metricCode = alert.metric ?? 'tempC';
    final duration = alert.durationAsOf(alert.resolvedAt ?? state.demoNow);
    final series = alert.metric == null
        ? <MetricSample>[]
        : state.seriesFor(
            alert.terrariumId,
            alert.metric!,
            from: alert.triggeredAt.subtract(const Duration(minutes: 10)),
            to: (alert.resolvedAt ?? state.demoNow).add(
              const Duration(minutes: 10),
            ),
          );
    final notifications = state.notificationsFor(alert.id);

    return Scaffold(
      appBar: AppBar(
        title: Text(
          alert.metric == null
              ? alert.source.label
              : Labels.metricName(alert.metric!),
        ),
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          Row(
            children: [
              StatusPill(
                label: alert.severity.label,
                color: color,
                icon: alert.severity == Severity.critical
                    ? Icons.error_outline
                    : Icons.warning_amber,
              ),
              const SizedBox(width: 6),
              StatusPill(
                label: alert.state.label,
                color: alert.isOpen ? ProtoColors.unknown : ProtoColors.inRange,
                icon: alert.isOpen
                    ? Icons.radio_button_unchecked
                    : Icons.check_circle_outline,
              ),
              const Spacer(),
              Text(
                '#${alert.id}',
                style: theme.textTheme.labelSmall?.copyWith(
                  fontFamily: 'monospace',
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          SectionCard(
            child: Column(
              children: [
                KeyValueRow(
                  label: Labels.alertTriggered,
                  value: formatInstant(alert.triggeredAt),
                  icon: Icons.play_arrow,
                ),
                KeyValueRow(
                  label: Labels.alertResolvedAt,
                  value: alert.resolvedAt == null
                      ? Labels.stateOpen
                      : formatInstant(alert.resolvedAt!),
                  icon: Icons.stop,
                ),
                KeyValueRow(
                  label: Labels.alertDuration,
                  value: formatShortDuration(duration),
                  icon: Icons.timer_outlined,
                ),
                if (alert.peakValue != null)
                  KeyValueRow(
                    label: Labels.alertPeak,
                    value:
                        '${formatMetric(alert.peakValue!, metricCode)}${unitFor(metricCode)}',
                    icon: Icons.trending_up,
                  ),
                if (band != null)
                  KeyValueRow(
                    label: Labels.alertBandInForce,
                    value:
                        '${formatMetric(band.targetMin, metricCode)}'
                        '–${formatMetric(band.targetMax, metricCode)}'
                        '${unitFor(metricCode)} '
                        '(crit ${formatMetric(band.criticalMin, metricCode)}'
                        '–${formatMetric(band.criticalMax, metricCode)})',
                    icon: Icons.horizontal_rule,
                  ),
                if (band != null)
                  KeyValueRow(
                    label: Labels.alertThresholdSource,
                    value: band.source == BandSource.override
                        ? Labels.sourceOverride
                        : Labels.sourceProfile,
                    icon: Icons.rule,
                  ),
                if (alert.resolvedReason != null)
                  KeyValueRow(
                    label: 'Resolved as',
                    value: alert.resolvedReason!.label,
                    icon: Icons.flag_outlined,
                  ),
              ],
            ),
          ),
          const SizedBox(height: 10),
          const Callout(
            message: Labels.alertDedupeNote,
            color: ProtoColors.info,
            icon: Icons.merge_type,
            dense: true,
          ),
          if (series.isNotEmpty) ...[
            const SizedBox(height: 14),
            SectionCard(
              title: Labels.alertTimeline,
              subtitle: Labels.alertTimelineBandNote,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TrendChart(
                    points: [
                      for (final sample in series)
                        TrendPoint(sample.recordedAt, sample.value),
                    ],
                    metricCode: metricCode,
                    bandMin: band?.targetMin,
                    bandMax: band?.targetMax,
                    criticalMin: band?.criticalMin,
                    criticalMax: band?.criticalMax,
                    intervals: [
                      TrendInterval(
                        from: alert.triggeredAt,
                        to: alert.resolvedAt ?? state.demoNow,
                        color: color,
                      ),
                    ],
                    height: 150,
                  ),
                  const SizedBox(height: 8),
                  if (band != null)
                    BandBar(
                      band: band,
                      value: alert.peakValue,
                      metricCode: metricCode,
                      height: 24,
                    ),
                ],
              ),
            ),
          ],
          if (notifications.isNotEmpty) ...[
            const SizedBox(height: 14),
            SectionCard(
              title: Labels.alertNotifications,
              subtitle: Labels.alertSuppressionSummary,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final record in notifications)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 6),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Icon(
                            record.isSent
                                ? Icons.send_outlined
                                : Icons.notifications_off_outlined,
                            size: 14,
                            color: record.isSent
                                ? ProtoColors.inRange
                                : ProtoColors.unknown,
                          ),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  '${record.channel.label} · '
                                  '${record.isSent ? Labels.notificationSent : _suppressionLabel(record.outcome)}',
                                  style: theme.textTheme.labelSmall?.copyWith(
                                    fontWeight: FontWeight.w600,
                                    color: record.isSent
                                        ? ProtoColors.inRange
                                        : ProtoColors.unknown,
                                  ),
                                ),
                                if (record.body != null)
                                  Text(
                                    record.body!.replaceAll('\n', ' · '),
                                    style: theme.textTheme.labelSmall,
                                  ),
                              ],
                            ),
                          ),
                          Text(
                            formatInstant(record.at),
                            style: theme.textTheme.labelSmall,
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ],
          const SizedBox(height: 14),
          if (alert.isOpen)
            _Actions(
              state: state,
              alert: alert,
              reason: _reason,
              note: _note,
              onReason: (value) => setState(() => _reason = value),
            )
          else
            const Callout(
              message: Labels.alertTerminalNote,
              color: ProtoColors.unknown,
              icon: Icons.lock_outline,
              dense: true,
            ),
        ],
      ),
    );
  }

  static String _suppressionLabel(SuppressedReason reason) {
    switch (reason) {
      case SuppressedReason.none:
        return Labels.notificationSent;
      case SuppressedReason.quietHours:
        return Labels.suppressionQuietHours;
      case SuppressedReason.silencedMetric:
        return Labels.suppressionSilenced;
      case SuppressedReason.maintenance:
        return Labels.suppressionMaintenance;
      case SuppressedReason.belowMinSeverity:
        return Labels.suppressionBelowMinSeverity;
      case SuppressedReason.digestCoalesced:
        return Labels.suppressionDigest;
      case SuppressedReason.preference:
        return Labels.suppressionPreference;
      case SuppressedReason.noChannel:
        return Labels.suppressionNoChannel;
    }
  }
}

class _Actions extends StatelessWidget {
  const _Actions({
    required this.state,
    required this.alert,
    required this.reason,
    required this.note,
    required this.onReason,
  });

  final PrototypeState state;
  final AlertEpisode alert;
  final ResolvedReason reason;
  final TextEditingController note;
  final ValueChanged<ResolvedReason> onReason;

  @override
  Widget build(BuildContext context) {
    return SectionCard(
      title: Labels.alertResolveHeading,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (alert.state == AlertState.open)
            Align(
              alignment: Alignment.centerLeft,
              child: OutlinedButton.icon(
                onPressed: () => state.acknowledge(alert.id),
                icon: const Icon(Icons.visibility_outlined, size: 16),
                label: const Text(Labels.alertAcknowledge),
              ),
            ),
          const SizedBox(height: 8),
          // Wrapped in a RadioGroup because the per-tile `groupValue`/`onChanged` pair is deprecated in this Flutter
          // version; the group owns the selection instead.
          RadioGroup<ResolvedReason>(
            groupValue: reason,
            onChanged: (value) => onReason(value!),
            child: Column(
              children: [
                for (final option in ResolvedReason.values)
                  RadioListTile<ResolvedReason>(
                    value: option,
                    title: Text(
                      option.label,
                      style: const TextStyle(fontSize: 13),
                    ),
                    subtitle: option.isLabelForV2Training
                        ? const Text(
                            'kept as a training label for v2',
                            style: TextStyle(fontSize: 11),
                          )
                        : null,
                    dense: true,
                    contentPadding: EdgeInsets.zero,
                  ),
              ],
            ),
          ),
          TextField(
            controller: note,
            decoration: const InputDecoration(
              labelText: Labels.alertNote,
              border: OutlineInputBorder(),
              isDense: true,
            ),
          ),
          const SizedBox(height: 10),
          FilledButton(
            onPressed: () {
              state.resolve(alert.id, reason, note: note.text);
              Navigator.of(context).maybePop();
            },
            child: const Text(Labels.alertResolve),
          ),
        ],
      ),
    );
  }
}
