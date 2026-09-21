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
    // A 503 body still carries the checks: that is exactly the interesting case.
    return { status: 'Unhealthy' };
  }
}
