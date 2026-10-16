import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import {
  consumeSessionLoss,
  onSessionChange,
  readSession,
  storedSession,
  writeSession,
  type StoredSession,
} from '../api/client';
import * as auth from '../api/endpoints';
import type { PermissionCode, UserProfile } from '../api/types';

/**
 * Who is signed in, and the one place that changes.
 *
 * Three jobs, and the third is the one that is easy to get wrong:
 *
 * 1. **Sign in / register / sign out** against `/auth/*`, storing the session through `api/client` so the
 *    transport and the shell never disagree about whether a session exists.
 * 2. **Re-read the profile** on mount when a stored session exists. `localStorage` is user-editable and a
 *    role read from it would be a permission decision made by the page (`02-design/06` §3), so the role and the
 *    permission codes the UI gates on come from `GET /auth/me`. A failure here is not an error to shout about: it
 *    means the session is gone, which is exactly what signing out looks like.
 * 3. **Explain a lost session.** A failed rotation drops the session from inside the transport, so the provider
 *    subscribes and reports `expired` — otherwise the keeper is bounced to the sign-in form with no reason
 *    given, which reads as a bug in the app rather than a 15-minute token that could not be rotated.
 */

interface SessionValue {
  session: StoredSession | null;
  user: UserProfile | null;
  /**
   * True when the account's role grants `code`. This — never the role's name — is what a screen gates on: a
   * deployment may re-point a role at another permission set, and a screen that tested the name would keep
   * rendering the old answer (`02-design/06` §3, FR-18). An unknown or absent code is false, so a screen is
   * hidden rather than shown to someone who cannot use it.
   */
  hasPermission: (code: PermissionCode) => boolean;
  /** Set when the transport lost the session, cleared once shown. */
  lostReason: 'expired' | null;
  clearLostReason: () => void;
  signIn: (session: AuthSessionLike) => void;
  signOut: () => Promise<void>;
  refreshProfile: () => Promise<void>;
}

/** What `signIn` accepts: a fresh session from `login`, or a stored one restored at boot. */
export type AuthSessionLike = Parameters<typeof storedSession>[0];

const SessionContext = createContext<SessionValue | null>(null);

export const SessionProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [session, setSession] = useState<StoredSession | null>(readSession);
  const [lostReason, setLostReason] = useState<'expired' | null>(null);

  // The transport can drop the session from inside a request; this is how the shell hears about it.
  useEffect(
    () =>
      onSessionChange((next) => {
        setSession(next);
        if (!next) {
          const loss = consumeSessionLoss();
          if (loss) {
            setLostReason(loss);
          }
        }
      }),
    []
  );

  // Trust the server's view of the account, not the cached copy: a role change or a deleted account must not
  // survive in the browser.
  useEffect(() => {
    if (!session) {
      return;
    }
    let cancelled = false;
    void auth
      .me()
      .then((profile) => {
        if (!cancelled) {
          const current = readSession();
          if (current) {
            writeSession({ ...current, user: profile });
          }
        }
      })
      .catch(() => {
        // The transport already handled a 401 by dropping the session; anything else (offline) leaves the
        // cached profile in place, because a network blip is not a reason to sign somebody out.
      });
    return () => {
      cancelled = true;
    };
    // Keyed on the *identity* of the session, not on the session object: the profile write below changes the
    // object, and depending on it would re-run this effect forever.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session?.refreshToken]);

  const signIn = useCallback((next: AuthSessionLike) => {
    const stored = storedSession(next);
    writeSession(stored);
    setSession(stored);
    setLostReason(null);
  }, []);

  const signOut = useCallback(async () => {
    const current = readSession();
    writeSession(null);
    setSession(null);
    if (current) {
      // Idempotent server-side: an already-revoked token is still a success, so this needs no error handling
      // beyond not blocking the sign-out on it.
      await auth.logout(current.refreshToken).catch(() => undefined);
    }
  }, []);

  const refreshProfile = useCallback(async () => {
    const profile = await auth.me();
    const current = readSession();
    if (current) {
      writeSession({ ...current, user: profile });
    }
  }, []);

  // A set rather than a scan of the array: the sidebar asks this once per entry on every render, and the account's
  // permissions do not change while a session lives.
  const permissions = useMemo(
    () => new Set(session?.user?.permissions ?? []),
    [session?.user?.permissions]
  );

  const hasPermission = useCallback(
    (code: PermissionCode) => permissions.has(code),
    [permissions]
  );

  const value = useMemo<SessionValue>(
    () => ({
      session,
      user: session?.user ?? null,
      hasPermission,
      lostReason,
      clearLostReason: () => setLostReason(null),
      signIn,
      signOut,
      refreshProfile,
    }),
    [session, hasPermission, lostReason, signIn, signOut, refreshProfile]
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
};

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) {
    throw new Error('useSession must be used inside SessionProvider');
  }
  return value;
}
