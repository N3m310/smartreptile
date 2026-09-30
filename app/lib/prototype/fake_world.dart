/// The prototype's fake world: three terrariums, four devices, three days of history, and the alerts the **real rule
/// kernel** produces from that history.
///
/// The design rule here is that no screen owns a number. Alerts, exposure figures, coverage and the light summary are
/// all *computed* by `rules/` from one generated sample stream, so the dashboard cannot contradict the Rule Lab and a
/// reviewer who spots a disagreement has found a real bug rather than a copy-paste error in a fixture.
///
/// What is fake: the measurements, the devices, the users, the clock.
/// What is real: every rule, threshold, dwell, hysteresis margin and suppression reason.
library;

import 'dart:math' as math;

import 'rules/daily_summary.dart';
import 'rules/domain.dart';
import 'rules/events.dart';
import 'rules/notification_policy.dart';
import 'rules/silence.dart';
import 'rules/species_profiles.dart';
import 'rules/threshold_engine.dart';

/// Role of a user, mirroring the RBAC model of FR-02.
enum UserRole {
  /// Owns terrariums and devices; may edit thresholds and claim devices.
  owner('Owner'),

  /// May acknowledge and resolve, may not edit thresholds (BR-12.6 grants silences to Technician too).
  technician('Technician'),

  /// Read-only.
  viewer('Viewer');

  const UserRole(this.label);

  /// Display label.
  final String label;
}

/// A fake account.
class FakeUser {
  /// Creates a user.
  const FakeUser({
    required this.id,
    required this.name,
    required this.email,
    required this.role,
    required this.preferences,
    this.telegramChatId,
  });

  /// Stable id.
  final String id;

  /// Display name.
  final String name;

  /// Login email.
  final String email;

  /// Role.
  final UserRole role;

  /// Notification preferences (BR-13.2), identical to the ones the engine consumes.
  final NotificationPreferences preferences;

  /// Telegram chat the user linked, when they did.
  final String? telegramChatId;

  /// Returns a copy with different preferences.
  FakeUser withPreferences(NotificationPreferences preferences) => FakeUser(
    id: id,
    name: name,
    email: email,
    role: role,
    preferences: preferences,
    telegramChatId: telegramChatId,
  );
}

/// A terrarium (FR-03).
class Terrarium {
  /// Creates a terrarium.
  const Terrarium({
    required this.id,
    required this.name,
    required this.speciesProfileId,
    required this.timeZoneId,
    required this.createdAt,
    this.notes,
  });

  /// Stable id.
  final String id;

  /// Display name.
  final String name;

  /// Assigned species profile (BR-10.1).
  final String speciesProfileId;

  /// IANA time zone; the summary and phase logic are local-day based (BR-14.1).
  final String timeZoneId;

  /// When it was created.
  final DateTime createdAt;

  /// Free-text note.
  final String? notes;

  /// The profile this terrarium is assigned to.
  SpeciesProfile get profile =>
      SpeciesProfiles.byId(speciesProfileId) ?? SpeciesProfiles.semiArid;
}

/// Device lifecycle status (FR-16).
enum DeviceStatus {
  /// Claimed by nobody; showing a pairing code.
  provisioning('provisioning'),

  /// Reporting on schedule.
  online('online'),

  /// Not reporting.
  offline('offline'),

  /// Deliberately not notifying (cleaning, lamp change).
  maintenance('maintenance'),

  /// Credentials revoked; refuses to authenticate.
  revoked('revoked');

  const DeviceStatus(this.label);

  /// Value the API returns and the UI shows.
  final String label;

  /// True when the device is expected to be producing data right now.
  bool get isReporting => this == DeviceStatus.online;
}

/// A sensor node (FR-04, FR-16).
class DeviceRecord {
  /// Creates a device.
  const DeviceRecord({
    required this.id,
    required this.terrariumId,
    required this.status,
    required this.firmwareVersion,
    required this.samplingIntervalSec,
    this.lastSeenAt,
    this.claimCode,
    this.claimCodeExpiresAt,
    this.batteryPct,
    this.rssiDbm,
    this.uptimeS,
    this.heapKb,
    this.tempOffsetC = 0,
    this.rhOffsetPct = 0,
    this.luxGain = 1,
    this.serialHint,
  });

  /// Short id, e.g. `sr-3f9a2c`.
  final String id;

  /// Terrarium it is bound to, or null while unclaimed (BR-04).
  final String? terrariumId;

  /// Lifecycle status.
  final DeviceStatus status;

  /// Firmware version, shown in the fleet view.
  final String firmwareVersion;

  /// Expected reporting interval in seconds; drives the staleness and silence thresholds.
  final int samplingIntervalSec;

  /// Last time the server heard from it.
  final DateTime? lastSeenAt;

  /// Current pairing code while provisioning.
  final String? claimCode;

  /// When that code expires (BR-04: 8 characters, 15 minutes).
  final DateTime? claimCodeExpiresAt;

  /// Battery percentage, null when mains powered.
  final int? batteryPct;

  /// Wi-Fi signal strength.
  final int? rssiDbm;

  /// Uptime in seconds.
  final int? uptimeS;

  /// Free heap in kilobytes.
  final int? heapKb;

  /// Calibration offset applied at ingest (BR-07.4).
  final double tempOffsetC;

  /// Calibration offset applied at ingest (BR-07.4).
  final double rhOffsetPct;

  /// Lux gain applied at ingest (BR-07.4).
  final double luxGain;

  /// A hint of the physical unit, so two identical-looking devices are distinguishable.
  final String? serialHint;

  /// True when the device is claimed.
  bool get isClaimed => terrariumId != null;

  /// Returns a copy with the given fields replaced.
  DeviceRecord copyWith({
    String? terrariumId,
    bool clearTerrarium = false,
    DeviceStatus? status,
    DateTime? lastSeenAt,
    String? claimCode,
    DateTime? claimCodeExpiresAt,
    double? tempOffsetC,
    double? rhOffsetPct,
    double? luxGain,
  }) => DeviceRecord(
    id: id,
    terrariumId: clearTerrarium ? null : (terrariumId ?? this.terrariumId),
    status: status ?? this.status,
    firmwareVersion: firmwareVersion,
    samplingIntervalSec: samplingIntervalSec,
    lastSeenAt: lastSeenAt ?? this.lastSeenAt,
    claimCode: claimCode ?? this.claimCode,
    claimCodeExpiresAt: claimCodeExpiresAt ?? this.claimCodeExpiresAt,
    batteryPct: batteryPct,
    rssiDbm: rssiDbm,
    uptimeS: uptimeS,
    heapKb: heapKb,
    tempOffsetC: tempOffsetC ?? this.tempOffsetC,
    rhOffsetPct: rhOffsetPct ?? this.rhOffsetPct,
    luxGain: luxGain ?? this.luxGain,
    serialHint: serialHint,
  );
}

/// One injected environmental event in the generated history.
///
/// The window is in **absolute local time** rather than relative to "now", deliberately: an event pegged to `now`
/// would land in a different phase depending on when the app is opened, so a "Warning that resolved" would silently
/// become a "Critical that resolved" at 23:00 and the fixture would stop testing what it says it tests.
class MetricEvent {
  /// Creates an event.
  const MetricEvent({
    required this.metric,
    required this.start,
    required this.duration,
    required this.label,
    this.toValue,
    this.peakDelta,
    this.rampMinutes = 10,
  });

  /// Metric it drives.
  final String metric;

  /// Absolute local start.
  final DateTime start;

  /// How long it lasts.
  final Duration duration;

  /// Human explanation, so the fake data is self-documenting.
  final String label;

  /// Absolute plateau value. Preferred over [peakDelta] because it does not depend on the baseline.
  final double? toValue;

  /// Additive plateau, used when the baseline is the interesting part.
  final double? peakDelta;

  /// Minutes spent ramping in and out.
  final double rampMinutes;

  /// End instant.
  DateTime get end => start.add(duration);

  /// True when [t] is inside the window.
  bool covers(DateTime t) => !t.isBefore(start) && !t.isAfter(end);
}

/// The fake world, ready for the screens.
class FakeWorld {
  /// Creates a world.
  const FakeWorld({
    required this.anchor,
    required this.users,
    required this.terrariums,
    required this.devices,
    required this.alerts,
    required this.notifications,
    required this.history,
    required this.latest,
    required this.summaries,
    required this.engineResults,
    required this.silences,
    required this.activeTerrariumId,
    required this.historyFrom,
    required this.historyInterval,
  });

  /// The instant the fake data ends: "now", rounded down to the minute.
  final DateTime anchor;

  /// Accounts.
  final List<FakeUser> users;

  /// Terrariums.
  final List<Terrarium> terrariums;

  /// Devices, claimed and unclaimed.
  final List<DeviceRecord> devices;

  /// Every alert the kernel produced, newest first.
  final List<AlertEpisode> alerts;

  /// Every notification attempt, sent and suppressed.
  final List<NotificationRecord> notifications;

  /// All samples per terrarium, sorted by time.
  final Map<String, List<MetricSample>> history;

  /// The most recent sample per metric per terrarium.
  final Map<String, Map<String, MetricSample>> latest;

  /// Per-day summaries per terrarium, ascending.
  final Map<String, List<DailySummary>> summaries;

  /// The raw engine run per terrarium, so the Lab can show the real trace for the demo terrarium too.
  final Map<String, EngineResult> engineResults;

  /// Silence windows in force.
  final List<SilenceWindow> silences;

  /// Terrarium the app opens on.
  final String activeTerrariumId;

  /// Oldest instant in [history].
  final DateTime historyFrom;

  /// Interval the fake devices report at.
  final Duration historyInterval;

  /// The signed-in user.
  FakeUser get user => users.first;

  /// Alerts for one terrarium, newest first.
  List<AlertEpisode> alertsFor(String terrariumId) => alerts
      .where((alert) => alert.terrariumId == terrariumId)
      .toList(growable: false);

  /// Open (or acknowledged) alerts for one terrarium.
  List<AlertEpisode> openAlertsFor(String terrariumId) => alerts
      .where((alert) => alert.terrariumId == terrariumId && alert.isOpen)
      .toList(growable: false);

  /// Device bound to a terrarium, if any.
  DeviceRecord? deviceFor(String terrariumId) {
    for (final device in devices) {
      if (device.terrariumId == terrariumId) {
        return device;
      }
    }
    return null;
  }

  /// Terrarium by id.
  Terrarium? terrariumById(String id) {
    for (final terrarium in terrariums) {
      if (terrarium.id == id) {
        return terrarium;
      }
    }
    return null;
  }

  /// Silences covering one terrarium.
  List<SilenceWindow> silencesFor(String terrariumId) => silences
      .where((silence) => silence.terrariumId == terrariumId)
      .toList(growable: false);
}

/// Generates the fake world.
class FakeWorldFactory {
  const FakeWorldFactory._();

  /// Days of 1-minute history to generate.
  ///
  /// Three days, because that is what the raw tier of BR-09.1 justifies: raw readings to 6 h, 5-minute averages to
  /// 48 h, and hourly rollups beyond that — which is an M4 pipeline the prototype does not pretend to have. The
  /// History screen therefore offers 1 h / 6 h / 24 h / 48 h and says why 7 d and 30 d are unavailable, instead of
  /// drawing a 30-day chart out of four days of data.
  static const historyDays = 3;

  /// Generates the world as of [now].
  static FakeWorld build({required DateTime now}) {
    final anchor = DateTime(now.year, now.month, now.day, now.hour, now.minute);
    final today = DateTime(anchor.year, anchor.month, anchor.day);
    const interval = 60;

    final terrariums = <Terrarium>[
      Terrarium(
        id: 't-gecko',
        name: "Linh's gecko box",
        speciesProfileId: 'semi-arid-leopard-gecko',
        timeZoneId: 'Asia/Ho_Chi_Minh',
        createdAt: _createdAt,
        notes: '40×30×30 cm, heat mat on the left, dimming LED bar.',
      ),
      Terrarium(
        id: 't-quarantine',
        name: 'Quarantine tub',
        speciesProfileId: 'tropical-crested-gecko',
        timeZoneId: 'Asia/Ho_Chi_Minh',
        createdAt: _createdAt,
      ),
      Terrarium(
        id: 't-beardie',
        name: 'Basking rack',
        speciesProfileId: 'arid-bearded-dragon',
        timeZoneId: 'Asia/Ho_Chi_Minh',
        createdAt: _createdAt,
        notes: 'No node claimed yet — this is the empty state, not a fault.',
      ),
    ];

    final devices = <DeviceRecord>[
      DeviceRecord(
        id: 'sr-3f9a2c',
        terrariumId: 't-gecko',
        status: DeviceStatus.online,
        firmwareVersion: '1.2.0',
        samplingIntervalSec: interval,
        lastSeenAt: anchor,
        rssiDbm: -63,
        uptimeS: 86400 * 3 + 4210,
        heapKb: 142,
        serialHint: 'node A · hot side',
      ),
      DeviceRecord(
        id: 'sr-71b0e5',
        terrariumId: 't-quarantine',
        status: DeviceStatus.offline,
        firmwareVersion: '1.1.4',
        samplingIntervalSec: interval,
        // 42 minutes of silence: past the 30-minute line that makes DeviceSilent Critical (BR-07.2).
        lastSeenAt: anchor.subtract(const Duration(minutes: 42)),
        rssiDbm: -81,
        batteryPct: 64,
        uptimeS: 12800,
        heapKb: 98,
        serialHint: 'node B · nursery shelf',
      ),
      DeviceRecord(
        id: 'sr-9c44d1',
        terrariumId: null,
        status: DeviceStatus.provisioning,
        firmwareVersion: '1.2.0',
        samplingIntervalSec: interval,
        claimCode: 'K7M2QP4T',
        claimCodeExpiresAt: anchor.add(
          const Duration(minutes: 11, seconds: 42),
        ),
        rssiDbm: -55,
        heapKb: 151,
        serialHint: 'node C · spare',
      ),
    ];

    final events = _eventsFor(anchor, today);
    final silences = <SilenceWindow>[];

    final history = <String, List<MetricSample>>{};
    final latest = <String, Map<String, MetricSample>>{};
    final engineResults = <String, EngineResult>{};
    final summaries = <String, List<DailySummary>>{};
    final allAlerts = <AlertEpisode>[];
    final allNotifications = <NotificationRecord>[];

    // Only the first two terrariums have ever had a node bound; the third is the empty state.
    for (final (index, terrarium) in terrariums.take(2).indexed) {
      final device = devices.firstWhere(
        (candidate) => candidate.terrariumId == terrarium.id,
      );
      final profile = terrarium.profile;
      final from = today.subtract(Duration(days: historyDays - 1));
      // An offline device stops contributing data at its last heartbeat, so its silence is visible in the history
      // rather than only in the device row.
      final until = device.status.isReporting
          ? anchor
          : (device.lastSeenAt ?? anchor);

      final samples = _generateHistory(
        profile: profile,
        from: from,
        until: until,
        intervalSeconds: interval,
        events: events[terrarium.id] ?? const [],
        darkDays: _darkDaysFor(terrarium.id, today),
      );

      history[terrarium.id] = samples;
      latest[terrarium.id] = _latestPerMetric(samples);

      final resolver = ThresholdResolver(profile: profile);

      // The engine is run exactly as the server will run it: over the entire history, with the silence watchdog
      // pointed at the anchor so an offline device is detected instead of merely recorded as offline.
      final result = ThresholdEngine().run(
        samples: samples,
        resolver: resolver,
        timeline: profile.phaseTimeline,
        terrariumName: terrarium.name,
        terrariumId: terrarium.id,
        samplingInterval: const Duration(seconds: interval),
        silenceWatchUntil: device.status.isReporting ? null : anchor,
        // The engine numbers its alerts from 1 because in production a database sequence does. The fixture gives each
        // terrarium its own range so the ids are unique across the world without pretending to own a sequence.
        startingAlertId: (index + 1) * 100,
      );
      engineResults[terrarium.id] = result;
      allAlerts.addAll(result.alerts);
      allNotifications.addAll(result.notifications);

      summaries[terrarium.id] = _summarise(
        terrarium: terrarium,
        samples: samples,
        resolver: resolver,
        result: result,
        from: from,
        until: anchor,
        intervalSeconds: interval,
        silences: silences,
      );
    }

    allAlerts.sort((a, b) => b.triggeredAt.compareTo(a.triggeredAt));

    return FakeWorld(
      anchor: anchor,
      users: const [
        FakeUser(
          id: 'u-linh',
          name: 'Linh Trần',
          email: 'linh@example.com',
          role: UserRole.owner,
          telegramChatId: '184920117',
          preferences: NotificationPreferences(),
        ),
        FakeUser(
          id: 'u-nam',
          name: 'Nam Phạm',
          email: 'nam@example.com',
          role: UserRole.technician,
          preferences: NotificationPreferences(
            channels: {Channel.inApp, Channel.email},
            minSeverity: Severity.critical,
          ),
        ),
      ],
      terrariums: terrariums,
      devices: devices,
      alerts: allAlerts,
      notifications: allNotifications,
      history: history,
      latest: latest,
      summaries: summaries,
      engineResults: engineResults,
      silences: silences,
      activeTerrariumId: 't-gecko',
      historyFrom: today.subtract(Duration(days: historyDays - 1)),
      historyInterval: Duration(seconds: interval),
    );
  }

  static final _createdAt = DateTime(2026, 8, 14);

  /// The injected events per terrarium.
  ///
  /// Each entry states *why* it exists, because fake data that cannot explain itself becomes a fixture nobody dares
  /// to change — and a demo that cannot answer "what is this?".
  static Map<String, List<MetricEvent>> _eventsFor(
    DateTime anchor,
    DateTime today,
  ) => {
    't-gecko': [
      // An ongoing heat-mat failure: critical in either phase, so the open alert on the dashboard is the same
      // incident whether the app is opened at noon or at midnight.
      MetricEvent(
        metric: 'tempC',
        start: anchor.subtract(const Duration(minutes: 22)),
        duration: const Duration(minutes: 300),
        toValue: 35.4,
        rampMinutes: 14,
        label: 'Heat mat thermostat stuck on — ongoing',
      ),
      // A critical excursion yesterday that escalated and then recovered: the Alert detail screen's material.
      MetricEvent(
        metric: 'tempC',
        start: today
            .subtract(const Duration(days: 1))
            .add(const Duration(hours: 13, minutes: 10)),
        duration: const Duration(minutes: 55),
        toValue: 35.2,
        rampMinutes: 10,
        label: 'Thermostat failure — escalated, then recovered',
      ),
      // A warning that never escalated, two days ago, so the inbox shows both severities and both outcomes.
      MetricEvent(
        metric: 'tempC',
        start: today
            .subtract(const Duration(days: 2))
            .add(const Duration(hours: 14)),
        duration: const Duration(minutes: 45),
        toValue: 33.6,
        rampMinutes: 10,
        label: 'Lid left ajar — warning only',
      ),
      // A night-time spike, and the reason it exists: it is the only incident that lands inside quiet hours
      // (22:00–06:00), so it is what puts suppressed rows in the notification log. Without it every notification in
      // the fixture is delivered and the suppression table the report needs has nothing to count.
      MetricEvent(
        metric: 'tempC',
        start: today
            .subtract(const Duration(days: 1))
            .add(const Duration(hours: 23, minutes: 20)),
        duration: const Duration(minutes: 55),
        toValue: 33.4,
        rampMinutes: 10,
        label: 'Night spike — warning suppressed, escalation delivered',
      ),
      // A dry spell: the humidity card gets a real event too, not just temperature.
      MetricEvent(
        metric: 'humidityPct',
        start: today
            .subtract(const Duration(days: 1))
            .add(const Duration(hours: 9)),
        duration: const Duration(minutes: 300),
        toValue: 25.5,
        rampMinutes: 25,
        label: 'Dry spell during a heat spike',
      ),
    ],
    't-quarantine': [
      MetricEvent(
        metric: 'tempC',
        start: today
            .subtract(const Duration(days: 1))
            .add(const Duration(hours: 15)),
        duration: const Duration(minutes: 40),
        toValue: 30.6,
        rampMinutes: 8,
        label: 'Warm afternoon — warning only, resolved',
      ),
      MetricEvent(
        metric: 'humidityPct',
        start: today
            .subtract(const Duration(days: 2))
            .add(const Duration(hours: 11)),
        duration: const Duration(minutes: 240),
        toValue: 46,
        rampMinutes: 20,
        label: 'Misting schedule missed',
      ),
    ],
  };

  /// Days on which the lamp "failed", so the accumulated light rule has something to report.
  static Set<DateTime> _darkDaysFor(String terrariumId, DateTime today) {
    if (terrariumId != 't-quarantine') {
      return const {};
    }
    // The tropical profile needs 10 h above 500 lx; a dead lamp is exactly what the accumulated rule is for.
    return {today.subtract(const Duration(days: 2))};
  }

  /// One sample per metric per minute, from [from] to [until].
  static List<MetricSample> _generateHistory({
    required SpeciesProfile profile,
    required DateTime from,
    required DateTime until,
    required int intervalSeconds,
    required List<MetricEvent> events,
    required Set<DateTime> darkDays,
  }) {
    final samples = <MetricSample>[];
    // The tropical profile has no surface probe in this fixture; the other two do.
    final metrics = <String>[
      for (final definition in MetricCatalog.all)
        if (definition.code != 'surfaceTempC' || profile.surfaceProbeFitted)
          definition.code,
    ];

    for (
      var at = from;
      !at.isAfter(until);
      at = at.add(Duration(seconds: intervalSeconds))
    ) {
      // A dead lamp stays dark all day; the reader can see the light hours collapse in the summary.
      final isDarkDay = darkDays.any(
        (day) =>
            day.year == at.year && day.month == at.month && day.day == at.day,
      );
      final isDayPhase = profile.phaseTimeline.phaseAt(at) == Phase.day;

      // The air temperature is computed first because the surface probe is *derived* from it: the rock follows the
      // air (with an offset), so one heat event raises both, exactly as the physical probe would see it.
      final air = metrics.contains('tempC')
          ? _valueAt(
              metric: 'tempC',
              at: at,
              profile: profile,
              events: events,
              isDayPhase: isDayPhase,
              lampDead: isDarkDay,
            )
          : 0.0;

      for (final metric in metrics) {
        final value = metric == 'tempC'
            ? air
            : _valueAt(
                metric: metric,
                at: at,
                profile: profile,
                events: events,
                isDayPhase: isDayPhase,
                lampDead: isDarkDay,
                airTemperature: air,
              );
        samples.add(
          MetricSample(
            metric: metric,
            value: value,
            recordedAt: at,
            ingestedAt: at.add(const Duration(seconds: 4)),
          ),
        );
      }
    }

    // The kernel sorts internally, but a stable order makes the fixture reproducible when printed.
    samples.sort((a, b) {
      final byTime = a.recordedAt.compareTo(b.recordedAt);
      return byTime != 0 ? byTime : a.metric.compareTo(b.metric);
    });
    return samples;
  }

  /// The value of one metric at one instant: a phase-aware baseline, deterministic noise, then any injected events.
  static double _valueAt({
    required String metric,
    required DateTime at,
    required SpeciesProfile profile,
    required List<MetricEvent> events,
    required bool isDayPhase,
    required bool lampDead,
    double? airTemperature,
  }) {
    var value = _baseline(
      metric: metric,
      at: at,
      profile: profile,
      isDayPhase: isDayPhase,
      lampDead: lampDead,
      airTemperature: airTemperature,
    );

    for (final event in events) {
      if (event.metric != metric || !event.covers(at)) {
        continue;
      }
      final total = event.duration.inMinutes.toDouble();
      final elapsed = at.difference(event.start).inMinutes.toDouble();
      final baselineAtStart = _baseline(
        metric: metric,
        at: event.start,
        profile: profile,
        isDayPhase: profile.phaseTimeline.phaseAt(event.start) == Phase.day,
        lampDead: lampDead,
        airTemperature: airTemperature,
      );
      final target = event.toValue ?? baselineAtStart + (event.peakDelta ?? 0);
      final ramp = math.min(event.rampMinutes, total / 2);

      if (elapsed < ramp) {
        value = _lerp(baselineAtStart, target, elapsed / ramp);
      } else if (elapsed > total - ramp) {
        value = _lerp(target, value, (total - elapsed) / ramp);
      } else {
        value = target;
      }
    }

    return double.parse(value.toStringAsFixed(2));
  }

  /// The undisturbed value: the middle of the band currently in force, with a small daily wave.
  static double _baseline({
    required String metric,
    required DateTime at,
    required SpeciesProfile profile,
    required bool isDayPhase,
    required bool lampDead,
    double? airTemperature,
  }) {
    final minutesOfDay = at.hour * 60 + at.minute;
    final wave = math.sin((minutesOfDay / 1440) * 2 * math.pi);

    switch (metric) {
      case 'tempC':
        final band = _bandFor(profile, 'tempC', isDayPhase);
        final mid = (band.targetMin + band.targetMax) / 2;
        final half = (band.targetMax - band.targetMin) / 2;
        return mid + wave * half * 0.45 + _noise(at, 0.12);

      case 'surfaceTempC':
        // Derived from the air value rather than from another mid-point, so one heat event raises both readings.
        // The clamp keeps a *good* day inside the surface band: the probe sits on a rock that is legitimately
        // warmer than the air, and that is the point of the band — not a permanent violation of it.
        final band = _bandFor(profile, 'surfaceTempC', isDayPhase);
        final air =
            airTemperature ??
            _baseline(
              metric: 'tempC',
              at: at,
              profile: profile,
              isDayPhase: isDayPhase,
              lampDead: lampDead,
            );
        final raw = air + 2.4 + (isDayPhase ? 0.9 : 0) + _noise(at, 0.15);
        return raw.clamp(band.targetMin + 0.8, band.targetMax - 0.8);

      case 'humidityPct':
        final band = _bandFor(profile, 'humidityPct', isDayPhase);
        final mid = (band.targetMin + band.targetMax) / 2;
        final half = (band.targetMax - band.targetMin) / 2;
        // Inverted against the daily wave: warmer air is drier air.
        return mid - wave * half * 0.35 + _noise(at, 0.8);

      case 'lightLux':
        if (!isDayPhase || lampDead) {
          return 4 + _noise(at, 1.5).abs();
        }
        final ramp = math.sin(math.pi * _dayProgress(at));
        return profile.lightThresholdLux * (1.15 + 1.05 * ramp) +
            _noise(at, 40);

      case 'uvIndex':
        if (!isDayPhase || lampDead) {
          return 0;
        }
        final band = _bandFor(profile, 'uvIndex', isDayPhase);
        final mid = (band.targetMin + band.targetMax) / 2;
        return mid +
            math.sin(math.pi * _dayProgress(at)) * mid * 0.35 +
            _noise(at, 0.05);

      default:
        return 0;
    }
  }

  /// How far through the photoperiod we are, 0 at lights-on and 1 at lights-off.
  static double _dayProgress(DateTime at) {
    final minutesOfDay = at.hour * 60 + at.minute;
    return ((minutesOfDay - 7 * 60) / (12 * 60)).clamp(0.0, 1.0);
  }

  static ThresholdBand _bandFor(
    SpeciesProfile profile,
    String metric,
    bool isDayPhase,
  ) {
    final bands = profile.bandsFor(metric);
    final phase = isDayPhase ? Phase.day : Phase.night;
    for (final band in bands) {
      if (band.phase == phase) {
        return band;
      }
    }
    return bands.first;
  }

  /// Deterministic jitter. No `Random()` anywhere: a prototype whose screenshots change on every rebuild cannot be
  /// used as evidence in a report, and a fixture that cannot be reproduced cannot be debugged.
  static double _noise(DateTime at, double amplitude) {
    final slot = at.millisecondsSinceEpoch ~/ 300000;
    final value = ((slot * 37 + 11) % 21) - 10;
    return value / 10 * amplitude;
  }

  static double _lerp(double a, double b, double t) => a + (b - a) * t;

  static Map<String, MetricSample> _latestPerMetric(
    List<MetricSample> samples,
  ) {
    final latest = <String, MetricSample>{};
    for (final sample in samples) {
      latest[sample.metric] = sample;
    }
    return latest;
  }

  /// Builds the per-day summaries, carrying the alert counts of the day they belong to.
  static List<DailySummary> _summarise({
    required Terrarium terrarium,
    required List<MetricSample> samples,
    required ThresholdResolver resolver,
    required EngineResult result,
    required DateTime from,
    required DateTime until,
    required int intervalSeconds,
    required List<SilenceWindow> silences,
  }) {
    const builder = DailySummaryBuilder(gapTolerance: Duration(minutes: 3));
    final summaries = <DailySummary>[];

    for (
      var day = from;
      !day.isAfter(until);
      day = day.add(const Duration(days: 1))
    ) {
      final dayEnd = DateTime(
        day.year,
        day.month,
        day.day,
      ).add(const Duration(days: 1));
      final cappedEnd = dayEnd.isAfter(until) ? until : dayEnd;

      final alertsThatDay = result.alerts
          .where(
            (alert) =>
                !alert.triggeredAt.isBefore(day) &&
                alert.triggeredAt.isBefore(dayEnd),
          )
          .toList();
      final elapsedSeconds = cappedEnd.difference(day).inSeconds;

      summaries.add(
        builder.build(
          localDate: day,
          samples: samples,
          resolver: resolver,
          timeline: terrarium.profile.phaseTimeline,
          requiredLightHours: terrarium.profile.minLightHoursPerDay,
          lightThresholdLux: terrarium.profile.lightThresholdLux,
          timeZoneLabel: terrarium.timeZoneId,
          // Coverage is a sample count against what the bound window expected, not against a whole day: a terrarium
          // created this morning must not read as 30 % covered (BR-14.6, §5.5).
          expectedSamples: (elapsedSeconds / intervalSeconds).ceil(),
          alertCount: alertsThatDay.length,
          criticalAlertCount: alertsThatDay
              .where((alert) => alert.severity == Severity.critical)
              .length,
          silenceWindows: silenceMinutesIn(
            from: day,
            to: cappedEnd,
            silences: silences,
          ),
          computedAt: cappedEnd,
        ),
      );
    }

    return summaries;
  }

  /// Minutes of a window covered by a silence (BR-12.6: silences stay visible in the record).
  static int silenceMinutesIn({
    required DateTime from,
    required DateTime to,
    required List<SilenceWindow> silences,
  }) {
    var minutes = 0;
    for (final silence in silences) {
      final start = silence.from.isAfter(from) ? silence.from : from;
      final end = silence.until.isBefore(to) ? silence.until : to;
      if (end.isAfter(start)) {
        minutes += end.difference(start).inMinutes;
      }
    }
    return minutes;
  }
}
