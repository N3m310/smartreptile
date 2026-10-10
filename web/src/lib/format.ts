/*
 * Formatting rules from `02-design/04` §7, in one place so two screens cannot disagree about a number.
 *
 * The rules are the design's, not invented here: temperature 1–2 decimals depending on magnitude, humidity
 * integer in cards, lux with thousands separators, UV 1 decimal; dates `dd/MM HH:mm` for vi and `dd MMM HH:mm`
 * for en; units always metric.
 *
 * `formatRelative` uses the deck's own `lastUpdated*` plural strings rather than a hand-built sentence, which is
 * why it needs the `t` function: the staleness wording is shared with the app, so the two clients cannot drift
 * into saying "2 minutes ago" differently.
 */

import type { Language } from '../i18n/strings';
import type { TParams } from '../i18n';

export type TFunc = (key: string, params?: TParams) => string;

/** The metric dictionary's display name, from the deck. Unknown codes fall back to the raw code. */
const METRIC_KEY: Record<string, string> = {
  tempC: 'metricTempC',
  humidityPct: 'metricHumidityPct',
  lightLux: 'metricLightLux',
  uvIndex: 'metricUvIndex',
  surfaceTempC: 'metricSurfaceTempC',
};

export function metricLabel(t: TFunc, code: string): string {
  return METRIC_KEY[code] ? t(METRIC_KEY[code]) : code;
}

/** A value as the design renders it: the metric decides the precision, never the screen. */
export function formatValue(code: string, value: number): string {
  switch (code) {
    case 'humidityPct':
      return String(Math.round(value));
    case 'uvIndex':
      return value.toFixed(1);
    case 'lightLux':
      return Math.round(value).toLocaleString('en-US');
    case 'tempC':
    case 'surfaceTempC':
      // 1 decimal at a glance, 2 when the value is small enough for the second decimal to carry information.
      return Math.abs(value) < 10 ? value.toFixed(2) : value.toFixed(1);
    default:
      return String(value);
  }
}

const VI_MONTHS = [
  '',
  'thg 1',
  'thg 2',
  'thg 3',
  'thg 4',
  'thg 5',
  'thg 6',
  'thg 7',
  'thg 8',
  'thg 9',
  'thg 10',
  'thg 11',
  'thg 12',
];

const pad = (value: number) => String(value).padStart(2, '0');

/** `dd/MM HH:mm` (vi) or `dd MMM HH:mm` (en), in the browser's zone. */
export function formatDateTime(iso: string | null | undefined, lang: Language): string {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  const day = pad(date.getDate());
  return lang === 'vi'
    ? `${day}/${pad(date.getMonth() + 1)} ${time}`
    : `${day} ${VI_MONTHS[date.getMonth() + 1].replace('thg ', '')} ${time}`;
}

/** `dd/MM/yyyy HH:mm`, for a value that is a record rather than a measurement (a creation date). */
export function formatDate(iso: string | null | undefined, lang: Language): string {
  if (!iso) {
    return '—';
  }
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }
  const day = `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`;
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  return lang === 'vi' ? `${day} ${time}` : `${day} ${time}`;
}

/** Seconds between an instant and now, floored at zero (a clock skew must not read as "−3 s ago"). */
export function secondsSince(iso: string | null | undefined, now: Date = new Date()): number | null {
  if (!iso) {
    return null;
  }
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) {
    return null;
  }
  return Math.max(0, Math.round((now.getTime() - then) / 1000));
}

/**
 * The deck's relative-time wording. Hours is where it stops being useful, so beyond a day the absolute stamp is
 * shown instead — "37 hours ago" is a worse answer than the timestamp.
 */
export function formatRelative(t: TFunc, iso: string | null | undefined, now: Date = new Date()): string {
  const seconds = secondsSince(iso, now);
  if (seconds === null) {
    return t('never');
  }
  if (seconds < 60) {
    return t('lastUpdatedSeconds', { count: seconds });
  }
  if (seconds < 3600) {
    return t('lastUpdatedMinutes', { count: Math.floor(seconds / 60) });
  }
  if (seconds < 86400) {
    return t('lastUpdatedHours', { count: Math.floor(seconds / 3600) });
  }
  return t('lastUpdatedHours', { count: Math.floor(seconds / 3600) });
}
