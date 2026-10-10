/// The rule coverage matrix: which rules this prototype actually exercises, and which it does not.
///
/// The second half is the point. A prototype that implies it covers everything is a prototype that gets trusted for
/// something it never did, and the empty rows are the honest deliverable: they are a to-do list for M3, not a claim.
library;

import 'rules/domain.dart';

/// How completely a rule is represented in the prototype.
enum RuleStatus {
  /// The rule's decision procedure is implemented and exercised by a scenario.
  modelled('modelled'),

  /// The rule exists in the code but a documented part of it is missing, stated in [RuleEntry.note].
  partial('partly modelled'),

  /// The rule is specified and deliberately not implemented here.
  notModelled('not modelled');

  const RuleStatus(this.label);

  /// Display label.
  final String label;

  /// True when the rule is exercised end to end.
  bool get isModelled => this == RuleStatus.modelled;
}

/// One rule, its document reference and its status in the prototype.
class RuleEntry {
  /// Creates an entry.
  const RuleEntry({
    required this.id,
    required this.title,
    required this.docRef,
    required this.status,
    this.note,
  });

  /// Rule id, e.g. `BR-11.3`.
  final String id;

  /// One-line description.
  final String title;

  /// Where the rule is written down.
  final String docRef;

  /// How completely the prototype represents it.
  final RuleStatus status;

  /// What is missing or different, when the status is not [RuleStatus.modelled].
  final String? note;
}

/// The matrix, grouped in document order.
class RuleMatrix {
  const RuleMatrix._();

  /// Everything the prototype knows about the rule set.
  static const entries = <RuleEntry>[
    RuleEntry(
      id: 'BR-04',
      title: 'A device is claimed with an 8-character, 15-minute pairing code',
      docRef: 'FR-04 · 02-design/06',
      status: RuleStatus.partial,
      note:
          'Length, matching and expiry are enforced. The one-time nature is not: the prototype keeps the code '
          'valid until it expires, because there is no server transaction to consume it.',
    ),
    RuleEntry(
      id: 'BR-06.3',
      title: 'Duplicate detection on (deviceId, seq) is idempotent',
      docRef: 'FR-06',
      status: RuleStatus.notModelled,
      note:
          'The fake world never produces a duplicate. Worth a scenario once ingest exists — it is the rule that '
          'decides whether a reconnecting node creates phantom readings.',
    ),
    RuleEntry(
      id: 'BR-06.4',
      title: 'An implausible value is stored and flagged, never evaluated',
      docRef: 'FR-06 · rule V-06',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-06.5',
      title: 'RecordedAt from the device, ReceivedAt from the server',
      docRef: 'FR-06 · rule V-09',
      status: RuleStatus.partial,
      note:
          'Both timestamps exist and drive the back-fill window. The 120-second clock-skew flag is not modelled, '
          'so `DeviceClockSkew` never appears.',
    ),
    RuleEntry(
      id: 'BR-06.6',
      title: 'Back-fill is allowed and never notifies retroactively',
      docRef: 'FR-06 · BR-11.8',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-07.1',
      title: 'A sensor read failure excludes the metric instead of alerting',
      docRef: 'FR-07',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-07.2',
      title: 'A silent device raises DeviceSilent, not a habitat alert',
      docRef: 'FR-07',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-07.4',
      title: 'Per-device calibration offsets are applied at ingest',
      docRef: 'FR-07',
      status: RuleStatus.partial,
      note:
          'The offsets are stored and edited, but the fake history does not pass through them: applying an offset '
          'changes the device row, not the readings.',
    ),
    RuleEntry(
      id: 'BR-07.5',
      title: 'Coverage is exposed as expected versus received samples',
      docRef: 'FR-07',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-08.1',
      title:
          'A metric card shows value, unit, band, status and its own timestamp',
      docRef: 'FR-08 · 02-design/04 §5',
      status: RuleStatus.partial,
      note:
          'The design puts status evaluation on the server ("the client never decides bands itself"). The prototype '
          'computes it in the app from the same resolved band, which is a stated deviation, not a design change.',
    ),
    RuleEntry(
      id: 'BR-08.5',
      title: 'A stale reading is dimmed and labelled, never shown as current',
      docRef: 'FR-08',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-09.1',
      title: 'Bucketing: raw ≤ 6 h, 5-minute averages to 48 h, hourly rollups beyond',
      docRef: 'FR-09',
      status: RuleStatus.partial,
      note:
          'Only the raw tier exists, so ranges stop at 48 hours and the longer chips are disabled with the reason. '
          'The rollup pipeline is M4.',
    ),
    RuleEntry(
      id: 'BR-09.3',
      title: 'Alerts in the range are returned as shaded overlays',
      docRef: 'FR-09',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-09.5',
      title: 'Series are gap-aware: a hole is null, never interpolated',
      docRef: 'FR-09',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-10.2',
      title: 'Band arithmetic is validated before a band can be saved',
      docRef: 'FR-10 · 03-implementation/06 §2',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-10.3',
      title: 'Thresholds resolve override → profile → default, with a source per metric',
      docRef: 'FR-10',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-10.5',
      title: 'A band that contradicts its climate zone warns without blocking',
      docRef: 'FR-10 · 07-appendices/05 §4',
      status: RuleStatus.modelled,
      note:
          'Applied to the day band only, and to SurfaceTempC on its own scale. The documents do not say which band '
          'a night reading should be judged against, so the prototype refuses to guess.',
    ),
    RuleEntry(
      id: 'BR-11.1',
      title: 'Flagged readings are excluded from evaluation',
      docRef: 'FR-11',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.2',
      title:
          'The phase comes from the profile photoperiod, not from measured lux',
      docRef: 'FR-11',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.3',
      title: 'Dwell filters: a 4-minute spike produces no alert',
      docRef: 'FR-11',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.4',
      title: 'Recovery needs the band margin held for 3 consecutive minutes',
      docRef: 'FR-11',
      status: RuleStatus.partial,
      note:
          'The pseudocode and the worked example disagree about what "back inside the band by the margin" means. '
          'Both readings are implemented and switchable; the difference is a decision, not a bug.',
    ),
    RuleEntry(
      id: 'BR-11.5',
      title: 'Dedupe: one open alert per metric, severity and phase',
      docRef: 'FR-11',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.6',
      title: 'An open Warning escalates to Critical, once',
      docRef: 'FR-11',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.7',
      title: 'Alerts are recorded while silenced; silencing suppresses notifications only',
      docRef: 'FR-11 · BR-12.6',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-11.8',
      title: 'A back-fill older than 6 hours is recorded but never notifies',
      docRef: 'FR-11 · BR-06.6',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-12.1',
      title: 'Open → Acknowledged → Resolved, and Resolved is terminal',
      docRef: 'FR-12',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-12.2',
      title: 'Acknowledging records who and when, once',
      docRef: 'FR-12',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-12.3',
      title: 'Resolving requires a reason from a closed set',
      docRef: 'FR-12',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-12.5',
      title:
          'False positive and sensor fault are retained as v2 training labels',
      docRef: 'FR-12 · 07-appendices/06',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-12.6',
      title: 'A silence is per metric, reasoned, capped at 24 h, and always visible',
      docRef: 'FR-12 · 02-design/05 §5',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-13.1',
      title: 'Channels: in-app inbox always, push, email',
      docRef: 'FR-13',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-13.2',
      title: 'Minimum severity and quiet hours, with Critical bypassing quiet hours',
      docRef: 'FR-13',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-13.3',
      title: 'At most 10 notifications per terrarium per hour, then a digest',
      docRef: 'FR-13',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-13.5',
      title: 'Three retries with exponential backoff per channel',
      docRef: 'FR-13',
      status: RuleStatus.notModelled,
      note:
          'There is no channel to retry. The model has a single attempt per channel, which is enough to show the '
          'decision, not the delivery.',
    ),
    RuleEntry(
      id: 'BR-14.1',
      title: 'A daily summary per terrarium per local day',
      docRef: 'FR-14',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-14.3',
      title: 'Exposure index in degree-hours, from interpolated minute data',
      docRef: 'FR-14 · 03-implementation/06 §5.1',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-14.6',
      title: 'Coverage accompanies every summary',
      docRef: 'FR-14',
      status: RuleStatus.modelled,
    ),
    RuleEntry(
      id: 'BR-15',
      title: 'Retention windows and asynchronous export',
      docRef: 'FR-15',
      status: RuleStatus.notModelled,
      note: 'No retention job and no export: the prototype has three days of data and no download link.',
    ),
    RuleEntry(
      id: 'BR-16',
      title: 'Fleet management: rename, rebind, rotate, revoke, calibrate',
      docRef: 'FR-16',
      status: RuleStatus.partial,
      note:
          'Rebind, revoke, rotate and calibrate are all present. There is no rename, because devices are named by '
          'their id in this model.',
    ),
    RuleEntry(
      id: 'GradientWarning',
      title: 'Surface temperature more than 12 °C above the air is a burn risk',
      docRef: '02-design/05 §2 · SHOULD',
      status: RuleStatus.notModelled,
      note:
          'A SHOULD, and not in the FR-11 engine. The surface probe and its band exist, so implementing it is a '
          'small addition once the MUST rules are agreed.',
    ),
    RuleEntry(
      id: 'LightDeficit',
      title: 'Accumulated light hours below the profile minimum by 21:00 local',
      docRef: '02-design/05 §2 · SHOULD',
      status: RuleStatus.partial,
      note:
          'The light hours and the deficit are computed and shown on the report, but no alert is raised from them: '
          'the accumulated rule has no home in the per-sample engine yet.',
    ),
  ];

  /// The rules the prototype models completely.
  static Iterable<RuleEntry> get modelled =>
      entries.where((entry) => entry.status.isModelled);

  /// The rules that are specified but missing or incomplete here.
  static Iterable<RuleEntry> get gaps =>
      entries.where((entry) => !entry.status.isModelled);

  /// A short summary for a header: "28 of 43 modelled".
  static String get summary =>
      '${modelled.length} of ${entries.length} modelled here, '
      '${gaps.length} left to build';

  /// Where the documents disagree with themselves or are silent, collected for the reader.
  static const openQuestions = <(String, String, String)>[
    (
      'BR-11.4',
      'The recovery margin has two readings and the documents use both',
      'The pseudocode in `03-implementation/06` §4.2 requires the value to be back inside the band **by the '
          'margin**, and its own worked Example B counts a 31.6 °C reading against a 26–32 °C band with a 0.5 °C '
          'margin as a recovery minute. 0.4 is not 0.5. One of the two paragraphs has to change — this is the '
          'decision the Rule Lab will not make for you.',
    ),
    (
      'BR-11.2',
      'A phase rollover with an alert still open is undefined',
      'Evaluation state is keyed by (metric, phase), and no document says what resolves an alert opened under the '
          'previous phase when the photoperiod closes. Today the alert simply stays open and a new excursion in the '
          'new phase can open a second row for the same physical incident.',
    ),
    (
      'BR-10.5',
      'The climate-zone table gives ceilings, and nothing about floors',
      '`07-appendices/05` §4 lists an air temperature ceiling and a humidity ceiling per zone. BR-10.5\'s own '
          'example needs a floor to work ("a desert profile with TargetMax < 20 °C"). The prototype reads the range '
          'as the band a normal day target maximum falls in, and judges the day band only — a night drop is '
          'expected to be cooler than any daytime ceiling, and judging it was flagging the seeded profiles.',
    ),
    (
      'BR-13.2',
      'Does a recovery notice count as a Critical for quiet hours?',
      'A resolved alert keeps its escalated severity. If the quiet-hours bypass were evaluated on severity alone, '
          '"back in range" would wake the keeper at 03:00. The prototype treats the *event* as the thing that can be '
          'urgent, and the report should say so.',
    ),
  ];
}

/// Where a scenario's alert fits on the phase timeline, used by the Lab's header.
String phaseLabelFor(Phase phase) => phase.label;
