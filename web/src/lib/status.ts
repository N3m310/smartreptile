/*
 * Status rendering — the ADR-005 rule, made structural (roadmap 4.17).
 *
 * The screen does not decide whether a value is in range. It renders `status` exactly as `readings/latest`
 * sent it, and shows the band the server said the value was judged against (`target`). That is why this file
 * has a total mapping from the API's five values to a label and a colour and no comparison anywhere: there is
 * no branch here that could look at a temperature and form an opinion about it. A metric the server sent
 * without a band renders with `target: null` and says so, rather than falling back to anything.
 *
 * The mapping is total on purpose. A status the server adds later falls to `Unavailable`'s grey and the raw
 * code, which is a visible gap, instead of defaulting to green — a monitoring UI that paints an unknown state
 * as healthy is worse than one that paints nothing.
 */

import type { ReadingStatus } from '../api/types';
import type { TFunc } from './format';

const STATUS_LABEL_KEY: Record<string, string> = {
  InRange: 'statusInRange',
  OutOfRange: 'statusOutOfRange',
  Critical: 'statusCritical',
  Unavailable: 'statusUnavailable',
  Maintenance: 'statusMaintenance',
};

/** Text and border colours, from the measured palette in `index.css`. Never colour alone (NFR-06). */
const STATUS_TEXT_CLASS: Record<string, string> = {
  InRange: 'text-inrange',
  OutOfRange: 'text-warning',
  Critical: 'text-critical',
  Unavailable: 'text-unknown',
  Maintenance: 'text-unknown',
};

const STATUS_BAR_CLASS: Record<string, string> = {
  InRange: 'bg-inrange',
  OutOfRange: 'bg-warning',
  Critical: 'bg-critical',
  Unavailable: 'bg-unknown',
  Maintenance: 'bg-unknown',
};

export function statusLabel(t: TFunc, status: string): string {
  const key = STATUS_LABEL_KEY[status];
  return key ? t(key) : status;
}

export function statusTextClass(status: string): string {
  return STATUS_TEXT_CLASS[status] ?? 'text-unknown';
}

export function statusBarClass(status: string): string {
  return STATUS_BAR_CLASS[status] ?? 'bg-unknown';
}

/** True for the two statuses that mean "we do not know", as opposed to "we know, and it is wrong". */
export function isUnknownStatus(status: string): boolean {
  return status === 'Unavailable' || status === 'Maintenance';
}

export function isReadingStatus(value: string): value is ReadingStatus {
  return STATUS_LABEL_KEY[value] !== undefined;
}

/** `deviceOnline` / `deviceOffline` / `deviceUnbound` — the device vocabulary, not the metric one. */
export function deviceStatusLabel(t: TFunc, deviceStatus: string | null): string {
  if (!deviceStatus) {
    return t('deviceUnbound');
  }
  return deviceStatus === 'online' ? t('deviceOnline') : t('deviceOffline');
}

/**
 * §6's freshness rule: a card older than three sampling intervals is stale, and stale values are dimmed and
 * labelled instead of being shown as if they were current. This compares *age*, not a value against a band, so
 * it does not cross the ADR-005 line — and the interval is the server's.
 */
export function isStale(
  capturedAt: string | null | undefined,
  samplingIntervalSec: number | null,
  now: Date = new Date()
): boolean {
  if (!capturedAt || !samplingIntervalSec) {
    return false;
  }
  const age = (now.getTime() - new Date(capturedAt).getTime()) / 1000;
  return Number.isFinite(age) && age > 3 * samplingIntervalSec;
}
