/// Report (S12) and Settings (S14).
///
/// The report is the screen the PRM393 write-up draws its evidence from, so it is built to be *quoted*: every row
/// carries its coverage next to its compliance number, the exposure index is shown in degree-hours rather than as an
/// adjective, and the two rules that make those numbers mean something are printed under the table.
///
/// Settings is where the notification policy becomes editable — and every control on it maps to a branch of the
/// decision flow in `02-design/05` §4, so a reviewer can change one and watch the Alert detail screen's suppression
/// reasons change with it.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../state/settings_provider.dart';
import '../labels.dart';
import '../prototype_state.dart';
import '../rules/domain.dart';
import '../rules/events.dart';
import '../rules/notification_policy.dart';
import '../widgets/prototype_ui.dart';
import 'devices_screen.dart';

/// S12: the daily environmental summaries.
class ReportScreen extends StatelessWidget {
  /// Creates the report screen.
  const ReportScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final summaries = state.summariesFor(state.activeTerrariumId);
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.reportTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          SectionCard(
            title: state.activeTerrarium.name,
            subtitle: '${state.activeTerrarium.profile.species} · local day',
            child: summaries.isEmpty
                ? const Text('No summary yet.')
                : SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: DataTable(
                      columnSpacing: 18,
                      headingRowHeight: 32,
                      dataRowMinHeight: 34,
                      dataRowMaxHeight: 46,
                      columns: const [
                        DataColumn(label: Text(Labels.reportDay)),
                        DataColumn(label: Text(Labels.reportCoverage)),
                        DataColumn(label: Text(Labels.reportOutOfRange)),
                        DataColumn(label: Text(Labels.reportExposure)),
                        DataColumn(label: Text(Labels.reportLight)),
                        DataColumn(label: Text(Labels.reportAlerts)),
                        DataColumn(label: Text(Labels.reportSilence)),
                      ],
                      rows: [
                        for (final summary in summaries)
                          DataRow(
                            cells: [
                              DataCell(
                                Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  children: [
                                    Text(
                                      '${summary.localDate.day}/'
                                      '${summary.localDate.month}',
                                      style: theme.textTheme.bodySmall,
                                    ),
                                    if (summary.isLowConfidence)
                                      Text(
                                        Labels.reportLowConfidence,
                                        style: theme.textTheme.labelSmall
                                            ?.copyWith(
                                              color: ProtoColors.warning,
                                            ),
                                      ),
                                  ],
                                ),
                              ),
                              DataCell(
                                Text(
                                  '${summary.coveragePct.toStringAsFixed(0)} %',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: summary.isLowConfidence
                                        ? ProtoColors.warning
                                        : ProtoColors.inRange,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ),
                              DataCell(
                                Text(
                                  '${summary.temperatureOutOfRangeMinutes} '
                                  '${Labels.unitMinutes}',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color:
                                        summary.temperatureOutOfRangeMinutes > 0
                                        ? ProtoColors.warning
                                        : null,
                                  ),
                                ),
                              ),
                              DataCell(
                                Text(
                                  '${summary.temperatureExposureDegCHours.toStringAsFixed(2)} °C·h',
                                  style: theme.textTheme.bodySmall,
                                ),
                              ),
                              DataCell(
                                Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  children: [
                                    Text(
                                      '${summary.lightHours.toStringAsFixed(1)} h',
                                      style: theme.textTheme.bodySmall,
                                    ),
                                    if (summary.lightDeficitHours > 0)
                                      Text(
                                        'deficit '
                                        '${summary.lightDeficitHours.toStringAsFixed(1)} h',
                                        style: theme.textTheme.labelSmall
                                            ?.copyWith(
                                              color: ProtoColors.warning,
                                            ),
                                      ),
                                  ],
                                ),
                              ),
                              DataCell(
                                Text(
                                  '${summary.criticalAlertCount} crit / '
                                  '${summary.alertCount}',
                                  style: theme.textTheme.bodySmall?.copyWith(
                                    color: summary.criticalAlertCount > 0
                                        ? ProtoColors.critical
                                        : null,
                                  ),
                                ),
                              ),
                              DataCell(
                                Text(
                                  '${summary.silenceWindows} ${Labels.unitMinutes}',
                                  style: theme.textTheme.bodySmall,
                                ),
                              ),
                            ],
                          ),
                      ],
                    ),
                  ),
          ),
          const SizedBox(height: 12),
          const Callout(
            message: Labels.reportCoverageRule,
            color: ProtoColors.info,
            icon: Icons.percent,
            title: Labels.reportCoverage,
            dense: true,
          ),
          const SizedBox(height: 8),
          const Callout(
            message: Labels.reportExposureNote,
            color: ProtoColors.info,
            icon: Icons.thermostat,
            title: Labels.reportExposure,
            dense: true,
          ),
          const SizedBox(height: 8),
          const Callout(
            message: Labels.reportLightNote,
            color: ProtoColors.info,
            icon: Icons.wb_sunny_outlined,
            title: Labels.reportLight,
            dense: true,
          ),
          const SizedBox(height: 14),
          SectionCard(
            title: Labels.reportExport,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  Labels.reportExportUnavailable,
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 8),
                const FilledButton(
                  onPressed: null,
                  child: Text(Labels.reportExport),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// S14: settings.
class SettingsScreen extends StatelessWidget {
  /// Creates the settings screen.
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    // Theme lives on the app's own SettingsProvider, which already persists it: a second copy of "is it dark?" is a
    // bug waiting to be reported as "the switch does nothing".
    final settings = context.watch<SettingsProvider>();
    final prefs = state.preferences;
    final theme = Theme.of(context);

    void update(NotificationPreferences next) => state.setPreferences(next);

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.settingsTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          SectionCard(
            title: Labels.settingsAccount,
            child: Column(
              children: [
                KeyValueRow(
                  label: Labels.fieldName,
                  value: state.user?.name ?? '—',
                ),
                KeyValueRow(
                  label: Labels.settingsEmail,
                  value: state.user?.email ?? '—',
                ),
                KeyValueRow(
                  label: Labels.settingsRole,
                  value: state.user?.role.label ?? '—',
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.settingsAppearance,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                SegmentedButton<ThemeMode>(
                  segments: const [
                    ButtonSegment(
                      value: ThemeMode.system,
                      label: Text(Labels.themeSystem),
                    ),
                    ButtonSegment(
                      value: ThemeMode.light,
                      label: Text(Labels.themeLight),
                    ),
                    ButtonSegment(
                      value: ThemeMode.dark,
                      label: Text(Labels.themeDark),
                    ),
                  ],
                  selected: {settings.themeMode},
                  onSelectionChanged: (selection) =>
                      settings.setThemeMode(selection.first),
                ),
                const SizedBox(height: 8),
                KeyValueRow(
                  label: Labels.settingsLanguage,
                  value: 'English',
                  icon: Icons.language,
                ),
                Text(
                  Labels.settingsLanguageNote,
                  style: theme.textTheme.labelSmall,
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.settingsNotifications,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final channel in Channel.values)
                  SwitchListTile.adaptive(
                    value: prefs.channels.contains(channel),
                    onChanged: (enabled) {
                      final next = {...prefs.channels};
                      if (enabled) {
                        next.add(channel);
                      } else {
                        next.remove(channel);
                      }
                      update(prefs.copyWith(channels: next));
                    },
                    title: Text(switch (channel) {
                      Channel.inApp => Labels.settingsChannelInApp,
                      Channel.push => Labels.settingsChannelPush,
                      Channel.email => Labels.settingsChannelEmail,
                    }, style: theme.textTheme.bodySmall),
                    contentPadding: EdgeInsets.zero,
                    dense: true,
                  ),
                const Divider(),
                Text(
                  Labels.settingsMinSeverity,
                  style: theme.textTheme.bodySmall,
                ),
                const SizedBox(height: 6),
                SegmentedButton<Severity>(
                  segments: const [
                    ButtonSegment(
                      value: Severity.warning,
                      label: Text(Labels.severityWarning),
                    ),
                    ButtonSegment(
                      value: Severity.critical,
                      label: Text(Labels.severityCritical),
                    ),
                  ],
                  selected: {prefs.minSeverity},
                  onSelectionChanged: (selection) =>
                      update(prefs.copyWith(minSeverity: selection.first)),
                ),
                const SizedBox(height: 10),
                SwitchListTile.adaptive(
                  value: prefs.quietHoursEnabled,
                  onChanged: (enabled) =>
                      update(prefs.copyWith(quietHoursEnabled: enabled)),
                  title: Text(
                    '${Labels.settingsQuietHours} '
                    '${Labels.settingsQuietHoursWindow(prefs.quietHoursStartHour, prefs.quietHoursEndHour)}',
                    style: theme.textTheme.bodySmall,
                  ),
                  subtitle: Text(
                    Labels.settingsQuietHoursNote,
                    style: theme.textTheme.labelSmall,
                  ),
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                ),
                SwitchListTile.adaptive(
                  value: prefs.recoveryNotices,
                  onChanged: (enabled) =>
                      update(prefs.copyWith(recoveryNotices: enabled)),
                  title: Text(
                    Labels.settingsRecoveryNotices,
                    style: theme.textTheme.bodySmall,
                  ),
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.settingsSilences,
            trailing: TextButton.icon(
              onPressed: () => _addSilence(context, state),
              icon: const Icon(Icons.add_alert_outlined, size: 16),
              label: const Text(Labels.settingsAddSilence),
            ),
            child: state.activeSilences.isEmpty
                ? Text(Labels.none, style: theme.textTheme.bodySmall)
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      for (final silence in state.activeSilences)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 6),
                          child: Row(
                            children: [
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      '${silence.metric == null ? 'All metrics' : Labels.metricName(silence.metric!)}'
                                      ' · ${silence.reason}',
                                      style: theme.textTheme.bodySmall,
                                    ),
                                    Text(
                                      'until ${formatInstant(silence.until)} · '
                                      '${silence.createdBy}',
                                      style: theme.textTheme.labelSmall,
                                    ),
                                  ],
                                ),
                              ),
                              TextButton(
                                onPressed: () => state.clearSilence(silence),
                                child: const Text(Labels.settingsEndSilence),
                              ),
                            ],
                          ),
                        ),
                    ],
                  ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.settingsPrototype,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        '${Labels.demoClockLabel}: '
                        '${formatInstant(state.demoNow)}',
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                    OutlinedButton(
                      onPressed: () =>
                          state.advanceDemoClock(const Duration(minutes: 10)),
                      child: const Text(Labels.demoClockAdvance),
                    ),
                    const SizedBox(width: 6),
                    OutlinedButton(
                      onPressed: state.resetDemoClock,
                      child: const Text(Labels.demoClockReset),
                    ),
                  ],
                ),
                const SizedBox(height: 4),
                Text(Labels.demoClockHint, style: theme.textTheme.labelSmall),
                const SizedBox(height: 10),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  leading: const Icon(Icons.science_outlined, size: 18),
                  title: const Text(
                    Labels.diagnosticsTitle,
                    style: TextStyle(fontSize: 13),
                  ),
                  trailing: const Icon(Icons.chevron_right, size: 18),
                  onTap: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => const DiagnosticsScreen(),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.settingsAbout,
            child: Text(
              Labels.settingsAboutBody,
              style: theme.textTheme.bodySmall?.copyWith(height: 1.4),
            ),
          ),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: state.signOut,
            icon: const Icon(Icons.logout, size: 18),
            label: const Text(Labels.signOut),
          ),
        ],
      ),
    );
  }

  Future<void> _addSilence(BuildContext context, PrototypeState state) async {
    final reason = TextEditingController(text: 'Lamp replacement');
    var metric = state.measuredMetrics(state.activeTerrariumId).first;
    var hours = 1.0;

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setLocalState) => AlertDialog(
          title: const Text(Labels.settingsAddSilence),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              DropdownButtonFormField<String>(
                initialValue: metric,
                decoration: const InputDecoration(
                  labelText: Labels.fieldSilenceMetric,
                  border: OutlineInputBorder(),
                  isDense: true,
                ),
                items: [
                  for (final code in state.measuredMetrics(
                    state.activeTerrariumId,
                  ))
                    DropdownMenuItem(
                      value: code,
                      child: Text(Labels.metricName(code)),
                    ),
                ],
                onChanged: (value) =>
                    setLocalState(() => metric = value ?? metric),
              ),
              const SizedBox(height: 10),
              TextField(
                controller: reason,
                decoration: const InputDecoration(
                  labelText: Labels.fieldSilenceReason,
                  border: OutlineInputBorder(),
                  isDense: true,
                ),
              ),
              const SizedBox(height: 10),
              Text(
                '${Labels.fieldSilenceDuration}: ${hours.toStringAsFixed(0)}',
                style: Theme.of(context).textTheme.labelSmall,
              ),
              Slider(
                value: hours,
                min: 1,
                max: 48,
                divisions: 47,
                label: hours.toStringAsFixed(0),
                onChanged: (value) => setLocalState(() => hours = value),
              ),
              Text(
                'The server caps a silence at 24 hours (BR-12.6); asking for 48 stores 24.',
                style: Theme.of(context).textTheme.labelSmall,
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text(Labels.cancel),
            ),
            FilledButton(
              onPressed: () {
                state.createSilence(
                  terrariumId: state.activeTerrariumId,
                  metric: metric,
                  duration: Duration(hours: hours.round()),
                  reason: reason.text,
                );
                Navigator.of(dialogContext).pop();
              },
              child: const Text(Labels.apply),
            ),
          ],
        ),
      ),
    );
    reason.dispose();
  }
}

/// S8: the terrarium list.
class TerrariumsScreen extends StatelessWidget {
  /// Creates the list.
  const TerrariumsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.moreTerrariums)),
      body: ListView.separated(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        itemCount: state.terrariums.length,
        separatorBuilder: (_, _) => const SizedBox(height: 10),
        itemBuilder: (context, index) {
          final terrarium = state.terrariums[index];
          final device = state.deviceFor(terrarium.id);
          final open = state.openAlertsFor(terrarium.id);
          final selected = terrarium.id == state.activeTerrariumId;

          return InkWell(
            onTap: () {
              state.selectTerrarium(terrarium.id);
              Navigator.of(context).maybePop();
            },
            child: SectionCard(
              title: terrarium.name,
              subtitle: terrarium.profile.species,
              trailing: selected
                  ? const StatusPill(
                      label: 'active',
                      color: ProtoColors.inRange,
                      icon: Icons.check_circle_outline,
                    )
                  : null,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  KeyValueRow(
                    label: Labels.devicesTitle,
                    value: device == null ? 'no node claimed' : device.id,
                    icon: Icons.memory,
                  ),
                  KeyValueRow(
                    label: Labels.tabAlerts,
                    value: open.isEmpty ? Labels.none : '${open.length} open',
                    valueColor: open.isEmpty ? null : ProtoColors.critical,
                    icon: Icons.notifications_none,
                  ),
                  if (terrarium.notes != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Text(
                        terrarium.notes!,
                        style: Theme.of(context).textTheme.labelSmall,
                      ),
                    ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}
