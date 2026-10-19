/*
 * Status rendering — the ADR-005 rule, made structural (roadmap 4.17).
 *
 * The screen does not decide whether a value is in range. It renders `status` exactly as `readings/latest`
 * sent it, and shows the band the server said the value was judged against (`target`). That is why this file
 * has a total mapping from the API's five values to a label and a colour and no comparison anywhere: there is
 * no branch here that could look at a temperature and form an opinion about it. A metric the server sent
 * without a band renders with `target: null` and says so, rather than falling back to anything.
 *
 * The mapping is total on purpose. A status the server adds later falls to `Unavailable`'s grey and the raw
 * code, which is a visible gap, instead of defaulting to green — a monitoring UI that paints an unknown state
 * as healthy is worse than one that paints nothing.
 */

import type { ReadingStatus } from '../api/types';
import type { TFunc } from './format';

const STATUS_LABEL_KEY: Record<string, string> = {
  InRange: 'statusInRange',
  OutOfRange: 'statusOutOfRange',
  Critical: 'statusCritical',
  Unavailable: 'statusUnavailable',
  Maintenance: 'statusMaintenance',
};

/** Text and border colours, from the measured palette in `index.css`. Never colour alone (NFR-06). */
const STATUS_TEXT_CLASS: Record<string, string> = {
  InRange: 'text-inrange',
  OutOfRange: 'text-warning',
  Critical: 'text-critical',
  Unavailable: 'text-unknown',
  Maintenance: 'text-unknown',
};

const STATUS_BAR_CLASS: Record<string, string> = {
  InRange: 'bg-inrange',
  OutOfRange: 'bg-warning',
  Critical: 'bg-critical',
  Unavailable: 'bg-unknown',
  Maintenance: 'bg-unknown',
};

export function statusLabel(t: TFunc, status: string): string {
  const key = STATUS_LABEL_KEY[status];
  return key ? t(key) : status;
}

export function statusTextClass(status: string): string {
  return STATUS_TEXT_CLASS[status] ?? 'text-unknown';
}

export function statusBarClass(status: string): string {
  return STATUS_BAR_CLASS[status] ?? 'bg-unknown';
}

/** True for the two statuses that mean "we do not know", as opposed to "we know, and it is wrong". */
export function isUnknownStatus(status: string): boolean {
  return status === 'Unavailable' || status === 'Maintenance';
}

export function isReadingStatus(value: string): value is ReadingStatus {
  return STATUS_LABEL_KEY[value] !== undefined;
}

/** `deviceOnline` / `deviceOffline` / `deviceUnbound` — the device vocabulary, not the metric one. */
export function deviceStatusLabel(t: TFunc, deviceStatus: string | null): string {
  if (!deviceStatus) {
    return t('deviceUnbound');
  }
  return deviceStatus === 'online' ? t('deviceOnline') : t('deviceOffline');
}

/** Which layer an effective band came from (`override` | `profile` | `default`, BR-10.3). */
const THRESHOLD_SOURCE_KEY: Record<string, string> = {
  override: 'sourceOverride',
  profile: 'sourceProfile',
  default: 'sourceDefault',
};

/**
 * The sentence for a band's `source`. Total like the status map above: a value this build does not know renders as
 * its own code, which is a visible gap rather than a sentence about the wrong layer.
 */
export function thresholdSourceLabel(t: TFunc, source: string): string {
  const key = THRESHOLD_SOURCE_KEY[source];
  return key ? t(key) : source;
}

/** An override is the layer the keeper authored, so it is the one that gets the accent colour. */
export function thresholdSourceClass(source: string): string {
  return source === 'override' ? 'text-accent' : 'text-muted';
}

/** The phase a band was resolved in: `any`, `day` or `night`. */
const THRESHOLD_PHASE_KEY: Record<string, string> = {
  any: 'phaseAny',
  day: 'phaseDay',
  night: 'phaseNight',
};

export function thresholdPhaseLabel(t: TFunc, phase: string): string {
  const key = THRESHOLD_PHASE_KEY[phase];
  return key ? t(key) : phase;
}

/** The four climate zones of §4.3, whose values are the enum's own names rather than translations. */
const CLIMATE_ZONE_KEY: Record<string, string> = {
  Tropical: 'climateZoneTropical',
  SemiArid: 'climateZoneSemiArid',
  Arid: 'climateZoneArid',
  Temperate: 'climateZoneTemperate',
};

/** Like the maps above: a zone this build does not know renders as its own name, not as a wrong label. */
export function climateZoneLabel(t: TFunc, zone: string): string {
  const key = CLIMATE_ZONE_KEY[zone];
  return key ? t(key) : zone;
}

/**
 * The two alert families, whose separation is a product decision rather than a label (`AlertFamilies.ApiKey`).
 * `enclosure` holds humidity, light and the board's own silences and faults; `species` holds temperature and UV,
 * judged against the species profile.
 */
const ALERT_FAMILY_KEY: Record<string, string> = {
  enclosure: 'adminFamilyEnclosure',
  species: 'adminFamilySpecies',
};

export function alertFamilyLabel(t: TFunc, family: string): string {
  const key = ALERT_FAMILY_KEY[family];
  return key ? t(key) : family;
}

/** `AlertState` as the admin list spells it: `Open`, `Acknowledged`, `Resolved`. */
const ALERT_STATE_KEY: Record<string, string> = {
  Open: 'alertStateOpen',
  Acknowledged: 'alertStateAcknowledged',
  Resolved: 'alertStateResolved',
};

export function alertStateLabel(t: TFunc, state: string): string {
  const key = ALERT_STATE_KEY[state];
  return key ? t(key) : state;
}

/** An open alert is the one that needs somebody; a resolved one is history. Colour is never the only signal. */
const ALERT_STATE_TEXT_CLASS: Record<string, string> = {
  Open: 'text-warning',
  Acknowledged: 'text-accent',
  Resolved: 'text-inrange',
};

export function alertStateTextClass(state: string): string {
  return ALERT_STATE_TEXT_CLASS[state] ?? 'text-unknown';
}

/** `AlertSeverity`, the three tiers the API stores. */
const SEVERITY_KEY: Record<string, string> = {
  Info: 'severityInfo',
  Warning: 'severityWarning',
  Critical: 'severityCritical',
};

export function severityLabel(t: TFunc, severity: string): string {
  const key = SEVERITY_KEY[severity];
  return key ? t(key) : severity;
}

const SEVERITY_TEXT_CLASS: Record<string, string> = {
  Info: 'text-muted',
  Warning: 'text-warning',
  Critical: 'text-critical',
};

export function severityTextClass(severity: string): string {
  return SEVERITY_TEXT_CLASS[severity] ?? 'text-unknown';
}

/** `AlertSource` — what produced an episode, as `Alert.cs` names the members. */
const ALERT_SOURCE_KEY: Record<string, string> = {
  Threshold: 'alertSourceThreshold',
  DeviceSilent: 'alertSourceDeviceSilent',
  SensorFault: 'alertSourceSensorFault',
  DeviceClockSkew: 'alertSourceDeviceClockSkew',
  Gradient: 'alertSourceGradient',
  LightDeficit: 'alertSourceLightDeficit',
};

export function alertSourceLabel(t: TFunc, source: string): string {
  const key = ALERT_SOURCE_KEY[source];
  return key ? t(key) : source;
}

/**
 * The stored lifecycle of a board (`DeviceStatus`), which the administrative lists render in place of the keeper's
 * derived online/offline badge: `Enum.ToString()` sends `Online`, `Offline`, `Provisioning`, `Revoked` or
 * `Maintenance`, so the lookup folds case rather than guessing which route spelled it which way.
 */
const DEVICE_LIFECYCLE_KEY: Record<string, string> = {
  provisioning: 'adminDeviceProvisioning',
  online: 'adminDeviceOnline',
  offline: 'adminDeviceOffline',
  revoked: 'adminDeviceRevoked',
  maintenance: 'adminDeviceMaintenance',
};

const DEVICE_LIFECYCLE_TEXT_CLASS: Record<string, string> = {
  provisioning: 'text-muted',
  online: 'text-inrange',
  offline: 'text-critical',
  revoked: 'text-critical',
  maintenance: 'text-warning',
};

export function deviceLifecycleLabel(t: TFunc, status: string | null): string {
  if (!status) {
    return t('deviceUnbound');
  }
  const key = DEVICE_LIFECYCLE_KEY[status.toLowerCase()];
  return key ? t(key) : status;
}

export function deviceLifecycleTextClass(status: string | null): string {
  return status ? DEVICE_LIFECYCLE_TEXT_CLASS[status.toLowerCase()] ?? 'text-unknown' : 'text-muted';
}

/** `TerrariumStatus`: `Active` while it is readable and judged, `Deleted` once its owner removed it. */
const TERRARIUM_STATUS_KEY: Record<string, string> = {
  Active: 'terrariumStatusActive',
  Deleted: 'terrariumStatusDeleted',
};

export function terrariumStatusLabel(t: TFunc, status: string): string {
  const key = TERRARIUM_STATUS_KEY[status];
  return key ? t(key) : status;
}

/**
 * The permission catalogue's labels, keyed by the codes of `Domain/Identity/Permissions.cs`. The server publishes
 * a name and a description too, but they are English copy written beside the code; a screen that has a label for
 * the code uses it and falls back to whatever the server sent, so a permission this build does not know is
 * visible as the server's own wording rather than as the wrong grant.
 */
const PERMISSION_LABEL_KEY: Record<string, string> = {
  'terrarium.read': 'permissionTerrariumReadName',
  'terrarium.write': 'permissionTerrariumWriteName',
  'device.read': 'permissionDeviceReadName',
  'device.write': 'permissionDeviceWriteName',
  'device.provision': 'permissionDeviceProvisionName',
  'alert.read': 'permissionAlertReadName',
  'alert.write': 'permissionAlertWriteName',
  'silence.write': 'permissionSilenceWriteName',
  'notification.read': 'permissionNotificationReadName',
  'audit.read': 'permissionAuditReadName',
  'species.read': 'permissionSpeciesReadName',
  'species.write': 'permissionSpeciesWriteName',
  'user.read': 'permissionUserReadName',
  'user.manage': 'permissionUserManageName',
  'data.read.any': 'permissionDataReadAnyName',
  'data.write.any': 'permissionDataWriteAnyName',
};

const PERMISSION_DESCRIPTION_KEY: Record<string, string> = {
  'terrarium.read': 'permissionTerrariumReadDesc',
  'terrarium.write': 'permissionTerrariumWriteDesc',
  'device.read': 'permissionDeviceReadDesc',
  'device.write': 'permissionDeviceWriteDesc',
  'device.provision': 'permissionDeviceProvisionDesc',
  'alert.read': 'permissionAlertReadDesc',
  'alert.write': 'permissionAlertWriteDesc',
  'silence.write': 'permissionSilenceWriteDesc',
  'notification.read': 'permissionNotificationReadDesc',
  'audit.read': 'permissionAuditReadDesc',
  'species.read': 'permissionSpeciesReadDesc',
  'species.write': 'permissionSpeciesWriteDesc',
  'user.read': 'permissionUserReadDesc',
  'user.manage': 'permissionUserManageDesc',
  'data.read.any': 'permissionDataReadAnyDesc',
  'data.write.any': 'permissionDataWriteAnyDesc',
};

export function permissionLabel(t: TFunc, code: string, serverName?: string): string {
  const key = PERMISSION_LABEL_KEY[code];
  return key ? t(key) : serverName ?? code;
}

export function permissionDescription(t: TFunc, code: string, serverDescription?: string): string {
  const key = PERMISSION_DESCRIPTION_KEY[code];
  return key ? t(key) : serverDescription ?? code;
}

/** The catalogue's display groups, which is what keeps a matrix of sixteen rows readable. */
const PERMISSION_GROUP_KEY: Record<string, string> = {
  Enclosure: 'adminGroupEnclosure',
  Alerts: 'adminGroupAlerts',
  Account: 'adminGroupAccount',
  Catalogue: 'adminGroupCatalogue',
  Accounts: 'adminGroupAccounts',
  'Whole system': 'adminGroupWholeSystem',
};

export function permissionGroupLabel(t: TFunc, group: string): string {
  const key = PERMISSION_GROUP_KEY[group];
  return key ? t(key) : group;
}

/**
 * §6's freshness rule: a card older than three sampling intervals is stale, and stale values are dimmed and
 * labelled instead of being shown as if they were current. This compares *age*, not a value against a band, so
 * it does not cross the ADR-005 line — and the interval is the server's.
 */
export function isStale(
  capturedAt: string | null | undefined,
  samplingIntervalSec: number | null,
  now: Date = new Date()
): boolean {
  if (!capturedAt || !samplingIntervalSec) {
    return false;
  }
  const age = (now.getTime() - new Date(capturedAt).getTime()) / 1000;
  return Number.isFinite(age) && age > 3 * samplingIntervalSec;
}
