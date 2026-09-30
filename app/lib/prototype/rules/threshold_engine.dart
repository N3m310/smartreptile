/// The threshold evaluation engine: FR-11 (`BR-11.1`…`BR-11.8`) plus the `DeviceSilent` watchdog (BR-07.2).
///
/// This is a **faithful prototyped port of the decision procedure in `03-implementation/06` §4.2**, written so a
/// reviewer can read every decision it makes. It is not the production engine: the real one runs server-side only
/// (BR-11, ADR-005) and owns `EvaluationState` rows in SQL Server. What this file buys is that the rules can be
/// argued about *before* an excursion needs a gecko to produce it.
///
/// Three properties the design asks for and this implementation preserves deliberately:
///  1. `TriggeredAt` is **back-dated** to the first out-of-band sample, so an alert says "out of range for 9 min"
///     rather than "4 min";
///  2. dwell is a **filter, not a delay** — a four-minute spike produces nothing at all;
///  3. **silence is never safety** — a device that stops talking produces a `DeviceSilent` alert and no
///     metric alerts, because there is no data to evaluate.
library;

import 'domain.dart';
import 'events.dart';
import 'notification_policy.dart';
import 'silence.dart';
import 'species_profiles.dart';

/// Tells the engine to watch for a silent device between the last sample and [until].
///
/// Silence is a property of *time*, not of a sample, so it cannot be detected by looking at samples alone: the
/// watchdog needs the range over which no sample arrived. Scenario D in the Rule Lab exists to make that visible.
class SilenceWatch {
  /// Creates a watch.
  const SilenceWatch({
    required this.firstSampleAt,
    required this.lastSampleAt,
    required this.until,
    this.samplingInterval = const Duration(seconds: 60),
  });

  /// First sample of the run; the minute grid starts here.
  final DateTime firstSampleAt;

  /// Most recent sample seen; silence is measured from here.
  final DateTime lastSampleAt;

  /// Instant the watchdog stops observing.
  final DateTime until;

  /// Expected sampling interval.
  final Duration samplingInterval;
}

/// The engine. Stateless between runs: all mutable state lives in a local map, so two scenarios cannot leak into
/// each other.
class ThresholdEngine {
  /// Creates an engine.
  const ThresholdEngine({
    this.config = EngineConfig.standard,
    this.cooldownMinutes = 60,
  });

  /// Engine parameters (dwell, recovery, silence, rate limit). Editable in the Rule Lab.
  final EngineConfig config;

  /// Minimum minutes between repeats for a still-open alert (`02-design/05` §5).
  final int cooldownMinutes;

  /// Notification policy applied to every alert event.
  ///
  /// Derived from [config] rather than injected, because the hourly cap previously existed in two places with two
  /// different values — `EngineConfig.rateCapPerHour` and `NotificationPolicy.rateCapPerHour` — and a test that set
  /// one of them silently kept the other. One owner, or the rule is a coin toss.
  NotificationPolicy get policy => NotificationPolicy(
    rateCapPerHour: config.rateCapPerHour,
    cooldownMinutes: cooldownMinutes,
  );

  /// Runs the engine over a series of samples.
  ///
  /// [samples] may arrive in any order and may contain duplicates of the same metric at the same instant (a
  /// back-fill overwriting a previous reading); the engine sorts by `recordedAt` and evaluates in that order, which
  /// is what rule V-08 requires.
  EngineResult run({
    required List<MetricSample> samples,
    required ThresholdResolver resolver,
    required PhaseTimeline timeline,
    required String terrariumName,
    String terrariumId = 'demo',
    Duration samplingInterval = const Duration(seconds: 60),
    DateTime? silenceWatchUntil,
    bool deviceInMaintenance = false,
    SilenceWindow? silence,
    NotificationPreferences preferences = const NotificationPreferences(),
    int startingAlertId = 1,
    bool includeSurfaceMetric = true,
  }) {
    final steps = <EvaluationStep>[];
    final alerts = <AlertEpisode>[];
    final notifications = <NotificationRecord>[];
    final states = <String, _MetricState>{};
    final sentPerHour = <int, int>{};
    final rolloverNoted = <String>{};
    // The phase each metric was last evaluated in. It has to live outside `states`, because `states` is keyed by
    // (metric, phase) — the very keying that makes a rollover invisible from the inside.
    final lastPhaseByMetric = <String, Phase>{};
    var counters = const EngineCounters();
    var nextAlertId = startingAlertId;
    AlertEpisode? silentAlert;

    final ordered = [...samples]
      ..sort((a, b) {
        final byTime = a.recordedAt.compareTo(b.recordedAt);
        return byTime != 0 ? byTime : a.metric.compareTo(b.metric);
      });

    // ---- timeline ---------------------------------------------------------------------------------------------
    // Samples and silence ticks are merged into one ordered walk so that "the device went quiet at 14:00 and spoke
    // again at 15:10" is evaluated in the order it happened, not in the order the data arrived.
    final timelineEvents = <_TimelineEvent>[
      for (final sample in ordered) _TimelineEvent.sample(sample),
    ];
    final lastSampleAt = ordered.isEmpty ? null : ordered.last.recordedAt;
    final gridStart = ordered.isEmpty
        ? null
        : ordered.first.recordedAt.add(samplingInterval);
    final gridEnd = silenceWatchUntil ?? lastSampleAt;

    if (gridStart != null && gridEnd != null) {
      for (
        var tick = gridStart;
        !tick.isAfter(gridEnd);
        tick = tick.add(samplingInterval)
      ) {
        timelineEvents.add(_TimelineEvent.tick(tick));
      }
    }

    timelineEvents.sort((a, b) {
      final byTime = a.at.compareTo(b.at);
      if (byTime != 0) {
        return byTime;
      }
      // A sample and a tick at the same instant: the sample wins, so the watchdog sees the fresh data first.
      return (a.sample == null ? 1 : 0).compareTo(b.sample == null ? 1 : 0);
    });

    DateTime? lastSeenSampleAt;

    for (final event in timelineEvents) {
      final sample = event.sample;

      if (sample != null) {
        lastSeenSampleAt = sample.recordedAt;

        // The device is talking again: close any silence alert before evaluating the new data.
        if (silentAlert != null && silentAlert.isOpen) {
          silentAlert
            ..state = AlertState.resolved
            ..resolvedAt = sample.recordedAt
            ..resolvedReason = ResolvedReason.recovered
            ..lastObservedAt = sample.recordedAt;
          counters = counters.withIncrements(resolutions: 1);
          steps.add(
            EvaluationStep(
              at: sample.recordedAt,
              kind: DecisionKind.deviceSilentCleared,
              ruleId: 'BR-07.2',
              explanation: 'The device is reporting again, so the silence alert resolved automatically.',
              alertId: silentAlert.id,
            ),
          );
          _dispatch(
            alert: silentAlert,
            severity: silentAlert.severity,
            band: null,
            value: null,
            isRecovery: true,
            canNotify: true,
            terrariumName: terrariumName,
            silence: silence,
            deviceInMaintenance: deviceInMaintenance,
            preferences: preferences,
            sentPerHour: sentPerHour,
            notifications: notifications,
            steps: steps,
            onSent: () =>
                counters = counters.withIncrements(notificationsSent: 1),
            onSuppressed: () =>
                counters = counters.withIncrements(notificationsSuppressed: 1),
          );
          silentAlert = null;
        }

        counters = counters.withIncrements(samplesSeen: 1);
        if (sample.isLateBackfill) {
          counters = counters.withIncrements(backfilledSamples: 1);
        }

        final outcome = _evaluateSample(
          sample: sample,
          resolver: resolver,
          terrariumId: terrariumId,
          states: states,
          alerts: alerts,
          steps: steps,
          nextAlertId: () => nextAlertId++,
          rolloverNoted: rolloverNoted,
          lastPhaseByMetric: lastPhaseByMetric,
          counters: counters,
        );
        counters = outcome.counters;

        // Notifications for open/escalate/resolve are dispatched here, where the policy and the hourly counters
        // live. The evaluator only reports which events happened.
        for (final evt in outcome.events) {
          _dispatch(
            alert: evt.alert,
            severity: evt.alert.severity,
            band: evt.alert.bandAtOpen,
            value: sample.value,
            isRecovery: evt.kind == DecisionKind.resolved,
            canNotify: !sample.isLateBackfill,
            terrariumName: terrariumName,
            silence: silence,
            deviceInMaintenance: deviceInMaintenance,
            preferences: preferences,
            sentPerHour: sentPerHour,
            notifications: notifications,
            steps: steps,
            onSent: () =>
                counters = counters.withIncrements(notificationsSent: 1),
            onSuppressed: () =>
                counters = counters.withIncrements(notificationsSuppressed: 1),
          );
        }
        continue;
      }

      // ---- silence watchdog -----------------------------------------------------------------------------------
      final now = event.at;
      if (lastSeenSampleAt == null ||
          now.difference(lastSeenSampleAt) <=
              samplingInterval * config.silenceWarnFactor) {
        continue;
      }

      final silentFor = now.difference(lastSeenSampleAt);
      final isCritical = silentFor >= config.silenceCriticalAfter;

      if (silentAlert == null) {
        final triggeredAt = lastSeenSampleAt.add(
          samplingInterval * config.silenceWarnFactor,
        );
        silentAlert = AlertEpisode(
          id: nextAlertId++,
          metric: null,
          severity: Severity.warning,
          source: AlertSource.deviceSilent,
          triggeredAt: triggeredAt,
          phase: Phase.any,
          bandAtOpen: null,
          terrariumId: terrariumId,
          lastObservedAt: now,
        );
        alerts.add(silentAlert);
        counters = counters.withIncrements(warningsOpened: 1);
        steps.add(
          EvaluationStep(
            at: now,
            kind: DecisionKind.deviceSilent,
            ruleId: 'BR-07.2',
            explanation:
                'No reading for ${silentFor.inMinutes} min '
                '(${config.silenceWarnFactor}× the 1-minute interval). '
                'Metric alerts are paused: there is no data to evaluate, and a quiet screen must not look safe.',
            alertId: silentAlert.id,
          ),
        );
        _dispatch(
          alert: silentAlert,
          severity: Severity.warning,
          band: null,
          value: null,
          isRecovery: false,
          canNotify: true,
          terrariumName: terrariumName,
          silence: silence,
          deviceInMaintenance: deviceInMaintenance,
          preferences: preferences,
          sentPerHour: sentPerHour,
          notifications: notifications,
          steps: steps,
          onSent: () =>
              counters = counters.withIncrements(notificationsSent: 1),
          onSuppressed: () =>
              counters = counters.withIncrements(notificationsSuppressed: 1),
        );
      } else if (isCritical && silentAlert.severity == Severity.warning) {
        silentAlert
          ..severity = Severity.critical
          ..lastObservedAt = now;
        counters = counters.withIncrements(escalations: 1);
        steps.add(
          EvaluationStep(
            at: now,
            kind: DecisionKind.deviceSilent,
            ruleId: 'BR-07.2',
            explanation:
                'The device has been silent for ${silentFor.inMinutes} min, so the alert escalated to Critical.',
            alertId: silentAlert.id,
          ),
        );
        _dispatch(
          alert: silentAlert,
          severity: Severity.critical,
          band: null,
          value: null,
          isRecovery: false,
          canNotify: true,
          terrariumName: terrariumName,
          silence: silence,
          deviceInMaintenance: deviceInMaintenance,
          preferences: preferences,
          sentPerHour: sentPerHour,
          notifications: notifications,
          steps: steps,
          onSent: () =>
              counters = counters.withIncrements(notificationsSent: 1),
          onSuppressed: () =>
              counters = counters.withIncrements(notificationsSuppressed: 1),
        );
      } else {
        silentAlert.lastObservedAt = now;
      }
    }

    return EngineResult(
      steps: steps,
      alerts: alerts,
      notifications: notifications,
      counters: counters,
    );
  }

  // -----------------------------------------------------------------------------------------------------------
  // Sample evaluation (the §4.2 decision procedure)
  // -----------------------------------------------------------------------------------------------------------

  _SampleOutcome _evaluateSample({
    required MetricSample sample,
    required ThresholdResolver resolver,
    required String terrariumId,
    required Map<String, _MetricState> states,
    required List<AlertEpisode> alerts,
    required List<EvaluationStep> steps,
    required int Function() nextAlertId,
    required Set<String> rolloverNoted,
    required Map<String, Phase> lastPhaseByMetric,
    required EngineCounters counters,
  }) {
    final definition = MetricCatalog.byCode(sample.metric);
    var nextCounters = counters;

    // BR-11.1 — a faulted or implausible reading is stored, never evaluated. Scenario K.
    if (!sample.isEvaluable) {
      steps.add(
        EvaluationStep(
          at: sample.recordedAt,
          kind: DecisionKind.skippedNotEvaluable,
          ruleId: 'BR-11.1',
          explanation: sample.isImplausible
              ? '${definition?.displayName ?? sample.metric} read '
                    '${sample.value.toStringAsFixed(1)}${definition?.unit ?? ''}, which is outside the '
                    'plausible range. Stored for calibration review, excluded from evaluation.'
              : 'The sensor reported a read failure, so this reading is ignored and the metric is shown as '
                    'unavailable rather than "fine".',
          metric: sample.metric,
          value: sample.value,
        ),
      );
      return _SampleOutcome(
        counters: nextCounters.withIncrements(samplesSkipped: 1),
      );
    }

    final resolvedBand = resolver
        .resolve(sample.metric, sample.recordedAt)
        .band;

    // The Lab may override the dwell for the whole run, because the documents classify dwell as an engine parameter
    // rather than a property of the species (FR-11).
    final band = resolvedBand == null
        ? null
        : (config.dwellWarnMinutesOverride == null &&
                  config.dwellCritMinutesOverride == null
              ? resolvedBand
              : resolvedBand.copyWith(
                  dwellWarnMinutes: config.dwellWarnMinutesOverride,
                  dwellCritMinutes: config.dwellCritMinutesOverride,
                ));

    // A metric the species does not measure, or an inert accumulated band, has no per-sample decision to make.
    if (band == null) {
      steps.add(
        EvaluationStep(
          at: sample.recordedAt,
          kind: DecisionKind.skippedNoBand,
          ruleId: 'BR-11.2',
          explanation:
              '${definition?.displayName ?? sample.metric} has no band for this profile, '
              'so nothing was evaluated. The app says "not measured" instead of inventing a limit.',
          metric: sample.metric,
          value: sample.value,
        ),
      );
      return _SampleOutcome(
        counters: nextCounters.withIncrements(samplesSkipped: 1),
      );
    }

    if (band.accumulatedOnly) {
      steps.add(
        EvaluationStep(
          at: sample.recordedAt,
          kind: DecisionKind.skippedNoBand,
          ruleId: 'BR-11.3',
          explanation:
              'Light is judged on accumulated hours, not per sample: a passing cloud is not an incident. '
              'The daily summary carries the light hours and any deficit.',
          metric: sample.metric,
          value: sample.value,
          phase: band.phase,
          band: band,
        ),
      );
      return _SampleOutcome(
        counters: nextCounters.withIncrements(samplesSkipped: 1),
      );
    }

    final stateKey = '${sample.metric}|${band.phase.label}';
    final state = states.putIfAbsent(stateKey, _MetricState.new);

    // A phase rollover while an alert is open is a genuine hole in the specification: the state machine is keyed by
    // (metric, phase), and no document says what happens to the alert that was opened under the other phase. The
    // engine refuses to invent an answer, and says so in the trace instead.
    final previousPhase = lastPhaseByMetric[sample.metric];
    if (previousPhase != null && previousPhase != band.phase) {
      final openUnderOtherPhase = alerts.any(
        (alert) =>
            alert.isOpen &&
            alert.metric == sample.metric &&
            alert.phase != band.phase,
      );
      if (openUnderOtherPhase && rolloverNoted.add(sample.metric)) {
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.inBand,
            ruleId: 'OPEN-QUESTION',
            explanation:
                'The phase changed from ${previousPhase.label} to ${band.phase.label} while an alert from the '
                'previous phase is still open. The documents do not define whether it resolves here — so the '
                'prototype leaves it open and this note exists to get a decision.',
            metric: sample.metric,
            value: sample.value,
            phase: band.phase,
            band: band,
          ),
        );
      }
    }
    lastPhaseByMetric[sample.metric] = band.phase;

    final value = sample.value;
    final hot = band.isHot(value);
    final cold = band.isCold(value);
    final critical = band.isCritical(value);
    final canNotify = !sample.isLateBackfill;
    final events = <_AlertEvent>[];

    if (hot || cold) {
      if (state.violation == Violation.none) {
        state.firstOutOfBandAt = sample.recordedAt;
      }
      state.violation = hot ? Violation.hot : Violation.cold;
      state.consecutiveRecoveryMinutes = 0;

      if (critical) {
        state.criticalSinceAt ??= sample.recordedAt;
      } else if (config.resetCriticalTimerWhenBackInsideBand) {
        state.criticalSinceAt = null;
      }

      final firstOut = state.firstOutOfBandAt ?? sample.recordedAt;
      final sustained =
          sample.recordedAt.difference(firstOut).inMinutes >=
          band.dwellWarnMinutes;
      final criticalSince = state.criticalSinceAt;
      final critSustained =
          critical &&
          criticalSince != null &&
          sample.recordedAt.difference(criticalSince).inMinutes >=
              band.dwellCritMinutes;

      final openAlert = _findOpenAlert(alerts, state.openAlertId);

      if (sustained && openAlert == null) {
        // One alert row per excursion, with TriggeredAt back-dated to the first out-of-band sample.
        final alert = AlertEpisode(
          id: nextAlertId(),
          metric: sample.metric,
          severity: Severity.warning,
          source: AlertSource.threshold,
          triggeredAt: firstOut,
          phase: band.phase,
          bandAtOpen: band,
          terrariumId: terrariumId,
          lastObservedAt: sample.recordedAt,
          peakValue: value,
        );
        alerts.add(alert);
        state.openAlertId = alert.id;
        nextCounters = nextCounters.withIncrements(warningsOpened: 1);
        events.add(_AlertEvent(kind: DecisionKind.openedWarning, alert: alert));
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.openedWarning,
            ruleId: 'BR-11.3',
            explanation:
                '${definition?.displayName ?? sample.metric} has been ${hot ? 'above' : 'below'} the target band '
                'for ${sample.recordedAt.difference(firstOut).inMinutes} min '
                '(the limit is ${band.dwellWarnMinutes} min). One Warning opened, timed from '
                '${_hhmm(firstOut)} — the start of the excursion, not now.',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: Violation.none,
            stateAfter: state.violation,
            alertId: alert.id,
          ),
        );
      } else if (critSustained &&
          openAlert != null &&
          openAlert.severity.rank < Severity.critical.rank) {
        openAlert
          ..severity = Severity.critical
          ..lastObservedAt = sample.recordedAt;
        if (value.abs() > (openAlert.peakValue ?? value).abs()) {
          openAlert.peakValue = value;
        }
        nextCounters = nextCounters.withIncrements(escalations: 1);
        events.add(_AlertEvent(kind: DecisionKind.escalated, alert: openAlert));
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.escalated,
            ruleId: 'BR-11.6',
            explanation:
                '${definition?.displayName ?? sample.metric} left the critical band '
                '(${_trim(band.criticalMin)}–${_trim(band.criticalMax)}${definition?.unit ?? ''}) for '
                '${sample.recordedAt.difference(criticalSince).inMinutes} min. The **same** alert was upgraded — '
                'one incident, one row, one new notification.',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: state.violation,
            stateAfter: state.violation,
            alertId: openAlert.id,
          ),
        );
      } else if (openAlert != null) {
        // BR-11.5 — a repeated excursion extends the same alert rather than opening a second one.
        openAlert.lastObservedAt = sample.recordedAt;
        if (value.abs() > (openAlert.peakValue ?? value).abs()) {
          openAlert.peakValue = value;
        }
        if (canNotify) {
          steps.add(
            EvaluationStep(
              at: sample.recordedAt,
              kind: DecisionKind.touched,
              ruleId: 'BR-11.5',
              explanation:
                  'Still out of band. The open alert was extended rather than duplicated '
                  '(dedupe key: metric + severity + phase); peak now '
                  '${openAlert.peakValue?.toStringAsFixed(1) ?? '—'}.',
              metric: sample.metric,
              value: value,
              phase: band.phase,
              band: band,
              stateBefore: state.violation,
              stateAfter: state.violation,
              alertId: openAlert.id,
            ),
          );
        }
      } else {
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.belowDwell,
            ruleId: 'BR-11.3',
            explanation:
                'Out of band for ${sample.recordedAt.difference(firstOut).inMinutes} min — '
                'shorter than the ${band.dwellWarnMinutes} min dwell, so **no alert is created**. '
                'This is the rule that lets a keeper open the lid for four minutes.',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: Violation.none,
            stateAfter: state.violation,
            recoveryTicks: 0,
          ),
        );
      }
    } else {
      // Back inside the target band: count consecutive recovery minutes (BR-11.4).
      final recovered = _isInsideByMargin(band, value, state.violation);
      if (recovered) {
        state.consecutiveRecoveryMinutes++;
      } else {
        state.consecutiveRecoveryMinutes = 0;
      }

      final openAlert = _findOpenAlert(alerts, state.openAlertId);
      final ticks = state.consecutiveRecoveryMinutes;

      if (ticks >= config.recoveryMinutes && openAlert != null) {
        openAlert
          ..state = AlertState.resolved
          ..resolvedAt = sample.recordedAt
          ..resolvedReason = ResolvedReason.recovered
          ..lastObservedAt = sample.recordedAt;
        state.violation = Violation.none;
        state.criticalSinceAt = null;
        state.openAlertId = null;
        state.firstOutOfBandAt = null;
        nextCounters = nextCounters.withIncrements(resolutions: 1);
        events.add(_AlertEvent(kind: DecisionKind.resolved, alert: openAlert));
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.resolved,
            ruleId: 'BR-11.4',
            explanation:
                'Back inside the band by the recovery margin '
                '(${_trim(band.recoveryMargin)}${definition?.unit ?? ''}) for $ticks consecutive minutes, so the '
                'alert resolved. The margin is what stops a value hovering on the boundary from flapping.',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: Violation.hot,
            stateAfter: Violation.none,
            recoveryTicks: ticks,
            alertId: openAlert.id,
          ),
        );
      } else if (!recovered) {
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: DecisionKind.recoveryReset,
            ruleId: 'BR-11.4',
            explanation:
                'Inside the target band but not yet by the full recovery margin — the recovery counter went back '
                'to zero. Hysteresis, not pedantry: without it an alert would open and close all afternoon.',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: state.violation,
            stateAfter: state.violation,
            recoveryTicks: 0,
            alertId: openAlert?.id,
          ),
        );
      } else {
        final remaining = config.recoveryMinutes - ticks;
        steps.add(
          EvaluationStep(
            at: sample.recordedAt,
            kind: openAlert == null
                ? DecisionKind.inBand
                : DecisionKind.recoveryTick,
            ruleId: openAlert == null ? 'BR-11.3' : 'BR-11.4',
            explanation: openAlert == null
                ? 'Inside the target band. Nothing to do.'
                : 'Recovering: $ticks of ${config.recoveryMinutes} consecutive minutes inside the band '
                      '($remaining to go).',
            metric: sample.metric,
            value: value,
            phase: band.phase,
            band: band,
            stateBefore: state.violation,
            stateAfter: state.violation,
            recoveryTicks: ticks,
            alertId: openAlert?.id,
          ),
        );
      }
    }

    return _SampleOutcome(
      counters: nextCounters.withIncrements(samplesEvaluated: 1),
      events: events,
    );
  }

  /// BR-11.4 — "back inside the band by the recovery margin", interpreted per [EngineConfig.recoveryMode].
  bool _isInsideByMargin(
    ThresholdBand band,
    double value,
    Violation violation,
  ) {
    final margin = band.recoveryMargin;
    switch (config.recoveryMode) {
      case RecoveryMode.plainInBand:
        return band.isInTargetBand(value);
      case RecoveryMode.directionAware:
        return switch (violation) {
          Violation.hot => value <= band.targetMax - margin,
          Violation.cold => value >= band.targetMin + margin,
          Violation.none =>
            value >= band.targetMin + margin &&
                value <= band.targetMax - margin,
        };
      case RecoveryMode.bothSides:
        return value >= band.targetMin + margin &&
            value <= band.targetMax - margin;
    }
  }

  AlertEpisode? _findOpenAlert(List<AlertEpisode> alerts, int? id) {
    if (id == null) {
      return null;
    }
    for (final alert in alerts) {
      if (alert.id == id && alert.isOpen) {
        return alert;
      }
    }
    return null;
  }

  // -----------------------------------------------------------------------------------------------------------
  // Notification dispatch
  // -----------------------------------------------------------------------------------------------------------

  void _dispatch({
    required AlertEpisode alert,
    required Severity severity,
    required ThresholdBand? band,
    required double? value,
    required bool isRecovery,
    required bool canNotify,
    required String terrariumName,
    required SilenceWindow? silence,
    required bool deviceInMaintenance,
    required NotificationPreferences preferences,
    required Map<int, int> sentPerHour,
    required List<NotificationRecord> notifications,
    required List<EvaluationStep> steps,
    required void Function() onSent,
    required void Function() onSuppressed,
  }) {
    final at = alert.resolvedAt ?? alert.lastObservedAt ?? alert.triggeredAt;

    // BR-11.8 — a back-filled excursion older than the window is recorded but never wakes anyone.
    if (!canNotify) {
      steps.add(
        EvaluationStep(
          at: at,
          kind: DecisionKind.backfillRecordedOnly,
          ruleId: 'BR-11.8',
          explanation:
              'This excursion arrived with a late back-fill, so it is recorded in the history but no notification '
              'is sent. Otherwise a device reconnecting after a day away would fire a burst of stale alarms.',
          metric: alert.metric,
          value: value,
          alertId: alert.id,
        ),
      );
      return;
    }

    final silenceCovers = silence?.covers(alert.metric, at) ?? false;
    final hourKey = _hourIndex(at);
    final sentThisHour = sentPerHour[hourKey] ?? 0;

    final decision = policy.evaluate(
      severity: severity,
      localTime: at,
      preferences: preferences,
      isRecovery: isRecovery,
      silencedMetric: silenceCovers,
      deviceInMaintenance: deviceInMaintenance,
      sentInCurrentHour: sentThisHour,
    );

    final definition = alert.metric == null
        ? null
        : MetricCatalog.byCode(alert.metric!);
    final metricName = definition?.displayName ?? alert.source.label;
    final title = NotificationPolicy.title(
      terrariumName: terrariumName,
      metricName: metricName,
      isHot: (alert.peakValue ?? value ?? 0) >= (band?.targetMax ?? 0),
      isCritical: severity == Severity.critical,
    );
    final body = band == null
        ? '${alert.source.label} — the device stopped reporting. Metrics are paused, not healthy.'
        : NotificationPolicy.body(
            value: value ?? alert.peakValue ?? 0,
            unit: definition?.unit ?? '',
            targetMin: band.targetMin,
            targetMax: band.targetMax,
            minutesOutOfRange: at.difference(alert.triggeredAt).inMinutes,
            isRecovery: isRecovery,
            criticalMax: band.criticalMax,
            criticalMin: band.criticalMin,
          );

    if (!decision.send) {
      notifications.add(
        NotificationRecord(
          at: at,
          alertId: alert.id,
          severity: severity,
          channel: Channel.inApp,
          outcome: decision.reason,
          title: title,
          body: body,
        ),
      );
      onSuppressed();
      steps.add(
        EvaluationStep(
          at: at,
          kind: DecisionKind.notificationSuppressed,
          ruleId: decision.ruleId,
          explanation: 'No notification sent: ${decision.detail}',
          metric: alert.metric,
          value: value,
          suppressedReason: decision.reason,
          alertId: alert.id,
        ),
      );
      return;
    }

    var isFirst = true;
    for (final channel in decision.channels) {
      notifications.add(
        NotificationRecord(
          at: at,
          alertId: alert.id,
          severity: severity,
          channel: channel,
          outcome: SuppressedReason.none,
          title: title,
          body: body,
        ),
      );
      if (isFirst) {
        isFirst = false;
        onSent();
        steps.add(
          EvaluationStep(
            at: at,
            kind: DecisionKind.notificationSent,
            ruleId: decision.ruleId,
            explanation: 'Notified: ${decision.detail}',
            metric: alert.metric,
            value: value,
            channel: channel,
            alertId: alert.id,
          ),
        );
      }
    }
    sentPerHour[hourKey] = sentThisHour + 1;
  }

  static int _hourIndex(DateTime at) => at.millisecondsSinceEpoch ~/ 3600000;

  static String _hhmm(DateTime at) =>
      '${at.hour.toString().padLeft(2, '0')}:${at.minute.toString().padLeft(2, '0')}';

  static String _trim(double value) => value == value.roundToDouble()
      ? value.toStringAsFixed(0)
      : value.toStringAsFixed(1);
}

/// Mutable per-`(metric, phase)` evaluation state (§4.1).
class _MetricState {
  Violation violation = Violation.none;
  DateTime? firstOutOfBandAt;
  DateTime? criticalSinceAt;
  int consecutiveRecoveryMinutes = 0;
  int? openAlertId;
}

/// One entry in the merged sample/silence timeline.
class _TimelineEvent {
  _TimelineEvent.sample(MetricSample sample)
    : sample = sample,
      at = sample.recordedAt;
  _TimelineEvent.tick(this.at) : sample = null;

  final DateTime at;
  final MetricSample? sample;
}

/// An alert event the evaluator produced and the dispatcher must notify about.
class _AlertEvent {
  const _AlertEvent({required this.kind, required this.alert});

  final DecisionKind kind;
  final AlertEpisode alert;
}

/// What one sample evaluation produced.
class _SampleOutcome {
  const _SampleOutcome({required this.counters, this.events = const []});

  /// Counters after this sample was evaluated.
  final EngineCounters counters;

  /// Alert events the caller must run through the notification policy.
  final List<_AlertEvent> events;
}
