import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../api/client';

/**
 * Polling, which is this client's live path until the SignalR push of 2.9 is wired to it (roadmap 4.16: "polling
 * until 2.9"). The legacy dashboard used the same 30-second interval, and the interval is a member so a screen can
 * change it — the wallboard is the one that matters there.
 *
 * The hook is deliberately dull: it fetches, keeps the last good value, and reports a failure without throwing
 * away what it already had. That combination is the only honest way to render an unreachable server: the last
 * known value with its timestamp, plus a banner that says the server cannot be reached — never a zero, and never
 * a silent freeze.
 */

export interface Polled<T> {
  data: T | null;
  error: ApiError | null;
  loading: boolean;
  /** The instant of the last successful response, so a screen can show how old what it renders is. */
  updatedAt: Date | null;
  reload: () => void;
}

export function usePolled<T>(
  load: () => Promise<T>,
  intervalMs: number | null,
  deps: unknown[] = []
): Polled<T> {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [loading, setLoading] = useState(true);
  const [updatedAt, setUpdatedAt] = useState<Date | null>(null);
  const [nonce, setNonce] = useState(0);
  const mounted = useRef(true);

  // `load` is rebuilt on every render by design (callers pass an inline closure over their state), so the
  // effect keys on the caller's dependencies instead. Depending on `load` would poll in a loop.
  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const run = useCallback(async () => {
    try {
      const value = await loadRef.current();
      if (mounted.current) {
        setData(value);
        setError(null);
        setUpdatedAt(new Date());
      }
    } catch (cause) {
      if (!mounted.current) {
        return;
      }
      // A refusal and an unreachable server are different states and the UI says which (02-design/04 §6).
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      if (mounted.current) {
        setLoading(false);
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);

  useEffect(() => {
    void run();
    if (intervalMs === null) {
      return;
    }
    const timer = setInterval(() => void run(), intervalMs);
    return () => clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run, intervalMs, nonce]);

  const reload = useCallback(() => setNonce((value) => value + 1), []);
  return { data, error, loading, updatedAt, reload };
}
