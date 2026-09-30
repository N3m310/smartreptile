/// Daily environmental summary and the exposure index (FR-14, BR-14.1…BR-14.6, `03-implementation/06` §5).
///
/// The maths here is the reason the report can say a day was *worse* rather than merely *longer*: two days with the
/// same 60 out-of-range minutes are not equivalent when one spent them 2 °C above target and the other 6 °C.
///
/// Computed client-side for the prototype, from fake data. The production version is a scheduler job writing
/// `DailyEnvironmentalSummary` rows (`03-implementation/06` §6) — the formulas are the same, the clock is not.
library;

import 'domain.dart';
import 'species_profiles.dart';

/// Summary of one metric over one local day.
class MetricDailyStats {
  /// Creates stats.
  const MetricDailyStats({
    required this.metric,
    required this.min,
    required this.max,
    required this.timeWeightedAverage,
    required this.outOfRangeMinutes,
    required this.minutesWithData,
    required this.hotExposureDegCHours,
    required this.coldExposureDegCHours,
    required this.dryHours,
    required this.wetHours,
    required this.hoursAboveLightThreshold,
    required this.bandSource,
    this.band,
  });

  /// Metric code.
  final String metric;

  /// Lowest observed value, or null when there was no data.
  final double? min;

  /// Highest observed value, or null when there was no data.
  final double? max;

  /// Time-weighted average — *not* a plain mean, so a gap cannot skew it (`03-implementation/06` §5.5).
  final double? timeWeightedAverage;

  /// Minutes outside the target band, in the phase in force at that minute.
  final int outOfRangeMinutes;

  /// Minutes that had an interpolated value.
  final int minutesWithData;

  /// `Σ max(0, T − TargetMax) · Δt` in degree-hours (BR-14.3).
  final double hotExposureDegCHours;

  /// `Σ max(0, TargetMin − T) · Δt` in degree-hours.
  final double coldExposureDegCHours;

  /// Humidity below the target band, in %-hours.
  final double dryHours;

  /// Humidity above the target band, in %-hours.
  final double wetHours;

  /// Hours at or above the profile's light threshold.
  final double hoursAboveLightThreshold;

  /// Where the band came from (BR-10.3) — a summary is meaningless without it.
  final BandSource? bandSource;

  /// The band used, for the table's "band" column.
  final ThresholdBand? band;

  /// Total exposure in degree-hours, hot plus cold (BR-14.2's `TempExposureDegC_Hours`).
  double get totalExposureDegCHours =>
      hotExposureDegCHours + coldExposureDegCHours;

  /// Compliance for this metric: `100 × (1 − outOfRange / minutesWithData)` (§5.4).
  ///
  /// Meaningless on its own — BR-14.6 requires it to be rendered next to [DailySummary.coveragePct].
  double get compliancePct => minutesWithData == 0
      ? 0
      : 100 * (1 - outOfRangeMinutes / minutesWithData);
}

/// One local day of a terrarium (BR-14.2).
class DailySummary {
  /// Creates a summary.
  const DailySummary({
    required this.localDate,
    required this.timeZoneLabel,
    required this.coveragePct,
    required this.samplesReceived,
    required this.expectedSamples,
    required this.perMetric,
    required this.alertCount,
    required this.criticalAlertCount,
    required this.lightHours,
    required this.requiredLightHours,
    required this.silenceWindows,
    this.computedAt,
  });

  /// The local date this summarises.
  final DateTime localDate;

  /// Terrarium time zone label, shown because "today" is not universal (BR-14.1).
  final String timeZoneLabel;

  /// `100 × received / expected` (§5.5).
  final double coveragePct;

  /// Samples received in the window.
  final int samplesReceived;

  /// Samples expected in the window given the sampling interval.
  final int expectedSamples;

  /// Per-metric statistics.
  final Map<String, MetricDailyStats> perMetric;

  /// Alerts that opened on this day.
  final int alertCount;

  /// Alerts that reached Critical on this day.
  final int criticalAlertCount;

  /// Accumulated light hours.
  final double lightHours;

  /// Light hours the profile requires.
  final int requiredLightHours;

  /// Minutes of the day covered by a silence window (BR-12.6 keeps these visible).
  final int silenceWindows;

  /// When the summary was computed; the field that makes a late back-fill's recomputation auditable.
  final DateTime? computedAt;

  /// `coverage < 80` (§5.5) — the flag that stops a 40 %-data day from reading as a healthy one.
  bool get isLowConfidence => coveragePct < 80;

  /// Overall compliance across metrics that had data, for the report headline.
  double get overallCompliancePct {
    final withData = perMetric.values
        .where((stats) => stats.minutesWithData > 0)
        .toList();
    if (withData.isEmpty) {
      return 0;
    }
    return withData
            .map((stats) => stats.compliancePct)
            .reduce((a, b) => a + b) /
        withData.length;
  }

  /// Light deficit against the profile's requirement (§5.3).
  double get lightDeficitHours =>
      (requiredLightHours - lightHours).clamp(0, double.infinity).toDouble();

  /// True when the day failed the accumulated light rule (§4).
  bool get hasLightDeficit => lightDeficitHours > 0;
}

/// Builds [DailySummary] values from a minute series.
///
/// The interpolation rule is the one the chart needs too: **gaps are null, never interpolated across**
/// (BR-09.5). [gapTolerance] is how long a hole may be before interpolation is considered a lie.
class DailySummaryBuilder {
  const DailySummaryBuilder({this.gapTolerance = const Duration(minutes: 5)});

  /// Longest hole that may be bridged by linear interpolation.
  final Duration gapTolerance;

  /// Builds a summary for one local day.
  ///
  /// [samples] may contain any number of metrics; each is accumulated separately.
  /// [expectedSamples] is supplied by the caller (it depends on when a device was bound to the terrarium), and
  /// defaults to the sample count of the widest metric so a scenario summary is never 0 %.
  DailySummary build({
    required DateTime localDate,
    required List<MetricSample> samples,
    required ThresholdResolver resolver,
    required PhaseTimeline timeline,
    required int requiredLightHours,
    double lightThresholdLux = 1000,
    String timeZoneLabel = 'Asia/Ho_Chi_Minh',
    int? expectedSamples,
    int alertCount = 0,
    int criticalAlertCount = 0,
    int silenceWindows = 0,
    DateTime? computedAt,
    Duration samplingInterval = const Duration(seconds: 60),
  }) {
    final dayStart = DateTime(localDate.year, localDate.month, localDate.day);
    final dayEnd = dayStart.add(const Duration(days: 1));

    final byMetric = <String, List<MetricSample>>{};
    for (final sample in samples) {
      if (sample.recordedAt.isBefore(dayStart) ||
          !sample.recordedAt.isBefore(dayEnd)) {
        continue;
      }
      byMetric.putIfAbsent(sample.metric, () => []).add(sample);
    }

    final stats = <String, MetricDailyStats>{};
    for (final entry in byMetric.entries) {
      stats[entry.key] = _accumulate(
        metric: entry.key,
        samples: entry.value,
        dayStart: dayStart,
        dayEnd: dayEnd,
        resolver: resolver,
        timeline: timeline,
        lightThresholdLux: lightThresholdLux,
      );
    }

    final received = byMetric.values.isEmpty
        ? 0
        : byMetric.values.map((list) => list.length).reduce((a, b) => a + b);
    final widest = byMetric.values.isEmpty
        ? 0
        : byMetric.values
              .map((list) => list.length)
              .reduce((a, b) => a > b ? a : b);

    // Clamped at 100: the expected count is derived from a window that includes both endpoints, so a complete day can
    // produce one sample more than "expected" and report 100.08 %. A percentage above 100 is not a measurement, it
    // is an artefact of counting a boundary twice.
    final coverage = expectedSamples == null || expectedSamples == 0
        ? 0.0
        : (100 * widest / expectedSamples).clamp(0.0, 100.0);

    return DailySummary(
      localDate: dayStart,
      timeZoneLabel: timeZoneLabel,
      coveragePct: coverage,
      samplesReceived: received,
      expectedSamples: expectedSamples ?? widest,
      perMetric: stats,
      alertCount: alertCount,
      criticalAlertCount: criticalAlertCount,
      lightHours: stats['lightLux']?.hoursAboveLightThreshold ?? 0,
      requiredLightHours: requiredLightHours,
      silenceWindows: silenceWindows,
      computedAt: computedAt,
    );
  }

  MetricDailyStats _accumulate({
    required String metric,
    required List<MetricSample> samples,
    required DateTime dayStart,
    required DateTime dayEnd,
    required ThresholdResolver resolver,
    required PhaseTimeline timeline,
    required double lightThresholdLux,
  }) {
    final ordered = [...samples]
      ..sort((a, b) => a.recordedAt.compareTo(b.recordedAt));

    double? min;
    double? max;
    var weightedSum = 0.0;
    var weightedMinutes = 0;
    var outOfRangeMinutes = 0;
    var minutesWithData = 0;
    var hot = 0.0;
    var cold = 0.0;
    var dry = 0.0;
    var wet = 0.0;
    var lightMinutes = 0;
    BandSource? bandSource;
    ThresholdBand? bandUsed;

    const stepMinutes = 1;

    // Two pointers instead of a rescan per minute. The naive version was O(minutes × samples), which is fine for one
    // worked example and far too slow once the fake world asks for eight days × five metrics × three terrariums.
    var cursor = 0;

    for (
      var minute = dayStart;
      minute.isBefore(dayEnd);
      minute = minute.add(const Duration(minutes: stepMinutes))
    ) {
      while (cursor + 1 < ordered.length &&
          !ordered[cursor + 1].recordedAt.isAfter(minute)) {
        cursor++;
      }

      final value = _interpolateAt(ordered, cursor, minute);
      if (value == null) {
        continue;
      }

      bandSource ??= resolver.resolve(metric, minute).source;
      final band = resolver.resolve(metric, minute).band;
      bandUsed ??= band;

      minutesWithData++;
      weightedSum += value;
      weightedMinutes++;

      final currentMin = min;
      if (currentMin == null || value < currentMin) {
        min = value;
      }
      final currentMax = max;
      if (currentMax == null || value > currentMax) {
        max = value;
      }

      if (metric == 'lightLux') {
        if (value >= lightThresholdLux) {
          lightMinutes++;
        }
        continue;
      }

      if (band == null) {
        continue;
      }

      final hours = stepMinutes / 60;
      if (band.isHot(value)) {
        outOfRangeMinutes++;
        if (metric == 'tempC') {
          hot += (value - band.targetMax) * hours;
        }
        if (metric == 'humidityPct') {
          wet += (value - band.targetMax) * hours;
        }
        if (metric == 'surfaceTempC') {
          hot += (value - band.targetMax) * hours;
        }
      } else if (band.isCold(value)) {
        outOfRangeMinutes++;
        if (metric == 'tempC') {
          cold += (band.targetMin - value) * hours;
        }
        if (metric == 'humidityPct') {
          dry += (band.targetMin - value) * hours;
        }
        if (metric == 'surfaceTempC') {
          cold += (band.targetMin - value) * hours;
        }
      }
    }

    return MetricDailyStats(
      metric: metric,
      min: min,
      max: max,
      timeWeightedAverage: weightedMinutes == 0
          ? null
          : weightedSum / weightedMinutes,
      outOfRangeMinutes: outOfRangeMinutes,
      minutesWithData: minutesWithData,
      hotExposureDegCHours: hot,
      coldExposureDegCHours: cold,
      dryHours: dry,
      wetHours: wet,
      hoursAboveLightThreshold: lightMinutes / 60,
      bandSource: bandSource,
      band: bandUsed,
    );
  }

  /// Linear interpolation with a gap guard (BR-09.5). Returns null when the reading would be invented.
  ///
  /// [cursor] is the index of the last sample at or before [minute], maintained by the caller's walk.
  double? _interpolateAt(
    List<MetricSample> ordered,
    int cursor,
    DateTime minute,
  ) {
    if (ordered.isEmpty) {
      return null;
    }

    final atCursor = ordered[cursor];
    final MetricSample? before = atCursor.recordedAt.isAfter(minute)
        ? null
        : atCursor;
    final MetricSample? after = cursor + 1 < ordered.length
        ? ordered[cursor + 1]
        : null;

    if (before == null && after == null) {
      return null;
    }
    if (before == null) {
      return after!.recordedAt.difference(minute) <= gapTolerance
          ? after.value
          : null;
    }
    if (after == null) {
      return minute.difference(before.recordedAt) <= gapTolerance
          ? before.value
          : null;
    }

    final beforeGap = minute.difference(before.recordedAt);
    final afterGap = after.recordedAt.difference(minute);
    // A reading that lands exactly on the grid minute is the reading, whatever the neighbours are doing. Without this
    // short-circuit a 1-minute series is never usable at a zero gap tolerance, which made every worked example in
    // §5 look like it had no data at all.
    if (beforeGap == Duration.zero) {
      return before.value;
    }
    if (afterGap == Duration.zero) {
      return after.value;
    }
    if (beforeGap > gapTolerance || afterGap > gapTolerance) {
      return null;
    }
    final span = after.recordedAt.difference(before.recordedAt).inSeconds;
    if (span == 0) {
      return before.value;
    }
    final ratio = minute.difference(before.recordedAt).inSeconds / span;
    return before.value + (after.value - before.value) * ratio;
  }

  /// Builds a synthetic day of samples for the prototype's fake history.
  ///
  /// Deterministic by construction — a sine plus a small fixed noise table, never `Random()` — because a prototype
  /// whose screenshots change on every rebuild cannot be used as evidence in a report.
  static List<MetricSample> syntheticDay({
    required DateTime localDay,
    required String metric,
    required double Function(int minute) valueAt,
    int intervalMinutes = 5,
    int defectMinutes = -1,
  }) {
    final samples = <MetricSample>[];
    final dayStart = DateTime(localDay.year, localDay.month, localDay.day);

    for (var minute = 0; minute < 24 * 60; minute += intervalMinutes) {
      if (minute == defectMinutes) {
        continue;
      }
      final at = dayStart.add(Duration(minutes: minute));
      samples.add(
        MetricSample(
          metric: metric,
          value: valueAt(minute),
          recordedAt: at,
          ingestedAt: at.add(const Duration(seconds: 4)),
        ),
      );
    }
    return samples;
  }
}
