/// The Rule Lab: the reason this prototype exists.
///
/// Every other screen shows *what* the system says. This one shows *why*, and it is built so that a reviewer can
/// disagree with a rule, change it, and watch the consequence — which is the only way a business rule gets reviewed
/// rather than rubber-stamped.
///
/// It works by playing a documented scenario back through the real engine and printing the decision trace. Each step
/// carries the rule id that produced it, so no decision is unattributable; each scenario carries what the
/// documentation *claims* will happen, so a disagreement between the two is visible rather than buried.
library;

import 'dart:async';

import 'package:flutter/material.dart';

import '../labels.dart';
import '../rule_matrix.dart';
import '../rules/domain.dart';
import '../rules/events.dart';
import '../rules/scenarios.dart';
import '../widgets/prototype_ui.dart';

/// The Rule Lab screen.
class RuleLabScreen extends StatefulWidget {
  /// Creates the lab, optionally opening on a specific scenario.
  const RuleLabScreen({super.key, this.initialScenarioId});

  /// Scenario id to open on, e.g. `B`.
  final String? initialScenarioId;

  @override
  State<RuleLabScreen> createState() => _RuleLabScreenState();
}

class _RuleLabScreenState extends State<RuleLabScreen> {
  late Scenario _scenario =
      Scenarios.byId(widget.initialScenarioId ?? '') ?? Scenarios.all.first;
  EngineConfig _config = EngineConfig.standard;
  bool _noteworthyOnly = false;
  bool _playing = false;
  int _minute = 0;
  Timer? _timer;

  int get _totalMinutes => _scenario.rows.last.minute;

  @override
  void initState() {
    super.initState();
    _config = _scenario.config;
    _minute = _totalMinutes;
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  void _select(Scenario scenario) {
    _timer?.cancel();
    setState(() {
      _scenario = scenario;
      _config = scenario.config;
      _minute = scenario == _scenario ? scenario.rows.last.minute : 0;
      _playing = false;
    });
    setState(() => _minute = scenario.rows.last.minute);
  }

  void _togglePlay() {
    if (_playing) {
      _timer?.cancel();
      setState(() => _playing = false);
      return;
    }
    setState(() {
      _playing = true;
      if (_minute >= _totalMinutes) {
        _minute = 0;
      }
    });
    _timer = Timer.periodic(const Duration(milliseconds: 260), (timer) {
      if (!mounted) {
        timer.cancel();
        return;
      }
      setState(() {
        _minute++;
        if (_minute >= _totalMinutes) {
          _minute = _totalMinutes;
          _playing = false;
          timer.cancel();
        }
      });
    });
  }

  @override
  Widget build(BuildContext context) {
    final check = ScenarioRunner.run(_scenario, config: _config);
    final cutoff = _scenario.start.add(Duration(minutes: _minute));
    final allSteps = check.result.steps
        .where((step) => !step.at.isAfter(cutoff))
        .toList(growable: false);
    final steps = _noteworthyOnly
        ? allSteps.where((step) => step.isNoteworthy).toList(growable: false)
        : allSteps;

    return Scaffold(
      appBar: AppBar(title: const Text(Labels.labTitle)),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(12, 12, 12, 32),
        children: [
          const Callout(
            message: Labels.labIntro,
            color: ProtoColors.prototype,
            icon: Icons.science_outlined,
            dense: true,
          ),
          const SizedBox(height: 12),

          // ---- scenario picker ---------------------------------------------------------------------------
          const SectionHeading(Labels.labScenarios),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final scenario in Scenarios.all)
                ChoiceChip(
                  label: Text('${scenario.id} · ${scenario.title}'),
                  selected: scenario.id == _scenario.id,
                  onSelected: (_) => _select(scenario),
                ),
            ],
          ),
          const SizedBox(height: 14),

          // ---- the claim being tested ---------------------------------------------------------------------
          SectionCard(
            title: '${_scenario.id} — ${_scenario.title}',
            subtitle: _scenario.docRef,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const SectionHeading(Labels.labQuestion),
                Text(
                  _scenario.question,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                const SizedBox(height: 10),
                const SectionHeading(Labels.labWhyItMatters),
                Text(
                  _scenario.whyItMatters,
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(height: 1.4),
                ),
                const SizedBox(height: 12),
                const SectionHeading(Labels.labExpectation),
                Text(
                  _scenario.expectation.docClaim ?? '—',
                  style: Theme.of(context).textTheme.bodySmall
                      ?.copyWith(fontStyle: FontStyle.italic, height: 1.4),
                ),
                const SizedBox(height: 10),
                Row(
                  children: [
                    StatusPill(
                      label: !check.matchesExpectation
                          ? Labels.labDisagreesWithDoc
                          : _scenario.knownDocIssue != null
                          ? Labels.labDocExampleDiffers
                          : Labels.labMatchesDoc,
                      color: !check.matchesExpectation
                          ? ProtoColors.critical
                          : _scenario.knownDocIssue != null
                          ? ProtoColors.warning
                          : ProtoColors.inRange,
                      icon: !check.matchesExpectation
                          ? Icons.report_problem_outlined
                          : _scenario.knownDocIssue != null
                          ? Icons.difference_outlined
                          : Icons.check_circle_outline,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        check.headline,
                        style: Theme.of(context).textTheme.labelSmall,
                      ),
                    ),
                  ],
                ),
                if (check.failures.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Callout(
                    message: check.failures.join('\n'),
                    color: ProtoColors.critical,
                    icon: Icons.difference_outlined,
                    title: 'The engine disagrees with the document',
                    dense: true,
                  ),
                ],
              ],
            ),
          ),

          if (_scenario.knownDocIssue != null) ...[
            const SizedBox(height: 12),
            SectionCard(
              title: Labels.labDocDisagreement,
              subtitle: Labels.labOpenQuestionsNote,
              child: Text(
                _scenario.knownDocIssue!,
                style: Theme.of(context).textTheme.bodySmall
                    ?.copyWith(height: 1.4),
              ),
            ),
          ],

          // ---- engine parameters --------------------------------------------------------------------------
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labToggles,
            subtitle: Labels.labTogglesNote,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _IntSlider(
                  label: Labels.labDwellWarn,
                  value:
                      _config.dwellWarnMinutesOverride ??
                      _scenario.profile
                          .bandsFor('tempC')
                          .first
                          .dwellWarnMinutes,
                  min: 0,
                  max: 30,
                  onChanged: (value) => setState(
                    () => _config = _config.copyWith(
                      dwellWarnMinutesOverride: value,
                    ),
                  ),
                ),
                _IntSlider(
                  label: Labels.labDwellCrit,
                  value:
                      _config.dwellCritMinutesOverride ??
                      _scenario.profile
                          .bandsFor('tempC')
                          .first
                          .dwellCritMinutes,
                  min: 0,
                  max: 20,
                  onChanged: (value) => setState(
                    () => _config = _config.copyWith(
                      dwellCritMinutesOverride: value,
                    ),
                  ),
                ),
                _IntSlider(
                  label: Labels.labRecoveryMinutes,
                  value: _config.recoveryMinutes,
                  min: 1,
                  max: 10,
                  onChanged: (value) => setState(
                    () => _config = _config.copyWith(recoveryMinutes: value),
                  ),
                ),
                const SizedBox(height: 6),
                Text(
                  Labels.labRecoveryMode,
                  style: Theme.of(context).textTheme.labelSmall,
                ),
                const SizedBox(height: 4),
                Wrap(
                  spacing: 8,
                  children: [
                    for (final mode in RecoveryMode.values)
                      ChoiceChip(
                        label: Text(
                          mode.label,
                          style: const TextStyle(fontSize: 11),
                        ),
                        selected: _config.recoveryMode == mode,
                        onSelected: (_) => setState(
                          () => _config = _config.copyWith(recoveryMode: mode),
                        ),
                      ),
                  ],
                ),
                TextButton.icon(
                  onPressed: () => setState(() {
                    _config = _scenario.config;
                  }),
                  icon: const Icon(Icons.restart_alt, size: 16),
                  label: const Text(Labels.labResetToggles),
                ),
              ],
            ),
          ),

          // ---- playback ------------------------------------------------------------------------------------
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labTrace,
            subtitle: Labels.labMinute(_minute, _totalMinutes),
            trailing: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                IconButton(
                  tooltip: Labels.labStepBack,
                  onPressed: () => setState(
                    () => _minute = (_minute - 1).clamp(0, _totalMinutes),
                  ),
                  icon: const Icon(Icons.remove),
                ),
                IconButton(
                  tooltip: _playing ? Labels.labPause : Labels.labPlay,
                  onPressed: _togglePlay,
                  icon: Icon(_playing ? Icons.pause : Icons.play_arrow),
                ),
                IconButton(
                  tooltip: Labels.labStep,
                  onPressed: () => setState(
                    () => _minute = (_minute + 1).clamp(0, _totalMinutes),
                  ),
                  icon: const Icon(Icons.add),
                ),
                IconButton(
                  tooltip: Labels.labRestart,
                  onPressed: () => setState(() => _minute = 0),
                  icon: const Icon(Icons.restart_alt),
                ),
              ],
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Slider(
                  value: _minute.toDouble(),
                  max: _totalMinutes.toDouble(),
                  divisions: _totalMinutes == 0 ? null : _totalMinutes,
                  label: Labels.labMinute(_minute, _totalMinutes),
                  onChanged: (value) => setState(() => _minute = value.round()),
                ),
                Row(
                  children: [
                    Text(
                      formatTime(
                        _scenario.start.add(Duration(minutes: _minute)),
                      ),
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                    const Spacer(),
                    SegmentedButton<bool>(
                      segments: const [
                        ButtonSegment(
                          value: false,
                          label: Text(
                            Labels.labTraceEverything,
                            style: TextStyle(fontSize: 11),
                          ),
                        ),
                        ButtonSegment(
                          value: true,
                          label: Text(
                            Labels.labTraceNoteworthy,
                            style: TextStyle(fontSize: 11),
                          ),
                        ),
                      ],
                      selected: {_noteworthyOnly},
                      onSelectionChanged: (selection) =>
                          setState(() => _noteworthyOnly = selection.first),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                if (steps.isEmpty)
                  Text(
                    'Nothing has happened yet.',
                    style: Theme.of(context).textTheme.bodySmall,
                  )
                else
                  for (final step in steps) _TraceTile(step: step),
                if (_noteworthyOnly && allSteps.length != steps.length)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Text(
                      '${allSteps.length - steps.length} quiet steps hidden.',
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ),
              ],
            ),
          ),

          // ---- what the run produced ----------------------------------------------------------------------
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labAlertsProduced,
            child: check.result.alerts.isEmpty
                ? Text(
                    Labels.labNoAlerts,
                    style: Theme.of(context).textTheme.bodySmall,
                  )
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      for (final alert in check.result.alerts)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 6),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
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
                                  '${Labels.alertTriggered} '
                                  '${formatTime(alert.triggeredAt)}'
                                  '${alert.resolvedAt == null ? ' · still open' : ' → ${formatTime(alert.resolvedAt!)}'}'
                                  '${alert.peakValue == null ? '' : ' · peak ${alert.peakValue!.toStringAsFixed(1)}'}',
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              ),
                            ],
                          ),
                        ),
                    ],
                  ),
          ),

          // ---- counters ------------------------------------------------------------------------------------
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labCounters,
            child: Column(
              children: [
                KeyValueRow(
                  label: Labels.labCounterEvaluated,
                  value: '${check.result.counters.samplesEvaluated}',
                ),
                KeyValueRow(
                  label: Labels.labCounterSkipped,
                  value: '${check.result.counters.samplesSkipped}',
                ),
                KeyValueRow(
                  label: Labels.labCounterOpened,
                  value: '${check.result.counters.warningsOpened}',
                ),
                KeyValueRow(
                  label: Labels.labCounterEscalated,
                  value: '${check.result.counters.escalations}',
                ),
                KeyValueRow(
                  label: Labels.labCounterResolved,
                  value: '${check.result.counters.resolutions}',
                ),
                KeyValueRow(
                  label: Labels.labCounterSent,
                  value: '${check.result.counters.notificationsSent}',
                ),
                KeyValueRow(
                  label: Labels.labCounterSuppressed,
                  value: '${check.result.counters.notificationsSuppressed}',
                ),
                KeyValueRow(
                  label: Labels.labCounterBackfilled,
                  value: '${check.result.counters.backfilledSamples}',
                ),
              ],
            ),
          ),

          // ---- coverage and open questions ------------------------------------------------------------------
          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labCoverage,
            subtitle: '${RuleMatrix.summary}\n${Labels.labCoverageNote}',
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final entry in RuleMatrix.gaps)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 6),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            StatusPill(
                              label: entry.status.label,
                              color: entry.status == RuleStatus.notModelled
                                  ? ProtoColors.unknown
                                  : ProtoColors.warning,
                              icon: entry.status == RuleStatus.notModelled
                                  ? Icons.remove_circle_outline
                                  : Icons.adjust,
                            ),
                            const SizedBox(width: 8),
                            Expanded(
                              child: Text(
                                '${entry.id} · ${entry.title}',
                                style: Theme.of(context).textTheme.bodySmall
                                    ?.copyWith(fontWeight: FontWeight.w600),
                              ),
                            ),
                          ],
                        ),
                        if (entry.note != null)
                          Padding(
                            padding: const EdgeInsets.only(left: 4, top: 2),
                            child: Text(
                              entry.note!,
                              style: Theme.of(context).textTheme.labelSmall
                                  ?.copyWith(height: 1.35),
                            ),
                          ),
                      ],
                    ),
                  ),
                const SizedBox(height: 6),
                Text(
                  'Modelled here: ${RuleMatrix.modelled.map((e) => e.id).join(', ')}',
                  style: Theme.of(context).textTheme.labelSmall,
                ),
              ],
            ),
          ),

          const SizedBox(height: 12),
          SectionCard(
            title: Labels.labOpenQuestions,
            subtitle: Labels.labOpenQuestionsNote,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final question in RuleMatrix.openQuestions)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          '${question.$1} — ${question.$2}',
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          question.$3,
                          style: Theme.of(context).textTheme.labelSmall
                              ?.copyWith(height: 1.4),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _IntSlider extends StatelessWidget {
  const _IntSlider({
    required this.label,
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
  });

  final String label;
  final int value;
  final int min;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      SizedBox(
        width: 150,
        child: Text(label, style: Theme.of(context).textTheme.bodySmall),
      ),
      Expanded(
        child: Slider(
          value: value.clamp(min, max).toDouble(),
          min: min.toDouble(),
          max: max.toDouble(),
          divisions: max - min,
          label: '$value ${Labels.unitMinutes}',
          onChanged: (raw) => onChanged(raw.round()),
        ),
      ),
      SizedBox(
        width: 42,
        child: Text(
          '$value',
          textAlign: TextAlign.end,
          style: Theme.of(context).textTheme.labelSmall,
        ),
      ),
    ],
  );
}

/// One line of the decision trace.
class _TraceTile extends StatelessWidget {
  const _TraceTile({required this.step});

  final EvaluationStep step;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final color = switch (step.kind) {
      DecisionKind.openedWarning => ProtoColors.warning,
      DecisionKind.escalated => ProtoColors.critical,
      DecisionKind.resolved => ProtoColors.inRange,
      DecisionKind.notificationSent => ProtoColors.info,
      DecisionKind.notificationSuppressed => ProtoColors.unknown,
      DecisionKind.skippedNotEvaluable ||
      DecisionKind.skippedNoBand => ProtoColors.unknown,
      DecisionKind.backfillRecordedOnly => ProtoColors.info,
      DecisionKind.deviceSilent ||
      DecisionKind.deviceSilentCleared => ProtoColors.critical,
      _ => theme.colorScheme.onSurfaceVariant,
    };
    final isRuleGap = step.ruleId == 'OPEN-QUESTION';

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              SizedBox(
                width: 44,
                child: Text(
                  formatTime(step.at),
                  style: theme.textTheme.labelSmall?.copyWith(
                    fontFamily: 'monospace',
                  ),
                ),
              ),
              StatusPill(
                label: isRuleGap ? 'question' : step.ruleId,
                color: isRuleGap ? ProtoColors.prototype : color,
                icon: isRuleGap ? Icons.help_outline : null,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  step.kind.label,
                  style: theme.textTheme.labelSmall?.copyWith(
                    fontWeight: FontWeight.w700,
                    color: color,
                  ),
                ),
              ),
              if (step.value != null)
                Text(
                  formatMetric(step.value!, step.metric ?? 'tempC'),
                  style: theme.textTheme.labelSmall?.copyWith(
                    fontFamily: 'monospace',
                    fontWeight: FontWeight.w700,
                  ),
                ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.only(left: 44, top: 2),
            child: Text(
              step.explanation,
              style: theme.textTheme.bodySmall?.copyWith(height: 1.35),
            ),
          ),
          if (step.band != null)
            Padding(
              padding: const EdgeInsets.only(left: 44, top: 2),
              child: Text(
                'band ${formatMetric(step.band!.targetMin, step.band!.metric)}'
                '–${formatMetric(step.band!.targetMax, step.band!.metric)} '
                '(${step.band!.source.label}, ${step.band!.phase.label}) · '
                'dwell ${step.band!.dwellWarnMinutes}/${step.band!.dwellCritMinutes}',
                style: theme.textTheme.labelSmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ),
        ],
      ),
    );
  }
}
