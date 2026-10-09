import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Activity, BatteryMedium, Box, Cpu, ExternalLink, Radio, Wifi } from 'lucide-react';
import * as api from '../api/endpoints';
import type { Coverage, LatestReadings, TerrariumList } from '../api/types';
import { MetricCard } from '../components/MetricCard';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, formatRelative } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

/**
 * W2 — the live dashboard (roadmap 4.16, W1's wallboard is `Wallboard.tsx`).
 *
 * Every number on this page is the API's: the terrarium list, the device summary, the metric cards and the
 * coverage figure. There is no arithmetic on a reading here at all — the cards render `status`, `target` and
 * `capturedAt` as sent (task 4.17), which is why the riskiest thing this file does is decide *which* terrarium to
 * show.
 *
 * The four states §6 insists on are all reachable and all visible:
 *   - nothing to show (an account with no terrarium) — an empty state that names the next action;
 *   - a terrarium with no device — `deviceUnbound`, and no coverage request at all, because the server answers
 *     `409 device_not_bound` and asking would turn a fact into an error;
 *   - a refused call — rendered from its code;
 *   - an unreachable server — the last good values stay on screen, dimmed by their own timestamps, under a banner
 *     that says the server cannot be reached. Freezing silently is the failure mode §6 names first.
 *
 * Polling is 30 s, the interval the retired dashboard used, and the honest stopgap until the SignalR push of 2.9
 * is wired (the roadmap's own instruction: "polling until 2.9").
 */

const POLL_MS = 30_000;
const DAY_MS = 24 * 60 * 60 * 1000;

export const Dashboard: React.FC = () => {
  const { t, lang } = useI18n();
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];

  // Select the first terrarium once the list arrives, and re-select if the current one disappears.
  useEffect(() => {
    if (items.length === 0) {
      setSelectedId(null);
      return;
    }
    if (!selectedId || !items.some((item) => item.id === selectedId)) {
      setSelectedId(items[0].id);
    }
  }, [items, selectedId]);

  const selected = items.find((item) => item.id === selectedId) ?? null;
  const hasDevice = Boolean(selected?.device);

  const latest = usePolled<LatestReadings | null>(
    () => (selectedId ? api.latestReadings(selectedId) : Promise.resolve(null)),
    selectedId ? POLL_MS : null,
    [selectedId]
  );

  // Coverage is a statement about a device, so it is only asked for when one is bound — and `409` is therefore
  // never an expected answer here.
  const coverage = usePolled<Coverage | null>(
    () => {
      if (!selectedId || !hasDevice) {
        return Promise.resolve(null);
      }
      const to = new Date();
      const from = new Date(to.getTime() - DAY_MS);
      return api.coverage(selectedId, from.toISOString(), to.toISOString());
    },
    selectedId && hasDevice ? 60_000 : null,
    [selectedId, hasDevice]
  );

  const readings = latest.data;
  const device = readings?.device ?? selected?.device ?? null;
  const networkProblem = terrariums.error?.isNetworkFailure || latest.error?.isNetworkFailure;

  if (terrariums.loading) {
    return <LoadingPanel label={t('signingIn')} />;
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('navDashboard')}</h1>
          <p className="mt-1 text-sm text-muted">{t('signInPrompt')}</p>
        </div>

        {items.length > 1 ? (
          <label className="text-xs uppercase tracking-wider text-muted">
            <span className="mb-1 block">{t('terrariumsTitle')}</span>
            <select
              value={selectedId ?? ''}
              onChange={(event) => setSelectedId(event.target.value)}
              className="rounded-xl border border-hairline bg-field px-3 py-2 text-sm normal-case tracking-normal text-ink outline-none focus:border-brand"
            >
              {items.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
        ) : null}
      </header>

      {networkProblem ? <UnreachableBanner onRetry={() => terrariums.reload()} /> : null}
      {!networkProblem && terrariums.error ? (
        <ErrorPanel error={terrariums.error} onRetry={() => terrariums.reload()} />
      ) : null}

      {items.length === 0 && !terrariums.error ? (
        <EmptyPanel
          title={t('emptyStateTitle')}
          body={t('emptyStateBody')}
          action={
            <Link
              to="/terrariums"
              className="rounded-xl bg-brand px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-brand-dark"
            >
              {t('terrariumsTitle')}
            </Link>
          }
        />
      ) : null}

      {selected ? (
        <>
          {/* Device header: the server's device summary, or the honest absence of one. */}
          <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div>
                <h2 className="font-heading text-lg font-semibold text-ink">{selected.name}</h2>
                <p className="mt-0.5 text-xs text-muted">
                  {selected.speciesName}
                  {selected.location ? ` · ${selected.location}` : ''}
                </p>
              </div>

              <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-xs text-muted">
                <span className="inline-flex items-center gap-1.5">
                  <Cpu className="h-3.5 w-3.5" aria-hidden="true" />
                  {device?.deviceName ?? t('deviceUnbound')}
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <Radio className="h-3.5 w-3.5" aria-hidden="true" />
                  {deviceStatusLabel(t, device?.status ?? null)}
                </span>
                <span className="inline-flex items-center gap-1.5">
                  <Activity className="h-3.5 w-3.5" aria-hidden="true" />
                  {t('lastSampleAt')}: {formatDateTime(readings?.lastSampleAt, lang)}
                </span>
                {device?.batteryPct !== null && device?.batteryPct !== undefined ? (
                  <span className="inline-flex items-center gap-1.5">
                    <BatteryMedium className="h-3.5 w-3.5" aria-hidden="true" />
                    {Math.round(device.batteryPct)}%
                  </span>
                ) : null}
                {device?.signalStrengthDbm !== null && device?.signalStrengthDbm !== undefined ? (
                  <span className="inline-flex items-center gap-1.5">
                    <Wifi className="h-3.5 w-3.5" aria-hidden="true" />
                    {device.signalStrengthDbm} dBm
                  </span>
                ) : null}
                <Link
                  to={`/terrariums/${selected.id}`}
                  className="inline-flex items-center gap-1 text-accent hover:underline"
                >
                  {t('terrariumDetail')}
                  <ExternalLink className="h-3 w-3" aria-hidden="true" />
                </Link>
              </div>
            </div>
          </section>

          {latest.error ? (
            <ErrorPanel error={latest.error} onRetry={latest.reload} />
          ) : latest.loading && !readings ? (
            <LoadingPanel label={t('signingIn')} />
          ) : readings && readings.metrics.length > 0 ? (
            <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
              {readings.metrics.map((metric) => (
                <MetricCard
                  key={metric.code}
                  metric={metric}
                  samplingIntervalSec={device?.samplingIntervalSec ?? null}
                />
              ))}
            </section>
          ) : readings ? (
            // The route answers: it just has no reading to carry, which is not an error and not a zero.
            <EmptyPanel
              title={t('statusNoData')}
              body={t('emptyStateBody')}
              action={
                <Link to="/history" className="text-sm text-accent hover:underline">
                  {t('navHistory')}
                </Link>
              }
            />
          ) : null}

          {/* §6: coverage travels with any summary, because a compliant day with 40% data is not compliance. */}
          <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
            <h3 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
              {t('coverage')}
            </h3>
            {coverage.data ? (
              <>
                <p className="mt-2 font-mono text-2xl text-ink">{coverage.data.coveragePct}%</p>
                <p className="mt-1 text-xs text-muted">
                  {t('coverageOfExpected', { pct: String(coverage.data.coveragePct) })} ·{' '}
                  {t('receivedSamples', { count: coverage.data.receivedSamples })} ·{' '}
                  {t('expectedSamples', { count: coverage.data.expectedSamples })} ·{' '}
                  {t('samplingInterval')}: {t('secondsShort', { count: coverage.data.samplingIntervalSec })}
                </p>
              </>
            ) : coverage.error ? (
              <p className="mt-2 text-xs text-muted">{coverage.error.code}</p>
            ) : hasDevice ? (
              <p className="mt-2 text-xs text-muted">{t('signingIn')}</p>
            ) : (
              <p className="mt-2 text-xs text-muted">{t('errorDeviceNotBound')}</p>
            )}
            {coverage.updatedAt ? (
              <p className="mt-2 text-[11px] text-muted">
                {t('lastUpdatedMinutes', {
                  count: Math.max(0, Math.round((Date.now() - coverage.updatedAt.getTime()) / 60000)),
                })}
              </p>
            ) : null}
          </section>

          <p className="flex items-center gap-2 text-[11px] text-muted">
            <Box className="h-3.5 w-3.5" aria-hidden="true" />
            {t('pollsEvery', { seconds: POLL_MS / 1000 })}
            {readings?.lastSampleAt
              ? ` · ${t('lastSampleAt')} ${formatRelative(t, readings.lastSampleAt)}`
              : ''}
          </p>
        </>
      ) : null}
    </div>
  );
};
