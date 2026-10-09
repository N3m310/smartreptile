/// The prototype's single mutable store: session, terrarium selection, alerts, thresholds, devices, silences and
/// notification preferences.
///
/// It is deliberately a `ChangeNotifier` with one provider, because `provider` is the state-management requirement the
/// PRM393 rubric asks for and because the prototype has to be *reviewable*: a reviewer can read this file top to
/// bottom in a couple of minutes and then follow any screen back to the rule it renders.
///
/// One deliberate deviation from the real client, stated here rather than discovered later: **the metric status is
/// computed in the app.** The design puts that on the server (BR-08.1 — "the client never decides bands itself"), and
/// the real payload carries a `status` per metric. There is no server yet, so the prototype computes it from the same
/// resolved band the engine uses. The day the `/readings/latest` endpoint lands, this method is deleted and the status
/// comes off the wire.
library;

import 'package:flutter/foundation.dart';

import '../core/clock.dart';
import '../core/status.dart';
import '../models/metric_value.dart';
import 'fake_world.dart';
import 'rules/band_validation.dart';
import 'rules/daily_summary.dart';
import 'rules/domain.dart';
import 'rules/events.dart';
import 'rules/notification_policy.dart';
import 'rules/silence.dart';
import 'rules/species_profiles.dart';

/// Everything one dashboard render needs, assembled once so a screen does not walk five maps itself.
class LiveView {
  /// Creates a live view.
  const LiveView({
    required this.terrarium,
    required this.device,
    required this.metrics,
    required this.bands,
    required this.openAlerts,
    required this.silences,
    required this.today,
    required this.now,
    required this.samplingIntervalSec,
  });

  /// The terrarium.
  final Terrarium terrarium;

  /// Its bound device, or null when nothing is claimed yet (BR-08.4).
  final DeviceRecord? device;

  /// Latest values keyed by metric code, ready for `MetricCard`.
  final Map<String, MetricValue> metrics;

  /// Effective bands keyed by metric code (BR-10.3), each carrying its source.
  final Map<String, EffectiveBand> bands;

  /// Alerts still needing attention.
  final List<AlertEpisode> openAlerts;

  /// Silence windows in force, which the dashboard must always show (BR-12.6).
  final List<SilenceWindow> silences;

  /// Today's summary, for the strip under the cards (FR-14).
  final DailySummarySnapshot? today;

  /// The instant the view was assembled; passed to the cards so ages are consistent across a frame.
  final DateTime now;

  /// Expected reporting interval, used for the staleness threshold.
  final int samplingIntervalSec;

  /// True when nothing has ever been measured here, so the screen shows the onboarding empty state.
  bool get hasNoData => metrics.isEmpty;

  /// True when the device is bound but not reporting, which must pause evaluation visibly (UC-06).
  bool get isDeviceOffline => device != null && !device!.status.isReporting;

  /// Age of the newest reading across all metrics.
  Duration? get newestAge {
    if (metrics.isEmpty) {
      return null;
    }
    final newest = metrics.values
        .map((value) => value.capturedAt)
        .reduce((a, b) => a.isAfter(b) ? a : b);
    return now.difference(newest);
  }

  /// True when the newest reading is older than `3 × samplingInterval` (BR-08.5).
  bool get isStale {
    final age = newestAge;
    if (age == null) {
      return false;
    }
    return age > Duration(seconds: samplingIntervalSec * 3);
  }

  /// The alert to show in the banner: the most severe open one, newest first.
  AlertEpisode? get bannerAlert {
    if (openAlerts.isEmpty) {
      return null;
    }
    final sorted = [...openAlerts]
      ..sort((a, b) {
        final bySeverity = b.severity.rank.compareTo(a.severity.rank);
        return bySeverity != 0
            ? bySeverity
            : b.triggeredAt.compareTo(a.triggeredAt);
      });
    return sorted.first;
  }
}

/// A minimal slice of a daily summary that a screen can hold without importing the summary builder.
class DailySummarySnapshot {
  /// Creates a snapshot.
  const DailySummarySnapshot({
    required this.localDate,
    required this.coveragePct,
    required this.isLowConfidence,
    required this.temperatureOutOfRangeMinutes,
    required this.lightHours,
    required this.requiredLightHours,
    required this.lightDeficitHours,
    required this.temperatureExposureDegCHours,
    required this.criticalAlertCount,
    required this.alertCount,
    required this.silenceWindows,
  });

  /// The local day.
  final DateTime localDate;

  /// Coverage percentage (BR-14.6).
  final double coveragePct;

  /// True when coverage is below 80 %.
  final bool isLowConfidence;

  /// Minutes the air temperature spent outside its band.
  final int temperatureOutOfRangeMinutes;

  /// Accumulated light hours.
  final double lightHours;

  /// Light hours the profile requires.
  final int requiredLightHours;

  /// Light deficit against that requirement.
  final double lightDeficitHours;

  /// Temperature exposure in degree-hours (BR-14.3).
  final double temperatureExposureDegCHours;

  /// Alerts opened that day.
  final int alertCount;

  /// Alerts that reached Critical that day.
  final int criticalAlertCount;

  /// Minutes of the day covered by a silence window (BR-12.6 keeps these visible).
  final int silenceWindows;
}

/// Filter for the alert inbox (BR-12.4). All fields are optional; the default is "everything, newest first".
class AlertFilter {
  /// Creates a filter.
  const AlertFilter({
    this.terrariumId,
    this.severity,
    this.metric,
    this.state,
    this.from,
    this.to,
  });

  /// Restrict to one terrarium.
  final String? terrariumId;

  /// Restrict to one severity.
  final Severity? severity;

  /// Restrict to one metric.
  final String? metric;

  /// Restrict to one lifecycle state.
  final AlertState? state;

  /// Only alerts triggered at or after this instant.
  final DateTime? from;

  /// Only alerts triggered before this instant.
  final DateTime? to;

  /// True when nothing is filtered out.
  bool get isEmpty =>
      terrariumId == null &&
      severity == null &&
      metric == null &&
      state == null &&
      from == null &&
      to == null;

  /// Returns a copy with the given fields replaced; `clear*` flags remove a field.
  AlertFilter copyWith({
    String? terrariumId,
    Severity? severity,
    String? metric,
    AlertState? state,
    DateTime? from,
    DateTime? to,
    bool clearSeverity = false,
    bool clearMetric = false,
    bool clearState = false,
  }) => AlertFilter(
    terrariumId: terrariumId ?? this.terrariumId,
    severity: clearSeverity ? null : (severity ?? this.severity),
    metric: clearMetric ? null : (metric ?? this.metric),
    state: clearState ? null : (state ?? this.state),
    from: from ?? this.from,
    to: to ?? this.to,
  );

  /// Applies the filter to one alert.
  bool matches(AlertEpisode alert) {
    if (terrariumId != null && alert.terrariumId != terrariumId) {
      return false;
    }
    if (severity != null && alert.severity != severity) {
      return false;
    }
    if (metric != null && alert.metric != metric) {
      return false;
    }
    if (state != null && alert.state != state) {
      return false;
    }
    if (from != null && alert.triggeredAt.isBefore(from!)) {
      return false;
    }
    if (to != null && !alert.triggeredAt.isBefore(to!)) {
      return false;
    }
    return true;
  }
}

/// The prototype store.
class PrototypeState extends ChangeNotifier {
  PrototypeState._({required this.clock, required FakeWorld world})
    : _world = world,
      _demoNow = world.anchor,
      _activeTerrariumId = world.activeTerrariumId,
      _resolvers = _resolversFor(world);

  /// Builds the store, generating the fake world as of the real clock.
  factory PrototypeState({required Clock clock, FakeWorld? world}) =>
      PrototypeState._(
        clock: clock,
        world: world ?? FakeWorldFactory.build(now: clock.nowUtc().toLocal()),
      );

  /// Builds a store over an already-generated world, for tests that need a fixed anchor.
  factory PrototypeState.forWorld(Clock clock, FakeWorld world) =>
      PrototypeState._(clock: clock, world: world);

  static Map<String, ThresholdResolver> _resolversFor(FakeWorld world) => {
    for (final terrarium in world.terrariums)
      terrarium.id: ThresholdResolver(profile: terrarium.profile),
  };

  /// Injected clock. The prototype uses it only to timestamp the fake world and to advance the demo clock, so no rule
  /// in this app depends on wall-clock time passing.
  final Clock clock;

  final FakeWorld _world;

  /// Effective thresholds per terrarium: the profile plus any override applied during the session.
  final Map<String, ThresholdResolver> _resolvers;

  /// The demo clock. It starts frozen at the instant the fake data ends, because a prototype whose "current" time
  /// drifts with the wall clock shows different numbers in every screenshot and cannot be re-checked later.
  DateTime _demoNow;

  String _activeTerrariumId;
  FakeUser? _signedInUser;
  String _lastClaimError = '';
  AlertFilter _alertFilter = const AlertFilter();
  final List<SilenceWindow> _silences = [];
  NotificationPreferences? _preferencesOverride;

  // -----------------------------------------------------------------------------------------------------------
  // Session
  // -----------------------------------------------------------------------------------------------------------

  /// The signed-in user, or null when signed out (S1 → S2).
  FakeUser? get user => _signedInUser;

  /// True once signed in.
  bool get isSignedIn => _signedInUser != null;

  /// True while the sign-in screen should be shown.
  bool get isSignedOut => _signedInUser == null;

  /// The demo credentials the login screen pre-fills, so the prototype is one tap from the dashboard.
  static const demoEmail = 'linh@example.com';

  /// The demo password; the prototype does not store credentials anywhere (NFR-04).
  static const demoPassword = 'demo1234';

  /// The characters the API's password policy insists on (BR-01.2), mirrored here so the demo cannot accept a
  /// password the server would refuse. Whitespace does not count as a special character, as on the server.
  static final _hasUppercase = RegExp(r'[A-Z]');
  static final _hasSpecial = RegExp(r'[^A-Za-z0-9\s]');

  /// Signs in when the credentials match the demo account. Returns an error message, or an empty string.
  String signIn({required String email, required String password}) {
    if (email.trim().isEmpty || password.isEmpty) {
      return 'Enter your email and password.';
    }
    final match = _world.users.where(
      (candidate) =>
          candidate.email.toLowerCase() == email.trim().toLowerCase(),
    );
    if (match.isEmpty || password != demoPassword) {
      // One message for both cases on purpose: distinguishing "no such account" from "wrong password" tells an
      // attacker which emails exist (BR-02.x, and the same rule applies to a prototype that will be demoed).
      return 'Email or password is incorrect.';
    }
    _signedInUser = match.first;
    _activeTerrariumId = _world.activeTerrariumId;
    notifyListeners();
    return '';
  }

  /// Creates an account. The prototype accepts any valid-looking input and signs the same demo user in.
  ///
  /// The password rules mirror the API's policy (BR-01.2) so a demo cannot accept something the server would
  /// refuse: at least 8 characters with an upper-case letter and a special character among them.
  String register({
    required String name,
    required String email,
    required String password,
  }) {
    if (name.trim().length < 2) {
      return 'Enter your name.';
    }
    if (!email.contains('@') || !email.contains('.')) {
      return 'Enter a valid email address.';
    }
    if (password.length < 8) {
      return 'Use at least 8 characters for the password.';
    }
    if (!_hasUppercase.hasMatch(password)) {
      return 'Add at least one upper-case letter to the password.';
    }
    if (!_hasSpecial.hasMatch(password)) {
      return 'Add at least one special character to the password.';
    }
    _signedInUser = _world.users.first;
    _activeTerrariumId = _world.activeTerrariumId;
    notifyListeners();
    return '';
  }

  /// Signs out and clears per-user view state, so one user cannot see another's values (UC-01).
  void signOut() {
    _signedInUser = null;
    _alertFilter = const AlertFilter();
    notifyListeners();
  }

  /// Effective notification preferences: the user's, once signed in.
  NotificationPreferences get preferences =>
      _preferencesOverride ??
      _signedInUser?.preferences ??
      const NotificationPreferences();

  /// Replaces the notification preferences (BR-13.2).
  void setPreferences(NotificationPreferences preferences) {
    _preferencesOverride = preferences;
    notifyListeners();
  }

  // -----------------------------------------------------------------------------------------------------------
  // Demo clock
  // -----------------------------------------------------------------------------------------------------------

  /// The instant the prototype believes it is.
  DateTime get demoNow => _demoNow;

  /// The instant the fake data ends.
  DateTime get anchor => _world.anchor;

  /// Moves the demo clock; used by the "advance 10 min" control to make staleness visible on demand.
  ///
  /// Ages are *derived* from a timestamp (never stored as a flag a timer flips), so moving this one value is enough to
  /// demonstrate the stale-state rule end to end.
  void advanceDemoClock(Duration by) {
    _demoNow = _demoNow.add(by);
    notifyListeners();
  }

  /// Returns the demo clock to the instant the data ends.
  void resetDemoClock() {
    _demoNow = _world.anchor;
    notifyListeners();
  }

  /// Sets the demo clock to an explicit instant.
  ///
  /// The screens use this for "show me the same terrarium at 14:00", which is the only way to demonstrate a
  /// phase-specific band: at 20:41 the night band is in force, so a daytime override genuinely does not apply and a
  /// reviewer would otherwise conclude the override was broken.
  void setDemoClock(DateTime instant) {
    _demoNow = instant;
    notifyListeners();
  }

  // -----------------------------------------------------------------------------------------------------------
  // Selection
  // -----------------------------------------------------------------------------------------------------------

  /// Terrariums the user can see.
  List<Terrarium> get terrariums => _world.terrariums;

  /// The selected terrarium id.
  String get activeTerrariumId => _activeTerrariumId;

  /// The selected terrarium.
  Terrarium get activeTerrarium =>
      _world.terrariumById(_activeTerrariumId) ?? _world.terrariums.first;

  /// Selects a terrarium; the dashboard, history and thresholds all follow it.
  void selectTerrarium(String terrariumId) {
    if (!_world.terrariums.any((terrarium) => terrarium.id == terrariumId)) {
      return;
    }
    _activeTerrariumId = terrariumId;
    notifyListeners();
  }

  // -----------------------------------------------------------------------------------------------------------
  // Live view
  // -----------------------------------------------------------------------------------------------------------

  /// The effective bands for a terrarium, including any override applied in this session.
  List<EffectiveBand> bandsFor(String terrariumId) {
    final resolver = _resolvers[terrariumId];
    final terrarium = _world.terrariumById(terrariumId);
    if (resolver == null || terrarium == null) {
      return const [];
    }
    return resolver.resolveAll(
      _demoNow,
      includeSurface: terrarium.profile.surfaceProbeFitted,
    );
  }

  /// Assembles the dashboard view for one terrarium.
  LiveView liveView(String terrariumId) {
    final terrarium =
        _world.terrariumById(terrariumId) ?? _world.terrariums.first;
    final device = _world.deviceFor(terrariumId);
    final samples =
        _world.latest[terrariumId] ?? const <String, MetricSample>{};
    final resolver = _resolvers[terrariumId];
    final samplingIntervalSec = device?.samplingIntervalSec ?? 60;
    final silences = _silences
        .where(
          (silence) =>
              silence.terrariumId == terrariumId &&
              !silence.isExpired(_demoNow),
        )
        .toList();

    final metrics = <String, MetricValue>{};
    final bands = <String, EffectiveBand>{};

    // The bands are resolved for every metric the profile *measures*, not only for the metrics that have a reading.
    // A terrarium with a bound node and no data yet still has thresholds — and the thresholds screen is exactly where
    // a keeper goes when the readings have not started.
    for (final metric in measuredMetrics(terrariumId)) {
      final effective = resolver?.resolve(metric, _demoNow);
      if (effective != null) {
        bands[metric] = effective;
      }
    }

    for (final entry in samples.entries) {
      final effective = bands[entry.key];
      final definition = MetricCatalog.byCode(entry.key);
      metrics[entry.key] = MetricValue(
        code: entry.key,
        value: entry.value.value,
        unit: definition?.unit ?? '',
        capturedAt: entry.value.recordedAt,
        status: _statusFor(
          value: entry.value.value,
          band: effective?.band,
          qualityFlags: entry.value.qualityFlags,
          device: device,
        ).toApiValue(),
        qualityFlags: entry.value.qualityFlags,
      );
    }

    final summaries = _world.summaries[terrariumId] ?? const [];
    final todaySummary = summaries.isEmpty ? null : _snapshot(summaries.last);

    return LiveView(
      terrarium: terrarium,
      device: device,
      metrics: metrics,
      bands: bands,
      openAlerts: openAlertsFor(terrariumId),
      silences: silences,
      today: todaySummary,
      now: _demoNow,
      samplingIntervalSec: samplingIntervalSec,
    );
  }

  /// The instantaneous card status (BR-08.1).
  ///
  /// Note what this is **not**: it is not the alert decision. A card turns orange the moment a value leaves the target
  /// band, while an alert needs the dwell to expire — so a four-minute spike colours the card and wakes nobody, which
  /// is the entire point of BR-11.3. The Rule Lab shows both numbers side by side, because the difference is the rule.
  ///
  /// Staleness deliberately does not change the status: dimming plus the age label carry "this is not current", and a
  /// colour change would hide a genuine violation behind a second meaning.
  MetricStatus _statusFor({
    required double value,
    required ThresholdBand? band,
    required int qualityFlags,
    required DeviceRecord? device,
  }) {
    if ((qualityFlags & QualityBits.notEvaluable) != 0) {
      return MetricStatus.unavailable;
    }
    if (device?.status == DeviceStatus.maintenance) {
      return MetricStatus.maintenance;
    }
    if (band == null) {
      return MetricStatus.noData;
    }
    return _bandStatus(value, band);
  }

  MetricStatus _bandStatus(double value, ThresholdBand band) {
    if (band.accumulatedOnly) {
      // Light has no per-sample band: the accumulated rule decides, so the card shows the target and stays green.
      return MetricStatus.inRange;
    }
    if (band.isCritical(value)) {
      return MetricStatus.critical;
    }
    if (!band.isInTargetBand(value)) {
      return MetricStatus.outOfRange;
    }
    return MetricStatus.inRange;
  }

  /// Every metric code the terrarium's profile measures, whether or not a reading exists yet.
  List<String> measuredMetrics(String terrariumId) {
    final terrarium = _world.terrariumById(terrariumId);
    if (terrarium == null) {
      return const [];
    }
    return [
      for (final definition in MetricCatalog.all)
        if (definition.code != 'surfaceTempC' ||
            terrarium.profile.surfaceProbeFitted)
          definition.code,
    ];
  }

  /// Data coverage for one terrarium (BR-07.5), from the sample count against what the interval expected.
  CoverageSnapshot coverage(String terrariumId) {
    final samples = _world.history[terrariumId] ?? const <MetricSample>[];
    final received = samples.where((sample) => sample.metric == 'tempC').length;
    final windowMinutes = received == 0
        ? 0
        : _world.historyInterval.inMinutes * received;
    final device = _world.deviceFor(terrariumId);

    return CoverageSnapshot(
      samplesReceived: received,
      expectedSamples: device == null ? 0 : windowMinutes,
      windowMinutes: windowMinutes,
      lastSampleAt: samples.isEmpty ? null : samples.last.recordedAt,
    );
  }

  // -----------------------------------------------------------------------------------------------------------
  // Alerts
  // -----------------------------------------------------------------------------------------------------------

  /// The inbox filter (BR-12.4).
  AlertFilter get alertFilter => _alertFilter;

  /// Replaces the inbox filter.
  void setAlertFilter(AlertFilter filter) {
    _alertFilter = filter;
    notifyListeners();
  }

  /// Every alert, newest first, before filtering.
  List<AlertEpisode> get allAlerts => _world.alerts;

  /// Alerts matching the current filter, newest first.
  List<AlertEpisode> get filteredAlerts =>
      _world.alerts.where(_alertFilter.matches).toList(growable: false);

  /// Alerts for a terrarium, newest first.
  List<AlertEpisode> alertsFor(String terrariumId) =>
      _world.alertsFor(terrariumId).toList()
        ..sort((a, b) => b.triggeredAt.compareTo(a.triggeredAt));

  /// Open or acknowledged alerts for a terrarium.
  List<AlertEpisode> openAlertsFor(String terrariumId) =>
      _world.openAlertsFor(terrariumId).toList()
        ..sort((a, b) => b.severity.rank.compareTo(a.severity.rank));

  /// Total open alerts, for the bottom-navigation badge.
  int get openAlertCount => _world.alerts.where((alert) => alert.isOpen).length;

  /// Open Critical alerts, for the dashboard's escalation banner.
  int get openCriticalCount => _world.alerts
      .where((alert) => alert.isOpen && alert.severity == Severity.critical)
      .length;

  /// Looks up one alert.
  AlertEpisode? alertById(int id) {
    for (final alert in _world.alerts) {
      if (alert.id == id) {
        return alert;
      }
    }
    return null;
  }

  /// Acknowledges an alert (BR-12.2).
  ///
  /// Requires `Open`, not merely "still needs attention": re-acknowledging an acknowledged alert would overwrite
  /// `AcknowledgedByUserId` and `AcknowledgedAt`, and BR-12.2 records *who owned it first*. The first version of this
  /// guard used `isOpen`, which includes `Acknowledged`, so a double tap quietly reassigned the ownership.
  bool acknowledge(int alertId, {String? by}) {
    final alert = alertById(alertId);
    final actor = by ?? _signedInUser?.name;
    if (alert == null || alert.state != AlertState.open || actor == null) {
      return false;
    }
    alert
      ..state = AlertState.acknowledged
      ..acknowledgedAt = _demoNow
      ..acknowledgedBy = actor;
    notifyListeners();
    return true;
  }

  /// Resolves an alert with a reason (BR-12.3).
  ///
  /// Accepts `Open` or `Acknowledged` — BR-12.1 allows `Open → Resolved` — and refuses everything else, because
  /// `Resolved` is terminal.
  bool resolve(int alertId, ResolvedReason reason, {String? note}) {
    final alert = alertById(alertId);
    if (alert == null || !alert.isOpen) {
      return false;
    }
    alert
      ..state = AlertState.resolved
      ..resolvedAt = _demoNow
      ..resolvedReason = reason
      ..note = note;
    notifyListeners();
    return true;
  }

  /// Re-opens nothing: BR-12.1 makes `Resolved` terminal, and the prototype refuses to pretend otherwise.
  ///
  /// The method exists so a screen that wants an "undo" gets a clear refusal instead of calling [resolve] twice and
  /// silently rewriting history.
  bool reopen(int alertId) => false;

  // -----------------------------------------------------------------------------------------------------------
  // Thresholds
  // -----------------------------------------------------------------------------------------------------------

  /// Validates a band against BR-10.2, the arithmetic rules and the climate envelope.
  List<ValidationIssue> validateBand(String terrariumId, ThresholdBand band) {
    final terrarium = _world.terrariumById(terrariumId);
    if (terrarium == null) {
      return const [];
    }
    return BandValidation.validate(
      band,
      siblings: terrarium.profile.bands,
      climateRange: terrarium.profile.climateRange,
    );
  }

  /// Applies a per-terrarium override (BR-10.3 step 1). Refused when the band is invalid.
  ///
  /// An override beats a profile band even when the profile band is phase-specific — the editor states this out loud
  /// because it surprises people (`03-implementation/06` §1).
  bool applyOverride(String terrariumId, ThresholdBand band, {String? reason}) {
    final terrarium = _world.terrariumById(terrariumId);
    final actor = _signedInUser?.name;
    if (terrarium == null ||
        actor == null ||
        !BandValidation.canSave(validateBand(terrariumId, band))) {
      return false;
    }
    final resolver = _resolvers[terrariumId];
    if (resolver == null) {
      return false;
    }
    _resolvers[terrariumId] = resolver.withOverride(
      ThresholdOverride(
        terrariumId: terrariumId,
        band: band.copyWith(source: BandSource.override),
        editedBy: actor,
        editedAt: _demoNow,
        reason: reason,
      ),
    );
    notifyListeners();
    return true;
  }

  /// Removes an override, falling back to the profile band.
  void clearOverride(String terrariumId, String metric, Phase phase) {
    final resolver = _resolvers[terrariumId];
    if (resolver == null) {
      return;
    }
    _resolvers[terrariumId] = resolver.withoutOverride(metric, phase);
    notifyListeners();
  }

  /// Overrides currently in force for a terrarium (BR-10.4: every edit is auditable).
  List<ThresholdOverride> overridesFor(String terrariumId) =>
      _resolvers[terrariumId]?.overrides ?? const [];

  // -----------------------------------------------------------------------------------------------------------
  // Devices
  // -----------------------------------------------------------------------------------------------------------

  /// Every device, claimed and unclaimed (FR-16).
  List<DeviceRecord> get devices => _world.devices;

  /// One device.
  DeviceRecord? deviceById(String id) {
    for (final device in _world.devices) {
      if (device.id == id) {
        return device;
      }
    }
    return null;
  }

  /// The device bound to a terrarium, or null when nothing is claimed yet (BR-08.4).
  DeviceRecord? deviceFor(String terrariumId) => _world.deviceFor(terrariumId);

  /// The error from the last claim attempt, or an empty string.
  String get lastClaimError => _lastClaimError;

  /// The device currently showing a pairing code.
  DeviceRecord? get deviceAwaitingClaim {
    for (final device in _world.devices) {
      if (device.status == DeviceStatus.provisioning) {
        return device;
      }
    }
    return null;
  }

  /// Claims a device by its pairing code (FR-04).
  ///
  /// Implements the rule as specified rather than as convenient: 8 characters after normalising the display dashes,
  /// matched against a device that is actually provisioning, and refused once the 15-minute code has expired. A claim
  /// screen that accepts anything would demonstrate nothing.
  String claimDevice({required String code, required String terrariumId}) {
    final normalised = code.toUpperCase().replaceAll(RegExp(r'[^A-Z0-9]'), '');
    if (normalised.length != 8) {
      _lastClaimError = 'A pairing code is 8 characters.';
      notifyListeners();
      return _lastClaimError;
    }

    final candidate = _world.devices.firstWhere(
      (device) => device.claimCode == normalised,
      orElse: () => const DeviceRecord(
        id: '',
        terrariumId: null,
        status: DeviceStatus.revoked,
        firmwareVersion: '',
        samplingIntervalSec: 60,
      ),
    );
    if (candidate.id.isEmpty) {
      _lastClaimError =
          'That code does not match a node waiting to be claimed.';
      notifyListeners();
      return _lastClaimError;
    }
    if (candidate.claimCodeExpiresAt != null &&
        !candidate.claimCodeExpiresAt!.isAfter(_demoNow)) {
      _lastClaimError =
          'That code has expired. The node shows a new one every 15 minutes.';
      notifyListeners();
      return _lastClaimError;
    }
    if (_world.devices.any((device) => device.terrariumId == terrariumId)) {
      _lastClaimError = 'That terrarium already has a node.';
      notifyListeners();
      return _lastClaimError;
    }

    _replaceDevice(
      candidate.copyWith(
        terrariumId: terrariumId,
        // A successful claim puts the node online immediately, with "now" as its heartbeat. Nothing else would move
        // on screen, and the claim screen would appear to have done nothing.
        status: DeviceStatus.online,
        lastSeenAt: _demoNow,
      ),
    );
    _lastClaimError = '';
    notifyListeners();
    return '';
  }

  /// Turns maintenance mode on or off. Device-level: records everything, notifies nobody (`02-design/05` §5).
  void setMaintenance(String deviceId, bool enabled) {
    final device = deviceById(deviceId);
    if (device == null) {
      return;
    }
    _replaceDevice(
      device.copyWith(
        status: enabled ? DeviceStatus.maintenance : DeviceStatus.online,
      ),
    );
    notifyListeners();
  }

  /// Saves per-device calibration offsets, applied at ingest (BR-07.4).
  void setCalibration(
    String deviceId, {
    required double tempOffsetC,
    required double rhOffsetPct,
    required double luxGain,
  }) {
    final device = deviceById(deviceId);
    if (device == null) {
      return;
    }
    _replaceDevice(
      device.copyWith(
        tempOffsetC: tempOffsetC,
        rhOffsetPct: rhOffsetPct,
        luxGain: luxGain,
      ),
    );
    notifyListeners();
  }

  /// Renames nothing — devices have ids, not names — but rebinding is real: this moves a node to another terrarium.
  void rebindDevice(String deviceId, String? terrariumId) {
    final device = deviceById(deviceId);
    if (device == null) {
      return;
    }
    _replaceDevice(
      terrariumId == null
          ? device.copyWith(clearTerrarium: true)
          : device.copyWith(terrariumId: terrariumId),
    );
    notifyListeners();
  }

  /// Revokes a device (FR-16). After this the node cannot authenticate, and the fleet view says so.
  void revokeDevice(String deviceId) {
    final device = deviceById(deviceId);
    if (device == null) {
      return;
    }
    _replaceDevice(device.copyWith(status: DeviceStatus.revoked));
    notifyListeners();
  }

  /// Rotates the device secret, returning the new one once so it can be flashed (ADR-016 flow).
  String rotateDeviceSecret(String deviceId) {
    final device = deviceById(deviceId);
    if (device == null) {
      return '';
    }
    // Deterministic, and obviously not a secret: a prototype must never look like it generated a real credential.
    final next = 'demo-${device.id}-${_demoNow.millisecondsSinceEpoch ~/ 1000}';
    notifyListeners();
    return next;
  }

  void _replaceDevice(DeviceRecord device) {
    final index = _world.devices.indexWhere(
      (candidate) => candidate.id == device.id,
    );
    if (index >= 0) {
      _world.devices[index] = device;
    }
  }

  // -----------------------------------------------------------------------------------------------------------
  // Silences
  // -----------------------------------------------------------------------------------------------------------

  /// Silence windows currently in force, newest first.
  List<SilenceWindow> get activeSilences => _silences
      .where((silence) => !silence.isExpired(_demoNow))
      .toList(growable: false);

  /// Creates a silence window (BR-12.6). Clamped to 24 hours, and a reason is required.
  bool createSilence({
    required String terrariumId,
    required String? metric,
    required Duration duration,
    required String reason,
  }) {
    final actor = _signedInUser?.name;
    if (actor == null || reason.trim().isEmpty) {
      return false;
    }
    _silences.add(
      SilenceWindow.create(
        terrariumId: terrariumId,
        metric: metric,
        now: _demoNow,
        duration: duration,
        reason: reason.trim(),
        createdBy: actor,
      ),
    );
    notifyListeners();
    return true;
  }

  /// Ends a silence early and records who did it.
  void clearSilence(SilenceWindow silence) {
    _silences.removeWhere(
      (candidate) =>
          candidate.metric == silence.metric && candidate.from == silence.from,
    );
    notifyListeners();
  }

  /// Silences covering one terrarium.
  List<SilenceWindow> silencesFor(String terrariumId) => _silences
      .where(
        (silence) =>
            silence.terrariumId == terrariumId && !silence.isExpired(_demoNow),
      )
      .toList(growable: false);

  // -----------------------------------------------------------------------------------------------------------
  // History and summaries
  // -----------------------------------------------------------------------------------------------------------

  /// Samples for one terrarium, oldest first.
  List<MetricSample> historyFor(String terrariumId) =>
      _world.history[terrariumId] ?? const [];

  /// Samples for one metric inside a window, oldest first.
  List<MetricSample> seriesFor(
    String terrariumId,
    String metric, {
    required DateTime from,
    required DateTime to,
  }) => [
    for (final sample in historyFor(terrariumId))
      if (sample.metric == metric &&
          !sample.recordedAt.isBefore(from) &&
          !sample.recordedAt.isAfter(to))
        sample,
  ];

  /// Per-day summaries for one terrarium, oldest first.
  List<DailySummarySnapshot> summariesFor(String terrariumId) => [
    for (final summary in _world.summaries[terrariumId] ?? const [])
      _snapshot(summary),
  ];

  /// The raw engine run for a terrarium, so the Lab can show the real trace for the demo data.
  EngineResult? engineResultFor(String terrariumId) =>
      _world.engineResults[terrariumId];

  /// Every notification attempt, newest first.
  List<NotificationRecord> get notifications {
    final sorted = [..._world.notifications]
      ..sort((a, b) => b.at.compareTo(a.at));
    return sorted;
  }

  /// Notification attempts for one alert, oldest first — the response timeline of one incident (§6).
  List<NotificationRecord> notificationsFor(int alertId) => [
    for (final record in _world.notifications.where(
      (r) => r.alertId == alertId,
    ))
      record,
  ]..sort((a, b) => a.at.compareTo(b.at));

  /// Why the prototype is showing a given number, for the Rule Lab's provenance panel.
  Provenance provenanceFor(String terrariumId, String metric) {
    final effective = _resolvers[terrariumId]?.resolve(metric, _demoNow);
    return Provenance(
      metric: metric,
      band: effective?.band,
      source: effective?.source,
      profileId: _world.terrariumById(terrariumId)?.speciesProfileId,
      hasOverride:
          _resolvers[terrariumId]?.overrides.any(
            (override) => override.band.metric == metric,
          ) ??
          false,
    );
  }

  DailySummarySnapshot _snapshot(DailySummary summary) => DailySummarySnapshot(
    localDate: summary.localDate,
    coveragePct: summary.coveragePct,
    isLowConfidence: summary.isLowConfidence,
    temperatureOutOfRangeMinutes:
        summary.perMetric['tempC']?.outOfRangeMinutes ?? 0,
    lightHours: summary.lightHours,
    requiredLightHours: summary.requiredLightHours,
    lightDeficitHours: summary.lightDeficitHours,
    temperatureExposureDegCHours:
        summary.perMetric['tempC']?.totalExposureDegCHours ?? 0,
    alertCount: summary.alertCount,
    criticalAlertCount: summary.criticalAlertCount,
    silenceWindows: summary.silenceWindows,
  );
}

/// Where a number on screen came from, so "why is my limit 32 and not 30?" is answerable in the app (BR-10.3).
class Provenance {
  /// Creates a provenance record.
  const Provenance({
    required this.metric,
    required this.band,
    required this.source,
    required this.profileId,
    required this.hasOverride,
  });

  /// Metric described.
  final String metric;

  /// The band in force.
  final ThresholdBand? band;

  /// Its source.
  final BandSource? source;

  /// Profile the terrarium is assigned to.
  final String? profileId;

  /// True when the terrarium has its own override for this metric.
  final bool hasOverride;
}

/// Data coverage for one terrarium (BR-07.5).
class CoverageSnapshot {
  /// Creates a coverage snapshot.
  const CoverageSnapshot({
    required this.samplesReceived,
    required this.expectedSamples,
    required this.windowMinutes,
    required this.lastSampleAt,
  });

  /// Samples actually received.
  final int samplesReceived;

  /// Samples the interval expected over the same window.
  final int expectedSamples;

  /// Length of the observed window in minutes.
  final int windowMinutes;

  /// Timestamp of the newest sample.
  final DateTime? lastSampleAt;

  /// Coverage percentage. Clamped at 100: the expected count is derived from a window whose endpoints are both
  /// inclusive, so a complete window can otherwise report 100.08 %.
  double get coveragePct => expectedSamples == 0
      ? 0
      : (100 * samplesReceived / expectedSamples).clamp(0.0, 100.0);

  /// True below the 98 % floor the FR-07 acceptance criteria quote.
  bool get isBelowTarget => coveragePct < 98;
}
