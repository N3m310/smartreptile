import React, { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, Activity, BatteryMedium, Cpu, History, Leaf, Loader2, Pencil, Plus, Radio, Save, Sliders, Undo2, Wifi } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import { PERMISSION_CODES } from '../api/types';
import type { Coverage, DeviceList, EffectiveThreshold, LatestReadings, SpeciesProfileList, TerrariumItem, TerrariumThresholds, ThresholdOverrideInput, UserPage } from '../api/types';
import { MetricCard } from '../components/MetricCard';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDate, formatDateTime, formatRelative, formatValue, metricLabel } from '../lib/format';
import { describeError, fieldHasError, fieldMessages } from '../lib/messages';
import { deviceStatusLabel, thresholdPhaseLabel, thresholdSourceClass, thresholdSourceLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

/**
 * W2 — one terrarium (roadmap 4.16) with its threshold editor (4.20, over 3.1's read and write halves).
 *
 * The read surface is the server's throughout: every value, the phase it was resolved in and the layer it came from
 * are rendered as sent (ADR-005), so the editor previews exactly what the evaluator will act on. The species can be
 * re-pointed here (2026-10-25); `DELETE` on the terrarium itself is still unwired, and belongs to a delete form this
 * page does not have yet.
 */

const POLL_MS = 30_000;
const DAY_MS = 24 * 60 * 60 * 1000;

/** The record's "empty species": a `PATCH` carrying this id detaches the profile (2026-10-25). */
const EMPTY_SPECIES_PROFILE_ID = '00000000-0000-0000-0000-000000000000';

export const TerrariumDetail: React.FC = () => {
  const { id = '' } = useParams();
  const { t, lang } = useI18n();
  const { hasPermission } = useSession();
  // The threshold write names `terrarium.write`: the override belongs to the enclosure, so whoever may edit the
  // enclosure may tune its bands (FR-18).
  const canEditThresholds = hasPermission(PERMISSION_CODES.terrariumWrite);
  // Binding a board is `device.provision`: the same permission that claims one, because a bind is a claim of an
  // enclosure slot (07-appendices/03 §4.4).
  const canBindDevice = hasPermission(PERMISSION_CODES.deviceProvision);
  // Handing an enclosure to another account is `data.write.any` and nothing else (2026-10-23): an ordinary keeper
  // cannot give their enclosure away, and the picker needs `user.read` to list candidates.
  const canAssignOwner =
    hasPermission(PERMISSION_CODES.dataWriteAny) && hasPermission(PERMISSION_CODES.userRead);
  // Re-pointing an enclosure at a different profile is `terrarium.write`: the bands it is judged against belong to
  // the enclosure, so whoever may edit the enclosure may also change which species it is (FR-18).
  const canEditSpecies = hasPermission(PERMISSION_CODES.terrariumWrite);

  const terrarium = usePolled<TerrariumItem>(() => api.getTerrarium(id), POLL_MS, [id]);
  const latest = usePolled<LatestReadings>(() => api.latestReadings(id), POLL_MS, [id]);
  const hasDevice = Boolean(terrarium.data?.device);

  // Fetched on mount only for an account that may provision: the picker is the account's own fleet, and an account
  // without `device.provision` never spends a request on a list it has no permission to see.
  const bindableDevices = usePolled<DeviceList>(
    () => (canBindDevice ? api.listDevices() : Promise.resolve({ items: [] })),
    null,
    [canBindDevice]
  );
  const [bindOpen, setBindOpen] = useState(false);
  const [bindDeviceId, setBindDeviceId] = useState('');
  const [bindBusy, setBindBusy] = useState(false);
  const [bindError, setBindError] = useState<ApiError | null>(null);

  const accounts = usePolled<UserPage>(
    () =>
      canAssignOwner
        ? api.listUsers({ take: 200 })
        : Promise.resolve({ items: [], total: 0, skip: 0, take: 0 }),
    null,
    [canAssignOwner]
  );
  const [ownerId, setOwnerId] = useState('');
  const [ownerBusy, setOwnerBusy] = useState(false);
  const [ownerError, setOwnerError] = useState<ApiError | null>(null);

  // The catalogue is the create panel's list, read the same way and for the same reason: it changes when someone
  // authors a profile on another screen, and a picker that reshuffles under a half-made choice is worse than one a
  // minute old. An account without `terrarium.write` never spends the request.
  const profiles = usePolled<SpeciesProfileList>(
    () => (canEditSpecies ? api.listSpeciesProfiles() : Promise.resolve({ items: [] })),
    null,
    [canEditSpecies]
  );
  // `null` means the keeper has not touched the picker, so the shown value follows the server's. That is what makes
  // a reload after a save — or the poll that follows — display the stored species instead of a stale choice.
  const [speciesChoice, setSpeciesChoice] = useState<string | null>(null);
  const [speciesBusy, setSpeciesBusy] = useState(false);
  const [speciesError, setSpeciesError] = useState<ApiError | null>(null);
  const [speciesSaved, setSpeciesSaved] = useState(false);
  // Bumped after a save so the thresholds below re-read: until an override exists, their bands are the profile's.
  const [bandRevision, setBandRevision] = useState(0);

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

  const storedSpecies = item.speciesProfileId ?? '';
  const selectedSpecies = speciesChoice ?? storedSpecies;
  const speciesDirty = selectedSpecies !== storedSpecies;

  const submitBind = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!bindDeviceId) {
      return;
    }

    setBindBusy(true);
    setBindError(null);
    try {
      await api.moveDevice(bindDeviceId, id);
      setBindDeviceId('');
      setBindOpen(false);
      // The enclosure's own row, its readings and its coverage all change with the board, so all three are re-read
      // rather than patched locally.
      terrarium.reload();
      latest.reload();
      coverage.reload();
      bindableDevices.reload();
    } catch (cause) {
      setBindError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBindBusy(false);
    }
  };

  const submitSpecies = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!speciesDirty) {
      return;
    }

    if (!item.rowVersion) {
      // The single-terrarium answer always carries the token; without it the server would refuse the patch anyway.
      setSpeciesError(new ApiError('precondition_required', 428, null));
      return;
    }

    setSpeciesBusy(true);
    setSpeciesError(null);
    setSpeciesSaved(false);
    try {
      await api.updateTerrarium(
        item.id,
        // "No species" is the all-zero GUID, never `null`: null omits the member and would leave the current profile.
        { speciesProfileId: selectedSpecies === '' ? EMPTY_SPECIES_PROFILE_ID : selectedSpecies },
        item.rowVersion
      );
      setSpeciesChoice(null);
      setSpeciesSaved(true);
      terrarium.reload();
      // The bands below come from the profile until an override exists, so the species change dates them.
      setBandRevision((revision) => revision + 1);
    } catch (cause) {
      setSpeciesError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setSpeciesBusy(false);
    }
  };

  const submitOwner = async (event: React.FormEvent) => {
    event.preventDefault();
    if (!ownerId) {
      return;
    }

    if (!item.rowVersion) {
      // The single-terrarium answer always carries the token; without it the server would refuse the patch anyway.
      setOwnerError(new ApiError('precondition_required', 428, null));
      return;
    }

    setOwnerBusy(true);
    setOwnerError(null);
    try {
      await api.assignTerrariumOwner(item.id, ownerId, item.rowVersion);
      setOwnerId('');
      terrarium.reload();
      accounts.reload();
    } catch (cause) {
      setOwnerError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setOwnerBusy(false);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-4">
        <Link to="/terrariums" className="inline-flex items-center gap-1 text-sm text-accent hover:underline">
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          {t('backToTerrariums')}
        </Link>
        <Link
          to={`/history?terrarium=${item.id}`}
          className="inline-flex items-center gap-1.5 rounded-xl border border-hairline bg-surface px-3 py-1.5 text-xs font-semibold text-ink shadow-sm transition hover:border-brand/60 hover:text-brand hover:bg-surface-raised"
        >
          <History className="h-3.5 w-3.5 text-brand" aria-hidden="true" />
          {t('navHistory')}
        </Link>
      </div>

      <header className="rounded-2xl border border-hairline bg-surface p-5">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <h1 className="font-heading text-2xl font-bold text-ink">{item.name}</h1>
            <p className="mt-1 text-sm text-muted">
              {item.speciesName ?? t('terrariumNoSpecies')}
              {item.location ? ` · ${item.location}` : ''}
            </p>
            {item.description ? <p className="mt-2 text-sm text-muted">{item.description}</p> : null}
          </div>
          <Link
            to={`/history?terrarium=${item.id}`}
            className="inline-flex shrink-0 items-center gap-2 rounded-xl border border-brand/30 bg-brand/10 px-3.5 py-2 text-xs font-semibold text-brand transition hover:bg-brand/20"
          >
            <History className="h-4 w-4" aria-hidden="true" />
            <span>{t('navHistory')}</span>
          </Link>
        </div>

        <dl className="mt-4 grid grid-cols-1 gap-x-8 gap-y-2 text-xs sm:grid-cols-2">
          {item.ownerEmail ? (
            <div className="flex justify-between gap-3">
              <dt className="text-muted">Chủ sở hữu</dt>
              <dd className="break-all text-ink">{item.ownerEmail}</dd>
            </div>
          ) : null}
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

      {/*
        Re-pointing the enclosure at another species (2026-10-25). The bands that judge it are the profile's, so this
        is an edit of the enclosure itself: `PATCH /terrariums/{id}` over `terrarium.write`, carrying the version read
        with the page. A save also dates the thresholds below, which re-read with it.
      */}
      {canEditSpecies ? (
        <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
          <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
            {t('terrariumSpecies')}
          </h2>
          <form onSubmit={(event) => void submitSpecies(event)} className="mt-3 flex flex-wrap items-end gap-2">
            <label htmlFor="terrarium-species-edit" className="min-w-[240px] flex-1 text-xs text-muted">
              {t('terrariumSpecies')}
              <select
                id="terrarium-species-edit"
                value={selectedSpecies}
                onChange={(event) => {
                  setSpeciesChoice(event.target.value);
                  setSpeciesSaved(false);
                }}
                className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"
              >
                <option value="">{t('terrariumNoSpeciesLong')}</option>
                {(profiles.data?.items ?? []).map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.scientificName ? `${profile.name} — ${profile.scientificName}` : profile.name}
                  </option>
                ))}
              </select>
            </label>
            <button
              type="submit"
              disabled={speciesBusy || !speciesDirty}
              className="inline-flex items-center gap-1.5 rounded-lg bg-brand px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
            >
              {speciesBusy ? (
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
              ) : (
                <Save className="h-4 w-4" aria-hidden="true" />
              )}
              {t('save')}
            </button>
          </form>
          <p className="mt-2 text-[11px] text-muted">
            {storedSpecies === '' ? t('terrariumNoSpeciesHint') : t('terrariumSpeciesOptional')}
          </p>
          {profiles.error ? (
            <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, profiles.error)}</p>
          ) : null}
          {speciesError ? (
            <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, speciesError)}</p>
          ) : null}
          {speciesSaved ? (
            <p role="status" className="mt-2 rounded-lg border border-brand/40 bg-brand/10 px-2.5 py-2 text-[11px] text-ink">
              {t('terrariumUpdated')}
            </p>
          ) : null}
        </section>
      ) : null}

      {canAssignOwner ? (
        <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
          <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
            Chủ sở hữu
          </h2>
          <p className="mt-2 text-xs text-muted">
            Hiện tại: <strong className="text-ink">{item.ownerEmail ?? 'Chưa gán — bể nuôi chưa thuộc tài khoản nào'}</strong>
          </p>
          <form onSubmit={(event) => void submitOwner(event)} className="mt-3 flex flex-wrap items-end gap-2">
            <label className="min-w-[240px] flex-1 text-xs text-muted">
              Gán cho tài khoản
              <select
                value={ownerId}
                onChange={(event) => setOwnerId(event.target.value)}
                className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"
              >
                <option value="">Chọn tài khoản</option>
                {(accounts.data?.items ?? [])
                  .filter((account) => account.email !== item.ownerEmail)
                  .map((account) => (
                    <option key={account.id} value={account.id}>
                      {account.username} · {account.email}
                    </option>
                  ))}
              </select>
            </label>
            <button
              disabled={ownerBusy || !ownerId}
              className="rounded-lg bg-brand px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
            >
              Gán chủ sở hữu
            </button>
          </form>
          <p className="mt-2 text-[11px] text-muted">
            Thiết bị đang gắn trong bể vẫn ở nguyên đó và chuyển theo chủ mới. Thao tác được ghi audit
            (<code>terrarium.owner_changed</code>).
          </p>
          {accounts.error ? (
            <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, accounts.error)}</p>
          ) : null}
          {ownerError ? (
            <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, ownerError)}</p>
          ) : null}
        </section>
      ) : null}

      {/* Device block. `unbound` is a fact, not an error: nothing is claimed, so there is nothing to report. */}
      <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
            {t('device')}
          </h2>
          {canBindDevice ? (
            <button
              type="button"
              onClick={() => {
                setBindOpen((open) => !open);
                setBindError(null);
                bindableDevices.reload();
              }}
              className="inline-flex items-center gap-1.5 rounded-lg border border-hairline bg-surface px-3 py-1.5 text-xs font-semibold text-accent hover:border-accent/40"
            >
              <Plus className="h-3.5 w-3.5" aria-hidden="true" />
              Gắn thiết bị vào bể
            </button>
          ) : null}
        </div>

        {bindOpen && canBindDevice ? (
          <form onSubmit={(event) => void submitBind(event)} className="mt-3 rounded-xl border border-hairline bg-raised/40 p-3">
            <div className="flex flex-wrap items-end gap-2">
              <label className="min-w-[220px] flex-1 text-xs text-muted">
                Thiết bị
                <select
                  value={bindDeviceId}
                  onChange={(event) => setBindDeviceId(event.target.value)}
                  className="mt-1 block w-full rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"
                >
                  <option value="">Chọn thiết bị</option>
                  {(bindableDevices.data?.items ?? [])
                    .filter((candidate) => candidate.status !== 'revoked')
                    .map((candidate) => (
                      <option key={candidate.deviceId} value={candidate.deviceId}>
                        {candidate.deviceName} ({candidate.deviceId})
                        {candidate.terrariumId && candidate.terrariumId !== id
                          ? ` · đang ở ${candidate.terrariumName ?? 'bể khác'}`
                          : ''}
                      </option>
                    ))}
                </select>
              </label>
              <button
                disabled={bindBusy || !bindDeviceId}
                className="rounded-lg bg-brand px-4 py-2 text-sm font-semibold text-white disabled:opacity-50"
              >
                Gắn vào bể
              </button>
            </div>
            <p className="mt-2 text-[11px] text-muted">
              Mỗi bể nuôi chỉ chứa một thiết bị: nếu bể đang có thiết bị khác, thiết bị đó sẽ được gỡ ra nhưng không bị thu hồi.
            </p>
            {bindableDevices.error ? (
              <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, bindableDevices.error)}</p>
            ) : null}
            {bindError ? (
              <p role="alert" className="mt-2 text-xs text-critical">{describeError(t, bindError)}</p>
            ) : null}
          </form>
        ) : null}

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

      {/* History section: provides quick access to historical charts and samples for this terrarium */}
      <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
        <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <div className="flex items-center gap-2">
              <History className="h-4 w-4 text-brand" aria-hidden="true" />
              <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
                {t('navHistory')}
              </h2>
            </div>
            <p className="mt-1 text-xs text-muted">
              {t('historyTitle')} — {item.name}
            </p>
          </div>
          <Link
            to={`/history?terrarium=${item.id}`}
            className="inline-flex shrink-0 items-center gap-2 rounded-xl border border-hairline bg-surface-raised px-4 py-2 text-xs font-semibold text-ink shadow-sm transition hover:border-brand hover:text-brand hover:bg-surface"
          >
            <History className="h-3.5 w-3.5 text-brand" aria-hidden="true" />
            <span>{t('navHistory')}</span>
          </Link>
        </div>
      </section>

      <ThresholdsSection terrariumId={id} canEdit={canEditThresholds} refreshToken={bandRevision} />
    </div>
  );
};

/** The editor's own state for one row: every field is a string, because that is what an input holds. */
interface BandDraft {
  targetMin: string;
  targetMax: string;
  criticalMin: string;
  criticalMax: string;
  dwellWarnMinutes: string;
  dwellCritMinutes: string;
  recoveryMargin: string;
  note: string;
}

/**
 * The effective bands and, for an Owner, the two writes over them (roadmap 4.20 using 3.1's halves).
 *
 * Three readings worth stating, because they are choices:
 *
 * 1. **Nothing is computed here.** The band, its `phase` and its `source` are the server's answer to
 *    `GET /terrariums/{id}/thresholds`, so the editor previews the set the evaluator acts on rather than a second
 *    opinion about it.
 * 2. **There is no phase picker.** A row is a band in force *now*, and an edit writes for the phase shown, so a
 *    keeper tunes what they can see. A metric the profile configures for the other phase alone is therefore absent
 *    — which the caption says, rather than showing a band that is not being judged against.
 * 3. **An empty critical box means "not configured"**, not zero: the contract derives the critical band from the
 *    target band and the margin when the field is omitted, so clearing it removes the explicit bounds instead of
 *    pinning them to the target's edges.
 */
const ThresholdsSection: React.FC<{ terrariumId: string; canEdit: boolean; refreshToken?: number }> = ({
  terrariumId,
  canEdit,
  refreshToken = 0,
}) => {
  const { t, lang } = useI18n();
  const thresholds = usePolled<TerrariumThresholds>(
    () => api.terrariumThresholds(terrariumId),
    POLL_MS,
    [terrariumId, refreshToken]
  );

  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState<BandDraft | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [clientError, setClientError] = useState<string | null>(null);
  const [retryAction, setRetryAction] = useState<(() => void) | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const rows = thresholds.data?.effectiveThresholds ?? [];
  const rowKey = (row: EffectiveThreshold) => `${row.metric}-${row.phase}`;

  const closeEditor = () => {
    setEditing(null);
    setDraft(null);
  };

  const startEdit = (row: EffectiveThreshold) => {
    setError(null);
    setClientError(null);
    setRetryAction(null);
    setNotice(null);
    setEditing(rowKey(row));
    setDraft({
      targetMin: String(row.targetMin),
      targetMax: String(row.targetMax),
      criticalMin: row.criticalMin === null ? '' : String(row.criticalMin),
      criticalMax: row.criticalMax === null ? '' : String(row.criticalMax),
      dwellWarnMinutes: String(row.dwellWarnMinutes),
      dwellCritMinutes: String(row.dwellCritMinutes),
      recoveryMargin: String(row.recoveryMargin),
      note: '',
    });
  };

  const submit = async (event: React.FormEvent, row: EffectiveThreshold) => {
    event.preventDefault();

    if (!draft) {
      return;
    }

    const values = [
      draft.targetMin,
      draft.targetMax,
      draft.criticalMin,
      draft.criticalMax,
      draft.dwellWarnMinutes,
      draft.dwellCritMinutes,
      draft.recoveryMargin,
    ];
    const parsed = values.map((value) => (value.trim() === '' ? null : Number(value.replace(',', '.'))));
    if (parsed.some((value) => value !== null && !Number.isFinite(value))) {
      setClientError(t('thresholdNumberInvalid'));
      return;
    }
    const [targetMin, targetMax] = parsed;
    if (targetMin === null || targetMax === null) {
      setClientError(t('thresholdNumberRequired'));
      return;
    }
    if (targetMin >= targetMax) {
      setClientError(t('thresholdOrderingInvalid'));
      return;
    }
    setClientError(null);

    await saveOverride(row, draft);
  };

  const saveOverride = async (row: EffectiveThreshold, currentDraft: BandDraft) => {
    setBusy(true);
    setError(null);
    setRetryAction(null);
    setNotice(null);
    try {
      await api.putTerrariumThresholds(terrariumId, [toInput(row, currentDraft)]);
      closeEditor();
      setNotice('thresholdSaved');
      thresholds.reload();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
      setRetryAction(() => () => { void saveOverride(row, currentDraft); });
    } finally {
      setBusy(false);
    }
  };

  const clearOverride = async (row: EffectiveThreshold) => {
    setBusy(true);
    setError(null);
    setRetryAction(null);
    setNotice(null);

    try {
      await api.deleteTerrariumThresholdOverride(terrariumId, row.metric);
      closeEditor();
      setNotice('thresholdCleared');
      thresholds.reload();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
      setRetryAction(() => () => { void clearOverride(row); });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="rounded-2xl border border-hairline bg-surface px-5 py-4">
      <h2 className="flex items-center gap-2 font-heading text-sm font-semibold text-ink">
        <Sliders className="h-4 w-4 text-brand" aria-hidden="true" />
        {t('thresholdsTitle')}
      </h2>
      <p className="mt-1 text-xs text-muted">{t('thresholdsCaption')}</p>
      <p className="mt-1 text-[11px] text-muted">
        {t('thresholdsPhaseNote')}
        {thresholds.data
          ? ` · ${formatDateTime(thresholds.data.capturedAtUtc, lang)} (${thresholds.data.timeZoneId})`
          : ''}
      </p>

      {thresholds.error ? (
        thresholds.error.isNetworkFailure ? (
          <UnreachableBanner onRetry={() => thresholds.reload()} />
        ) : (
          <ErrorPanel error={thresholds.error} onRetry={() => thresholds.reload()} />
        )
      ) : null}

      {thresholds.loading ? <LoadingPanel label={t('thresholdsLoading')} /> : null}

      {clientError ? (
        <p role="alert" className="mt-3 rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          {clientError}
        </p>
      ) : null}

      {error && editing === null ? (
        <p role="alert" className="mt-3 rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          {describeError(t, error)}
          <button type="button" disabled={busy} onClick={retryAction ?? (() => thresholds.reload())} className="ml-2 underline disabled:opacity-60">
            {t('thresholdRetry')}
          </button>
        </p>
      ) : null}

      {!thresholds.loading && !thresholds.error && rows.length === 0 ? (
        <p className="mt-3 text-xs text-muted">{t('thresholdsEmpty')}</p>
      ) : null}

      <ul className="mt-3 space-y-2">
        {rows.map((row) => (
          <li key={rowKey(row)} className="rounded-xl border border-hairline px-4 py-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-sm font-medium text-ink">
                {metricLabel(t, row.metric)}{' '}
                <span className="text-[11px] font-normal text-muted">
                  {row.unit} · {thresholdPhaseLabel(t, row.phase)}
                </span>
              </span>
              <span className={`text-[11px] font-medium ${thresholdSourceClass(row.source)}`}>
                {thresholdSourceLabel(t, row.source)}
              </span>
            </div>

            <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-[11px] sm:grid-cols-4">
              <div>
                <dt className="text-muted">{t('thresholdColumnTarget')}</dt>
                <dd className="text-ink">
                  {t('targetBandLabel', {
                    min: formatValue(row.metric, row.targetMin),
                    max: formatValue(row.metric, row.targetMax),
                    unit: row.unit,
                  })}
                </dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnCritical')}</dt>
                <dd className="text-ink">
                  {row.criticalMin === null && row.criticalMax === null
                    ? '—'
                    : t('targetBandLabel', {
                        min: row.criticalMin === null ? '—' : formatValue(row.metric, row.criticalMin),
                        max: row.criticalMax === null ? '—' : formatValue(row.metric, row.criticalMax),
                        unit: row.unit,
                      })}
                </dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnDwell')}</dt>
                <dd className="text-ink">
                  {row.dwellWarnMinutes}/{row.dwellCritMinutes}
                </dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnRecovery')}</dt>
                <dd className="text-ink">
                  {formatValue(row.metric, row.recoveryMargin)} {row.unit}
                </dd>
              </div>
            </dl>

            {canEdit ? (
              <div className="mt-2 flex items-center gap-3 text-[11px]">
                <button
                  type="button"
                  onClick={() => (editing === rowKey(row) ? closeEditor() : startEdit(row))}
                  aria-expanded={editing === rowKey(row)}
                  className="inline-flex items-center gap-1 text-accent hover:underline"
                >
                  <Pencil className="h-3 w-3" aria-hidden="true" />
                  {t('thresholdEdit')}
                </button>
                {row.source === 'override' ? (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => void clearOverride(row)}
                    className="inline-flex items-center gap-1 text-muted hover:text-ink hover:underline disabled:opacity-60"
                  >
                    <Undo2 className="h-3 w-3" aria-hidden="true" />
                    {t('thresholdClear')}
                  </button>
                ) : null}
              </div>
            ) : null}

            {canEdit && draft && editing === rowKey(row) ? (
              <form
                onSubmit={(event) => void submit(event, row)}
                className="mt-3 space-y-3 border-t border-hairline pt-3"
              >
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <NumberField
                    id={`${rowKey(row)}-target-min`}
                    label={`${t('thresholdMin')} (${row.unit})`}
                    value={draft.targetMin}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, targetMin: value }); }}
                    invalid={fieldHasError(error, 'overrides[0].targetMin')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-target-max`}
                    label={`${t('thresholdMax')} (${row.unit})`}
                    value={draft.targetMax}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, targetMax: value }); }}
                    invalid={fieldHasError(error, 'overrides[0].targetMax')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-critical-min`}
                    label={`${t('thresholdCritMin')} (${row.unit})`}
                    value={draft.criticalMin}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, criticalMin: value }); }}
                    invalid={fieldHasError(error, 'overrides[0].criticalMin')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-critical-max`}
                    label={`${t('thresholdCritMax')} (${row.unit})`}
                    value={draft.criticalMax}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, criticalMax: value }); }}
                    invalid={fieldHasError(error, 'overrides[0].criticalMax')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-dwell-warn`}
                    label={t('thresholdDwellWarn')}
                    value={draft.dwellWarnMinutes}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, dwellWarnMinutes: value }); }}
                    step="1"
                    invalid={fieldHasError(error, 'overrides[0].dwellWarnMinutes')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-dwell-crit`}
                    label={t('thresholdDwellCrit')}
                    value={draft.dwellCritMinutes}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, dwellCritMinutes: value }); }}
                    step="1"
                    invalid={fieldHasError(error, 'overrides[0].dwellCritMinutes')}
                  />
                  <NumberField
                    id={`${rowKey(row)}-margin`}
                    label={`${t('thresholdMargin')} (${row.unit})`}
                    value={draft.recoveryMargin}
                    onChange={(value) => { setClientError(null); setDraft({ ...draft, recoveryMargin: value }); }}
                    invalid={fieldHasError(error, 'overrides[0].recoveryMargin')}
                  />
                  <div>
                    <label htmlFor={`${rowKey(row)}-note`} className="text-[11px] text-muted">
                      {t('thresholdNote')}
                    </label>
                    <input
                      id={`${rowKey(row)}-note`}
                      value={draft.note}
                      onChange={(event) => setDraft({ ...draft, note: event.target.value })}
                      maxLength={300}
                      className="mt-1 w-full rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
                    />
                  </div>
                </div>

                {error ? (
                  <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
                    <p>{describeError(t, error)}</p>
                    {fieldMessages(t, error.errors).map((message) => (
                      <p key={message}>{message}</p>
                    ))}
                    <button type="button" disabled={busy} onClick={retryAction ?? (() => thresholds.reload())} className="mt-1 underline disabled:opacity-60">
                      {t('thresholdRetry')}
                    </button>
                  </div>
                ) : null}

                <div className="flex items-center gap-2">
                  <button
                    type="submit"
                    disabled={busy}
                    className="inline-flex items-center gap-2 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white transition-colors hover:bg-brand-dark disabled:opacity-70"
                  >
                    {busy ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                    ) : (
                      <Save className="h-3.5 w-3.5" aria-hidden="true" />
                    )}
                    <span>{busy ? t('saving') : t('save')}</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      closeEditor();
                      setError(null);
                    }}
                    className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised"
                  >
                    {t('cancel')}
                  </button>
                </div>
              </form>
            ) : null}
          </li>
        ))}
      </ul>

      {notice ? (
        <p
          role="status"
          className="mt-3 rounded-lg border border-brand/40 bg-brand/10 px-2.5 py-2 text-[11px] text-ink"
        >
          {t(notice)}
        </p>
      ) : null}
    </section>
  );
};

/** One numeric input of the band form. `invalid` outlines the field the server refused, by name. */
const NumberField: React.FC<{
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  invalid: boolean;
  step?: string;
}> = ({ id, label, value, onChange, invalid, step = 'any' }) => (
  <div>
    <label htmlFor={id} className="text-[11px] text-muted">
      {label}
    </label>
    <input
      id={id}
      type="text"
      inputMode="decimal"
      step={step}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      className={`mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
        invalid ? 'border-critical' : 'border-hairline'
      }`}
    />
  </div>
);

/**
 * The row as the write contract wants it: a metric, the phase it was resolved in, and the numbers as typed. An
 * empty critical box means "not configured" and an empty note means "no note" — both nulls, because the contract
 * distinguishes "absent" from a value.
 */
function toInput(row: EffectiveThreshold, draft: BandDraft): ThresholdOverrideInput {
  const optional = (value: string): number | null =>
    value.trim() === '' ? null : Number(value.replace(',', '.'));

  const optionalInt = (value: string): number | null => {
    const parsed = optional(value);
    return parsed === null ? null : Math.trunc(parsed);
  };

  return {
    metric: row.metric,
    phase: row.phase,
    targetMin: Number(draft.targetMin.replace(',', '.')),
    targetMax: Number(draft.targetMax.replace(',', '.')),
    criticalMin: optional(draft.criticalMin),
    criticalMax: optional(draft.criticalMax),
    dwellWarnMinutes: optionalInt(draft.dwellWarnMinutes),
    dwellCritMinutes: optionalInt(draft.dwellCritMinutes),
    recoveryMargin: optional(draft.recoveryMargin),
    note: draft.note.trim() === '' ? null : draft.note.trim(),
  };
}
