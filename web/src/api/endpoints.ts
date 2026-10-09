/*
 * One function per route the web client calls, typed against `07-appendices/03` §4.
 *
 * Only the routes that exist: the auth set (FR-01) and the terrarium read surface (2.8). Nothing here reaches
 * for an endpoint that is merely specified — `02-design/04` §6's rule is that an unbuilt route must surface as
 * "not built yet", which can only stay true if the client does not pretend to call it.
 */

import { apiRequest } from './client';
import type {
  AuthSession,
  Coverage,
  HealthReport,
  LatestReadings,
  MetricsSnapshot,
  RangeSeries,
  TerrariumItem,
  TerrariumList,
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
