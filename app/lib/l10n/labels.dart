import 'package:flutter/material.dart';

import '../core/formatting.dart';
import '../core/problem.dart';
import '../core/status.dart';
import '../theme/app_theme.dart';
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
    status == 'online' ? AppColors.statusNormal : AppColors.textMuted;

// -------------------------------------------------------------------------------------------------------------
// The administrator's surfaces. Same rule as above: the API's vocabulary is mapped to a label in exactly one
// place, so an unknown value degrades to "Unknown" instead of reaching a screen as a raw enum name.
// -------------------------------------------------------------------------------------------------------------

/// Localised label for an alert family key (`enclosure` / `species`).
String alertFamilyLabel(AppLocalizations l10n, String key) => switch (key) {
  'enclosure' => l10n.adminFamilyEnclosure,
  'species' => l10n.adminFamilySpecies,
  _ => l10n.adminUnknown,
};

/// Localised sentence describing what an alert family watches.
String alertFamilyDetail(AppLocalizations l10n, String key) => switch (key) {
  'enclosure' => l10n.adminFamilyEnclosureDetail,
  'species' => l10n.adminFamilySpeciesDetail,
  _ => l10n.adminUnknown,
};

/// Icon for an alert family key.
IconData alertFamilyIcon(String key) => switch (key) {
  'enclosure' => Icons.water_drop_outlined,
  'species' => Icons.thermostat,
  _ => Icons.help_outline,
};

/// Colour for an alert family key.
Color alertFamilyColor(String key) => switch (key) {
  'enclosure' => const Color(0xFF2E86C1),
  'species' => AppColors.accent,
  _ => AppColors.textMuted,
};

/// Localised label for a device status as the REST surface spells it (`Online`, `Provisioning`, …).
String deviceStatusLabel(AppLocalizations l10n, String status) => switch (status) {
  'Online' => l10n.adminDeviceOnline,
  'Offline' => l10n.adminDeviceOffline,
  'Provisioning' => l10n.adminDeviceProvisioning,
  'Revoked' => l10n.adminDeviceRevoked,
  'Maintenance' => l10n.adminDeviceMaintenance,
  _ => l10n.adminUnknown,
};

/// Colour for a device status.
Color deviceStatusPillColor(String status) => switch (status) {
  'Online' => AppColors.statusNormal,
  'Provisioning' => AppColors.statusWarning,
  'Maintenance' => AppColors.textMuted,
  _ => AppColors.statusDanger,
};

/// Localised label for an alert state (`Open`, `Acknowledged`, `Resolved`).
String alertStateLabel(AppLocalizations l10n, String state) => switch (state) {
  'Open' => l10n.alertStateOpen,
  'Acknowledged' => l10n.alertStateAcknowledged,
  'Resolved' => l10n.alertStateResolved,
  _ => l10n.adminUnknown,
};

/// Localised label for an alert severity.
String severityLabel(AppLocalizations l10n, String severity) => switch (severity) {
  'Info' => l10n.severityInfo,
  'Warning' => l10n.severityWarning,
  'Critical' => l10n.severityCritical,
  _ => l10n.adminUnknown,
};

/// Colour for an alert severity. Critical reads as the danger token, not as a shade of the same red as Warning.
Color severityColor(String severity) => switch (severity) {
  'Info' => AppColors.textMuted,
  'Warning' => AppColors.statusWarning,
  'Critical' => AppColors.statusDanger,
  _ => AppColors.textMuted,
};

/// Localised label for what produced an alert.
String alertSourceLabel(AppLocalizations l10n, String source) => switch (source) {
  'Threshold' => l10n.alertSourceThreshold,
  'DeviceSilent' => l10n.alertSourceDeviceSilent,
  'SensorFault' => l10n.alertSourceSensorFault,
  'DeviceClockSkew' => l10n.alertSourceDeviceClockSkew,
  'Gradient' => l10n.alertSourceGradient,
  'LightDeficit' => l10n.alertSourceLightDeficit,
  _ => l10n.adminUnknown,
};

/// Localised label for an enclosure's lifecycle status.
String terrariumStatusLabel(AppLocalizations l10n, String status) =>
    switch (status) {
      'Active' => l10n.terrariumStatusActive,
      'Deleted' => l10n.terrariumStatusDeleted,
      _ => l10n.adminUnknown,
    };

/// The metric keys of a family, as the names a keeper reads rather than as API keys.
String metricListLabel(AppLocalizations l10n, List<String> metrics) => metrics
    .map((code) => metricDisplayName(l10n, code))
    .join(', ');

/// Turns an [AppFailure] into a sentence, by its `messageKey`.
///
/// The keys account and role management can answer with are listed here and nothing else: a message key with no
/// string in the deck would render as "adminErrorSomething", which is worse than the generic sentence.
String failureMessage(AppLocalizations l10n, AppFailure failure) {
  switch (failure.messageKey) {
    case 'invalidCredentials':
      return l10n.invalidCredentials;
    case 'accountLocked':
      return l10n.accountLocked;
    case 'rateLimited':
      return l10n.rateLimited;
    case 'unauthenticated':
      return l10n.unauthenticated;
    case 'registrationInvalid':
      return l10n.registrationInvalid;
    case 'usernameTaken':
      return l10n.usernameTaken;
    case 'recoveryFailed':
      return l10n.recoveryFailed;
    case 'identifierUnknown':
      return l10n.identifierUnknown;
    case 'errorForbidden':
      return l10n.errorForbidden;
    case 'adminAccountNotFound':
      return l10n.adminAccountNotFound;
    case 'adminErrorSelfChange':
      return l10n.adminErrorSelfChange;
    case 'adminErrorLastAdministrator':
      return l10n.adminErrorLastAdministrator;
    case 'adminErrorAccountInUse':
      return l10n.adminErrorAccountInUse;
    case 'adminErrorAccountInvalid':
      return l10n.adminErrorAccountInvalid;
    case 'passwordPolicyViolation':
      return l10n.passwordPolicyViolation;
    case 'adminErrorRoleInvalid':
      return l10n.adminErrorRoleInvalid;
    case 'adminErrorRoleNotFound':
      return l10n.adminErrorRoleNotFound;
    case 'adminErrorRoleInUse':
      return l10n.adminErrorRoleInUse;
    case 'adminErrorSystemRole':
      return l10n.adminErrorSystemRole;
    case 'adminErrorPermissionUnknown':
      return l10n.adminErrorPermissionUnknown;
    case 'adminErrorConflict':
      return l10n.adminErrorConflict;
    case 'errorBackendUnreachable':
      return l10n.errorBackendUnreachable;
    default:
      return l10n.errorGeneric;
  }
}

/// Localised name of a permission group. The group arrives from the catalogue as an English word, so an unknown one
/// is shown as it came rather than as "Unknown": a group this build has never heard of is still information.
String permissionGroupLabel(AppLocalizations l10n, String group) =>
    switch (group) {
      'Enclosure' => l10n.adminGroupEnclosure,
      'Alerts' => l10n.adminGroupAlerts,
      'Account' => l10n.adminGroupAccount,
      'Catalogue' => l10n.adminGroupCatalogue,
      'Accounts' => l10n.adminGroupAccounts,
      'Whole system' => l10n.adminGroupWholeSystem,
      _ => group,
    };

/// Localised name of a permission code, falling back to the catalogue's own label for a code this build does not
/// know — the app translates the codes it ships with, and passes through anything a newer server sends.
String permissionNameLabel(AppLocalizations l10n, String code, String fallback) =>
    switch (code) {
      'terrarium.read' => l10n.permissionTerrariumReadName,
      'terrarium.write' => l10n.permissionTerrariumWriteName,
      'device.read' => l10n.permissionDeviceReadName,
      'device.write' => l10n.permissionDeviceWriteName,
      'device.provision' => l10n.permissionDeviceProvisionName,
      'alert.read' => l10n.permissionAlertReadName,
      'alert.write' => l10n.permissionAlertWriteName,
      'silence.write' => l10n.permissionSilenceWriteName,
      'notification.read' => l10n.permissionNotificationReadName,
      'audit.read' => l10n.permissionAuditReadName,
      'species.read' => l10n.permissionSpeciesReadName,
      'species.write' => l10n.permissionSpeciesWriteName,
      'user.read' => l10n.permissionUserReadName,
      'user.manage' => l10n.permissionUserManageName,
      'data.read.any' => l10n.permissionDataReadAnyName,
      'data.write.any' => l10n.permissionDataWriteAnyName,
      _ => fallback,
    };

/// Localised description of a permission code, with the same fallback rule as [permissionNameLabel].
String permissionDescriptionLabel(
  AppLocalizations l10n,
  String code,
  String fallback,
) => switch (code) {
  'terrarium.read' => l10n.permissionTerrariumReadDesc,
  'terrarium.write' => l10n.permissionTerrariumWriteDesc,
  'device.read' => l10n.permissionDeviceReadDesc,
  'device.write' => l10n.permissionDeviceWriteDesc,
  'device.provision' => l10n.permissionDeviceProvisionDesc,
  'alert.read' => l10n.permissionAlertReadDesc,
  'alert.write' => l10n.permissionAlertWriteDesc,
  'silence.write' => l10n.permissionSilenceWriteDesc,
  'notification.read' => l10n.permissionNotificationReadDesc,
  'audit.read' => l10n.permissionAuditReadDesc,
  'species.read' => l10n.permissionSpeciesReadDesc,
  'species.write' => l10n.permissionSpeciesWriteDesc,
  'user.read' => l10n.permissionUserReadDesc,
  'user.manage' => l10n.permissionUserManageDesc,
  'data.read.any' => l10n.permissionDataReadAnyDesc,
  'data.write.any' => l10n.permissionDataWriteAnyDesc,
  _ => fallback,
};
