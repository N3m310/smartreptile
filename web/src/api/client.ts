/*
 * The transport: one place that knows the base URL, the session and what a refusal means.
 *
 * Two rules from the roadmap shape it.
 *
 * 1. **A failure is rendered from its stable `code`, never from the message** (task 4.15). The API answers RFC
 *    7807 with a `code` and, for a field-level refusal, an `errors[]` of `{field, code, message}`. `ApiError`
 *    carries all of it, so a screen maps codes it knows and shows an honest fallback for one it does not —
 *    which is what makes a new server-side refusal a visible gap instead of a wrong sentence.
 *
 * 2. **A 401 rotates the session once — when the 401 is about the token.** Access tokens live 15 minutes
 *    (BR-01.3), so a rotation is the common path, not the exceptional one. It is single-flight: ten parallel
 *    requests that all see a 401 must produce one `POST /auth/refresh`, because the refresh token is single-use
 *    and a second rotation attempt with a consumed token revokes the whole family (TC-U-34) — i.e. a naive "retry
 *    each on 401" logs the keeper out for being busy. Two refusals are *not* about the token and must not rotate:
 *    a 401 that refused the credential this request carried (the current password, a recovery code) and a 401
 *    that names the refresh token as spent. Only the second ends the session, with subscribers told, so the shell
 *    can send the keeper to the sign-in form with `sessionExpired` rather than leaving a page that 401s on every
 *    click. A refusal that is about neither — a rate limit, a 5xx, an unreachable server — leaves the session
 *    where it is and is reported as itself, because signing somebody out over a rate limit is a lie about what
 *    happened *and* throws away a session that still works.
 *
 * Token storage is `localStorage`, and that is a deliberate, documented compromise: a browser client has no
 * keystore (`flutter_secure_storage` is the app's answer, not the web's), so any XSS on this origin can read the
 * tokens. `02-design/06` §2's rule is that tokens never go in a place a script cannot read — which the web
 * cannot satisfy, and which is why the web client is the demo/kiosk surface rather than the keeper's phone.
 */

import type { AuthSession, FieldViolation, UserProfile } from './types';

const BASE_URL = (import.meta.env.VITE_API_BASE ?? 'http://localhost:8080').replace(/\/+$/, '');
const SESSION_KEY = 'terraguard.session';
const REQUEST_TIMEOUT_MS = 15000;

export const apiBaseUrl = BASE_URL;

/** A refused or unreachable call, carrying the API's own vocabulary. */
export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly detail: string | null;
  readonly errors: FieldViolation[];

  constructor(code: string, status: number, detail: string | null, errors: FieldViolation[] = []) {
    super(detail ?? code);
    this.name = 'ApiError';
    this.code = code;
    this.status = status;
    this.detail = detail;
    this.errors = errors;
  }

  /** True when the server could not be reached at all — the only case that may be called a connection failure. */
  get isNetworkFailure(): boolean {
    return this.status === 0;
  }

  /** True when the route does not exist: a 404 with no `code` means "not built", not "not yours" (`02-design/04` §6). */
  get isNotBuilt(): boolean {
    return this.status === 404 && this.code === 'http_404';
  }
}

/** What a session is, as this client stores it: the tokens plus the profile they came with. */
export interface StoredSession {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
  user: UserProfile;
}

export function readSession(): StoredSession | null {
  const raw = localStorage.getItem(SESSION_KEY);
  if (!raw) {
    return null;
  }
  try {
    const parsed = JSON.parse(raw) as StoredSession;
    return parsed?.refreshToken && parsed?.accessToken ? parsed : null;
  } catch {
    // A corrupt entry is not a session: drop it rather than failing every call with a parse error.
    localStorage.removeItem(SESSION_KEY);
    return null;
  }
}

export function writeSession(session: StoredSession | null): void {
  if (session) {
    localStorage.setItem(SESSION_KEY, JSON.stringify(session));
  } else {
    localStorage.removeItem(SESSION_KEY);
  }
  for (const listener of sessionListeners) {
    listener(session);
  }
}

export function storedSession(session: AuthSession): StoredSession {
  return {
    accessToken: session.accessToken,
    accessTokenExpiresAtUtc: session.accessTokenExpiresAtUtc,
    refreshToken: session.refreshToken,
    refreshTokenExpiresAtUtc: session.refreshTokenExpiresAtUtc,
    user: session.user,
  };
}

type SessionListener = (session: StoredSession | null) => void;
const sessionListeners = new Set<SessionListener>();

/** Subscribes to session changes, including the one the refresh path makes when it gives up. */
export function onSessionChange(listener: SessionListener): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

/** True once the session has been dropped by a failed rotation, so the shell can explain why. */
let lastSessionLoss: 'expired' | null = null;

export function consumeSessionLoss(): 'expired' | null {
  const loss = lastSessionLoss;
  lastSessionLoss = null;
  return loss;
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  /** `false` for the auth routes themselves, which must never trigger a rotation. */
  allowRefresh?: boolean;
  signal?: AbortSignal;
}

/**
 * The refusals that answer "the credential in this request is wrong" rather than "this access token is not
 * usable". Rotating for one of them would spend a single-use refresh token on a typo.
 */
const CREDENTIAL_REFUSALS = new Set(['invalid_credentials', 'invalid_recovery_code', 'invalid_reset_code']);

/** The refresh refusals that mean the session is over. Anything else refused *this attempt*, not the session. */
const SESSION_ENDED = new Set(['refresh_token_invalid', 'token_reused']);

/** How a rotation attempt ended: a fresh session, or the error worth reporting and whether the session survived. */
type RotationResult =
  | { rotated: true }
  | { rotated: false; error: ApiError; sessionEnded: boolean };

/** The refresh promise while a rotation is in flight — the single-flight guard. */
let rotation: Promise<RotationResult> | null = null;

/** The stable code of a refusal, read from a clone so the body survives for `toApiError`. */
async function problemCode(response: Response): Promise<string | null> {
  try {
    const problem = (await response.clone().json()) as { code?: unknown };
    return typeof problem?.code === 'string' ? problem.code : null;
  } catch {
    // Not a problem document (an empty body, a proxy's own 401): no code to act on, so the caller keeps the
    // behaviour it had before codes existed.
    return null;
  }
}

async function rotateOnce(): Promise<RotationResult> {
  const session = readSession();
  if (!session) {
    return { rotated: false, error: new ApiError('unauthenticated', 401, null), sessionEnded: true };
  }

  rotation ??= (async (): Promise<RotationResult> => {
    try {
      const response = await send('/api/v1/auth/refresh', {
        method: 'POST',
        body: { refreshToken: session.refreshToken },
        allowRefresh: false,
      });

      if (!response.ok) {
        // A refusal is not a rotation. Reading this body as if it were a session wrote a problem document with no
        // tokens into `localStorage`, which `readSession` then rejected — i.e. a silent sign-out, from the one
        // request that exists to prevent one.
        const error = await toApiError(response);
        const sessionEnded = response.status === 401 && SESSION_ENDED.has(error.code);

        if (sessionEnded) {
          lastSessionLoss = 'expired';
          writeSession(null);
        }

        return { rotated: false, error, sessionEnded };
      }

      const renewed = (await response.json()) as AuthSession;

      if (!renewed?.accessToken || !renewed?.refreshToken) {
        return { rotated: false, error: new ApiError('unknown', response.status, null), sessionEnded: false };
      }

      writeSession(storedSession(renewed));
      return { rotated: true };
    } catch (cause) {
      // Never reached a server: offline, timed out. The session is not the thing that failed, so it stays.
      return {
        rotated: false,
        error: cause instanceof ApiError ? cause : new ApiError('network_unreachable', 0, null),
        sessionEnded: false,
      };
    } finally {
      rotation = null;
    }
  })();

  return rotation;
}

async function send(path: string, options: RequestOptions): Promise<Response> {
  const session = readSession();
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    return await fetch(`${BASE_URL}${path}`, {
      method: options.method ?? 'GET',
      headers: {
        ...(options.body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(session && path !== '/api/v1/auth/refresh'
          ? { Authorization: `Bearer ${session.accessToken}` }
          : {}),
      },
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal ?? controller.signal,
    });
  } catch (cause) {
    // No status at all: the request never reached a server. Not the same as a refusal, and the UI says so.
    if ((cause as Error)?.name === 'AbortError') {
      throw new ApiError('timeout', 0, null);
    }
    throw new ApiError('network_unreachable', 0, null);
  } finally {
    clearTimeout(timeout);
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const text = await response.text().catch(() => '');
  let code = `http_${response.status}`;
  let detail: string | null = null;
  let errors: FieldViolation[] = [];

  try {
    const problem = JSON.parse(text) as {
      code?: string;
      detail?: string;
      errors?: FieldViolation[];
    };
    code = problem.code ?? code;
    detail = problem.detail ?? null;
    errors = Array.isArray(problem.errors) ? problem.errors : [];
  } catch {
    // Not JSON. `http_404` with no body is how "this route is not built" arrives, and the UI distinguishes it.
  }

  return new ApiError(code, response.status, detail, errors);
}

/** One API call. Returns the parsed body, or throws `ApiError`. */
export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const allowRefresh = options.allowRefresh ?? true;
  let response = await send(path, options);

  if (response.status === 401 && allowRefresh && readSession()) {
    // A 401 this request earned by sending a wrong credential — the current password on the change-password form,
    // a recovery code — is the endpoint's answer, not a sign that the access token is unusable. Rotating for it
    // spends a refresh token per typo, and a refusal *of* that rotation is what turned a mistyped password into a
    // signed-out keeper.
    const code = await problemCode(response);

    if (code === null || !CREDENTIAL_REFUSALS.has(code)) {
      const attempt = await rotateOnce();

      if (attempt.rotated) {
        response = await send(path, options);
      } else if (!attempt.sessionEnded) {
        // The session is intact: report what actually refused the call instead of the stale 401.
        throw attempt.error;
      }
    }
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
