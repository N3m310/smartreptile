import React from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, Activity, BatteryMedium, Cpu, Radio, Wifi } from 'lucide-react';
import * as api from '../api/endpoints';
import type { Coverage, LatestReadings, TerrariumItem } from '../api/types';
import { MetricCard } from '../components/MetricCard';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDate, formatDateTime, formatRelative } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

/**
 * W2 — one terrarium (roadmap 4.16). Read-only on purpose: `PATCH`/`DELETE` and the threshold routes are
 * specified and unbuilt, so there is nothing here to submit. An edit form whose save button always fails would be
 * worse than no form.
 */

const POLL_MS = 30_000;
const DAY_MS = 24 * 60 * 60 * 1000;

export const TerrariumDetail: React.FC = () => {
  const { id = '' } = useParams();
  const { t, lang } = useI18n();

  const terrarium = usePolled<TerrariumItem>(() => api.getTerrarium(id), POLL_MS, [id]);
  const latest = usePolled<LatestReadings>(() => api.latestReadings(id), POLL_MS, [id]);
  const hasDevice = Boolean(terrarium.data?.device);

  const coverage = usePolled<Coverage | null>(
    () => {
      if (!hasDevice) {
        return Promise.resolve(null);
      }
      const to = new Date();
      const from = new Date(to.getTime() - DAY_MS);
      return api.coverage(id, from.toISOString(), to.toISOString());
    },
    hasDevice ? 60_000 : null,
    [id, hasDevice]
  );

  if (terrarium.loading) {
    return <LoadingPanel label={t('signingIn')} />;
  }

  if (terrarium.error) {
    return (
      <div className="space-y-4">
        {terrarium.error.isNetworkFailure ? (
          <UnreachableBanner onRetry={() => terrarium.reload()} />
        ) : (
          // A 404 here is not a bug: the route hides a foreign or unknown id identically (BR-02.2).
          <ErrorPanel error={terrarium.error} onRetry={() => terrarium.reload()} />
        )}
        <Link to="/terrariums" className="inline-flex items-center gap-1 text-sm text-accent hover:underline">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          {t('backToTerrariums')}
        </Link>
      </div>
    );
  }

  const item = terrarium.data;
  if (!item) {
    return null;
  }

  const device = latest.data?.device ?? item.device;

  return (
    <div className="space-y-6">
      <Link to="/terrariums" className="inline-flex items-center gap-1 text-sm text-accent hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        {t('backToTerrariums')}
      </Link>

      <header className="rounded-2xl border border-hairline bg-surface p-5">
        <h1 className="font-heading text-2xl font-bold text-ink">{item.name}</h1>
        <p className="mt-1 text-sm text-muted">
          {item.speciesName}
          {item.location ? ` · ${item.location}` : ''}
        </p>
        {item.description ? <p className="mt-2 text-sm text-muted">{item.description}</p> : null}

        <dl className="mt-4 grid grid-cols-1 gap-x-8 gap-y-2 text-xs sm:grid-cols-2">
          <div className="flex justify-between gap-3">
            <dt className="text-muted">{t('terrariumTimeZone')}</dt>
            <dd className="text-ink">{item.timeZoneId}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted">{t('createdAt')}</dt>
            <dd className="text-ink">{formatDate(item.createdAt, lang)}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted">{t('openAlerts')}</dt>
            <dd className="text-ink">{item.openAlertCount}</dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-muted">{t('lastSampleAt')}</dt>
            <dd className="text-ink">
              {item.latestSampleAt
                ? `${formatDateTime(item.latestSampleAt, lang)} · ${formatRelative(t, item.latestSampleAt)}`
                : t('never')}
            </dd>
          </div>
        </dl>
      </header>

      {/* Device block. `unbound` is a fact, not an error: nothing is claimed, so there is nothing to report. */}
      <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
        <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
          {t('device')}
        </h2>
        {device ? (
          <div className="mt-3 flex flex-wrap items-center gap-x-6 gap-y-2 text-xs text-muted">
            <span className="inline-flex items-center gap-1.5 text-ink">
              <Cpu className="h-3.5 w-3.5" aria-hidden="true" />
              {device.deviceName}
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Radio className="h-3.5 w-3.5" aria-hidden="true" />
              {deviceStatusLabel(t, device.status)}
            </span>
            {device.firmwareVersion ? (
              <span>
                {t('firmwareVersion')}: {device.firmwareVersion}
              </span>
            ) : null}
            {device.samplingIntervalSec ? (
              <span>
                {t('samplingInterval')}: {t('secondsShort', { count: device.samplingIntervalSec })}
              </span>
            ) : null}
            {device.batteryPct !== null && device.batteryPct !== undefined ? (
              <span className="inline-flex items-center gap-1.5">
                <BatteryMedium className="h-3.5 w-3.5" aria-hidden="true" />
                {Math.round(device.batteryPct)}%
              </span>
            ) : null}
            {device.signalStrengthDbm !== null && device.signalStrengthDbm !== undefined ? (
              <span className="inline-flex items-center gap-1.5">
                <Wifi className="h-3.5 w-3.5" aria-hidden="true" />
                {device.signalStrengthDbm} dBm
              </span>
            ) : null}
            {device.lastSeenAt ? (
              <span className="inline-flex items-center gap-1.5">
                <Activity className="h-3.5 w-3.5" aria-hidden="true" />
                {formatRelative(t, device.lastSeenAt)}
              </span>
            ) : null}
          </div>
        ) : (
          <p className="mt-2 text-xs text-muted">{t('deviceUnbound')}</p>
        )}
      </section>

      {latest.error ? (
        <ErrorPanel error={latest.error} onRetry={latest.reload} />
      ) : latest.data && latest.data.metrics.length > 0 ? (
        <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {latest.data.metrics.map((metric) => (
            <MetricCard
              key={metric.code}
              metric={metric}
              samplingIntervalSec={device?.samplingIntervalSec ?? null}
            />
          ))}
        </section>
      ) : latest.data ? (
        <EmptyPanel title={t('statusNoData')} body={t('emptyStateBody')} />
      ) : (
        <LoadingPanel label={t('signingIn')} />
      )}

      <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
        <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
          {t('coverage')}
        </h2>
        {coverage.data ? (
          <>
            <p className="mt-2 font-mono text-2xl text-ink">{coverage.data.coveragePct}%</p>
            <p className="mt-1 text-xs text-muted">
              {t('coverageOfExpected', { pct: String(coverage.data.coveragePct) })} ·{' '}
              {t('receivedSamples', { count: coverage.data.receivedSamples })} ·{' '}
              {t('expectedSamples', { count: coverage.data.expectedSamples })}
            </p>
          </>
        ) : (
          <p className="mt-2 text-xs text-muted">
            {hasDevice ? t('signingIn') : t('errorDeviceNotBound')}
          </p>
        )}
      </section>
    </div>
  );
};
