// Tiny observable store for the dashboard.
//
// It mirrors the Flutter TelemetryProvider semantics on purpose (docs/03-implementation/05 §2 and §6):
// freshness is derived at render time from the sample age, and one ingestion batch produces one render pass.
// Two clients that disagree about what "stale" means is a bug waiting to happen.

/** Staleness threshold: three missed sampling intervals (FR-07 / BR-07.2). */
export function freshnessOf(capturedAt, samplingIntervalSec, kind) {
  if (!capturedAt || kind === 'NoData' || kind === 'Unavailable') {
    return { kind: 'NoData', ageMs: null, stale: false };
  }

  const ageMs = Date.now() - new Date(capturedAt).getTime();
  const thresholdMs = (samplingIntervalSec ?? 60) * 3 * 1000;

  return { kind: 'Fresh', ageMs, stale: ageMs > thresholdMs };
}

/** Formats an age in ms the way the app does: "12 s", "12 min", "3 h". */
export function formatAge(ageMs) {
  if (ageMs === null || ageMs === undefined) {
    return '—';
  }
  const seconds = Math.max(0, Math.round(ageMs / 1000));
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  return `${Math.round(minutes / 60)} h`;
}

/** Formats a metric value with the precision of the metric dictionary. */
export function formatValue(value, metricCode) {
  if (value === null || value === undefined) {
    return '—';
  }
  const digits = metricCode === 'humidityPct' || metricCode === 'lightLux' ? 0 : 1;
  const text = Number(value).toFixed(digits);
  return metricCode === 'lightLux' ? text.replace(/\B(?=(\d{3})+(?!\d))/g, ' ') : text;
}

/** Creates a store that notifies subscribers once per `apply` call, not once per metric. */
export function createStore() {
  const state = {
    terrariums: [],
    latest: {},        // terrariumId -> { metrics: [...], device: {...} }
    readiness: null,
    error: null,
  };

  const subscribers = new Set();

  function notify() {
    for (const subscriber of subscribers) {
      subscriber(state);
    }
  }

  return {
    state,

    /** Subscribes to changes; returns an unsubscribe function. */
    subscribe(fn) {
      subscribers.add(fn);
      return () => subscribers.delete(fn);
    },

    /** Replaces the terrarium list. */
    setTerrariums(list) {
      state.terrariums = list;
      notify();
    },

    /** Stores the latest snapshot for one terrarium (one notify for the whole batch). */
    setLatest(terrariumId, payload) {
      state.latest[terrariumId] = payload;
      state.error = null;
      notify();
    },

    /** Records that the backend could not be reached, keeping the cached values visible. */
    setError(error) {
      state.error = error;
      notify();
    },

    /** Stores the readiness report. */
    setReadiness(readiness) {
      state.readiness = readiness;
      notify();
    },
  };
}
