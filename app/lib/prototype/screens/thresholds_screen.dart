/// Thresholds (S9): the effective bands, where each one came from, and the override editor.
///
/// This screen exists to answer one question out loud — *"why is my limit 32 and not 30?"* — so every band shows its
/// **source**, the phase it applies to, whether it is in force right now, and its provenance string. The editor
/// validates as you type and, when a rule only *warns* (BR-10.5), it says so and saves anyway: a sanity check that
/// blocks a keeper is a sanity check that gets worked around.
library;

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../labels.dart';
import '../prototype_state.dart';
import '../rules/band_validation.dart';
import '../rules/domain.dart';
import '../rules/species_profiles.dart';
import '../widgets/band_bar.dart';
import '../widgets/prototype_ui.dart';

/// S9: effective thresholds for one terrarium.
class ThresholdsScreen extends StatelessWidget {
  /// Creates the screen.
  const ThresholdsScreen({super.key, required this.terrariumId});

  /// Terrarium whose thresholds are shown.
  final String terrariumId;

  @override
  Widget build(BuildContext context) {
    final state = context.watch<PrototypeState>();
    final terrarium = state.terrariums.firstWhere(
      (candidate) => candidate.id == terrariumId,
      orElse: () => state.terrariums.first,
    );
    final view = state.liveView(terrariumId);
    final profile = terrarium.profile;

    // Every band of every measured metric, not only the phase in force: a keeper who edits the night band at noon
    // still has to be able to see it.
    final bands = <ThresholdBand>[
      for (final metric in state.measuredMetrics(terrariumId))
        ...profile.bandsFor(metric),
    ];
    final effectiveNow = {
      for (final entry in view.bands.entries) entry.key: entry.value.band,
    };

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.thresholdsTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          SectionCard(
            title: terrarium.name,
            subtitle: profile.species,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                KeyValueRow(
                  label: Labels.sourceProfile,
                  value: profile.name,
                  icon: Icons.rule_outlined,
                ),
                KeyValueRow(
                  label: Labels.thresholdsPhase,
                  value:
                      'on ${profile.phaseTimeline.lightsOnHour.toString().padLeft(2, '0')}:00, '
                      '${profile.phaseTimeline.photoperiodHours} h',
                  icon: Icons.schedule,
                ),
                KeyValueRow(
                  label: 'Light rule',
                  value:
                      '≥ ${formatMetric(profile.lightThresholdLux, 'lightLux')} lx '
                      'for ${profile.minLightHoursPerDay} h',
                  icon: Icons.wb_sunny_outlined,
                ),
                if (profile.note != null) ...[
                  const SizedBox(height: 8),
                  Callout(
                    title: Labels.thresholdsProfileNote,
                    message: profile.note!,
                    color: ProtoColors.info,
                    icon: Icons.sticky_note_2_outlined,
                    dense: true,
                  ),
                ],
              ],
            ),
          ),
          const SizedBox(height: 14),
          for (final band in bands) ...[
            _BandCard(
              state: state,
              terrariumId: terrariumId,
              band: band,
              isInForce: effectiveNow[band.metric]?.phase == band.phase,
              existingOverride: state
                  .overridesFor(terrariumId)
                  .where(
                    (candidate) =>
                        candidate.band.metric == band.metric &&
                        candidate.band.phase == band.phase,
                  )
                  .firstOrNull,
              currentValue: view.metrics[band.metric]?.value,
            ),
            const SizedBox(height: 10),
          ],
          const Callout(
            message: Labels.thresholdsCitationCaveat,
            color: ProtoColors.warning,
            icon: Icons.report_outlined,
            title: Labels.thresholdsCitation,
            dense: true,
          ),
        ],
      ),
    );
  }
}

class _BandCard extends StatelessWidget {
  const _BandCard({
    required this.state,
    required this.terrariumId,
    required this.band,
    required this.isInForce,
    required this.existingOverride,
    required this.currentValue,
  });

  final PrototypeState state;
  final String terrariumId;
  final ThresholdBand band;
  final bool isInForce;
  final ThresholdOverride? existingOverride;
  final double? currentValue;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final unit = unitFor(band.metric);

    return SectionCard(
      title:
          '${Labels.metricName(band.metric)}'
          '${band.phase == Phase.any ? '' : ' · ${band.phase.label}'}',
      trailing: existingOverride == null
          ? null
          : const StatusPill(
              label: Labels.sourceOverride,
              color: ProtoColors.info,
              icon: Icons.person_outline,
            ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          BandSourceChip(band: band, inForce: isInForce),
          const SizedBox(height: 10),
          BandBar(band: band, value: currentValue, metricCode: band.metric),
          const SizedBox(height: 10),
          KeyValueRow(
            label: Labels.thresholdsTarget,
            value:
                '${formatMetric(band.targetMin, band.metric)}–'
                '${formatMetric(band.targetMax, band.metric)}$unit',
          ),
          KeyValueRow(
            label: Labels.thresholdsCritical,
            value:
                '${formatMetric(band.criticalMin, band.metric)}–'
                '${formatMetric(band.criticalMax, band.metric)}$unit',
          ),
          KeyValueRow(
            label: Labels.thresholdsDwell,
            value:
                '${band.dwellWarnMinutes} / ${band.dwellCritMinutes} ${Labels.unitMinutes}',
          ),
          KeyValueRow(
            label: Labels.thresholdsRecovery,
            value: '${band.recoveryMargin}$unit',
          ),
          if (band.citation != null)
            KeyValueRow(
              label: Labels.thresholdsCitation,
              value: band.citation!,
            ),
          if (band.note != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                band.note!,
                style: theme.textTheme.labelSmall?.copyWith(height: 1.35),
              ),
            ),
          const SizedBox(height: 6),
          Row(
            children: [
              TextButton.icon(
                onPressed: () => _edit(context, existingOverride?.band ?? band),
                icon: const Icon(Icons.edit_outlined, size: 16),
                label: const Text(Labels.thresholdsAddOverride),
              ),
              if (existingOverride != null)
                TextButton.icon(
                  onPressed: () =>
                      state.clearOverride(terrariumId, band.metric, band.phase),
                  icon: const Icon(Icons.undo, size: 16),
                  label: const Text(Labels.thresholdsClearOverride),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _edit(BuildContext context, ThresholdBand startingPoint) async {
    await showDialog<void>(
      context: context,
      builder: (_) => _BandEditorDialog(
        state: state,
        terrariumId: terrariumId,
        initial: startingPoint.copyWith(source: BandSource.override),
      ),
    );
  }
}

/// The override editor, with live validation.
class _BandEditorDialog extends StatefulWidget {
  const _BandEditorDialog({
    required this.state,
    required this.terrariumId,
    required this.initial,
  });

  final PrototypeState state;
  final String terrariumId;
  final ThresholdBand initial;

  @override
  State<_BandEditorDialog> createState() => _BandEditorDialogState();
}

class _BandEditorDialogState extends State<_BandEditorDialog> {
  late final Map<String, TextEditingController> _fields = {
    'targetMin': TextEditingController(
      text: widget.initial.targetMin.toString(),
    ),
    'targetMax': TextEditingController(
      text: widget.initial.targetMax.toString(),
    ),
    'criticalMin': TextEditingController(
      text: widget.initial.criticalMin.toString(),
    ),
    'criticalMax': TextEditingController(
      text: widget.initial.criticalMax.toString(),
    ),
    'dwellWarnMinutes': TextEditingController(
      text: widget.initial.dwellWarnMinutes.toString(),
    ),
    'dwellCritMinutes': TextEditingController(
      text: widget.initial.dwellCritMinutes.toString(),
    ),
    'recoveryMargin': TextEditingController(
      text: widget.initial.recoveryMargin.toString(),
    ),
  };

  String? _parseError;

  @override
  void dispose() {
    for (final controller in _fields.values) {
      controller.dispose();
    }
    super.dispose();
  }

  ThresholdBand? _current() {
    final targetMin = double.tryParse(_fields['targetMin']!.text);
    final targetMax = double.tryParse(_fields['targetMax']!.text);
    final criticalMin = double.tryParse(_fields['criticalMin']!.text);
    final criticalMax = double.tryParse(_fields['criticalMax']!.text);
    final dwellWarn = int.tryParse(_fields['dwellWarnMinutes']!.text);
    final dwellCrit = int.tryParse(_fields['dwellCritMinutes']!.text);
    final margin = double.tryParse(_fields['recoveryMargin']!.text);

    if ([
          targetMin,
          targetMax,
          criticalMin,
          criticalMax,
          margin,
        ].any((value) => value == null) ||
        dwellWarn == null ||
        dwellCrit == null) {
      setState(() => _parseError = Labels.invalidNumber);
      return null;
    }
    setState(() => _parseError = null);

    return widget.initial.copyWith(
      targetMin: targetMin,
      targetMax: targetMax,
      criticalMin: criticalMin,
      criticalMax: criticalMax,
      dwellWarnMinutes: dwellWarn,
      dwellCritMinutes: dwellCrit,
      recoveryMargin: margin,
    );
  }

  @override
  Widget build(BuildContext context) {
    final band = _current();
    final issues = band == null
        ? const <ValidationIssue>[]
        : widget.state.validateBand(widget.terrariumId, band);
    final canSave = band != null && BandValidation.canSave(issues);

    return AlertDialog(
      title: Text(
        '${Labels.thresholdsEditTitle} · ${Labels.metricName(widget.initial.metric)}',
      ),
      content: SizedBox(
        width: 420,
        child: SingleChildScrollView(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final entry in const [
                ('targetMin', Labels.fieldTargetMin),
                ('targetMax', Labels.fieldTargetMax),
                ('criticalMin', Labels.fieldCriticalMin),
                ('criticalMax', Labels.fieldCriticalMax),
                ('dwellWarnMinutes', Labels.fieldDwellWarn),
                ('dwellCritMinutes', Labels.fieldDwellCrit),
                ('recoveryMargin', Labels.fieldRecoveryMargin),
              ])
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: TextField(
                    controller: _fields[entry.$1],
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    onChanged: (_) => setState(() {}),
                    decoration: InputDecoration(
                      labelText: entry.$2,
                      border: const OutlineInputBorder(),
                      isDense: true,
                    ),
                  ),
                ),
              if (_parseError != null)
                Callout(
                  message: _parseError!,
                  color: ProtoColors.warning,
                  icon: Icons.error_outline,
                  dense: true,
                ),
              if (issues.isNotEmpty) ...[
                const SizedBox(height: 4),
                for (final issue in issues)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 4),
                    child: Callout(
                      title: issue.isBlocking
                          ? Labels.thresholdsBlocking
                          : Labels.thresholdsWarningOnly,
                      message: '${issue.ruleId} — ${issue.message}',
                      color: issue.isBlocking
                          ? ProtoColors.critical
                          : ProtoColors.warning,
                      icon: issue.isBlocking ? Icons.block : Icons.info_outline,
                      dense: true,
                    ),
                  ),
              ],
              const SizedBox(height: 4),
              Text(
                Labels.thresholdsCitationCaveat,
                style: Theme.of(context).textTheme.labelSmall,
              ),
            ],
          ),
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text(Labels.cancel),
        ),
        FilledButton(
          onPressed: canSave
              ? () {
                  widget.state.applyOverride(
                    widget.terrariumId,
                    band,
                    reason: 'Edited in the prototype',
                  );
                  Navigator.of(context).pop();
                }
              : null,
          child: const Text(Labels.thresholdsSave),
        ),
      ],
    );
  }
}

extension<T> on Iterable<T> {
  /// The first element, or null. Used here because the override lookup is a filter, not a search that can fail.
  T? get firstOrNull => isEmpty ? null : first;
}
