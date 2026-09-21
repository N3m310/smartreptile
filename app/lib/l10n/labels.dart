import 'package:flutter/material.dart';

import '../core/formatting.dart';
import '../core/status.dart';
import 'generated/app_localizations.dart';

/// Maps API status values to localised labels.
///
/// One place, exhaustive switch: a new status cannot be added to the API without the compiler forcing a
/// decision here, and the widget layer never contains user-facing literals (NFR-06, TC-W-18).
extension MetricStatusLabel on MetricStatus {
  /// Localised label for this status.
  String label(AppLocalizations l10n) {
    switch (this) {
      case MetricStatus.inRange:
        return l10n.statusInRange;
      case MetricStatus.outOfRange:
        return l10n.statusOutOfRange;
      case MetricStatus.critical:
        return l10n.statusCritical;
      case MetricStatus.noData:
        return l10n.statusNoData;
      case MetricStatus.unavailable:
        return l10n.statusUnavailable;
      case MetricStatus.maintenance:
        return l10n.statusMaintenance;
    }
  }
}

/// Localised metric name for a metric code.
///
/// The server owns the metric dictionary; these strings exist so a value is never displayed as a raw code.
String metricDisplayName(AppLocalizations l10n, String metricCode) {
  switch (metricCode) {
    case 'tempC':
      return l10n.metricTempC;
    case 'humidityPct':
      return l10n.metricHumidityPct;
    case 'lightLux':
      return l10n.metricLightLux;
    case 'uvIndex':
      return l10n.metricUvIndex;
    case 'surfaceTempC':
      return l10n.metricSurfaceTempC;
    default:
      return metricCode;
  }
}

/// Localised "12 s ago" style text for a sample age, using the plural buckets from `core/formatting.dart`.
String localisedAge(AppLocalizations l10n, Duration age) {
  switch (MetricFormat.ageBucket(age)) {
    case AgeBucket.seconds:
      return l10n.lastUpdatedSeconds(age.inSeconds);
    case AgeBucket.minutes:
      return l10n.lastUpdatedMinutes(age.inMinutes);
    case AgeBucket.hours:
      return l10n.lastUpdatedHours(age.inHours);
  }
}

/// Icon for a device status string.
IconData deviceStatusIcon(String status) =>
    status == 'online' ? Icons.circle : Icons.circle_outlined;

/// Colour for a device status string.
Color deviceStatusColor(String status) =>
    status == 'online' ? const Color(0xFF2E7D32) : const Color(0xFF616161);
