import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { Box, ExternalLink, History as HistoryIcon, Plus, X, Loader2 } from 'lucide-react';
import * as api from '../api/endpoints';
import type { SpeciesProfileItem, TerrariumList } from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, formatRelative } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

const POLL_MS = 30_000;

export const Terrariums: React.FC = () => {
  const { t, lang } = useI18n();
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [profiles, setProfiles] = useState<SpeciesProfileItem[]>([]);
  const [loadingProfiles, setLoadingProfiles] = useState(false);

  const [name, setName] = useState('');
  const [speciesProfileId, setSpeciesProfileId] = useState('');
  const [location, setLocation] = useState('');
  const [description, setDescription] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('Asia/Ho_Chi_Minh');

  const [isSubmitting, setIsSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const handleOpenModal = async () => {
    setIsModalOpen(true);
    setSubmitError(null);
    if (profiles.length === 0) {
      setLoadingProfiles(true);
      try {
        const res = await api.listSpeciesProfiles();
        setProfiles(res.items);
        if (res.items.length > 0) {
          setSpeciesProfileId(res.items[0].id);
        }
      } catch (err) {
        setSubmitError((err as Error).message);
      } finally {
        setLoadingProfiles(false);
      }
    }
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setName('');
    setLocation('');
    setDescription('');
    setSubmitError(null);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !speciesProfileId) return;

    setIsSubmitting(true);
    setSubmitError(null);

    try {
      await api.createTerrarium({
        name: name.trim(),
        speciesProfileId,
        location: location.trim() || null,
        description: description.trim() || null,
        timeZoneId,
      });

      handleCloseModal();
      void terrariums.reload();
    } catch (err) {
      setSubmitError((err as Error).message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('terrariumsTitle')}</h1>
        </div>
        <button
          type="button"
          onClick={() => void handleOpenModal()}
          className="inline-flex items-center gap-2 rounded-xl bg-brand px-4 py-2.5 text-xs font-semibold text-white shadow-sm transition hover:bg-brand-dark"
        >
          <Plus className="h-4 w-4" />
          <span>{t('terrariumNew')}</span>
        </button>
      </header>

      {terrariums.error?.isNetworkFailure ? (
        <UnreachableBanner onRetry={() => terrariums.reload()} />
      ) : null}
      {!terrariums.error?.isNetworkFailure && terrariums.error ? (
        <ErrorPanel error={terrariums.error} onRetry={() => terrariums.reload()} />
      ) : null}

      {terrariums.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {!terrariums.loading && items.length === 0 && !terrariums.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('emptyStateBody')} />
      ) : null}

      {items.length > 0 ? (
        <ul className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {items.map((item) => (
            <li
              key={item.id}
              className="flex flex-col justify-between rounded-2xl border border-hairline bg-surface p-5 shadow-sm transition-all hover:border-brand/40"
            >
              <div>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="font-heading text-lg font-semibold text-ink">{item.name}</h2>
                    <p className="mt-0.5 text-xs text-muted">
                      {item.speciesName}
                      {item.location ? ` · ${item.location}` : ''}
                    </p>
                  </div>
                  <Box className="h-5 w-5 shrink-0 text-brand" aria-hidden="true" />
                </div>

                <dl className="mt-4 space-y-1.5 text-xs">
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">{t('device')}</dt>
                    <dd className="truncate text-ink">
                      {item.device ? item.device.deviceName : t('deviceUnbound')}
                    </dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">{t('navLive')}</dt>
                    <dd className="text-ink">{deviceStatusLabel(t, item.device?.status ?? null)}</dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">{t('lastSampleAt')}</dt>
                    <dd className="text-right text-ink">
                      {item.latestSampleAt ? (
                        <>
                          <span>{formatDateTime(item.latestSampleAt, lang)}</span>
                          <span className="block text-[11px] text-muted">
                            {formatRelative(t, item.latestSampleAt)}
                          </span>
                        </>
                      ) : (
                        t('never')
                      )}
                    </dd>
                  </div>
                  <div className="flex justify-between gap-3">
                    <dt className="text-muted">{t('openAlerts')}</dt>
                    <dd className="text-ink">{item.openAlertCount}</dd>
                  </div>
                </dl>
              </div>

              <div className="mt-5 flex items-center gap-4 text-xs">
                <Link
                  to={`/terrariums/${item.id}`}
                  className="inline-flex items-center gap-1 text-accent hover:underline"
                >
                  {t('terrariumDetail')}
                  <ExternalLink className="h-3 w-3" aria-hidden="true" />
                </Link>
                <Link
                  to={`/history?terrarium=${item.id}`}
                  className="inline-flex items-center gap-1 text-accent hover:underline"
                >
                  <HistoryIcon className="h-3 w-3" aria-hidden="true" />
                  {t('navHistory')}
                </Link>
              </div>
            </li>
          ))}
        </ul>
      ) : null}

      {/* Modal tạo bể nuôi mới */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
          <div className="w-full max-w-lg rounded-3xl border border-hairline bg-surface p-6 shadow-2xl">
            <div className="flex items-center justify-between pb-4 border-b border-hairline">
              <h3 className="font-heading text-lg font-bold text-ink">{t('terrariumNew')}</h3>
              <button
                type="button"
                onClick={handleCloseModal}
                className="rounded-lg p-1 text-muted hover:bg-hairline hover:text-ink"
              >
                <X className="h-5 w-5" />
              </button>
            </div>

            <form onSubmit={handleCreate} className="mt-5 space-y-4">
              {submitError && (
                <div className="rounded-xl border border-critical/40 bg-critical/10 p-3 text-xs text-critical">
                  {submitError}
                </div>
              )}

              <div>
                <label className="block text-xs font-semibold text-muted mb-1.5">
                  {t('terrariumName')} *
                </label>
                <input
                  type="text"
                  required
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="Ví dụ: Bể Rồng Úc phòng khách"
                  className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-muted mb-1.5">
                  {t('terrariumSpecies')} *
                </label>
                {loadingProfiles ? (
                  <p className="text-xs text-muted flex items-center gap-2">
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    {t('loadingSpecies')}
                  </p>
                ) : (
                  <select
                    value={speciesProfileId}
                    onChange={(e) => setSpeciesProfileId(e.target.value)}
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
                    value={location}
                    onChange={(e) => setLocation(e.target.value)}
                    placeholder="Tầng 1, Kệ số 3"
                    className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold text-muted mb-1.5">
                    {t('terrariumTimeZone')}
                  </label>
                  <input
                    type="text"
                    value={timeZoneId}
                    onChange={(e) => setTimeZoneId(e.target.value)}
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
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Ghi chú thêm về điều kiện bể..."
                  className="w-full rounded-xl border border-hairline bg-field px-3.5 py-2.5 text-xs text-ink outline-none focus:border-brand"
                />
              </div>

              <div className="flex justify-end gap-3 pt-4 border-t border-hairline">
                <button
                  type="button"
                  onClick={handleCloseModal}
                  className="rounded-xl border border-hairline px-4 py-2 text-xs font-medium text-ink hover:bg-hairline/20"
                >
                  {t('cancel')}
                </button>
                <button
                  type="submit"
                  disabled={isSubmitting || !name.trim()}
                  className="inline-flex items-center gap-2 rounded-xl bg-brand px-5 py-2 text-xs font-semibold text-white transition hover:bg-brand-dark disabled:opacity-50"
                >
                  {isSubmitting ? (
                    <>
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      <span>{t('creating')}</span>
                    </>
                  ) : (
                    <span>{t('create')}</span>
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
