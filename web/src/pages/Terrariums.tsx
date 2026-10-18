import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { Box, ExternalLink, History as HistoryIcon, Loader2, Plus } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import { PERMISSION_CODES } from '../api/types';
import type { DeviceList, SpeciesProfileList, TerrariumList, TimeZoneList } from '../api/types';
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
  const { hasPermission } = useSession();
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];
  const [createOpen, setCreateOpen] = useState(false);
  // The create route names `terrarium.write`, not the Owner role: a deployment may grant the write to another
  // role, and a screen that tested the name would keep hiding the form from somebody who holds it (FR-18).
  const canCreate = hasPermission(PERMISSION_CODES.terrariumWrite);

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

  // The catalogue and the fleet are loaded once each rather than polled, for the reason the profile list is: they
  // change when someone else edits them, and a picker that reshuffles under a half-filled form is worse than one a
  // minute old (TASKS 2026-10-14, items 2 and 11).
  const zones = usePolled<TimeZoneList>(() => api.listTimeZones(), null);
  const boards = usePolled<DeviceList>(() => api.listDevices(), null);

  const [name, setName] = useState('');
  const [speciesProfileId, setSpeciesProfileId] = useState('');
  const [location, setLocation] = useState('');
  const [timeZoneId, setTimeZoneId] = useState('');
  const [deviceId, setDeviceId] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [created, setCreated] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setCreated(false);
    setSecret(null);

    try {
      const item = await api.createTerrarium({
        name,
        speciesProfileId,
        // An empty box is "not given", not an empty location: the contract stores null for the latter and the
        // card renders the difference. The same applies to the zone and the board.
        location: location.trim() === '' ? null : location,
        timeZoneId: timeZoneId === '' ? null : timeZoneId,
        deviceId: deviceId === '' ? null : deviceId,
      });

      setName('');
      setSpeciesProfileId('');
      setLocation('');
      setTimeZoneId('');
      setDeviceId('');
      setCreated(true);

      // Shown once, by the server's own rule: naming a board is a claim, so this is the only moment the secret
      // exists outside the board (item 2). It is deliberately kept out of the list below.
      setSecret(item.deviceSecret ?? null);
      onCreated();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const nameError = fieldMessage(t, error, 'name');
  const profileError = fieldMessage(t, error, 'speciesProfileId');
  const zoneError = fieldMessage(t, error, 'timeZoneId');
  const deviceError = fieldMessage(t, error, 'deviceId') ?? fieldMessage(t, error, 'macAddress');
  const inputClass = (field: string) =>
    `mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline'
    }`;

  // Only boards that are the caller's and not already bound can be chosen: the server refuses the rest with
  // `device_already_bound`, and offering a row whose only outcome is a refusal is a worse form.
  const claimable = (boards.data?.items ?? []).filter(
    (board) => board.terrariumId === null && board.status !== 'revoked'
  );

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

      {/*
        The zone comes from the catalogue rather than a free-text box (item 11): the server validates against the
        same list, so a typed id could only be refused, and the offset is what a keeper actually reads off it.
      */}
      <div>
        <label htmlFor="terrarium-zone" className="text-[11px] text-muted">
          {t('terrariumTimeZone')}
        </label>
        <select
          id="terrarium-zone"
          value={timeZoneId}
          onChange={(event) => setTimeZoneId(event.target.value)}
          className={inputClass('timeZoneId')}
        >
          <option value="">{t('terrariumTimeZoneDefault')}</option>
          {(zones.data?.items ?? []).map((zone) => (
            <option key={zone.ianaId} value={zone.ianaId}>
              {`${zone.utcOffset} — ${zone.displayName}`}
            </option>
          ))}
        </select>
        {zoneError ? <p className="mt-1 text-[11px] text-critical">{zoneError}</p> : null}
        {zones.error ? <p className="mt-1 text-[11px] text-critical">{describeError(t, zones.error)}</p> : null}
      </div>

      {/*
        Naming a board here is a claim: the enclosure and the binding commit together, and the answer carries the
        secret once (item 2). Only a board with no enclosure is offered — the rest would be refused by the server.
      */}
      <div>
        <label htmlFor="terrarium-device" className="text-[11px] text-muted">
          {t('terrariumBindDevice')}
        </label>
        <select
          id="terrarium-device"
          value={deviceId}
          onChange={(event) => setDeviceId(event.target.value)}
          className={inputClass('deviceId')}
        >
          <option value="">{t('terrariumBindDeviceNone')}</option>
          {claimable.map((board) => (
            <option key={board.deviceId} value={board.deviceId}>
              {board.deviceName === '' ? board.deviceId : `${board.deviceName} (${board.deviceId})`}
            </option>
          ))}
        </select>
        {deviceError ? <p className="mt-1 text-[11px] text-critical">{deviceError}</p> : null}
        {boards.error ? <p className="mt-1 text-[11px] text-critical">{describeError(t, boards.error)}</p> : null}
        {boards.data && claimable.length === 0 && !boards.loading ? (
          <p className="mt-1 text-[11px] text-muted">{t('terrariumBindDeviceEmpty')}</p>
        ) : null}
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

      {secret ? (
        <div
          role="status"
          className="space-y-1 rounded-lg border border-warn/40 bg-warn/10 px-2.5 py-2 text-[11px] text-ink"
        >
          <p>{t('terrariumDeviceSecretOnce')}</p>
          <code className="block select-all break-all font-mono text-[11px] text-ink">{secret}</code>
        </div>
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
