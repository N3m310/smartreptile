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
  role: string;
  preferredLanguage: string;
  timeZoneId: string;
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
 * The item shape of `GET /species-profiles`. The bands travel with the profile, so a picker needs one request
 * rather than one per row.
 */
export interface SpeciesProfileItem {
  id: string;
  name: string;
  scientificName: string;
  climateZone: string;
  /** True for the seeded profiles: read-only, and the ones a keeper copies to tune (FR-10 A2). */
  isBuiltIn: boolean;
  photoperiodHours: number;
  lightsOnLocalTime: string;
  lightThresholdLux: number;
  minLightHoursPerDay: number;
  notes: string | null;
  bands: SpeciesProfileBand[];
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
