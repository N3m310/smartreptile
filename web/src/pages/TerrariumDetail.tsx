import React, { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  ArrowLeft,
  Activity,
  BatteryMedium,
  Cpu,
  Radio,
  Wifi,
  Edit2,
  Trash2,
  X,
  Loader2,
  Sliders,
} from 'lucide-react';
import * as api from '../api/endpoints';
import type {
  Coverage,
  EffectiveThresholdsResponse,
  LatestReadings,
  SpeciesProfileItem,
  TerrariumItem,
} from '../api/types';
import { MetricCard } from '../components/MetricCard';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDate, formatDateTime, formatRelative } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

const POLL_MS = 30_000;
const DAY_MS = 24 * 60 * 60 * 1000;

export const TerrariumDetail: React.FC = () => {
  const { id = '' } = useParams();
  const navigate = useNavigate();
  const { t, lang } = useI18n();

  const terrarium = usePolled<TerrariumItem>(() => api.getTerrarium(id), POLL_MS, [id]);
  const latest = usePolled<LatestReadings>(() => api.latestReadings(id), POLL_MS, [id]);
  const thresholds = usePolled<EffectiveThresholdsResponse>(
    () => api.effectiveThresholds(id),
    POLL_MS,
    [id]
  );
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

  // Edit modal state
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [profiles, setProfiles] = useState<SpeciesProfileItem[]>([]);
  const [loadingProfiles, setLoadingProfiles] = useState(false);
  const [editName, setEditName] = useState('');
  const [editSpeciesProfileId, setEditSpeciesProfileId] = useState('');
  const [editLocation, setEditLocation] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editTimeZoneId, setEditTimeZoneId] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  // Delete state
  const [isDeleting, setIsDeleting] = useState(false);

  const handleOpenEdit = async () => {
    if (!terrarium.data) return;
    setEditName(terrarium.data.name);
    setEditSpeciesProfileId(terrarium.data.speciesProfileId);
    setEditLocation(terrarium.data.location ?? '');
    setEditDescription(terrarium.data.description ?? '');
    setEditTimeZoneId(terrarium.data.timeZoneId);
    setEditError(null);
    setIsEditOpen(true);

    if (profiles.length === 0) {
      setLoadingProfiles(true);
      try {
        const res = await api.listSpeciesProfiles();
        setProfiles(res.items);
      } catch (err) {
        setEditError((err as Error).message);
      } finally {
        setLoadingProfiles(false);
      }
    }
  };

  const handleSaveEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editName.trim()) return;

    setIsSubmitting(true);
    setEditError(null);

    try {
      await api.updateTerrarium(id, {
        name: editName.trim(),
        speciesProfileId: editSpeciesProfileId || undefined,
        location: editLocation.trim() || null,
        description: editDescription.trim() || null,
        timeZoneId: editTimeZoneId.trim() || undefined,
      });

      setIsEditOpen(false);
      void terrarium.reload();
      void thresholds.reload();
    } catch (err) {
      setEditError((err as Error).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!window.confirm(t('confirmDeleteTerrarium'))) return;

    setIsDeleting(true);
    try {
      await api.deleteTerrarium(id, true);
      navigate('/terrariums');
    } catch (err) {
      alert((err as Error).message);
      setIsDeleting(false);
    }
  };

  if (terrarium.loading) {
    return <LoadingPanel label={t('signingIn')} />;
  }

  if (terrarium.error) {
    return (
      <div className="space-y-4">
        {terrarium.error.isNetworkFailure ? (
          <UnreachableBanner onRetry={() => terrarium.reload()} />
        ) : (
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
      <div className="flex items-center justify-between">
        <Link to="/terrariums" className="inline-flex items-center gap-1 text-sm text-accent hover:underline">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          {t('backToTerrariums')}
        </Link>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => void handleOpenEdit()}
            className="inline-flex items-center gap-1.5 rounded-xl border border-hairline bg-surface px-3 py-1.5 text-xs font-medium text-ink transition hover:bg-hairline/20"
          >
            <Edit2 className="h-3.5 w-3.5" />
            <span>{t('editTerrarium')}</span>
          </button>
          <button
            type="button"
            disabled={isDeleting}
            onClick={() => void handleDelete()}
            className="inline-flex items-center gap-1.5 rounded-xl border border-critical/30 bg-critical/10 px-3 py-1.5 text-xs font-medium text-critical transition hover:bg-critical/20 disabled:opacity-50"
          >
            {isDeleting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
            <span>{t('deleteTerrarium')}</span>
          </button>
        </div>
      </div>

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

      {/* Device block */}
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

      {/* Sensor readings */}
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

      {/* Effective Thresholds */}
      <section className="rounded-2xl border border-hairline bg-surface p-5">
        <div className="flex items-center justify-between pb-3 border-b border-hairline">
          <div className="flex items-center gap-2">
            <Sliders className="h-4 w-4 text-brand" />
            <h2 className="font-heading text-sm font-semibold text-ink">
              {t('effectiveThresholds')}
            </h2>
          </div>
          {thresholds.data?.timeZoneId && (
            <span className="text-xs text-muted">
              {t('terrariumTimeZone')}: {thresholds.data.timeZoneId}
            </span>
          )}
        </div>

        {thresholds.loading ? (
          <div className="py-4 text-xs text-muted flex items-center gap-2">
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
            <span>{t('signingIn')}</span>
          </div>
        ) : thresholds.data && thresholds.data.effectiveThresholds.length > 0 ? (
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead>
                <tr className="border-b border-hairline text-muted">
                  <th className="pb-2.5 font-medium">{t('metricLabel')}</th>
                  <th className="pb-2.5 font-medium">{t('navLive')}</th>
                  <th className="pb-2.5 font-medium">{t('targetRange')}</th>
                  <th className="pb-2.5 font-medium">{t('criticalRange')}</th>
                  <th className="pb-2.5 font-medium">{t('terrariumSpecies')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hairline">
                {thresholds.data.effectiveThresholds.map((th) => (
                  <tr key={`${th.metric}-${th.phase}`}>
                    <td className="py-3 font-semibold text-ink">
                      {th.metric === 'temp_c'
                        ? t('metricTempC')
                        : th.metric === 'humidity_pct'
                        ? t('metricHumidityPct')
                        : th.metric === 'light_lux'
                        ? t('metricLightLux')
                        : th.metric === 'uv_index'
                        ? t('metricUvIndex')
                        : th.metric === 'surface_temp_c'
                        ? t('metricSurfaceTempC')
                        : th.metric}
                    </td>
                    <td className="py-3 text-muted">
                      <span className="rounded-md bg-field px-2 py-1 text-[11px] font-mono">
                        {th.phase}
                      </span>
                    </td>
                    <td className="py-3 font-mono text-ink">
                      {th.targetMin ?? '—'} – {th.targetMax ?? '—'}
                    </td>
                    <td className="py-3 font-mono text-muted">
                      {th.criticalMin !== null || th.criticalMax !== null
                        ? `${th.criticalMin ?? '—'} – ${th.criticalMax ?? '—'}`
                        : '—'}
                    </td>
                    <td className="py-3 text-muted">
                      <span className="rounded-md bg-brand/10 text-brand px-2 py-0.5 text-[11px]">
                        {th.layer}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="mt-3 text-xs text-muted">{t('statusNoData')}</p>
        )}
      </section>

      {/* Coverage */}
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

      {/* Edit Modal */}
      {isEditOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
          <div className="w-full max-w-lg rounded-3xl border border-hairline bg-surface p-6 shadow-2xl">
            <div className="flex items-center justify-between pb-4 border-b border-hairline">
              <h3 className="font-heading text-lg font-bold text-ink">{t('editTerrarium')}</h3>
              <button
                type="button"
                onClick={() => setIsEditOpen(false)}
                className="rounded-lg p-1 text-muted hover:bg-hairline hover:text-ink"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <form onSubmit={handleSaveEdit} className="mt-5 space-y-4">
              {editError && (
                <div className="rounded-xl border border-critical/40 bg-critical/10 p-3 text-xs text-critical">
                  {editError}
                </div>
              )}

              <div>
                <label className="block text-xs font-semibold text-muted mb-1.5">
                  {t('terrariumName')} *
                </label>
                <input
                  type="text"
                  required
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-muted mb-1.5">
                  {t('terrariumSpecies')}
                </label>
                {loadingProfiles ? (
                  <p className="text-xs text-muted flex items-center gap-2">
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    {t('loadingSpecies')}
                  </p>
                ) : (
                  <select
                    value={editSpeciesProfileId}
                    onChange={(e) => setEditSpeciesProfileId(e.target.value)}
                    className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                  >
                    {profiles.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.name} ({p.scientificName})
                      </option>
                    ))}
                  </select>
                )}
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-semibold text-muted mb-1.5">
                    {t('terrariumLocation')}
                  </label>
                  <input
                    type="text"
                    value={editLocation}
                    onChange={(e) => setEditLocation(e.target.value)}
                    className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-muted mb-1.5">
                    {t('terrariumTimeZone')}
                  </label>
                  <input
                    type="text"
                    value={editTimeZoneId}
                    onChange={(e) => setEditTimeZoneId(e.target.value)}
                    className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-semibold text-muted mb-1.5">
                  {t('terrariumDescription')}
                </label>
                <textarea
                  rows={3}
                  value={editDescription}
                  onChange={(e) => setEditDescription(e.target.value)}
                  className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                />
              </div>

              <div className="flex justify-end gap-3 pt-4 border-t border-hairline">
                <button
                  type="button"
                  onClick={() => setIsEditOpen(false)}
                  className="rounded-xl border border-hairline px-4 py-2 text-xs font-medium text-ink hover:bg-hairline/20"
                >
                  {t('cancel')}
                </button>
                <button
                  type="submit"
                  disabled={isSubmitting || !editName.trim()}
                  className="inline-flex items-center gap-2 rounded-xl bg-brand px-5 py-2 text-xs font-semibold text-white transition hover:bg-brand-dark disabled:opacity-50"
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      <span>{t('saving')}</span>
                    </>
                  ) : (
                    <span>{t('save')}</span>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
