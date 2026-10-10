import React from 'react';
import type { MetricReading } from '../api/types';
import { useI18n } from '../i18n';
import { formatDateTime, formatRelative, formatValue, metricLabel } from '../lib/format';
import { isStale, statusBarClass, statusLabel, statusTextClass } from '../lib/status';

/**
 * One metric card, shared by the dashboard, the terrarium detail and the wallboard so the same reading cannot be
 * rendered two ways (BR-08.1: value, band and timestamp travel together).
 *
 * Everything it shows is the server's: `value`, `unit`, `status`, `target` and `capturedAt` all come from
 * `readings/latest`, and there is no comparison anywhere in this file (task 4.17). A metric the server sent
 * without a band renders no band line — it does not invent one from the species profile, because the profile's
 * band is phase-resolved by the evaluator and a second resolution here would be a second opinion.
 *
 * §6's two rules are enforced visibly: the timestamp is on every card, and a reading older than three sampling
 * intervals is dimmed *and* labelled rather than shown as current.
 */
export const MetricCard: React.FC<{
  metric: MetricReading;
  samplingIntervalSec: number | null;
  variant?: 'card' | 'wall';
  now?: Date;
}> = ({ metric, samplingIntervalSec, variant = 'card', now = new Date() }) => {
  const { t, lang } = useI18n();
  const stale = isStale(metric.capturedAt, samplingIntervalSec, now);
  const wall = variant === 'wall';

  // §8: a card is announced with name, value, unit and status — the three things a glance would take from it.
  const label = `${metricLabel(t, metric.code)} ${formatValue(metric.code, metric.value)} ${metric.unit}, ${statusLabel(
    t,
    metric.status
  )}`;

  return (
    <article
      aria-label={label}
      className={`rounded-2xl border border-hairline bg-surface ${
        wall ? 'px-6 py-6' : 'px-5 py-4'
      } ${stale ? 'is-stale' : ''}`}
    >
      <header className="flex items-start justify-between gap-3">
        <h3
          className={`font-heading font-semibold uppercase tracking-wider text-muted ${
            wall ? 'text-sm' : 'text-[11px]'
          }`}
        >
          {metricLabel(t, metric.code)}
        </h3>
        <span
          className={`inline-flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wider ${statusTextClass(
            metric.status
          )}`}
        >
          <span className={`h-2 w-2 rounded-full ${statusBarClass(metric.status)}`} aria-hidden="true" />
          {statusLabel(t, metric.status)}
        </span>
      </header>

      <p className="mt-3 flex items-baseline gap-2">
        <span
          className={`font-mono font-semibold text-ink ${wall ? 'text-6xl' : 'text-3xl'}`}
        >
          {formatValue(metric.code, metric.value)}
        </span>
        <span className={`text-muted ${wall ? 'text-lg' : 'text-sm'}`}>{metric.unit}</span>
      </p>

      {/* The band the value was judged against, exactly as sent. Absent when the server configured none. */}
      {metric.target && (metric.target.min !== null || metric.target.max !== null) ? (
        <p className="mt-2 text-xs text-muted">
          {t('targetBandLabel', {
            min: metric.target.min ?? '—',
            max: metric.target.max ?? '—',
            unit: metric.unit,
          })}
        </p>
      ) : null}

      <footer className="mt-2 flex flex-wrap items-center gap-x-2 text-[11px] text-muted">
        <time dateTime={metric.capturedAt}>{formatDateTime(metric.capturedAt, lang)}</time>
        <span aria-hidden="true">·</span>
        <span>{formatRelative(t, metric.capturedAt, now)}</span>
        {stale ? (
          <>
            <span aria-hidden="true">·</span>
            <span className="text-warning">{t('staleReading')}</span>
          </>
        ) : null}
      </footer>
    </article>
  );
};
