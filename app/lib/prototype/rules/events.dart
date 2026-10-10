/// The events the rule kernel produces: decision-trace steps, alert episodes, notifications and the run result.
///
/// The prototype renders these directly. That is the whole design: instead of a screen that *implies* the rules,
/// every evaluation produces a [EvaluationStep] carrying the rule id (`BR-11.3`) and a sentence in the keeper's
/// language, so a reviewer can read the reasoning instead of trusting the colour of a card.
library;

import 'domain.dart';

/// What the evaluator decided on one sample for one metric.
enum DecisionKind {
  /// Inside the band, nothing open: the quiet case.
  inBand('in band'),

  /// Outside the target band but the dwell timer has not expired yet — the case that keeps a keeper asleep.
  belowDwell('below dwell, no alert'),

  /// The dwell timer expired: one Warning alert was opened.
  openedWarning('opened Warning'),

  /// An open alert had its `LastObservedAt` and `PeakValue` updated (BR-11.5).
  touched('extended existing alert'),

  /// The critical dwell expired while a Warning was open: the same alert became Critical (BR-11.6).
  escalated('escalated to Critical'),

  /// One consecutive recovery minute was counted (BR-11.4).
  recoveryTick('recovery tick'),

  /// The recovery margin was held long enough: the alert resolved (BR-11.4).
  resolved('resolved'),

  /// Still out of band but not recoverable yet: the recovery counter was reset.
  recoveryReset('recovery counter reset'),

  /// The reading was flagged implausible or faulted, so it was stored but not evaluated (BR-11.1).
  skippedNotEvaluable('skipped: flagged reading'),

  /// No band applies to this metric/phase, e.g. UV without the LTR390 fitted.
  skippedNoBand('skipped: metric not measured'),

  /// Evaluated for the record but past the notification window (BR-11.8).
  backfillRecordedOnly('back-filled: recorded, not notified'),

  /// A `DeviceSilent` alert was opened or escalated instead of metric alerts (BR-07.2).
  deviceSilent('device silent'),

  /// A `DeviceSilent` alert auto-resolved because samples arrived again.
  deviceSilentCleared('device speaking again'),

  /// An alert was created but the notification was suppressed by policy (`02-design/05` §4).
  notificationSuppressed('notification suppressed'),

  /// A notification was sent.
  notificationSent('notification sent');

  const DecisionKind(this.label);

  /// Human-readable label for the trace table.
  final String label;

  /// True when this step is a notification-policy outcome rather than a threshold decision.
  bool get isNotificationStep =>
      this == DecisionKind.notificationSent ||
      this == DecisionKind.notificationSuppressed;
}

/// One line of the decision trace: everything the evaluator knew and everything it did.
class EvaluationStep {
  /// Creates a step.
  const EvaluationStep({
    required this.at,
    required this.kind,
    required this.ruleId,
    required this.explanation,
    this.metric,
    this.value,
    this.phase,
    this.band,
    this.stateBefore = Violation.none,
    this.stateAfter = Violation.none,
    this.recoveryTicks,
    this.alertId,
    this.suppressedReason,
    this.channel,
  });

  /// Local time of the sample this step evaluated.
  final DateTime at;

  /// What was decided.
  final DecisionKind kind;

  /// The rule that produced the decision, e.g. `BR-11.3`. Every step has one: a decision with no rule id would be
  /// an invention, and the prototype exists to stop those.
  final String ruleId;

  /// The decision in one sentence, phrased for a keeper (no "dwell", no "hysteresis" — `02-design/05` §7).
  final String explanation;

  /// Metric evaluated, or null for device-level steps.
  final String? metric;

  /// Observed value, or null for device-level steps.
  final double? value;

  /// Phase in force for this sample.
  final Phase? phase;

  /// The effective band used, after override → profile → default resolution (BR-10.3).
  final ThresholdBand? band;

  /// Violation state before this step.
  final Violation stateBefore;

  /// Violation state after this step.
  final Violation stateAfter;

  /// Recovery minutes accumulated after this step, when the step touched the recovery counter.
  final int? recoveryTicks;

  /// Alert this step acted on, when it acted on one.
  final int? alertId;

  /// Suppression reason, when the step is a suppressed notification.
  final SuppressedReason? suppressedReason;

  /// Channel a notification went to, when the step sent one.
  final Channel? channel;

  /// True when the step changed something a reviewer should look at (opened/escalated/resolved/sent).
  bool get isNoteworthy =>
      kind == DecisionKind.openedWarning ||
      kind == DecisionKind.escalated ||
      kind == DecisionKind.resolved ||
      kind == DecisionKind.deviceSilent ||
      kind == DecisionKind.deviceSilentCleared ||
      kind == DecisionKind.notificationSent;
}

/// Notification channels (`02-design/05` §3).
enum Channel {
  /// In-app inbox — always on, the audit trail.
  inApp('In-app'),

  /// FCM push (Android).
  push('Push'),

  /// SMTP email.
  email('Email');

  const Channel(this.label);

  /// Display label.
  final String label;
}

/// Why a notification was not sent (`02-design/05` §4). Every suppression is recorded, never silent.
enum SuppressedReason {
  /// Nothing suppressed.
  none('sent'),

  /// Inside the user's quiet hours.
  quietHours('quiet hours'),

  /// The metric is inside an active silence window (BR-12.6).
  silencedMetric('metric silenced'),

  /// The device is in maintenance mode.
  maintenance('device in maintenance'),

  /// Below the user's minimum severity.
  belowMinSeverity('below minimum severity'),

  /// The hourly cap was exceeded, so the event went into the digest.
  digestCoalesced('coalesced into digest'),

  /// The user opted out (e.g. no recovery notices).
  preference('preference'),

  /// No channel is enabled for this user.
  noChannel('no channel enabled');

  const SuppressedReason(this.label);

  /// Display label.
  final String label;

  /// True when a notification was actually delivered for at least one channel.
  bool get isSent => this == SuppressedReason.none;
}

/// One attempt to notify, logged per channel and per attempt (BR-13.4).
class NotificationRecord {
  /// Creates a record.
  const NotificationRecord({
    required this.at,
    required this.alertId,
    required this.severity,
    required this.channel,
    required this.outcome,
    this.attempt = 1,
    this.title,
    this.body,
  });

  /// When the attempt was made.
  final DateTime at;

  /// Alert the notification is about.
  final int alertId;

  /// Severity at the time of the attempt.
  final Severity severity;

  /// Channel attempted.
  final Channel channel;

  /// Outcome: `none` means delivered, anything else is the suppression reason.
  final SuppressedReason outcome;

  /// Attempt number; FCM retries 3× with backoff (BR-13.5).
  final int attempt;

  /// Notification title, using the content contract in `02-design/05` §7.
  final String? title;

  /// Notification body, using the content contract in `02-design/05` §7.
  final String? body;

  /// True when this record represents a delivery.
  bool get isSent => outcome.isSent;
}

/// An alert episode: one row in the history, exactly one per excursion (BR-11.5/BR-12.1).
class AlertEpisode {
  /// Creates an episode.
  AlertEpisode({
    required this.id,
    required this.metric,
    required this.severity,
    required this.source,
    required this.triggeredAt,
    required this.phase,
    required this.bandAtOpen,
    this.terrariumId = 'demo',
    this.state = AlertState.open,
    this.lastObservedAt,
    this.peakValue,
    this.resolvedAt,
    this.resolvedReason,
    this.acknowledgedBy,
    this.acknowledgedAt,
    this.note,
  });

  /// Terrarium the alert belongs to. The engine is per-terrarium and simply stamps the id it was given; the client
  /// needs it to filter an inbox (`Alert.TerrariumId` in the real schema).
  final String terrariumId;

  /// Server-assigned id; the prototype uses the order of creation.
  final int id;

  /// Metric that triggered it, or null for a device-level alert.
  final String? metric;

  /// Current severity; escalation is one-way (BR-11.6).
  Severity severity;

  /// Source of the alert (`02-design/05` §2).
  final AlertSource source;

  /// Back-dated to the first out-of-band sample, not to the moment the dwell timer expired
  /// (`03-implementation/06` §4.2 note 1).
  final DateTime triggeredAt;

  /// Phase in force when the alert opened; part of the dedupe key.
  final Phase phase;

  /// The band that was in force when the alert opened. An open alert keeps its original band even if the
  /// thresholds are later edited (`03-implementation/06` §1).
  final ThresholdBand? bandAtOpen;

  /// Lifecycle state (BR-12.1).
  AlertState state;

  /// Last time the excursion was still observed (BR-11.5).
  DateTime? lastObservedAt;

  /// Most extreme value seen during the episode.
  double? peakValue;

  /// When it resolved.
  DateTime? resolvedAt;

  /// Why it resolved.
  ResolvedReason? resolvedReason;

  /// Who acknowledged it (BR-12.2).
  String? acknowledgedBy;

  /// When it was acknowledged (BR-12.2).
  DateTime? acknowledgedAt;

  /// Free-text note from a manual resolve.
  String? note;

  /// True while the episode still needs attention.
  bool get isOpen =>
      state == AlertState.open || state == AlertState.acknowledged;

  /// How long the excursion ran: to [resolvedAt] when resolved, otherwise to [lastObservedAt].
  Duration durationAsOf(DateTime now) {
    final end = resolvedAt ?? lastObservedAt ?? now;
    return end.difference(triggeredAt);
  }

  /// One-line summary for the inbox list.
  String get summary {
    final peak = peakValue == null
        ? ''
        : ', peak ${peakValue!.toStringAsFixed(1)}';
    return '${metric ?? source.label} · ${severity.label}$peak';
  }
}

/// Runtime configuration of the prototype engine. Defaults come from the FR text; every one of them is editable in
/// the Rule Lab, because "dwell is 5 minutes" is the kind of rule that is only defensible once someone has tried to
/// break it.
class EngineConfig {
  /// Creates a configuration.
  const EngineConfig({
    this.recoveryMinutes = 3,
    this.resetCriticalTimerWhenBackInsideBand = false,
    this.recoveryMode = RecoveryMode.directionAware,
    this.silenceWarnFactor = 3,
    this.silenceCriticalAfter = const Duration(minutes: 30),
    this.rateCapPerHour = 10,
    this.suppressRecoveryNotifications = false,
    this.dwellWarnMinutesOverride,
    this.dwellCritMinutesOverride,
  });

  /// Consecutive recovery minutes required to resolve (BR-11.4).
  final int recoveryMinutes;

  /// Whether leaving the critical band resets the critical dwell timer.
  ///
  /// The documented pseudocode never resets it (`03-implementation/06` §4.2), which means a critical excursion
  /// that dips briefly back inside the critical band re-escalates instantly. That is arguably right, arguably
  /// wrong — so the Lab exposes it instead of picking one silently.
  final bool resetCriticalTimerWhenBackInsideBand;

  /// How "back inside the band by the recovery margin" is interpreted.
  final RecoveryMode recoveryMode;

  /// Multiples of the sampling interval without data that trigger `DeviceSilent` (BR-07.2).
  final int silenceWarnFactor;

  /// Silence duration that escalates `DeviceSilent` to Critical (BR-07.2).
  final Duration silenceCriticalAfter;

  /// Notifications allowed per terrarium per hour (BR-13.3).
  final int rateCapPerHour;

  /// Whether recovery notifications are suppressed by default feel (`02-design/05` §4).
  final bool suppressRecoveryNotifications;

  /// Replaces every band's warning dwell for this run, or null to use the band's own value.
  ///
  /// Dwell is an **engine parameter** in the documents' own words — *"dwell and recovery margin are engine
  /// parameters (FR-11)"* — so the Rule Lab is allowed to change it without touching a species profile. That is what
  /// makes "is five minutes right?" a question a reviewer can answer by looking rather than by arguing.
  final int? dwellWarnMinutesOverride;

  /// Replaces every band's critical dwell for this run.
  final int? dwellCritMinutesOverride;

  /// Samples ingested more than this long after they were recorded never notify (BR-11.8).
  static const backfillNotifyWindow = RuleWindows.backfillNotifyWindow;

  /// Defaults used by every scenario unless it says otherwise.
  static const standard = EngineConfig();

  /// Returns a copy with the given fields replaced.
  EngineConfig copyWith({
    int? recoveryMinutes,
    bool? resetCriticalTimerWhenBackInsideBand,
    RecoveryMode? recoveryMode,
    int? silenceWarnFactor,
    Duration? silenceCriticalAfter,
    int? rateCapPerHour,
    bool? suppressRecoveryNotifications,
    int? dwellWarnMinutesOverride,
    int? dwellCritMinutesOverride,
    bool clearDwellOverrides = false,
  }) => EngineConfig(
    recoveryMinutes: recoveryMinutes ?? this.recoveryMinutes,
    resetCriticalTimerWhenBackInsideBand:
        resetCriticalTimerWhenBackInsideBand ??
        this.resetCriticalTimerWhenBackInsideBand,
    recoveryMode: recoveryMode ?? this.recoveryMode,
    silenceWarnFactor: silenceWarnFactor ?? this.silenceWarnFactor,
    silenceCriticalAfter: silenceCriticalAfter ?? this.silenceCriticalAfter,
    rateCapPerHour: rateCapPerHour ?? this.rateCapPerHour,
    suppressRecoveryNotifications:
        suppressRecoveryNotifications ?? this.suppressRecoveryNotifications,
    dwellWarnMinutesOverride: clearDwellOverrides
        ? null
        : (dwellWarnMinutesOverride ?? this.dwellWarnMinutesOverride),
    dwellCritMinutesOverride: clearDwellOverrides
        ? null
        : (dwellCritMinutesOverride ?? this.dwellCritMinutesOverride),
  );
}

/// Interpretation of the recovery margin (BR-11.4).
///
/// This enum is the prototype's most useful single control, because the documents are ambiguous here and the Lab
/// exists to make the ambiguity chooseable rather than hidden.
///
/// * [directionAware] is what `03-implementation/06` §4.2 literally says (`insideByAtLeast(band, value, margin)`) and
///   is the default.
/// * [plainInBand] counts any in-band reading as a recovery minute. It reproduces the worked Example B in the same
///   document exactly, which the literal reading does *not* — the two parts of the documentation contradict each
///   other, and scenario `B` says so in its own note.
/// * [bothSides] is the strictest reading: comfortably inside on both sides.
///
/// Whichever is chosen must be stated in the report, because it changes how long an alert stays open.
enum RecoveryMode {
  /// The margin is required on the side the excursion came from: after a hot excursion the value must fall below
  /// `targetMax − margin`. Standard hysteresis, and the literal reading of §4.2.
  directionAware('direction-aware (§4.2 literal)'),

  /// Any reading inside the target band counts. Reproduces the §3 Example B timeline.
  plainInBand('plain in band (§3 Example B)'),

  /// The value must sit inside the band by the margin on *both* sides. Stricter; it can hold an alert open while
  /// the value is already comfortably in range, which is why it is not a default.
  bothSides('both sides of the band');

  const RecoveryMode(this.label);

  /// Display label.
  final String label;
}

/// Counters a run produces; the diagnostics screen and the report quote these instead of estimating.
class EngineCounters {
  /// Creates counters.
  const EngineCounters({
    this.samplesSeen = 0,
    this.samplesEvaluated = 0,
    this.samplesSkipped = 0,
    this.backfilledSamples = 0,
    this.warningsOpened = 0,
    this.escalations = 0,
    this.resolutions = 0,
    this.notificationsSent = 0,
    this.notificationsSuppressed = 0,
  });

  /// Readings offered to the evaluator.
  final int samplesSeen;

  /// Readings actually evaluated against a band.
  final int samplesEvaluated;

  /// Readings skipped because of a quality flag or a missing band.
  final int samplesSkipped;

  /// Readings that arrived late enough to be notification-suppressed (BR-11.8).
  final int backfilledSamples;

  /// Warning alerts opened.
  final int warningsOpened;

  /// Warning → Critical escalations.
  final int escalations;

  /// Alerts resolved.
  final int resolutions;

  /// Notifications delivered.
  final int notificationsSent;

  /// Notifications suppressed, for any reason.
  final int notificationsSuppressed;

  /// Returns a copy with the given counters incremented.
  EngineCounters withIncrements({
    int samplesSeen = 0,
    int samplesEvaluated = 0,
    int samplesSkipped = 0,
    int backfilledSamples = 0,
    int warningsOpened = 0,
    int escalations = 0,
    int resolutions = 0,
    int notificationsSent = 0,
    int notificationsSuppressed = 0,
  }) => EngineCounters(
    samplesSeen: this.samplesSeen + samplesSeen,
    samplesEvaluated: this.samplesEvaluated + samplesEvaluated,
    samplesSkipped: this.samplesSkipped + samplesSkipped,
    backfilledSamples: this.backfilledSamples + backfilledSamples,
    warningsOpened: this.warningsOpened + warningsOpened,
    escalations: this.escalations + escalations,
    resolutions: this.resolutions + resolutions,
    notificationsSent: this.notificationsSent + notificationsSent,
    notificationsSuppressed:
        this.notificationsSuppressed + notificationsSuppressed,
  );

  @override
  String toString() =>
      'evaluated $samplesEvaluated, skipped $samplesSkipped, '
      'opened $warningsOpened, escalated $escalations, resolved $resolutions, '
      'notified $notificationsSent, suppressed $notificationsSuppressed';
}

/// Everything one engine run produced.
class EngineResult {
  /// Creates a result.
  const EngineResult({
    required this.steps,
    required this.alerts,
    required this.notifications,
    required this.counters,
  });

  /// The full decision trace, in evaluation order.
  final List<EvaluationStep> steps;

  /// Alert episodes produced or updated by the run.
  final List<AlertEpisode> alerts;

  /// Notification attempts, sent and suppressed.
  final List<NotificationRecord> notifications;

  /// Run counters.
  final EngineCounters counters;

  /// Steps that changed something a reviewer would care about.
  List<EvaluationStep> get noteworthy =>
      steps.where((step) => step.isNoteworthy).toList(growable: false);

  /// Alerts still open at the end of the run.
  List<AlertEpisode> get openAlerts =>
      alerts.where((alert) => alert.isOpen).toList(growable: false);

  /// The single most important number for the sub-dwell case: alerts created.
  int get alertCount => alerts.length;
}
