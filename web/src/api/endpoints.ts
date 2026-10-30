/*
 * One function per route the web client calls, typed against `07-appendices/03` §4.
 *
 * Only the routes that exist: the auth set (FR-01), the terrarium surface including its threshold read and write
 * (2.8, 3.1) and the species-profile catalogue, reads and writes (3.9). Nothing here reaches for an endpoint that is
 * merely specified — `02-design/04` §6's rule is that an unbuilt route must surface as "not built yet", which can
 * only stay true if the client does not pretend to call it.
 */

import { apiBlob, apiRequest } from './client';
import type {
  AdminAlertView,
  AdminDeviceView,
  AdminOverview,
  AdminPage,
  AdminTerrariumView,
  AlertDetail,
  AlertPage,
  AlertTimelineEntry,
  AlertView,
  AuthSession,
  Coverage,
  CreateRoleBody,
  CreateSpeciesProfileBody,
  CreateTerrariumBody,
  CreateUserBody,
  DeviceItem,
  DeviceList,
  DeviceMoveResult,
  HealthReport,
  LatestReadings,
  MetricsSnapshot,
  MetricSilence,
  PermissionSummary,
  RangeSeries,
  RoleSummary,
  SpeciesProfileItem,
  SpeciesProfileList,
  TerrariumItem,
  TerrariumList,
  TerrariumThresholds,
  ThresholdOverrideInput,
  TimeZoneList,
  UpdateAccountBody,
  UpdateRoleBody,
  UpdateProfileBody,
  UpdateSpeciesProfileBody,
  UpdateTerrariumBody,
  UserPage,
  UserProfile,
  UserSummary,
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

export async function updateMyProfile(body: UpdateProfileBody): Promise<UserProfile> {
  return apiRequest('/api/v1/auth/me', { method: 'PATCH', body });
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

/**
 * `PATCH /terrariums/{id}` — requires `terrarium.write` and the version the caller read, sent as `If-Match`
 * (`07-appendices/03` §4.2). Omitted members are left unchanged, so a caller sends only what it edited; the
 * all-zero GUID detaches the species profile, which is what "no species" has to be spelled as.
 */
export async function updateTerrarium(
  id: string,
  body: UpdateTerrariumBody,
  rowVersion: string
): Promise<TerrariumItem> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    headers: { 'If-Match': rowVersion },
    body,
  });
}

/** `GET /species-profiles` — the built-ins plus this account's own, each with its bands (roadmap 3.9). */
export async function listSpeciesProfiles(): Promise<SpeciesProfileList> {
  return apiRequest('/api/v1/species-profiles');
}

/**
 * `GET /time-zones` — the time-zone catalogue, ordered west to east (TASKS 2026-10-14, item 11). Readable by every
 * authenticated account, because a picker is not privileged data and both clients need it before they can create
 * anything. `ianaId` is the value `POST /terrariums` and `PATCH /auth/me` expect.
 */
export async function listTimeZones(): Promise<TimeZoneList> {
  return apiRequest('/api/v1/time-zones');
}

/** `GET /devices` — the signed-in account's boards, with both the stored state and the derived badge (item 9b). */
export async function listDevices(): Promise<DeviceList> {
  return apiRequest('/api/v1/devices');
}

export async function getDevice(deviceId: string): Promise<DeviceItem> {
  return apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}`);
}

/**
 * `PATCH /devices/{id}` — rename a board or change its intervals (item 9b). A patch: an omitted field keeps its
 * value. The interval is stored, not pushed: the board picks it up on its next connection.
 */
export async function patchDevice(
  deviceId: string,
  body: { deviceName?: string; samplingIntervalSec?: number; publishIntervalSec?: number }
): Promise<DeviceItem> {
  return apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}`, { method: 'PATCH', body });
}

/**
 * `DELETE /devices/{id}` — detach a board from its enclosure (item 9b, changed 2026-10-25). The board keeps its
 * credentials and stays in the fleet as `provisioning`, so it can be bound again; `purge` erases the row instead and
 * is refused with `device_has_history` while anything still references the board.
 */
export async function deleteDevice(deviceId: string, purge = false): Promise<DeviceItem> {
  const query = purge ? '?purge=true' : '';
  return apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}${query}`, { method: 'DELETE' });
}

/**
 * `POST /devices/{id}/move` — bind a board to an enclosure. One enclosure holds one board, so a target that already
 * has one displaces it (the displaced board is unbound, not revoked); the board leaves whichever enclosure it held.
 * An owner may only pair their own board with their own enclosure; `data.write.any` lifts that.
 */
export async function moveDevice(deviceId: string, terrariumId: string): Promise<DeviceMoveResult> {
  return apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}/move`, {
    method: 'POST',
    body: { terrariumId },
  });
}

/**
 * `POST /devices/claim-by-mac` — bind a self-registered board by the address printed on it
 * (TASKS 2026-10-14, item 8). The lower-trust pairing path: the API description says so, `Owner` is required, and a
 * MAC that is unknown, already bound or revoked answers one `device_not_found`.
 */
export async function claimDeviceByMac(body: {
  macAddress: string;
  terrariumId: string;
  /** Name to give the board in the same request; omitted keeps the default label (2026-10-23). */
  deviceName?: string;
}): Promise<{ deviceId: string; secret: string; terrariumId: string; boundAtUtc: string }> {
  return apiRequest('/api/v1/devices/claim-by-mac', { method: 'POST', body });
}

export async function claimDevice(body: {
  claimCode: string;
  terrariumId: string;
  /** Name to give the board in the same request; omitted keeps the default label (2026-10-23). */
  deviceName?: string;
}): Promise<{
  deviceId: string;
  secret: string;
  terrariumId: string;
  boundAtUtc: string;
}> {
  return apiRequest('/api/v1/devices/claim', { method: 'POST', body });
}

export async function rotateDeviceSecret(deviceId: string): Promise<{
  deviceId: string;
  secret: string;
  previousUsableUntilUtc: string;
}> {
  return apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}/rotate-secret`, { method: 'POST' });
}

export async function revokeDevice(deviceId: string): Promise<void> {
  await apiRequest(`/api/v1/devices/${encodeURIComponent(deviceId)}/revoke`, { method: 'POST' });
}

export async function listAlerts(params: {
  state?: string;
  severity?: string;
  from?: string;
  to?: string;
  cursor?: string;
  pageSize?: number;
}): Promise<AlertPage> {
  const query = new URLSearchParams();
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== '') {
      query.set(key, String(value));
    }
  });
  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return apiRequest(`/api/v1/alerts${suffix}`);
}

export async function getAlert(alertId: number): Promise<AlertDetail> {
  return apiRequest(`/api/v1/alerts/${alertId}`);
}

export async function getAlertTimeline(alertId: number): Promise<{ items: AlertTimelineEntry[] }> {
  return apiRequest(`/api/v1/alerts/${alertId}/timeline`);
}

export async function acknowledgeAlert(alertId: number): Promise<AlertView> {
  return apiRequest(`/api/v1/alerts/${alertId}/ack`, { method: 'POST' });
}

export async function resolveAlert(
  alertId: number,
  body: { reason: 'Recovered' | 'FalsePositive' | 'SensorFault' | 'Accepted'; note?: string }
): Promise<AlertView> {
  return apiRequest(`/api/v1/alerts/${alertId}/resolve`, { method: 'POST', body });
}

export async function listTerrariumSilences(terrariumId: string): Promise<{ items: MetricSilence[] }> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(terrariumId)}/silences`);
}

export async function createTerrariumSilence(
  terrariumId: string,
  body: { metric?: string; untilUtc: string; reason: string }
): Promise<MetricSilence> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(terrariumId)}/silences`, {
    method: 'POST',
    body,
  });
}

export async function cancelTerrariumSilence(terrariumId: string, silenceId: string): Promise<void> {
  await apiRequest(
    `/api/v1/terrariums/${encodeURIComponent(terrariumId)}/silences/${encodeURIComponent(silenceId)}`,
    { method: 'DELETE' }
  );
}

/**
 * `POST /species-profiles` — Admin only (the seeded system administrator). Answers the created profile whole,
 * bands included, so the caller can render what it just authored without a second read (there is no read-by-id
 * route).
 */
export async function createSpeciesProfile(body: CreateSpeciesProfileBody): Promise<SpeciesProfileItem> {
  return apiRequest('/api/v1/species-profiles', { method: 'POST', body });
}

/**
 * `PATCH /species-profiles/{id}` — Admin only. Omitted fields are left alone and `bands` replaces the set; a
 * built-in answers `403 builtin_immutable`.
 */
export async function updateSpeciesProfile(
  profileId: string,
  body: UpdateSpeciesProfileBody
): Promise<SpeciesProfileItem> {
  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}`, { method: 'PATCH', body });
}

/**
 * `POST /species-profiles/{id}/duplicate` — Admin only. The copy is always a custom profile: a built-in is
 * read-only (FR-10 A2), so this is the way one is tuned. An omitted name derives "<source> (copy)".
 */
export async function duplicateSpeciesProfile(profileId: string, name?: string): Promise<SpeciesProfileItem> {
  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}/duplicate`, {
    method: 'POST',
    body: { name: name?.trim() ? name : null },
  });
}

/** `DELETE /species-profiles/{id}` — Admin only. Answers `204`, or `409 profile_in_use` when a terrarium uses it. */
export async function deleteSpeciesProfile(profileId: string): Promise<void> {
  await apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}`, { method: 'DELETE' });
}

/**
 * `POST /species-profiles/{id}/image` — Admin only, `multipart/form-data` with one part named `file`. Replaces any
 * picture already there and answers the updated profile, so the caller re-renders from one payload. PNG, JPEG or
 * WebP, at most 2 MB — and the server reads the type from the bytes, so a renamed file is refused as
 * `image_invalid` rather than stored as what it claimed to be.
 */
export async function setSpeciesProfileImage(
  profileId: string,
  file: File
): Promise<SpeciesProfileItem> {
  const form = new FormData();
  form.append('file', file);

  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}/image`, {
    method: 'POST',
    form,
  });
}

/**
 * `DELETE /species-profiles/{id}/image` — Admin only. Idempotent, and answers the updated profile rather than a
 * `204`, so the card re-renders from the payload instead of guessing what the profile looks like now.
 */
export async function deleteSpeciesProfileImage(profileId: string): Promise<SpeciesProfileItem> {
  return apiRequest(`/api/v1/species-profiles/${encodeURIComponent(profileId)}/image`, {
    method: 'DELETE',
  });
}

/**
 * The picture's bytes. Not `apiRequest`: the answer is an image, and the bytes are what a browser needs rather
 * than a parsed body. The path comes from the view's `imageUrl`, `?v=` included — that value is what stops a
 * replaced picture being served from a cache under the URL the old one had.
 */
export async function speciesProfilePicture(
  imageUrl: string,
  imageUpdatedAt: string | null,
  signal?: AbortSignal
): Promise<Blob> {
  const separator = imageUrl.includes('?') ? '&' : '?';
  const versioned = imageUpdatedAt ? `${imageUrl}${separator}v=${encodeURIComponent(imageUpdatedAt)}` : imageUrl;

  return apiBlob(versioned, { signal });
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

/**
 * `PATCH /terrariums/{id}` with `ownerUserId` — gives the enclosure to another account (2026-10-23).
 *
 * **Administrators only** (`data.write.any`): an ordinary keeper's request is refused with
 * `errors[0].code = owner_change_forbidden`. The bound board follows the enclosure — it stays in it, and its owner
 * becomes the new account so the new keeper's fleet lists it. `If-Match` carries the version the caller read, so a
 * concurrent edit answers `412` rather than silently overwriting it. Recorded as `terrarium.owner_changed`.
 */
export async function assignTerrariumOwner(
  id: string,
  ownerUserId: string,
  rowVersion: string
): Promise<TerrariumItem> {
  return apiRequest(`/api/v1/terrariums/${encodeURIComponent(id)}`, {
    method: 'PATCH',
    headers: { 'If-Match': rowVersion },
    body: { ownerUserId },
  });
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

/**
 * A paged read's query string. An absent filter is absent from the URL rather than sent empty, because
 * `?search=` and no `search` at all are the same filter to the API and only one of them is what the caller meant.
 */
function pageQuery(params: {
  search?: string;
  family?: string;
  state?: string;
  skip?: number;
  take?: number;
}): string {
  const query = new URLSearchParams();

  if (params.search) {
    query.set('search', params.search);
  }
  if (params.family) {
    query.set('family', params.family);
  }
  if (params.state) {
    query.set('state', params.state);
  }
  if (params.skip !== undefined) {
    query.set('skip', String(params.skip));
  }
  if (params.take !== undefined) {
    query.set('take', String(params.take));
  }

  return query.toString();
}

// ---- Account management: `/api/v1/users` (FR-18) ---------------------------------------------------

/** `GET /users` — requires `user.read`. `total` counts the whole filtered set, so a pager can be rendered. */
export async function listUsers(params: {
  search?: string;
  skip?: number;
  take?: number;
}): Promise<UserPage> {
  return apiRequest(`/api/v1/users?${pageQuery(params)}`);
}

/**
 * `POST /users` — requires `user.manage`. The administrator supplies the first password: this deployment has no
 * channel that could deliver a generated one, which is why the form has a password box at all (`R-13`).
 */
export async function createUser(body: CreateUserBody): Promise<UserSummary> {
  return apiRequest('/api/v1/users', { method: 'POST', body });
}

/** `GET /users/{id}` — requires `user.read`. `404 user_not_found` for an id no account holds. */
export async function getUser(userId: string): Promise<UserSummary> {
  return apiRequest(`/api/v1/users/${encodeURIComponent(userId)}`);
}

/**
 * `PATCH /users/{id}` — requires `user.manage`. A patch, so only what the form changed is sent; `quietHoursStart`
 * and `quietHoursEnd` must travel as a pair (`400 quiet_hours_incomplete` otherwise).
 */
export async function updateUser(userId: string, body: UpdateAccountBody): Promise<UserSummary> {
  return apiRequest(`/api/v1/users/${encodeURIComponent(userId)}`, { method: 'PATCH', body });
}

/**
 * `POST /users/{id}/reset-password` — requires `user.manage`. Every session of that account ends, so a stolen one
 * cannot outlive the reset performed to kill it.
 */
export async function resetUserPassword(userId: string, newPassword: string): Promise<UserSummary> {
  return apiRequest(`/api/v1/users/${encodeURIComponent(userId)}/reset-password`, {
    method: 'POST',
    body: { newPassword },
  });
}

/**
 * `PATCH /users/{id}/role` — requires `user.manage`. `role` is a role **code**, matched case-insensitively; the
 * target's sessions end because permissions travel in the token.
 */
export async function setUserRole(userId: string, role: string): Promise<UserSummary> {
  return apiRequest(`/api/v1/users/${encodeURIComponent(userId)}/role`, {
    method: 'PATCH',
    body: { role },
  });
}

/** `POST /users/{id}/disable` and `/enable` — requires `user.manage`. Idempotent, and answers the account. */
export async function setUserDisabled(userId: string, disabled: boolean): Promise<UserSummary> {
  return apiRequest(
    `/api/v1/users/${encodeURIComponent(userId)}/${disabled ? 'disable' : 'enable'}`,
    { method: 'POST' }
  );
}

/**
 * `DELETE /users/{id}` — requires `user.manage`. A hard delete: the account's enclosures and their readings, alerts
 * and summaries go with it, its boards are unbound rather than revoked, and its own species profiles and inbox are
 * removed. `POST /users/{id}/disable` is the reversible act, and the only way to keep what the account held.
 */
export async function deleteUser(userId: string): Promise<UserSummary> {
  return apiRequest(`/api/v1/users/${encodeURIComponent(userId)}`, {
    method: 'DELETE',
  });
}

// ---- The role matrix: `/api/v1/admin/roles` and `/admin/permissions` (FR-18) ------------------------

/** `GET /admin/roles` — requires `user.read`. Every role with its grants, so the matrix needs one call. */
export async function listRoles(): Promise<{ items: RoleSummary[] }> {
  return apiRequest('/api/v1/admin/roles');
}

/** `GET /admin/roles/{id}` — requires `user.read`. `404 role_not_found` for an id no role holds. */
export async function getRole(roleId: string): Promise<RoleSummary> {
  return apiRequest(`/api/v1/admin/roles/${encodeURIComponent(roleId)}`);
}

/** `POST /admin/roles` — requires `user.manage`. A code another role uses is `400 role_invalid`. */
export async function createRole(body: CreateRoleBody): Promise<RoleSummary> {
  return apiRequest('/api/v1/admin/roles', { method: 'POST', body });
}

/**
 * `PATCH /admin/roles/{id}` — requires `user.manage`. `permissions` replaces the whole set, because a merge would
 * make a permission impossible to take away. Sessions are *not* ended by this route, so a revocation takes up to
 * one access-token lifetime to bite — which is what the editor warns about.
 */
export async function updateRole(roleId: string, body: UpdateRoleBody): Promise<RoleSummary> {
  return apiRequest(`/api/v1/admin/roles/${encodeURIComponent(roleId)}`, { method: 'PATCH', body });
}

/**
 * `DELETE /admin/roles/{id}` — requires `user.manage`. A shipped role answers `409 system_role_immutable` and a
 * role accounts still hold answers `409 role_in_use`, so the screen offers delete only where it can succeed.
 */
export async function deleteRole(roleId: string): Promise<RoleSummary> {
  return apiRequest(`/api/v1/admin/roles/${encodeURIComponent(roleId)}`, { method: 'DELETE' });
}

/** `GET /admin/permissions` — requires `user.read`. The catalogue's groups and labels drive the matrix's rows. */
export async function listPermissions(): Promise<{ items: PermissionSummary[] }> {
  return apiRequest('/api/v1/admin/permissions');
}

// ---- The get-all dashboard: `/api/v1/admin/*` (requires `data.read.any`) -----------------------------

/**
 * `GET /admin/overview` — the whole first screen in one answer: the counts, every account with its holdings and the
 * description of each alert family. One call rather than eight, because asking the database eight times for a
 * header is how a dashboard becomes slower than the thing it summarises.
 */
export async function adminOverview(): Promise<AdminOverview> {
  return apiRequest('/api/v1/admin/overview');
}

/** `GET /admin/terrariums` — every account's enclosures, newest first. `search` matches name or owner username. */
export async function listAdminTerrariums(params: {
  search?: string;
  skip?: number;
  take?: number;
}): Promise<AdminPage<AdminTerrariumView>> {
  return apiRequest(`/api/v1/admin/terrariums?${pageQuery(params)}`);
}

/** `GET /admin/devices` — every board in the deployment, unclaimed ones included. `search` matches id, name, owner. */
export async function listAdminDevices(params: {
  search?: string;
  skip?: number;
  take?: number;
}): Promise<AdminPage<AdminDeviceView>> {
  return apiRequest(`/api/v1/admin/devices?${pageQuery(params)}`);
}

/**
 * `GET /admin/alerts` — every account's alerts of both families, newest first. `family` is `enclosure` or `species`
 * and `state` is `Open`, `Acknowledged` or `Resolved`; either is a bad request rather than a silent fallback when it
 * names something this build does not have.
 */
export async function listAdminAlerts(params: {
  family?: string;
  state?: string;
  skip?: number;
  take?: number;
}): Promise<AdminPage<AdminAlertView>> {
  return apiRequest(`/api/v1/admin/alerts?${pageQuery(params)}`);
}
