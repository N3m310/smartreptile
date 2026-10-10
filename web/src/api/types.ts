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

export interface SpeciesProfileItem {
  id: string;
  name: string;
  scientificName: string;
  climateZone: string;
  photoperiodHours: number;
  notes: string | null;
  isBuiltIn: boolean;
}

export interface EffectiveThreshold {
  metric: string;
  layer: 'SpeciesProfile' | 'Override' | string;
  phase: 'Day' | 'Night' | 'Any' | string;
  targetMin: number | null;
  targetMax: number | null;
  criticalMin: number | null;
  criticalMax: number | null;
}

export interface EffectiveThresholdsResponse {
  terrariumId: string;
  capturedAtUtc: string;
  timeZoneId: string;
  effectiveThresholds: EffectiveThreshold[];
}

export interface CreateTerrariumInput {
  name: string;
  speciesProfileId: string;
  location?: string | null;
  description?: string | null;
  timeZoneId?: string;
}

export interface UpdateTerrariumInput {
  name?: string;
  speciesProfileId?: string;
  location?: string | null;
  description?: string | null;
  timeZoneId?: string;
}

