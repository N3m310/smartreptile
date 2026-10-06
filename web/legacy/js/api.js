// Minimal REST client for the dashboard.
//
// One place knows the API base URL (from the <meta name="api-base"> tag, so the same static files work on
// localhost and on the demo host) and how to read an RFC 7807 problem body.

const meta = document.querySelector('meta[name="api-base"]');
export const apiBase = (meta?.content ?? 'http://localhost:8080').replace(/\/$/, '');

// Sessions live in localStorage rather than in memory: a page reload must not sign the keeper out, and the
// wallboard tab has to share the session the Live page started.
const ACCESS_KEY = 'sr.accessToken';
const REFRESH_KEY = 'sr.refreshToken';

/** Access token for authenticated calls, restored from the last page load. */
let accessToken = localStorage.getItem(ACCESS_KEY);

/** Refresh token, spent once to survive the 15-minute access-token lifetime (BR-01.3). */
let refreshToken = localStorage.getItem(REFRESH_KEY);

/** Stores a session. Either argument may be null, which is exactly what signing out is. */
export function setSession(access, refresh) {
  accessToken = access ?? null;
  refreshToken = refresh ?? null;

  persist(ACCESS_KEY, accessToken);
  persist(REFRESH_KEY, refreshToken);
}

/** Forgets the session. */
export function clearSession() {
  setSession(null, null);
}

/** True when an access token is held; the Live page shows its sign-in form when it is not. */
export function hasSession() {
  return Boolean(accessToken);
}

function persist(key, value) {
  if (value) {
    localStorage.setItem(key, value);
  } else {
    localStorage.removeItem(key);
  }
}

/** Error carrying the API problem code so pages can explain what happened. */
export class ApiError extends Error {
  constructor(code, status, detail) {
    super(detail ?? code);
    this.code = code;
    this.status = status;
    this.detail = detail ?? null;
  }

  /** True when the server could not be reached at all. */
  get isNetworkFailure() {
    return this.status === 0;
  }

  /** True when a server answered but has no such endpoint (e.g. the M2 REST routes before they exist). */
  get isMissingEndpoint() {
    return this.status === 404;
  }
}

/**
 * How a failed call should be described, decided in one place so no page invents its own wording:
 *   'unreachable'      nothing answered, so the values on screen really are the last known ones;
 *   'missing-endpoint' a server answered 404 with no explanation at all -> the route is simply not built yet;
 *   'not-found'        a server answered 404 *with* a problem code -> the API understood the request and is
 *                      refusing it, which is how ownership is hidden (BR-02.2: cross-owner access returns 404);
 *   'failed'           any other non-2xx, i.e. the request was understood and refused (401, 500, ...).
 * The first two must not be conflated: reporting "cannot reach the server" for an endpoint that is merely
 * unbuilt is what the M1 snapshot pass caught, and reporting "not built yet" for someone else's terrarium
 * would be the same mistake in the other direction (docs/06-report/snapshots/README.md).
 */
export function failureKind(error) {
  if (error?.isNetworkFailure) return 'unreachable';
  if (error?.isMissingEndpoint && error.detail === null) return 'missing-endpoint';
  if (error?.isMissingEndpoint) return 'not-found';
  return 'failed';
}

/**
 * Signs in and stores the session (§07-appendices/03 §4.1). Throws ApiError carrying the server's stable problem
 * code, so a page can tell a wrong password (`invalid_credentials`) from a lockout (`account_locked`) without
 * parsing prose.
 */
export async function login(usernameOrEmail, password) {
  const session = await postJson('/api/v1/auth/login', { usernameOrEmail, password });
  setSession(session.accessToken, session.refreshToken);

  return session.user;
}

/** POST a JSON document. */
export function postJson(path, body, allowRefresh = true) {
  return send('POST', path, body, allowRefresh);
}

/**
 * Replaces a forgotten password with the account's backup recovery code (§07-appendices/03 §4.1). Returns the
 * replacement code, which the caller must show once — the code it consumed is spent.
 */
export function recover(usernameOrEmail, recoveryCode, newPassword) {
  return postJson('/api/v1/auth/recover', { usernameOrEmail, recoveryCode, newPassword })
    .then((result) => result.recoveryCode);
}

/**
 * Asks the server to issue a reset code (§07-appendices/03 §4.1). Always answers 202, whether or not the
 * identifier has an account — the caller cannot tell the two apart, and must not try to.
 */
export function forgotPassword(usernameOrEmail) {
  return postJson('/api/v1/auth/forgot-password', { usernameOrEmail });
}

/**
 * Replaces a forgotten password with a code the server issued. Returns the new backup recovery code, for the same
 * reason as {@link recover}: the reset rotates it, so the keeper has to leave with the working one.
 */
export function resetPassword(usernameOrEmail, resetCode, newPassword) {
  return postJson('/api/v1/auth/reset-password', { usernameOrEmail, resetCode, newPassword })
    .then((result) => result.recoveryCode);
}

/** GET a JSON document; throws ApiError on any non-2xx response. */
export function getJson(path) {
  return send('GET', path, null, true);
}

async function send(method, path, body, allowRefresh) {
  let response;

  try {
    response = await fetch(`${apiBase}${path}`, {
      method,
      headers: {
        Accept: 'application/json',
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
    });
  } catch {
    // No status code: unreachable. Callers show cached values with explicit timestamps instead of pretending.
    throw new ApiError('network_unreachable', 0, null);
  }

  const text = await response.text();

  if (!response.ok) {
    // An access token lasts 15 minutes, so a single 401 is far likelier to be an expired session than a bad one.
    // Rotating once and repeating the call keeps the dashboard alive across a demo; a second 401 means the refresh
    // token is spent and the keeper has to sign in again.
    if (response.status === 401 && allowRefresh && refreshToken && (await rotateSession())) {
      return send(method, path, body, false);
    }

    throw toApiError(response, text);
  }

  return text.length ? JSON.parse(text) : {};
}

/** Rotates the session. False means the refresh token no longer works, and the stored session is dropped. */
async function rotateSession() {
  try {
    const session = await postJson('/api/v1/auth/refresh', { refreshToken }, false);
    setSession(session.accessToken, session.refreshToken);

    return true;
  } catch {
    clearSession();

    return false;
  }
}

function toApiError(response, text) {
  let code = `http_${response.status}`;
  let detail = null;

  try {
    const problem = JSON.parse(text);
    code = problem.code ?? code;
    detail = problem.detail ?? null;
  } catch {
    /* not JSON: keep the status-derived code */
  }

  return new ApiError(code, response.status, detail);
}

/** Reads readiness (database + MQTT broker) for the "live updates paused" banner. */
export async function getReadiness() {
  try {
    return await getJson('/health/ready');
  } catch (error) {
    if (error instanceof ApiError && error.isNetworkFailure) {
      return { status: 'Unreachable' };
    }
    // A 404 means this server has no readiness endpoint at all; reporting 'Unhealthy' would blame the database.
    if (error instanceof ApiError && error.isMissingEndpoint) {
      return { status: 'MissingEndpoint' };
    }
    // A 503 body still carries the checks: that is exactly the interesting case.
    return { status: 'Unhealthy' };
  }
}
