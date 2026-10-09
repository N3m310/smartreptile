import React, { useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { Maximize2, MonitorPlay } from 'lucide-react';
import { Line, LineChart, ResponsiveContainer } from 'recharts';
import * as api from '../api/endpoints';
import type { LatestReadings, RangeSeries, TerrariumList } from '../api/types';
import { MetricCard } from '../components/MetricCard';
import { ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { LANGUAGES } from '../i18n/strings';
import { formatDateTime, formatRelative, metricLabel } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

/**
 * W1 — the wallboard (roadmap 4.19, and the one screen `02-design/04` §1.2 notes is *new* work rather than a port).
 *
 * It is a kiosk route: no sidebar, no controls, built to be left on a screen. What it renders is the same
 * `readings/latest` payload the dashboard renders, through the same card component, so the two surfaces cannot
 * disagree about a value or a status — the failure this design is most exposed to, because the wallboard is the
 * one nobody is standing next to.
 *
 * §3 sizes it for arm's length (metric value 32 sp and up) and §6 keeps it calm: numbers change on value change,
 * never continuously. The sparkline is the 24-hour trend §1.2 asks for, drawn from the same bucketed series the
 * history page uses, with gaps left as gaps.
 *
 * The chosen terrarium is a query parameter (`?terrarium=<id>`) so a second screen can be pointed at a specific
 * enclosure — the M4 definition of done is "the wallboard on a second screen".
 */

const POLL_MS = 30_000;
const DAY_MS = 24 * 60 * 60 * 1000;

export const Wallboard: React.FC = () => {
  const { t, lang, setLang } = useI18n();
  const [params, setParams] = useSearchParams();
  const [fullscreen, setFullscreen] = useState(false);

  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];
  const requested = params.get('terrarium');
  const selectedId =
    (requested && items.some((item) => item.id === requested) ? requested : null) ?? items[0]?.id ?? null;
  const selected = items.find((item) => item.id === selectedId) ?? null;

  useEffect(() => {
    if (selectedId && requested !== selectedId) {
      const next = new URLSearchParams(params);
      next.set('terrarium', selectedId);
      setParams(next, { replace: true });
    }
  }, [selectedId, requested, params, setParams]);

  const latest = usePolled<LatestReadings | null>(
    () => (selectedId ? api.latestReadings(selectedId) : Promise.resolve(null)),
    selectedId ? POLL_MS : null,
    [selectedId]
  );

  // The trend line follows the first metric the server actually sent — the wallboard has no opinion about which
  // metric matters, and a screenshot of it should not depend on this file guessing right.
  const trendMetric = latest.data?.metrics[0]?.code ?? null;
  const window = useMemo(() => {
    const to = new Date();
    const from = new Date(to.getTime() - DAY_MS);
    return { from: from.toISOString(), to: to.toISOString() };
  }, []);

  const trend = usePolled<RangeSeries | null>(
    () =>
      selectedId && trendMetric
        ? api.readings(selectedId, trendMetric, window.from, window.to)
        : Promise.resolve(null),
    5 * 60_000,
    [selectedId, trendMetric, window.from, window.to]
  );

  const readings = latest.data;
  const device = readings?.device ?? selected?.device ?? null;

  if (terrariums.loading) {
    return <LoadingPanel label={t('signingIn')} />;
  }

  if (items.length === 0) {
    return (
      <div className="flex min-h-screen items-center justify-center p-8">
        <div className="max-w-md text-center">
          <h1 className="font-heading text-2xl font-bold text-ink">{t('wallboardTitle')}</h1>
          <p className="mt-2 text-sm text-muted">{t('wallboardEmpty')}</p>
          <Link to="/terrariums" className="mt-4 inline-block text-sm text-accent hover:underline">
            {t('terrariumsTitle')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col gap-6 p-6 lg:p-10">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="font-heading text-4xl font-extrabold tracking-wide text-ink">
            {selected?.name ?? t('wallboardTitle')}
          </h1>
          <p className="mt-1 text-sm text-muted">
            {selected?.speciesName}
            {selected?.location ? ` · ${selected.location}` : ''}
          </p>
        </div>

        <div className="flex items-center gap-4 text-sm text-muted">
          <span className="inline-flex items-center gap-2">
            <span
              className={`h-2.5 w-2.5 rounded-full ${
                device?.status === 'online' ? 'bg-inrange' : 'bg-unknown'
              }`}
              aria-hidden="true"
            />
            {device ? device.deviceName : t('deviceUnbound')} ·{' '}
            {deviceStatusLabel(t, device?.status ?? null)}
          </span>
          <span>
            {t('lastSampleAt')}: {formatDateTime(readings?.lastSampleAt, lang)}
            {readings?.lastSampleAt ? ` · ${formatRelative(t, readings.lastSampleAt)}` : ''}
          </span>
          <div className="flex items-center gap-1">
            {LANGUAGES.map((option) => (
              <button
                key={option}
                type="button"
                onClick={() => setLang(option)}
                aria-pressed={lang === option}
                className={`rounded px-2 py-0.5 text-xs transition-colors ${
                  lang === option ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
                }`}
              >
                {option.toUpperCase()}
              </button>
            ))}
          </div>
          <button
            type="button"
            onClick={() => setFullscreen((value) => !value)}
            aria-label={t('wallboardTitle')}
            aria-pressed={fullscreen}
            className="rounded-lg border border-hairline p-1.5 text-muted transition-colors hover:text-ink"
          >
            <Maximize2 className="h-4 w-4" aria-hidden="true" />
          </button>
        </div>
      </header>

      {terrariums.error?.isNetworkFailure || latest.error?.isNetworkFailure ? (
        <UnreachableBanner onRetry={() => latest.reload()} />
      ) : null}
      {latest.error && !latest.error.isNetworkFailure ? (
        <ErrorPanel error={latest.error} onRetry={latest.reload} />
      ) : null}

      {!fullscreen && readings && readings.metrics.length > 0 ? (
        <section className="grid grid-cols-1 gap-5 sm:grid-cols-2 xl:grid-cols-3">
          {readings.metrics.map((metric) => (
            <MetricCard
              key={metric.code}
              metric={metric}
              samplingIntervalSec={device?.samplingIntervalSec ?? null}
              variant="wall"
            />
          ))}
        </section>
      ) : null}

      {/* The 24-hour trend §1.2 asks for. Bucketed by the server; gaps stay gaps. */}
      {!fullscreen && trend.data && trendMetric ? (
        <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
          <h2 className="font-heading text-xs font-semibold uppercase tracking-widest text-muted">
            {metricLabel(t, trendMetric)} · {t('range24h')}
          </h2>
          <div className="mt-3 h-28 w-full">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={trend.data.points}>
                <Line
                  type="monotone"
                  dataKey="avg"
                  stroke="#4a9e6a"
                  strokeWidth={2}
                  dot={false}
                  connectNulls={false}
                  name={metricLabel(t, trendMetric)}
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </section>
      ) : null}

      <footer className="mt-auto flex flex-wrap items-center justify-between gap-3 text-xs text-muted">
        <span className="inline-flex items-center gap-2">
          <MonitorPlay className="h-4 w-4" aria-hidden="true" />
          {t('pollsEvery', { seconds: POLL_MS / 1000 })}
        </span>
        <Link to="/dashboard" className="text-accent hover:underline">
          {t('navDashboard')}
        </Link>
      </footer>
    </div>
  );
};
