// Minimal REST client for the dashboard.
//
// One place knows the API base URL (from the <meta name="api-base"> tag, so the same static files work on
// localhost and on the demo host) and how to read an RFC 7807 problem body.

const meta = document.querySelector('meta[name="api-base"]');
export const apiBase = (meta?.content ?? 'http://localhost:8080').replace(/\/$/, '');

/** Access token for authenticated calls (set after login in M2). */
let accessToken = null;

/** Stores the bearer token used by subsequent requests. */
export function setAccessToken(token) {
  accessToken = token;
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

/** GET a JSON document; throws ApiError on any non-2xx response. */
export async function getJson(path) {
  let response;

  try {
    response = await fetch(`${apiBase}${path}`, {
      headers: {
        Accept: 'application/json',
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
    });
  } catch {
    // No status code: unreachable. Callers show cached values with explicit timestamps instead of pretending.
    throw new ApiError('network_unreachable', 0, null);
  }

  const text = await response.text();

  if (!response.ok) {
    let code = `http_${response.status}`;
    let detail = null;
    try {
      const problem = JSON.parse(text);
      code = problem.code ?? code;
      detail = problem.detail ?? null;
    } catch {
      /* not JSON: keep the status-derived code */
    }
    throw new ApiError(code, response.status, detail);
  }

  return text.length ? JSON.parse(text) : {};
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
