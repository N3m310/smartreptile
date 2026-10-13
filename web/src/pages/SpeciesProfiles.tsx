import React, { useState } from 'react';
import { Copy, Loader2, Pencil, Plus, Save, Trash2, X } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import type {
  CreateSpeciesProfileBody,
  SpeciesProfileBand,
  SpeciesProfileBandInput,
  SpeciesProfileItem,
  SpeciesProfileList,
  UpdateSpeciesProfileBody,
} from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatValue, metricKeys, metricLabel, type TFunc } from '../lib/format';
import { describeError, fieldHasError, fieldMessage, fieldMessages } from '../lib/messages';
import { climateZoneLabel, thresholdPhaseLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

/**
 * The species-profile catalogue (FR-10) — roadmap 3.9's five routes, on a screen.
 *
 * Until now the catalogue could only be *read*, and only as the picker inside the terrarium create form: a keeper
 * could choose a profile and had no way to author one, so every band that did not match a built-in had to become a
 * per-terrarium override. This screen is the other half — list, create, edit, duplicate, delete — and the writes
 * are Owner-only because the routes are (all four name the Owner policy).
 *
 * Two things this screen deliberately does not do. It does not offer Edit or Delete on a built-in: FR-10 A2 says a
 * built-in is read-only and the API answers `403 builtin_immutable`, so the honest rendering is the hint that says
 * to duplicate it — a button whose only outcome is a refusal is worse than no button. And it does not decide that a
 * metric or a climate zone is invalid: it offers the two lists the contract names and lets `metric_invalid` /
 * `climate_zone_invalid` be the server's answer, because the client is not the authority on either.
 *
 * The catalogue is loaded once and reloaded after a write rather than polled, the reading the terrarium picker
 * took: it changes only when someone authors a profile here, and a list that reshuffles under a half-filled form
 * is worse than a minute-old one.
 */

/** The three phase spellings the write contract accepts. */
const PHASES = ['any', 'day', 'night'];

/** The climate zones of §4.3. The values are the enum's own names; only the labels are translated. */
const CLIMATE_ZONES = ['Tropical', 'SemiArid', 'Arid', 'Temperate'];

export const SpeciesProfiles: React.FC = () => {
  const { t } = useI18n();
  const { user } = useSession();
  const profiles = usePolled<SpeciesProfileList>(() => api.listSpeciesProfiles(), null);
  const items = profiles.data?.items ?? [];
  const [createOpen, setCreateOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const canWrite = user?.role === 'Owner';

  return (
    <div className="space-y-6">
      <header className="flex items-start justify-between gap-4">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('speciesProfilesTitle')}</h1>
          <p className="mt-1 max-w-2xl text-xs text-muted">{t('speciesProfilesCaption')}</p>
        </div>
        {canWrite ? (
          <button
            type="button"
            onClick={() => setCreateOpen((open) => !open)}
            aria-expanded={createOpen}
            className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span>{t('speciesProfileNew')}</span>
          </button>
        ) : null}
      </header>

      {notice ? (
        <p
          role="status"
          className="rounded-lg border border-brand/40 bg-brand/10 px-3 py-2 text-[11px] text-ink"
        >
          {t(notice)}
        </p>
      ) : null}

      {createOpen && canWrite ? (
        <CreatePanel
          onCreated={() => {
            setNotice('speciesProfileCreated');
            profiles.reload();
          }}
          onCancel={() => setCreateOpen(false)}
        />
      ) : null}

      {profiles.error?.isNetworkFailure ? (
        <UnreachableBanner onRetry={() => profiles.reload()} />
      ) : null}
      {!profiles.error?.isNetworkFailure && profiles.error ? (
        <ErrorPanel error={profiles.error} onRetry={() => profiles.reload()} />
      ) : null}

      {profiles.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {!profiles.loading && items.length === 0 && !profiles.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('emptyStateBody')} />
      ) : null}

      <ul className="space-y-4">
        {items.map((profile) => (
          <li key={profile.id}>
            <ProfileCard
              profile={profile}
              canWrite={canWrite}
              onChanged={(key) => {
                setNotice(key);
                profiles.reload();
              }}
            />
          </li>
        ))}
      </ul>
    </div>
  );
};

/** The create panel. A panel rather than a page because the list it lands in is right below it. */
const CreatePanel: React.FC<{ onCreated: () => void; onCancel: () => void }> = ({
  onCreated,
  onCancel,
}) => {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const submit = async (draft: ProfileDraft) => {
    setBusy(true);
    setError(null);

    try {
      await api.createSpeciesProfile(toCreateBody(draft));
      onCreated();
      onCancel();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="rounded-2xl border border-hairline bg-surface p-5">
      <h2 className="font-heading text-sm font-semibold text-ink">{t('speciesProfileNew')}</h2>
      <div className="mt-3">
        <ProfileForm
          idPrefix="profile-create"
          initial={emptyDraft()}
          submitLabel={t('create')}
          busyLabel={t('creating')}
          busy={busy}
          error={error}
          onSubmit={(draft) => void submit(draft)}
          onCancel={onCancel}
        />
      </div>
    </section>
  );
};

/** One profile: what it holds, its bands, and — for an Owner — the four ways of changing it. */
const ProfileCard: React.FC<{
  profile: SpeciesProfileItem;
  canWrite: boolean;
  onChanged: (notice: string) => void;
}> = ({ profile, canWrite, onChanged }) => {
  const { t } = useI18n();
  const [editing, setEditing] = useState(false);
  const [duplicating, setDuplicating] = useState(false);
  const [copyName, setCopyName] = useState('');
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const writable = canWrite && !profile.isBuiltIn;

  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);

    try {
      await action();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const save = (draft: ProfileDraft) =>
    run(async () => {
      await api.updateSpeciesProfile(profile.id, toUpdateBody(draft));
      setEditing(false);
      onChanged('speciesProfileUpdated');
    });

  const duplicate = () =>
    run(async () => {
      await api.duplicateSpeciesProfile(profile.id, copyName);
      setDuplicating(false);
      setCopyName('');
      onChanged('speciesProfileDuplicated');
    });

  const remove = () =>
    run(async () => {
      await api.deleteSpeciesProfile(profile.id);
      setConfirmingDelete(false);
      onChanged('speciesProfileDeleted');
    });

  return (
    <article className="rounded-2xl border border-hairline bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-heading text-lg font-semibold text-ink">{profile.name}</h2>
          <p className="mt-0.5 text-xs text-muted">
            {profile.scientificName ? `${profile.scientificName} · ` : ''}
            {climateZoneLabel(t, profile.climateZone)}
          </p>
        </div>
        {profile.isBuiltIn ? (
          <span className="rounded-full border border-hairline px-2.5 py-1 text-[11px] text-muted">
            {t('speciesProfileBuiltIn')}
          </span>
        ) : null}
      </div>

      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-1 text-[11px] sm:grid-cols-4">
        <div>
          <dt className="text-muted">{t('profilePhotoperiodHours')}</dt>
          <dd className="text-ink">{profile.photoperiodHours}</dd>
        </div>
        <div>
          <dt className="text-muted">{t('profileLightsOn')}</dt>
          <dd className="text-ink">{profile.lightsOnLocalTime}</dd>
        </div>
        <div>
          <dt className="text-muted">{t('profileLightThreshold')}</dt>
          <dd className="text-ink">{formatValue('lightLux', profile.lightThresholdLux)}</dd>
        </div>
        <div>
          <dt className="text-muted">{t('profileMinLightHours')}</dt>
          <dd className="text-ink">{profile.minLightHoursPerDay}</dd>
        </div>
      </dl>

      {profile.notes ? <p className="mt-3 text-xs text-muted">{profile.notes}</p> : null}

      <h3 className="mt-4 flex items-baseline gap-2 text-[11px] font-medium text-muted">
        <span>{t('speciesProfileBands')}</span>
        <span className="font-normal">
          {t('speciesProfileBandCount', { count: profile.bands.length })}
        </span>
      </h3>
      <ul className="mt-2 space-y-2">
        {profile.bands.map((band) => (
          <li
            key={`${band.metric}-${band.phase}`}
            className="rounded-xl border border-hairline px-4 py-3"
          >
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <span className="text-sm font-medium text-ink">
                {metricLabel(t, band.metric)}{' '}
                <span className="text-[11px] font-normal text-muted">
                  {band.unit} · {thresholdPhaseLabel(t, band.phase)}
                </span>
              </span>
              {band.enabled ? null : (
                <span className="text-[11px] text-muted">{t('bandDisabled')}</span>
              )}
            </div>

            <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-[11px] sm:grid-cols-4">
              <div>
                <dt className="text-muted">{t('thresholdColumnTarget')}</dt>
                <dd className="text-ink">{bandText(t, band)}</dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnCritical')}</dt>
                <dd className="text-ink">{criticalText(t, band)}</dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnDwell')}</dt>
                <dd className="text-ink">
                  {band.dwellWarnMinutes}/{band.dwellCritMinutes}
                </dd>
              </div>
              <div>
                <dt className="text-muted">{t('thresholdColumnRecovery')}</dt>
                <dd className="text-ink">
                  {formatValue(band.metric, band.recoveryMargin)} {band.unit}
                </dd>
              </div>
            </dl>

            {band.sourceRef ? (
              <p className="mt-2 text-[11px] text-muted">
                {t('bandSourceRef')}:{' '}
                {band.sourceUrl ? (
                  <a
                    href={band.sourceUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="text-accent hover:underline"
                  >
                    {band.sourceRef}
                  </a>
                ) : (
                  band.sourceRef
                )}
              </p>
            ) : null}
          </li>
        ))}
      </ul>

      {profile.isBuiltIn ? (
        <p className="mt-3 text-[11px] text-muted">{t('speciesProfileReadOnlyHint')}</p>
      ) : null}

      {error ? (
        <div className="mt-3 rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          <p>{describeError(t, error)}</p>
          {fieldMessages(t, error.errors).map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

      {canWrite ? (
        <div className="mt-4 flex flex-wrap items-center gap-4 text-[11px]">
          {writable ? (
            <button
              type="button"
              onClick={() => {
                setError(null);
                setEditing((open) => !open);
              }}
              aria-expanded={editing}
              className="inline-flex items-center gap-1 text-accent hover:underline"
            >
              <Pencil className="h-3 w-3" aria-hidden="true" />
              {t('speciesProfileEdit')}
            </button>
          ) : null}
          <button
            type="button"
            disabled={busy}
            onClick={() => {
              setError(null);
              setDuplicating((open) => !open);
            }}
            aria-expanded={duplicating}
            className="inline-flex items-center gap-1 text-accent hover:underline disabled:opacity-60"
          >
            <Copy className="h-3 w-3" aria-hidden="true" />
            {t('speciesProfileDuplicate')}
          </button>
          {writable ? (
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                setError(null);
                setConfirmingDelete((open) => !open);
              }}
              aria-expanded={confirmingDelete}
              className="inline-flex items-center gap-1 text-muted hover:text-ink hover:underline disabled:opacity-60"
            >
              <Trash2 className="h-3 w-3" aria-hidden="true" />
              {t('speciesProfileDelete')}
            </button>
          ) : null}
        </div>
      ) : null}

      {duplicating ? (
        <div className="mt-3 space-y-2 rounded-xl border border-hairline p-3">
          <label htmlFor={`copy-${profile.id}`} className="text-[11px] text-muted">
            {t('speciesProfileDuplicateName')}
          </label>
          <div className="flex items-center gap-2">
            <input
              id={`copy-${profile.id}`}
              value={copyName}
              onChange={(event) => setCopyName(event.target.value)}
              maxLength={80}
              className="w-full rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
            />
            <button
              type="button"
              disabled={busy}
              onClick={() => void duplicate()}
              className="inline-flex shrink-0 items-center gap-2 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white transition-colors hover:bg-brand-dark disabled:opacity-70"
            >
              {busy ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              ) : (
                <Copy className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              <span>{t('speciesProfileDuplicate')}</span>
            </button>
          </div>
        </div>
      ) : null}

      {confirmingDelete ? (
        <div className="mt-3 flex items-center gap-2">
          <button
            type="button"
            disabled={busy}
            onClick={() => void remove()}
            className="inline-flex items-center gap-2 rounded-lg border border-critical/40 bg-critical/10 px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-critical/20 disabled:opacity-70"
          >
            {busy ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            ) : (
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
            )}
            <span>{t('speciesProfileDeleteConfirm')}</span>
          </button>
          <button
            type="button"
            onClick={() => setConfirmingDelete(false)}
            className="inline-flex items-center gap-1 rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised"
          >
            <X className="h-3.5 w-3.5" aria-hidden="true" />
            <span>{t('cancel')}</span>
          </button>
        </div>
      ) : null}

      {editing ? (
        <div className="mt-4 border-t border-hairline pt-4">
          <ProfileForm
            idPrefix={`profile-edit-${profile.id}`}
            initial={draftFrom(profile)}
            submitLabel={t('save')}
            busyLabel={t('saving')}
            busy={busy}
            error={error}
            onSubmit={(draft) => void save(draft)}
            onCancel={() => setEditing(false)}
          />
        </div>
      ) : null}
    </article>
  );
};

/**
 * The profile's own fields plus its bands. One component for create and edit, because the two send the same
 * fields and differ only in which route they reach — two forms would drift into disagreeing about a default.
 */
const ProfileForm: React.FC<{
  /** Unique per form on the page, because both the create panel and every card's editor render one at a time. */
  idPrefix: string;
  initial: ProfileDraft;
  submitLabel: string;
  busyLabel: string;
  busy: boolean;
  error: ApiError | null;
  onSubmit: (draft: ProfileDraft) => void;
  onCancel: () => void;
}> = ({ idPrefix, initial, submitLabel, busyLabel, busy, error, onSubmit, onCancel }) => {
  const { t } = useI18n();
  const [draft, setDraft] = useState<ProfileDraft>(initial);

  const inputClass = (field: string) =>
    `mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline'
    }`;

  const textField = (
    field: 'name' | 'scientificName' | 'photoperiodHours' | 'lightThresholdLux' | 'minLightHoursPerDay' | 'notes',
    labelKey: string,
    extra?: { type?: string; maxLength?: number; step?: string }
  ) => {
    const message = fieldMessage(t, error, field);
    const errorId = `${idPrefix}-${field}-error`;

    return (
      <div className={field === 'notes' ? 'sm:col-span-2' : undefined}>
        <label htmlFor={`${idPrefix}-${field}`} className="text-[11px] text-muted">
          {t(labelKey)}
        </label>
        <input
          id={`${idPrefix}-${field}`}
          type={extra?.type ?? 'text'}
          step={extra?.step}
          inputMode={extra?.type === 'number' ? 'decimal' : undefined}
          maxLength={extra?.maxLength}
          value={draft[field]}
          onChange={(event) => setDraft({ ...draft, [field]: event.target.value })}
          aria-invalid={message ? true : undefined}
          aria-describedby={message ? errorId : undefined}
          className={inputClass(field)}
        />
        {message ? (
          <p id={errorId} className="mt-1 text-[11px] text-critical">
            {message}
          </p>
        ) : null}
      </div>
    );
  };

  const bandField = (index: number) => (field: keyof BandDraft) =>
    `bands[${index}].${field as string}`;

  const setBand = (index: number, patch: Partial<BandDraft>) =>
    setDraft({
      ...draft,
      bands: draft.bands.map((band, position) =>
        position === index ? { ...band, ...patch } : band
      ),
    });

  const bandsMessage = fieldMessage(t, error, 'bands');

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(draft);
      }}
      className="space-y-3"
    >
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {textField('name', 'profileName', { maxLength: 80 })}
        {textField('scientificName', 'profileScientificName', { maxLength: 120 })}

        <div>
          <label htmlFor={`${idPrefix}-climateZone`} className="text-[11px] text-muted">
            {t('profileClimateZone')}
          </label>
          <select
            id={`${idPrefix}-climateZone`}
            value={draft.climateZone}
            onChange={(event) => setDraft({ ...draft, climateZone: event.target.value })}
            className={inputClass('climateZone')}
          >
            {CLIMATE_ZONES.map((zone) => (
              <option key={zone} value={zone}>
                {climateZoneLabel(t, zone)}
              </option>
            ))}
          </select>
          {fieldMessage(t, error, 'climateZone') ? (
            <p className="mt-1 text-[11px] text-critical">
              {fieldMessage(t, error, 'climateZone')}
            </p>
          ) : null}
        </div>

        {textField('photoperiodHours', 'profilePhotoperiodHours', {
          type: 'number',
          step: 'any',
        })}

        <div>
          <label htmlFor={`${idPrefix}-lightsOn`} className="text-[11px] text-muted">
            {t('profileLightsOn')}
          </label>
          <input
            id={`${idPrefix}-lightsOn`}
            type="time"
            value={draft.lightsOnLocalTime}
            onChange={(event) => setDraft({ ...draft, lightsOnLocalTime: event.target.value })}
            className={inputClass('lightsOnLocalTime')}
          />
          {fieldMessage(t, error, 'lightsOnLocalTime') ? (
            <p className="mt-1 text-[11px] text-critical">
              {fieldMessage(t, error, 'lightsOnLocalTime')}
            </p>
          ) : null}
        </div>

        {textField('lightThresholdLux', 'profileLightThreshold', { type: 'number', step: 'any' })}
        {textField('minLightHoursPerDay', 'profileMinLightHours', { type: 'number', step: 'any' })}
        {textField('notes', 'profileNotes', { maxLength: 1000 })}
      </div>

      <div className="space-y-2 border-t border-hairline pt-3">
        <div className="flex items-center justify-between gap-3">
          <h3 className="text-[11px] font-medium text-muted">{t('speciesProfileBands')}</h3>
          <button
            type="button"
            onClick={() => setDraft({ ...draft, bands: [...draft.bands, emptyBand()] })}
            className="inline-flex items-center gap-1 rounded-lg border border-hairline px-2.5 py-1.5 text-[11px] text-ink transition-colors hover:bg-raised"
          >
            <Plus className="h-3 w-3" aria-hidden="true" />
            {t('bandAdd')}
          </button>
        </div>

        {bandsMessage ? <p className="text-[11px] text-critical">{bandsMessage}</p> : null}

        {draft.bands.map((band, index) => {
          const bandFieldError = bandField(index);
          const metricMessage = fieldMessage(t, error, bandFieldError('metric'));
          // A metric this build does not know stays an option rather than being silently swapped for the first
          // one in the list: the stored band keeps what the server sent, and the server is the one that judges it.
          const options = metricKeys.includes(band.metric)
            ? metricKeys
            : [band.metric, ...metricKeys];

          return (
            <div key={index} className="rounded-xl border border-hairline p-3">
              <div className="flex items-start justify-between gap-2">
                <div className="grid flex-1 grid-cols-2 gap-2 sm:grid-cols-4">
                  <div>
                    <label htmlFor={`${idPrefix}-band-${index}-metric`} className="text-[11px] text-muted">
                      {t('bandMetric')}
                    </label>
                    <select
                      id={`${idPrefix}-band-${index}-metric`}
                      value={band.metric}
                      onChange={(event) => setBand(index, { metric: event.target.value })}
                      aria-invalid={metricMessage ? true : undefined}
                      className={inputClass(bandFieldError('metric'))}
                    >
                      {options.map((key) => (
                        <option key={key} value={key}>
                          {metricLabel(t, key)}
                        </option>
                      ))}
                    </select>
                    {metricMessage ? (
                      <p className="mt-1 text-[11px] text-critical">{metricMessage}</p>
                    ) : null}
                  </div>

                  <div>
                    <label htmlFor={`${idPrefix}-band-${index}-phase`} className="text-[11px] text-muted">
                      {t('bandPhase')}
                    </label>
                    <select
                      id={`${idPrefix}-band-${index}-phase`}
                      value={band.phase}
                      onChange={(event) => setBand(index, { phase: event.target.value })}
                      aria-invalid={fieldHasError(error, bandFieldError('phase')) ? true : undefined}
                      className={inputClass(bandFieldError('phase'))}
                    >
                      {PHASES.map((phase) => (
                        <option key={phase} value={phase}>
                          {thresholdPhaseLabel(t, phase)}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="grid grid-cols-2 gap-2 sm:col-span-2">
                    <NumberInput
                      id={`${idPrefix}-band-${index}-target-min`}
                      label={t('thresholdMin')}
                      value={band.targetMin}
                      onChange={(value) => setBand(index, { targetMin: value })}
                      className={inputClass(bandFieldError('targetMin'))}
                      invalid={fieldHasError(error, bandFieldError('targetMin'))}
                    />
                    <NumberInput
                      id={`${idPrefix}-band-${index}-target-max`}
                      label={t('thresholdMax')}
                      value={band.targetMax}
                      onChange={(value) => setBand(index, { targetMax: value })}
                      className={inputClass(bandFieldError('targetMax'))}
                      invalid={fieldHasError(error, bandFieldError('targetMax'))}
                    />
                  </div>
                </div>

                <button
                  type="button"
                  aria-label={t('bandRemove')}
                  title={t('bandRemove')}
                  disabled={draft.bands.length === 1}
                  onClick={() =>
                    setDraft({
                      ...draft,
                      bands: draft.bands.filter((_, position) => position !== index),
                    })
                  }
                  className="inline-flex shrink-0 items-center rounded-lg border border-hairline p-2 text-muted transition-colors hover:bg-raised hover:text-ink disabled:opacity-40"
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                </button>
              </div>

              <div className="mt-2 grid grid-cols-2 gap-2 sm:grid-cols-4">
                <NumberInput
                  id={`${idPrefix}-band-${index}-critical-min`}
                  label={t('thresholdCritMin')}
                  value={band.criticalMin}
                  onChange={(value) => setBand(index, { criticalMin: value })}
                  className={inputClass(bandFieldError('criticalMin'))}
                  invalid={fieldHasError(error, bandFieldError('criticalMin'))}
                />
                <NumberInput
                  id={`${idPrefix}-band-${index}-critical-max`}
                  label={t('thresholdCritMax')}
                  value={band.criticalMax}
                  onChange={(value) => setBand(index, { criticalMax: value })}
                  className={inputClass(bandFieldError('criticalMax'))}
                  invalid={fieldHasError(error, bandFieldError('criticalMax'))}
                />
                <NumberInput
                  id={`${idPrefix}-band-${index}-dwell-warn`}
                  label={t('thresholdDwellWarn')}
                  value={band.dwellWarnMinutes}
                  onChange={(value) => setBand(index, { dwellWarnMinutes: value })}
                  className={inputClass(bandFieldError('dwellWarnMinutes'))}
                  invalid={fieldHasError(error, bandFieldError('dwellWarnMinutes'))}
                />
                <NumberInput
                  id={`${idPrefix}-band-${index}-dwell-crit`}
                  label={t('thresholdDwellCrit')}
                  value={band.dwellCritMinutes}
                  onChange={(value) => setBand(index, { dwellCritMinutes: value })}
                  className={inputClass(bandFieldError('dwellCritMinutes'))}
                  invalid={fieldHasError(error, bandFieldError('dwellCritMinutes'))}
                />
                <NumberInput
                  id={`${idPrefix}-band-${index}-margin`}
                  label={t('thresholdMargin')}
                  value={band.recoveryMargin}
                  onChange={(value) => setBand(index, { recoveryMargin: value })}
                  className={inputClass(bandFieldError('recoveryMargin'))}
                  invalid={fieldHasError(error, bandFieldError('recoveryMargin'))}
                />

                <div>
                  <label htmlFor={`${idPrefix}-band-${index}-source`} className="text-[11px] text-muted">
                    {t('bandSourceRef')}
                  </label>
                  <input
                    id={`${idPrefix}-band-${index}-source`}
                    value={band.sourceRef}
                    onChange={(event) => setBand(index, { sourceRef: event.target.value })}
                    maxLength={300}
                    className={inputClass(bandFieldError('sourceRef'))}
                  />
                </div>

                <label className="flex items-center gap-2 self-end text-[11px] text-muted">
                  <input
                    id={`${idPrefix}-band-${index}-enabled`}
                    type="checkbox"
                    checked={band.enabled}
                    onChange={(event) => setBand(index, { enabled: event.target.checked })}
                    className="h-4 w-4 accent-brand"
                  />
                  {t('bandEnabled')}
                </label>
              </div>
            </div>
          );
        })}
      </div>

      {error ? (
        <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          <p>{describeError(t, error)}</p>
          {fieldMessages(t, error.errors).map((message) => (
            <p key={message}>{message}</p>
          ))}
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
          <span>{busy ? busyLabel : submitLabel}</span>
        </button>
        <button
          type="button"
          onClick={onCancel}
          className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised"
        >
          {t('cancel')}
        </button>
      </div>
    </form>
  );
};

/** One numeric input of a band. The band's own violations arrive addressed by position, so the id is too. */
const NumberInput: React.FC<{
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  className: string;
  invalid: boolean;
}> = ({ id, label, value, onChange, className, invalid }) => (
  <div>
    <label htmlFor={id} className="text-[11px] text-muted">
      {label}
    </label>
    <input
      id={id}
      type="number"
      inputMode="decimal"
      step="any"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      aria-invalid={invalid ? true : undefined}
      className={className}
    />
  </div>
);

/** One band as the form holds it: every number a string, because a half-typed box is not a number yet. */
interface BandDraft {
  metric: string;
  phase: string;
  targetMin: string;
  targetMax: string;
  criticalMin: string;
  criticalMax: string;
  dwellWarnMinutes: string;
  dwellCritMinutes: string;
  recoveryMargin: string;
  enabled: boolean;
  sourceRef: string;
}

interface ProfileDraft {
  name: string;
  scientificName: string;
  climateZone: string;
  photoperiodHours: string;
  lightsOnLocalTime: string;
  lightThresholdLux: string;
  minLightHoursPerDay: string;
  notes: string;
  bands: BandDraft[];
}

/** A new band starts on the metric every profile has to carry, in the phase that always applies. */
const emptyBand = (): BandDraft => ({
  metric: 'tempC',
  phase: 'any',
  targetMin: '',
  targetMax: '',
  criticalMin: '',
  criticalMax: '',
  dwellWarnMinutes: '',
  dwellCritMinutes: '',
  recoveryMargin: '',
  enabled: true,
  sourceRef: '',
});

/** A blank profile: the API's own defaults where it has one, so the form shows what it will get. */
const emptyDraft = (): ProfileDraft => ({
  name: '',
  scientificName: '',
  climateZone: 'Tropical',
  photoperiodHours: '12',
  lightsOnLocalTime: '07:00',
  lightThresholdLux: '1000',
  minLightHoursPerDay: '8',
  notes: '',
  bands: [emptyBand()],
});

const draftFrom = (profile: SpeciesProfileItem): ProfileDraft => ({
  name: profile.name,
  scientificName: profile.scientificName ?? '',
  climateZone: profile.climateZone,
  photoperiodHours: String(profile.photoperiodHours),
  lightsOnLocalTime: profile.lightsOnLocalTime,
  lightThresholdLux: String(profile.lightThresholdLux),
  minLightHoursPerDay: String(profile.minLightHoursPerDay),
  notes: profile.notes ?? '',
  bands: profile.bands.map((band) => ({
    metric: band.metric,
    phase: band.phase,
    targetMin: String(band.targetMin),
    targetMax: String(band.targetMax),
    criticalMin: band.criticalMin === null ? '' : String(band.criticalMin),
    criticalMax: band.criticalMax === null ? '' : String(band.criticalMax),
    dwellWarnMinutes: String(band.dwellWarnMinutes),
    dwellCritMinutes: String(band.dwellCritMinutes),
    recoveryMargin: String(band.recoveryMargin),
    enabled: band.enabled,
    sourceRef: band.sourceRef ?? '',
  })),
});

/**
 * A box the keeper emptied is "not given", not zero: the contract derives `criticalMin`/`criticalMax` from the
 * target band and the margin, and takes the documented default for the dwells, so an empty box has to travel as
 * null rather than as a number nobody typed. `targetMin`/`targetMax` are the exception — they are required, and a
 * missing one is refused by name (`threshold_target_required`).
 */
function numberOrNull(value: string): number | null {
  return value.trim() === '' ? null : Number(value.replace(',', '.'));
}

function integerOrNull(value: string): number | null {
  const parsed = numberOrNull(value);
  return parsed === null ? null : Math.trunc(parsed);
}

function toBandInputs(bands: BandDraft[]): SpeciesProfileBandInput[] {
  return bands.map((band) => ({
    metric: band.metric,
    phase: band.phase,
    targetMin: numberOrNull(band.targetMin),
    targetMax: numberOrNull(band.targetMax),
    criticalMin: numberOrNull(band.criticalMin),
    criticalMax: numberOrNull(band.criticalMax),
    dwellWarnMinutes: integerOrNull(band.dwellWarnMinutes),
    dwellCritMinutes: integerOrNull(band.dwellCritMinutes),
    recoveryMargin: numberOrNull(band.recoveryMargin),
    enabled: band.enabled,
    sourceRef: band.sourceRef.trim() === '' ? null : band.sourceRef.trim(),
  }));
}

function toCreateBody(draft: ProfileDraft): CreateSpeciesProfileBody {
  return {
    name: draft.name.trim(),
    scientificName: draft.scientificName.trim(),
    climateZone: draft.climateZone,
    photoperiodHours: numberOrNull(draft.photoperiodHours),
    lightsOnLocalTime: draft.lightsOnLocalTime,
    lightThresholdLux: numberOrNull(draft.lightThresholdLux),
    minLightHoursPerDay: numberOrNull(draft.minLightHoursPerDay),
    notes: draft.notes.trim(),
    bands: toBandInputs(draft.bands),
  };
}

/**
 * The patch this form sends: every field it shows, and the whole band set.
 *
 * A field sent as an empty string is the contract's way of *clearing* the scientific name and the notes — a `null`
 * member and an absent one are indistinguishable once the body is bound, and both mean "leave it alone" — so the
 * empty box travels as `''` and not as `null`. The bands always travel, because the editor shows the set it would
 * replace: sending only the changed rows would leave the ones the keeper removed in place.
 */
function toUpdateBody(draft: ProfileDraft): UpdateSpeciesProfileBody {
  return {
    name: draft.name.trim(),
    scientificName: draft.scientificName.trim(),
    climateZone: draft.climateZone,
    photoperiodHours: numberOrNull(draft.photoperiodHours),
    lightsOnLocalTime: draft.lightsOnLocalTime,
    lightThresholdLux: numberOrNull(draft.lightThresholdLux),
    minLightHoursPerDay: numberOrNull(draft.minLightHoursPerDay),
    notes: draft.notes.trim(),
    bands: toBandInputs(draft.bands),
  };
}

/** The target band as `02-design/04` §7 renders a range, from the metric's own precision. */
function bandText(t: TFunc, band: SpeciesProfileBand): string {
  return t('targetBandLabel', {
    min: formatValue(band.metric, band.targetMin),
    max: formatValue(band.metric, band.targetMax),
    unit: band.unit,
  });
}

/** A band that relies on the derived margin has no critical bounds to show, and says so rather than showing zero. */
function criticalText(t: TFunc, band: SpeciesProfileBand): string {
  if (band.criticalMin === null && band.criticalMax === null) {
    return '—';
  }

  return t('targetBandLabel', {
    min: band.criticalMin === null ? '—' : formatValue(band.metric, band.criticalMin),
    max: band.criticalMax === null ? '—' : formatValue(band.metric, band.criticalMax),
    unit: band.unit,
  });
}
