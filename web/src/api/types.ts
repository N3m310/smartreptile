/*
 * Wire types, mirroring `docs/07-appendices/03` §4 exactly.
 *
 * Nothing here is guessed: every field name and every closed vocabulary comes from the contract, because the
 * promotion's whole point is that the numbers on the screen are the server's. Where the contract has a value
 * the client must not invent — `status`, `target`, `bucket` — the type is a union of the documented values, so
 * a new server value is a compile error here rather than a silently unstyled chip.
 */

/** `02-design/04` §5 state vocabulary. The API never sends anything else, and the UI never invents one. */
export const READING_STATUSES = [
  'InRange',
  'OutOfRange',
  'Critical',
  'Unavailable',
  'Maintenance',
] as const;
export type ReadingStatus = (typeof READING_STATUSES)[number];

/** Device liveness as `readings/latest` derives it (FR-07 BR-07.2), plus the bound-device status field. */
export const DEVICE_STATUSES = ['online', 'offline', 'maintenance'] as const;
export type DeviceStatus = (typeof DEVICE_STATUSES)[number];

/** The metric dictionary keys (`07-appendices/01`). `metric` on the range route must be one of these. */
export const METRIC_CODES = [
  'tempC',
  'humidityPct',
  'lightLux',
  'uvIndex',
  'surfaceTempC',
] as const;
export type MetricCode = (typeof METRIC_CODES)[number];

/** Bucket widths the server chooses from the requested window (BR-09.1); echoed back, never assumed. */
export const BUCKETS = ['raw', '5min', 'hourly'] as const;
export type Bucket = (typeof BUCKETS)[number];

export interface UserProfile {
  id: string;
  username: string;
  email: string;
  /** The role's code, e.g. `Owner`. Shown, never tested: nothing authorises on it (FR-18). */
  role: string;
  /** The role's identity, so a screen can name the role in force rather than its code. */
  roleId: string;
  /**
   * The codes the account's role grants, in catalogue order. **This** is what a screen gates on: a deployment may
   * re-point a role at a different permission set, and a screen that tested the role's name would keep rendering
   * the old answer. The access token carries the same codes, so the two agree by construction.
   */
  permissions: string[];
  preferredLanguage: string;
  timeZoneId: string;
  quietHoursStart: string | null;
  quietHoursEnd: string | null;
  minNotifySeverity: string;
  channelFcmEnabled: boolean;
  channelEmailEnabled: boolean;
  hasFcmToken: boolean;
}

export interface UpdateProfileBody {
  preferredLanguage?: string;
  timeZoneId?: string;
  quietHoursStart?: string;
  quietHoursEnd?: string;
  minNotifySeverity?: string;
  channelFcmEnabled?: boolean;
  channelEmailEnabled?: boolean;
  fcmToken?: string;
}

/** `POST /auth/login`, `/auth/refresh`. Timestamps are ISO-8601 instants in UTC. */
export interface AuthSession {
  user: UserProfile;
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

/** One `errors[]` entry of an RFC 7807 refusal (`07-appendices/03` §5). */
export interface FieldViolation {
  field: string;
  code: string;
  message: string;
}

export interface DeviceSummary {
  deviceId: string;
  deviceName: string;
  status: string;
  firmwareVersion: string | null;
  lastSeenAt: string | null;
  samplingIntervalSec: number | null;
  signalStrengthDbm: number | null;
  batteryPct: number | null;
  uptimeSeconds: number | null;
}

/** The item shape of `GET /terrariums` (inside `items`), `POST /terrariums` and `GET /terrariums/{id}`. */
export interface TerrariumItem {
  id: string;
  name: string;
  speciesProfileId: string;
  speciesName: string;
  location: string | null;
  description: string | null;
  timeZoneId: string;
  createdAt: string;
  updatedAt: string;
  device: DeviceSummary | null;
  latestSampleAt: string | null;
  openAlertCount: number;
  /**
   * The concurrency token an update sends back as `If-Match`, base64 — the same value as the response's `ETag`
   * header, repeated in the body for clients that cannot read headers. Only the single-terrarium answers carry it:
   * `GET /terrariums` items are read-only and omit the field (`07-appendices/03` §4.2).
   */
  rowVersion?: string;
  /**
   * The account that owns the enclosure. Present **only** on the answers an administrator receives, whose list can
   * hold rows belonging to accounts they did not name (TASKS 2026-10-14, item 5).
   */
  ownerUserId?: string;
  /**
   * The owning account's email, set exactly when `ownerUserId` is — an id alone does not say whose enclosure it is
   * (2026-10-23).
   */
  ownerEmail?: string;
  /**
   * The one-time secret of a board bound by the same `POST /terrariums`, present only on that response
   * (TASKS 2026-10-14, item 2). Shown once and never retrievable: only its hash is stored.
   */
  deviceSecret?: string;
}

export interface TerrariumList {
  items: TerrariumItem[];
}

/** One band of a species profile (`07-appendices/03` §4.3) — the numbers a terrarium is judged against. */
export interface SpeciesProfileBand {
  metric: string;
  unit: string;
  phase: string;
  targetMin: number;
  targetMax: number;
  criticalMin: number | null;
  criticalMax: number | null;
  dwellWarnMinutes: number;
  dwellCritMinutes: number;
  recoveryMargin: number;
  enabled: boolean;
  sourceRef: string | null;
  sourceUrl: string | null;
}

/**
 * The item shape of `GET /species-profiles` and of `GET /species-profiles/{id}`. The bands travel with the profile,
 * so a picker needs one request rather than one per row.
 */
export interface SpeciesProfileItem {
  id: string;
  name: string;
  scientificName: string;
  climateZone: string;
  /** True for the seeded profiles: read-only, and the ones a keeper copies to tune (FR-10 A2). */
  isBuiltIn: boolean;
  /**
   * True when this caller may edit the profile: it is one of their own rather than a built-in. The client shows the
   * edit controls on it and offers "duplicate it instead" when it is false.
   */
  canEdit: boolean;
  photoperiodHours: number;
  lightsOnLocalTime: string;
  lightThresholdLux: number;
  minLightHoursPerDay: number;
  notes: string | null;
  bands: SpeciesProfileBand[];
  /**
   * Where the profile's picture is, relative to the API's origin — null when it has none, which is the ordinary
   * state of a built-in. The URL needs the caller's bearer token, so it is *fetched* (`speciesProfilePicture`)
   * rather than put in an `img src`.
   */
  imageUrl: string | null;
  /**
   * When the picture was last set. It is the cache-busting value for `imageUrl`: replacing a picture does not
   * change its URL, so a client that keeps the bytes keys them on this.
   */
  imageUpdatedAt: string | null;
}

export interface SpeciesProfileList {
  items: SpeciesProfileItem[];
}

/** Body of `POST /terrariums`. `timeZoneId` omitted means the deployment's default (ADR-015). */
export interface CreateTerrariumBody {
  name: string;
  speciesProfileId: string;
  location?: string | null;
  description?: string | null;
  timeZoneId?: string | null;
  /**
   * A board to bind in the same request, named by its public id or by the MAC printed on it
   * (TASKS 2026-10-14, items 2 and 8). Naming one is a claim, so the response carries `deviceSecret` once; naming
   * both is refused rather than guessed at. Omitting both is the behaviour that existed before.
   */
  deviceId?: string | null;
  macAddress?: string | null;
}

/**
 * One entry of the time-zone catalogue — the value to store plus what a picker renders
 * (`GET /time-zones`, TASKS 2026-10-14, item 11).
 */
export interface TimeZoneItem {
  ianaId: string;
  displayName: string;
  countryCode: string;
  utcOffsetMinutes: number;
  /** The same offset pre-formatted (`UTC+07:00`), so the client need not agree on a spelling. */
  utcOffset: string;
}

export interface TimeZoneList {
  items: TimeZoneItem[];
}

/** One board of the fleet screen (`GET /devices`, TASKS 2026-10-14, item 9b). */
export interface DeviceItem {
  deviceId: string;
  deviceName: string;
  chipId: string;
  macAddress: string;
  firmwareVersion: string | null;
  /** Stored lifecycle state: `provisioning`, `online`, `offline`, `revoked`, `maintenance`. */
  status: string;
  /** Derived online/offline badge, from the same rule the terrarium header uses. */
  runtime: string;
  protocol: string;
  terrariumId: string | null;
  terrariumName: string | null;
  lastSeenAt: string | null;
  samplingIntervalSec: number;
  publishIntervalSec: number;
  provisionedAt: string | null;
  revokedAt: string | null;
  createdAt: string;
  signalStrengthDbm: number | null;
  batteryPct: number | null;
  uptimeSeconds: number | null;
  freeHeapKb: number | null;
  calibrationJson: string | null;
}

export interface DeviceList {
  items: DeviceItem[];
}

/**
 * The answer to `POST /devices/{id}/move` — the board as it now stands, plus what the move displaced.
 * `displacedDeviceId` is the board the target enclosure used to hold, because one enclosure holds one board.
 */
export interface DeviceMoveResult {
  device: DeviceItem;
  previousTerrariumId: string | null;
  displacedDeviceId: string | null;
}

export interface AlertView {
  id: number;
  terrariumId: string;
  deviceId: string;
  metric: string | null;
  family: string;
  severity: string;
  state: string;
  source: string;
  phase: string;
  triggeringValue: number | null;
  peakValue: number | null;
  bandMin: number | null;
  bandMax: number | null;
  triggeredAt: string;
  lastObservedAt: string | null;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
  resolvedReason: string | null;
  durationSeconds: number | null;
}

export interface AlertPage {
  items: AlertView[];
  nextCursor: string | null;
}

export interface AlertDetail {
  alert: AlertView;
  series: { at: string; value: number }[];
  seriesTruncated: boolean;
}

export interface AlertTimelineEntry {
  event: string;
  at: string;
  actorUserId: string | null;
  reason: string | null;
  note: string | null;
}

export interface MetricSilence {
  id: string;
  terrariumId: string;
  metric: string | null;
  untilUtc: string;
  reason: string;
  createdAt: string;
  createdByUserId: string;
  active: boolean;
}

/**
 * One band of `POST`/`PATCH /species-profiles` (`07-appendices/03` §4.3). `metric`, `targetMin` and `targetMax` are
 * required and the rest are optional on the wire, so a missing field becomes a field-level violation addressed by
 * position (`bands[0].targetMin`) rather than a deserialisation failure.
 */
export interface SpeciesProfileBandInput {
  metric: string;
  phase?: string;
  /**
   * Required by the contract, and nullable here because a form can send a box nobody filled: the server then
   * answers `threshold_target_required` by field name instead of failing to deserialise the body.
   */
  targetMin: number | null;
  targetMax: number | null;
  criticalMin?: number | null;
  criticalMax?: number | null;
  dwellWarnMinutes?: number | null;
  dwellCritMinutes?: number | null;
  recoveryMargin?: number | null;
  enabled?: boolean;
  sourceRef?: string | null;
}

/** Body of `POST /species-profiles` — Admin only (the seeded system administrator). At least one band, and a name this account already uses is `409 profile_name_taken`. */
export interface CreateSpeciesProfileBody {
  name: string;
  scientificName?: string | null;
  climateZone: string;
  photoperiodHours?: number | null;
  lightsOnLocalTime?: string | null;
  lightThresholdLux?: number | null;
  minLightHoursPerDay?: number | null;
  notes?: string | null;
  bands: SpeciesProfileBandInput[];
}

/**
 * Body of `PATCH /species-profiles/{id}` — Admin only. An **omitted** member is left unchanged; sending `bands`
 * replaces the whole set, because merging a band field by field could leave it in a state neither the band nor the
 * request described. An empty `scientificName` or `notes` clears it, since JSON cannot tell "absent" from "null"
 * once the body is bound.
 */
export interface UpdateSpeciesProfileBody {
  name?: string | null;
  scientificName?: string | null;
  climateZone?: string | null;
  photoperiodHours?: number | null;
  lightsOnLocalTime?: string | null;
  lightThresholdLux?: number | null;
  minLightHoursPerDay?: number | null;
  notes?: string | null;
  bands?: SpeciesProfileBandInput[] | null;
}

/**
 * The band one metric is judged against right now, resolved by the server (`TerrariumService.EffectiveThresholds`).
 * Nothing here is computed on this side: `source` says which layer it came from, so a limit can be explained rather
 * than guessed at (BR-10.3, ADR-005).
 */
export interface EffectiveThreshold {
  metric: string;
  unit: string;
  /** Phase it was resolved in: `day`, `night`, or `any` when the metric is not split day/night. */
  phase: string;
  /** Which layer holds it: `override`, `profile` or `default`. */
  source: string;
  targetMin: number;
  targetMax: number;
  criticalMin: number | null;
  criticalMax: number | null;
  dwellWarnMinutes: number;
  dwellCritMinutes: number;
  recoveryMargin: number;
}

/**
 * `GET`, `PUT` and `DELETE` on `/terrariums/{id}/thresholds` all answer this shape, so a write can be rendered
 * without a follow-up read. A metric with no band for the phase in force is **absent** rather than null-bounded:
 * a metric configured for Day alone is missing at night, which is what the evaluator will act on.
 */
export interface TerrariumThresholds {
  terrariumId: string;
  capturedAtUtc: string;
  timeZoneId: string;
  effectiveThresholds: EffectiveThreshold[];
}

/** One band a client writes (UC-03). Nullable members so a missing field is a refusal, not a binding failure. */
export interface ThresholdOverrideInput {
  metric: string;
  /** Phase the override applies in; omitted means `any`. The editor sends the phase the row resolved in. */
  phase?: string | null;
  targetMin: number;
  targetMax: number;
  criticalMin?: number | null;
  criticalMax?: number | null;
  /** Omitted falls back to the stored band's value, or the domain default (5) when there is none. */
  dwellWarnMinutes?: number | null;
  /** Omitted falls back to the stored band's value, or the domain default (2) when there is none. */
  dwellCritMinutes?: number | null;
  /** Omitted falls back to the stored band's value, or the domain default (0.5) when there is none. */
  recoveryMargin?: number | null;
  /** Why this terrarium differs from its profile; at most 300 characters. */
  note?: string | null;
  /** False keeps the numbers without judging against them; defaults to true. */
  enabled?: boolean | null;
}

/** The band a value was judged against — phase-resolved by the server, `null` when no band is configured. */
export interface TargetBand {
  min: number | null;
  max: number | null;
}

export interface MetricReading {
  code: string;
  value: number;
  unit: string;
  capturedAt: string;
  status: ReadingStatus;
  qualityFlags: number;
  target: TargetBand | null;
}

/** `GET /terrariums/{id}/readings/latest`. A metric with no stored reading is absent, not null. */
export interface LatestReadings {
  terrariumId: string;
  lastSampleAt: string | null;
  device: DeviceSummary | null;
  metrics: MetricReading[];
}

/** One bucket. `count: 0` with null values is a gap — never interpolated (BR-09.5). */
export interface SeriesPoint {
  t: string;
  min: number | null;
  max: number | null;
  avg: number | null;
  count: number;
}

export interface RangeSeries {
  metric: string;
  unit: string;
  bucket: Bucket;
  fromUtc: string;
  toUtc: string;
  points: SeriesPoint[];
  alerts: { id: number; severity: string; from: string; to: string; metric: string }[];
}

export interface Coverage {
  terrariumId: string;
  fromUtc: string;
  toUtc: string;
  samplingIntervalSec: number;
  expectedSamples: number;
  receivedSamples: number;
  coveragePct: number;
}

export interface VersionInfo {
  api: string;
  schema: string;
  minFirmware: string;
  timestamp: string;
}

/** One entry of `/health/ready`'s report — the diagnosis the retired `health.html` page used to show. */
export interface HealthCheckReport {
  name: string;
  status: string;
  description: string | null;
  durationMs: number;
  error: string | null;
  data: Record<string, unknown>;
}

export interface HealthReport {
  status: string;
  totalDurationMs: number;
  timestamp: string;
  checks: HealthCheckReport[];
}

export interface MetricsSnapshot {
  counters: Record<string, number>;
  brokerRunning: boolean;
  timestamp: string;
}

/**
 * The permission catalogue (`Domain/Identity/Permissions.cs`), as the signed-in account's codes are spelled. Every
 * authorisation decision the API makes is one of these, so this is the vocabulary a screen gates on — and the
 * labels a matrix renders are looked up by code rather than by position, so a code this build does not know stays
 * visible as itself instead of being labelled with the wrong grant.
 */
export const PERMISSION_CODES = {
  terrariumRead: 'terrarium.read',
  terrariumWrite: 'terrarium.write',
  deviceRead: 'device.read',
  deviceWrite: 'device.write',
  deviceProvision: 'device.provision',
  alertRead: 'alert.read',
  alertWrite: 'alert.write',
  silenceWrite: 'silence.write',
  notificationRead: 'notification.read',
  auditRead: 'audit.read',
  speciesRead: 'species.read',
  speciesWrite: 'species.write',
  userRead: 'user.read',
  userManage: 'user.manage',
  dataReadAny: 'data.read.any',
  dataWriteAny: 'data.write.any',
} as const;
export type PermissionCode = (typeof PERMISSION_CODES)[keyof typeof PERMISSION_CODES];

/**
 * One account as the administrator's list describes it (`UserAdminContracts.UserSummary`). Carries the role's
 * permission codes rather than making a caller ask per row: "what can this account do" is the first question an
 * administrator asks of a list.
 */
export interface UserSummary {
  id: string;
  username: string;
  email: string;
  /** The role's code, e.g. `Owner`. */
  role: string;
  /** The role's identity, which is what `PATCH /users/{id}/role` is driven from. */
  roleId: string;
  permissions: string[];
  preferredLanguage: string;
  timeZoneId: string;
  quietHoursStart: string | null;
  quietHoursEnd: string | null;
  minNotifySeverity: string;
  createdAt: string;
  lastLoginAt: string | null;
  /** When the account was disabled. A soft delete is a disable, so this is also its deletion marker. */
  disabledAt: string | null;
  isActive: boolean;
}

/** One page of accounts, with the total so a pager can be rendered (`GET /users`). */
export interface UserPage {
  items: UserSummary[];
  total: number;
  skip: number;
  take: number;
}

/** Body of `POST /users`. `role` is a role code and defaults to `Owner`; the password is the administrator's. */
export interface CreateUserBody {
  username: string;
  email: string;
  password: string;
  role?: string;
  preferredLanguage?: string;
}

/**
 * Body of `PATCH /users/{id}` (`UserAdminContracts.UpdateAccountRequest`). An omitted member keeps its value, and
 * `quietHoursStart`/`quietHoursEnd` are a pair: both empty clears the window, one alone is
 * `400 quiet_hours_incomplete`. The account's time zone is *not* here — it is edited from the account's own screen.
 */
export interface UpdateAccountBody {
  username?: string;
  email?: string;
  preferredLanguage?: string;
  minNotifySeverity?: string;
  quietHoursStart?: string;
  quietHoursEnd?: string;
}

/** One role of the matrix (`RoleAdminContracts.RoleSummary`), as `GET /admin/roles` sends it. */
export interface RoleSummary {
  id: string;
  code: string;
  name: string;
  description: string;
  /** True for the four the product ships, which cannot be deleted or re-coded. */
  isSystem: boolean;
  /** Display order, lowest first. */
  rank: number;
  permissions: string[];
  /** The subset of `permissions` that reaches data outside the caller's own account. */
  crossAccountPermissions: string[];
  /** How many accounts hold it, so a delete can be refused with a number. */
  userCount: number;
  /**
   * The role's optimistic-concurrency token, base64. Read from the response and echoed by the OpenAPI document;
   * `PATCH /admin/roles/{id}` carries no member to send it back in, so this client re-reads after a conflict
   * instead of pretending to hold a token the route does not accept.
   */
  rowVersion: string | null;
}

/**
 * Body of `POST /admin/roles`. `permissions` is the complete set to grant — an unknown code is refused rather than
 * dropped, and `rank` defaults to 100 server-side so a custom role sorts below the shipped four.
 */
export interface CreateRoleBody {
  code: string;
  name: string;
  description?: string | null;
  permissions?: string[];
  rank?: number | null;
}

/** Body of `PATCH /admin/roles/{id}`. `permissions` **replaces** the set; an omitted member keeps its value. */
export interface UpdateRoleBody {
  name?: string | null;
  description?: string | null;
  permissions?: string[] | null;
  rank?: number | null;
}

/** One permission in the catalogue (`RoleAdminContracts.PermissionSummary`), as the console offers it. */
export interface PermissionSummary {
  code: string;
  group: string;
  name: string;
  description: string;
  crossesAccounts: boolean;
}

/** The counts of the administrator's header (`AdminCounts`). */
export interface AdminCounts {
  accounts: number;
  accountsActive: number;
  terrariums: number;
  terrariumsWithDevice: number;
  devices: number;
  devicesOnline: number;
  alertsOpen: number;
  alertsOpenEnclosure: number;
  alertsOpenSpecies: number;
  alertsCritical: number;
  speciesProfiles: number;
}

/** One account's holdings, as the get-all table lists them (`AdminAccountRollup`). */
export interface AdminAccountRollup {
  userId: string;
  username: string;
  email: string;
  role: string;
  isActive: boolean;
  terrariums: number;
  devices: number;
  alertsOpen: number;
  createdAt: string;
  lastLoginAt: string | null;
}

/**
 * One alert family as `GET /admin/overview` describes it (`AdminFamilyView`): the key the alert list filters on, the
 * metric keys that belong to it and how many of its alerts are open. The metrics travel with the family so a client
 * can label its two panels without carrying a list of its own.
 */
export interface AdminFamilyView {
  key: string;
  metrics: string[];
  alertsOpen: number;
}

/** `GET /admin/overview`: the header's figures, the get-all table and the two families. */
export interface AdminOverview {
  counts: AdminCounts;
  accounts: AdminAccountRollup[];
  families: AdminFamilyView[];
}

/** One enclosure of any account (`AdminTerrariumView`). */
export interface AdminTerrariumView {
  id: string;
  name: string;
  ownerId: string;
  ownerUsername: string;
  speciesProfileId: string;
  speciesProfileName: string;
  status: string;
  devicePublicId: string | null;
  deviceStatus: string | null;
  deviceLastSeenAt: string | null;
  openAlerts: number;
  createdAt: string;
}

/** One board in the deployment, bound or not (`AdminDeviceView`). */
export interface AdminDeviceView {
  id: string;
  publicId: string;
  deviceName: string;
  status: string;
  ownerUsername: string | null;
  terrariumId: string | null;
  terrariumName: string | null;
  firmwareVersion: string;
  lastSeenAt: string | null;
  signalStrengthDbm: number | null;
  batteryPct: number | null;
}

/** One alert of any account (`AdminAlertView`), which is what makes both families reviewable across the deployment. */
export interface AdminAlertView {
  id: number;
  /** The family's REST key: `enclosure` or `species`. */
  family: string;
  severity: string;
  /** Lifecycle state: `Open`, `Acknowledged` or `Resolved`. */
  state: string;
  source: string;
  metric: string | null;
  deviceId: string;
  terrariumId: string;
  terrariumName: string;
  ownerUsername: string;
  triggeredAt: string;
  resolvedAt: string | null;
}

/** One page of an administrative list, with the total so a pager can be rendered (`AdminPage<T>`). */
export interface AdminPage<T> {
  items: T[];
  total: number;
  skip: number;
  take: number;
}
