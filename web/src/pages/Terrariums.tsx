import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { Box, ExternalLink, History as HistoryIcon, Loader2, Plus } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import type { SpeciesProfileList, TerrariumList } from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, formatRelative } from '../lib/format';
import { describeError, fieldHasError, fieldMessage, fieldMessages } from '../lib/messages';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

/**
 * W2's terrarium list and create (roadmap 4.16).
 *
 * Create is wired now that the route it needed exists: `POST /terrariums` takes a `speciesProfileId`, and
 * `GET /api/v1/species-profiles` (roadmap 3.9) supplies one, so the picker is filled from the API rather than
 * from a hard-coded id. The panel is Owner-only, because the create route names the Owner policy — a form that
 * always refuses is worse than no form.
 */

const POLL_MS = 30_000;

export const Terrariums: React.FC = () => {
  const { t, lang } = useI18n();
  const { user } = useSession();
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];
  const [createOpen, setCreateOpen] = useState(false);
  const canCreate = user?.role === 'Owner';

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <h1 className="font-heading text-2xl font-bold text-ink">{t('terrariumsTitle')}</h1>
        {canCreate ? (
          <button
            type="button"
            onClick={() => setCreateOpen((open) => !open)}
            aria-expanded={createOpen}
            className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span>{t('terrariumNew')}</span>
          </button>
        ) : null}
      </header>

      {createOpen && canCreate ? <CreatePanel onCreated={() => terrariums.reload()} /> : null}

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
              className="flex flex-col justify-between rounded-2xl border border-hairline bg-surface p-5"
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

    </div>
  );
};

/**
 * The create panel (roadmap 4.16). Owner-only by the route's own policy, and shown as a panel rather than a page
 * because everything it needs is already on this one.
 *
 * The profile list is loaded once instead of polled: it changes when someone authors a profile on another screen,
 * and a picker that reshuffles under a half-filled form is worse than one a minute old. It is the reason this form
 * could not exist before roadmap 3.9 — `POST /terrariums` needs a `speciesProfileId` and nothing could list one.
 */
const CreatePanel: React.FC<{ onCreated: () => void }> = ({ onCreated }) => {
  const { t } = useI18n();
  const profiles = usePolled<SpeciesProfileList>(() => api.listSpeciesProfiles(), null);
  const [name, setName] = useState('');
  const [speciesProfileId, setSpeciesProfileId] = useState('');
  const [location, setLocation] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [created, setCreated] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setCreated(false);

    try {
      await api.createTerrarium({
        name,
        speciesProfileId,
        // An empty box is "not given", not an empty location: the contract stores null for the latter and the
        // card renders the difference.
        location: location.trim() === '' ? null : location,
      });

      setName('');
      setSpeciesProfileId('');
      setLocation('');
      setCreated(true);
      onCreated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const nameError = fieldMessage(t, error, 'name');
  const profileError = fieldMessage(t, error, 'speciesProfileId');
  const inputClass = (field: string) =>
    `mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline'
    }`;

  return (
    <form onSubmit={submit} className="space-y-3 rounded-2xl border border-hairline bg-surface p-5">
      <div>
        <label htmlFor="terrarium-name" className="text-[11px] text-muted">
          {t('terrariumName')}
        </label>
        <input
          id="terrarium-name"
          value={name}
          onChange={(event) => setName(event.target.value)}
          required
          maxLength={60}
          className={inputClass('name')}
        />
        {nameError ? <p className="mt-1 text-[11px] text-critical">{nameError}</p> : null}
      </div>

      <div>
        <label htmlFor="terrarium-species" className="text-[11px] text-muted">
          {t('terrariumSpecies')}
        </label>
        <select
          id="terrarium-species"
          value={speciesProfileId}
          onChange={(event) => setSpeciesProfileId(event.target.value)}
          required
          className={inputClass('speciesProfileId')}
        >
          <option value="" disabled>
            {t('speciesProfileChoose')}
          </option>
          {(profiles.data?.items ?? []).map((profile) => (
            <option key={profile.id} value={profile.id}>
              {profile.scientificName ? `${profile.name} — ${profile.scientificName}` : profile.name}
            </option>
          ))}
        </select>
        {profileError ? <p className="mt-1 text-[11px] text-critical">{profileError}</p> : null}
        {profiles.error ? (
          <p className="mt-1 text-[11px] text-critical">{describeError(t, profiles.error)}</p>
        ) : null}
      </div>

      <div>
        <label htmlFor="terrarium-location" className="text-[11px] text-muted">
          {t('terrariumLocation')}
        </label>
        <input
          id="terrarium-location"
          value={location}
          onChange={(event) => setLocation(event.target.value)}
          maxLength={120}
          className={inputClass('location')}
        />
      </div>

      {error ? (
        <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          <p>{describeError(t, error)}</p>
          {fieldMessages(t, error.errors).map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

      {created ? (
        <p
          role="status"
          className="rounded-lg border border-brand/40 bg-brand/10 px-2.5 py-2 text-[11px] text-ink"
        >
          {t('terrariumCreated')}
        </p>
      ) : null}

      <button
        type="submit"
        disabled={busy || profiles.loading}
        className="flex w-full items-center justify-center gap-2 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white transition-colors hover:bg-brand-dark disabled:opacity-70"
      >
        {busy ? (
          <>
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            <span>{t('creating')}</span>
          </>
        ) : (
          <span>{t('create')}</span>
        )}
      </button>
    </form>
  );
};
