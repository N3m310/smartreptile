/*
 * One function per route the web client calls, typed against `07-appendices/03` §4.
 *
 * Only the routes that exist: the auth set (FR-01), the terrarium surface including its threshold read and write
 * (2.8, 3.1) and the species-profile catalogue, reads and writes (3.9). Nothing here reaches for an endpoint that is
 * merely specified — `02-design/04` §6's rule is that an unbuilt route must surface as "not built yet", which can
 * only stay true if the client does not pretend to call it.
 */

import { apiRequest } from './client';
import type {
  AuthSession,
  Coverage,
  CreateSpeciesProfileBody,
  CreateTerrariumBody,
  HealthReport,
  LatestReadings,
  MetricsSnapshot,
  RangeSeries,
  SpeciesProfileItem,
  SpeciesProfileList,
  TerrariumItem,
  TerrariumList,
  TerrariumThresholds,
  ThresholdOverrideInput,
  UpdateSpeciesProfileBody,
  UserProfile,
  VersionInfo,
} from './types';

/** `POST /auth/register` — the only path that hands back a recovery code without a delivery channel. */
export async function register(body: {
  username: string;
  email: string;
  password: string;
  preferredLanguage?: string;
}): Promise<{ status: string; recoveryCode: string }> {
  return apiRequest('/api/v1/auth/register', { method: 'POST', body, allowRefresh: false });
}

export async function login(body: {
  usernameOrEmail: string;
  password: string;
}): Promise<AuthSession> {
  // `usernameOrEmail` is the contract's field name (`LoginRequest`), not a UI label.
  return apiRequest('/api/v1/auth/login', { method: 'POST', body, allowRefresh: false });
}

export async function logout(refreshToken: string): Promise<void> {
  await apiRequest('/api/v1/auth/logout', {
    method: 'POST',
    body: { refreshToken },
    allowRefresh: false,
  });
}

export async function me(): Promise<UserProfile> {
  return apiRequest('/api/v1/auth/me');
}

export async function changePassword(body: {
  currentPassword: string;
  newPassword: string;
  /** The caller's own refresh token: the session the change keeps alive. Every other one ends. */
  refreshToken: string;
}): Promise<void> {
  await apiRequest('/api/v1/auth/change-password', { method: 'POST', body });
}

export async function recover(body: {
  usernameOrEmail: string;
  recoveryCode: string;
  newPassword: string;
}): Promise<{ recoveryCode: string }> {
  return apiRequest('/api/v1/auth/recover', { method: 'POST', body, allowRefresh: false });
}

export async function forgotPassword(body: {
  usernameOrEmail: string;
}): Promise<void> {
  await apiRequest('/api/v1/auth/forgot-password', { method: 'POST', body, allowRefresh: false });
}

export async function resetPassword(body: {
  usernameOrEmail: string;
  resetCode: string;
  newPassword: string;
}): Promise<{ recoveryCode: string }> {
  return apiRequest('/api/v1/auth/reset-password', { method: 'POST', body, allowRefresh: false });
}

/** `GET /terrariums` — not paginated; the per-account count is small and the cursor convention is unimplemented. */
export async function listTerrariums(): Promise<TerrariumList> {
  return apiRequest('/api/v1/terrariums');
}

/**
 * `POST /terrariums` — requires the Owner role. Answers the created terrarium, so a caller can render it without
 * a second read; it needs a `speciesProfileId` from the catalogue below, which is why it could not be wired before
 * roadmap 3.9 built that route.
 */
export async function createTerrarium(body: CreateTerrariumBody): Promise<TerrariumItem> {
  return apiRequest('/api/v1/terrariums', { method: 'POST', body });
}

/** `GET /species-profiles` — the built-ins plus this account's own, each with its bands (roadmap 3.9). */
export async function listSpeciesProfiles(): Promise<SpeciesProfileList> {
  return apiRequest('/api/v1/species-profiles');
}

/**
 * `POST /species-profiles` — Owner only. Answers the created profile whole, bands included, so the caller can
 * render what it just authored without a second read (there is no read-by-id route).
 */
export async function createSpeciesProfile(body: CreateSpeciesProfileBody): Promise<SpeciesProfileItem> {
  return apiRequest('/api/v1/species-profiles', { method: 'POST', body });
}

/**
 * `PATCH /species-profiles/{id}` — Owner only. Omitted fields are left alone and `bands` replaces the set; a
 * built-in answers `403 builtin_immutable`.
 */
export async function updateSpeciesProfile(
  profileId: string,
  body: UpdateSpeciesProfileBody
): Promise<SpeciesProfileItem> {
  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}`, { method: 'PATCH', body });
}

/**
 * `POST /species-profiles/{id}/duplicate` — Owner only. The copy is always a custom profile: a built-in is
 * read-only (FR-10 A2), so this is the way one is tuned. An omitted name derives "<source> (copy)".
 */
export async function duplicateSpeciesProfile(profileId: string, name?: string): Promise<SpeciesProfileItem> {
  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}/duplicate`, {
    method: 'POST',
    body: { name: name?.trim() ? name : null },
  });
}

/** `DELETE /species-profiles/{id}` — Owner only. Answers `204`, or `409 profile_in_use` when a terrarium uses it. */
export async function deleteSpeciesProfile(profileId: string): Promise<void> {
  await apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}`, { method: 'DELETE' });
}

/**
 * `GET /terrariums/{id}/thresholds` — the effective band per metric with its `source` (BR-10.3, roadmap 3.1's read
 * half). `capturedAtUtc` dates the answer, because which bands resolve depends on the phase in force.
 */
export async function terrariumThresholds(id: string): Promise<TerrariumThresholds> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}/thresholds`);
}

/**
 * `PUT /terrariums/{id}/thresholds` — Owner only. Upserts the bands the body names (a metric it does not mention
 * keeps its override) and answers the resulting effective set. Every band is validated before anything is written,
 * so one incoherent band changes nothing.
 */
export async function putTerrariumThresholds(
  id: string,
  overrides: ThresholdOverrideInput[]
): Promise<TerrariumThresholds> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}/thresholds`, {
    method: 'PUT',
    body: { overrides },
  });
}

/**
 * `DELETE /terrariums/{id}/thresholds/{metric}` — Owner only. Drops every override the terrarium holds for the
 * metric, whichever phase, and answers the resulting effective set. Idempotent.
 */
export async function deleteTerrariumThresholdOverride(
  id: string,
  metric: string
): Promise<TerrariumThresholds> {
  return apiRequest(
    `/api/v1/terrariums/${encodeURIComponent(id)}/thresholds/${encodeURIComponent(metric)}`,
    { method: 'DELETE' }
  );
}

export async function getTerrarium(id: string): Promise<TerrariumItem> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}`);
}

export async function latestReadings(id: string): Promise<LatestReadings> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}/readings/latest`);
}

export async function readings(
  id: string,
  metric: string,
  fromUtc: string,
  toUtc: string
): Promise<RangeSeries> {
  const query = new URLSearchParams({ metric, from: fromUtc, to: toUtc });
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}/readings?${query}`);
}

/** `GET .../coverage` answers `409 device_not_bound` when nothing is bound — there is nothing to cover. */
export async function coverage(
  id: string,
  fromUtc: string,
  toUtc: string
): Promise<Coverage> {
  const query = new URLSearchParams({ from: fromUtc, to: toUtc });
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}/coverage?${query}`);
}

export async function version(): Promise<VersionInfo> {
  return apiRequest('/version', { allowRefresh: false });
}

/**
 * Readiness and the ops snapshot (FR-18). Unauthenticated by design — they return no sensitive detail — which is
 * what lets the diagnosis page work when the reason you are looking at it is that signing in fails.
 */
export async function readiness(): Promise<HealthReport> {
  return apiRequest('/health/ready', { allowRefresh: false });
}

export async function metrics(): Promise<MetricsSnapshot> {
  return apiRequest('/metrics', { allowRefresh: false });
}
