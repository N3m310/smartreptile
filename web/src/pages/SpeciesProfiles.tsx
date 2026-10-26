import React, { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import {
  Copy,
  ImagePlus,
  Leaf,
  Loader2,
  Pencil,
  Plus,
  RefreshCw,
  Save,
  Search,
  Trash2,
  X,
} from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import { PERMISSION_CODES } from '../api/types';
import type {
  CreateSpeciesProfileBody,
  SpeciesProfileBand,
  SpeciesProfileBandInput,
  SpeciesProfileItem,
  SpeciesProfileList,
  TerrariumList,
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
 * are Admin-only because the routes are (all four name the Admin policy). The Admin role is the system
 * administrator the API seeds from configuration, not the keeper's own `Owner` role, so a signed-in keeper sees
 * the catalogue and the picker and nothing they could be refused for: the four write affordances are simply absent.
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
 *
 * A profile's **picture** is the one field that is not JSON: it travels on routes of its own, so the form's Save
 * sends two calls — the fields first, then the file if one was chosen — and the picture section reports its own
 * refusal at the picker. A keeper may keep the paperwork and lose the photograph; the other order would refuse the
 * whole form over a file. The bytes are fetched rather than linked, because the image route needs the token, and a
 * profile without a picture shows the words for it rather than an empty frame.
 *
 * *Remove picture* is its own call, because it is its own state — and it drops a file chosen but not yet saved,
 * since "this profile has no picture now" is what the keeper asked for and a save that re-uploaded one would be the
 * opposite of it.
 */

/** The three phase spellings the write contract accepts. */
const PHASES = ['any', 'day', 'night'];

/** The climate zones of §4.3. The values are the enum's own names; only the labels are translated. */
const CLIMATE_ZONES = ['Tropical', 'SemiArid', 'Arid', 'Temperate'];

/**
 * The picture types the route accepts, as the picker's own filter. The server checks the bytes rather than this
 * list, so a keeper who picks something else anyway gets `image_invalid` rather than a silently stored file.
 */
const PICTURE_TYPES = 'image/png,image/jpeg,image/webp';

/**
 * A stored picture.
 *
 * The route behind `imageUrl` needs the caller's token, so an `img src` pointed at it would be refused `401` and
 * show a broken frame. The bytes are fetched instead and handed to the browser as an object URL — which is also
 * why the URL is revoked on unmount, since the blob it names outlives the element that used it otherwise.
 *
 * Nothing is rendered when there is no picture or the fetch failed: the card and the form both say "no picture
 * yet" in words, and an empty frame beside that sentence would be a second, worse answer (FR-17.4's rule for the
 * camera panel, applied here).
 */
const ProfilePicture: React.FC<{
  imageUrl: string;
  imageUpdatedAt: string | null;
  alt: string;
  className: string;
}> = ({ imageUrl, imageUpdatedAt, alt, className }) => {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  useEffect(() => {
    let created: string | null = null;
    let cancelled = false;
    const controller = new AbortController();

    api
      .speciesProfilePicture(imageUrl, imageUpdatedAt, controller.signal)
      .then((blob) => {
        if (cancelled) {
          return;
        }
        created = URL.createObjectURL(blob);
        setObjectUrl(created);
      })
      .catch(() => {
        // A picture that cannot be fetched is not a reason to fail the screen: the rest of the profile is real and
        // the picture simply does not show.
        setObjectUrl(null);
      });

    return () => {
      cancelled = true;
      controller.abort();
      if (created) {
        URL.revokeObjectURL(created);
      }
    };
  }, [imageUrl, imageUpdatedAt]);

  return objectUrl ? <img src={objectUrl} alt={alt} className={className} /> : null;
};

export const SpeciesProfiles: React.FC = () => {
  const { t } = useI18n();
  const navigate = useNavigate();
  const { hasPermission } = useSession();
  const profiles = usePolled<SpeciesProfileList>(() => api.listSpeciesProfiles(), null);
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), null);
  const items = profiles.data?.items ?? [];
  const usage = new Map<string, number>();
  for (const terrarium of terrariums.data?.items ?? []) {
    // An enclosure with no species yet names no profile, so it counts towards none (2026-10-25).
    if (terrarium.speciesProfileId === null) continue;
    usage.set(terrarium.speciesProfileId, (usage.get(terrarium.speciesProfileId) ?? 0) + 1);
  }
  const usageUnavailable = terrariums.data === null && !terrariums.loading;
  const usageLoading = terrariums.data === null && terrariums.loading;
  const [createOpen, setCreateOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [climateZone, setClimateZone] = useState('');
  // The four write routes name `species.write`, which the shipped roles grant to Admin alone — but the gate is on
  // the permission, because a deployment may grant it to a role of its own (FR-18).
  const canWrite = hasPermission(PERMISSION_CODES.speciesWrite);
  const query = search.trim().toLocaleLowerCase();
  const filteredItems = items.filter((profile) => {
    if (climateZone && profile.climateZone !== climateZone) {
      return false;
    }
    return (
      !query ||
      profile.name.toLocaleLowerCase().includes(query) ||
      profile.scientificName.toLocaleLowerCase().includes(query)
    );
  });

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

      <div className="flex flex-col gap-2 sm:flex-row">
        <label className="relative min-w-0 flex-1">
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted"
            aria-hidden="true"
          />
          <input
            type="search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('adminSearchSpeciesProfiles')}
            aria-label={t('adminSearchSpeciesProfiles')}
            className="w-full rounded-lg border border-hairline bg-field py-2 pl-9 pr-3 text-xs text-ink outline-none focus:border-brand"
          />
        </label>
        <select
          value={climateZone}
          onChange={(event) => setClimateZone(event.target.value)}
          aria-label={t('profileClimateZone')}
          className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
        >
          <option value="">{t('speciesProfileAllZones')}</option>
          {CLIMATE_ZONES.map((zone) => (
            <option key={zone} value={zone}>
              {climateZoneLabel(t, zone)}
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={() => {
            profiles.reload();
            terrariums.reload();
          }}
          aria-label={t('speciesProfileRefresh')}
          title={t('speciesProfileRefresh')}
          className="inline-flex shrink-0 items-center justify-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
        >
          <RefreshCw className="h-4 w-4" aria-hidden="true" />
          <span className="sm:hidden">{t('speciesProfileRefresh')}</span>
        </button>
      </div>

      {createOpen && canWrite ? (
        <CreatePanel
          onCreated={(profileId) => {
            setNotice('speciesProfileCreated');
            profiles.reload();
            navigate(`/species-profiles/${encodeURIComponent(profileId)}`);
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
      {terrariums.error ? (
        <p role="status" className="text-xs text-muted">
          {t('profileTerrariumCountUnavailable')}{' '}
          <button type="button" className="underline" onClick={() => terrariums.reload()}>
            {t('retry')}
          </button>
        </p>
      ) : null}

      {profiles.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {!profiles.loading && items.length === 0 && !profiles.error ? (
        <p className="rounded-xl border border-hairline bg-surface px-4 py-6 text-center text-xs text-muted">
          {t('speciesProfileEmpty')}
        </p>
      ) : null}

      {!profiles.loading && items.length > 0 && filteredItems.length === 0 ? (
        <div
          role="status"
          className="flex flex-wrap items-center justify-center gap-3 rounded-xl border border-hairline bg-surface px-4 py-6 text-center text-xs text-muted"
        >
          <span>{t('speciesProfileNoMatches')}</span>
          <button
            type="button"
            onClick={() => {
              setSearch('');
              setClimateZone('');
            }}
            className="text-accent underline"
          >
            {t('speciesProfileClearFilters')}
          </button>
        </div>
      ) : null}

      <ul className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {filteredItems.map((profile) => (
          <li key={profile.id}>
            <ProfileCard
              profile={profile}
              canWrite={canWrite}
              compact
              terrariumCount={usageUnavailable ? null : usage.get(profile.id) ?? 0}
              terrariumCountLoading={usageLoading}
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

/**
 * One refusal, rendered from its stable code: the top-level sentence, then the field rules behind it.
 *
 * The field sentences are filtered against the top-level one. A refusal whose problem code and whose field code
 * are the same string — `image_too_large`, `profile_name_taken` — otherwise says the same sentence twice, and the
 * second copy reads as a second, different problem.
 */
const RefusalBanner: React.FC<{ error: ApiError; className?: string }> = ({ error, className }) => {
  const { t } = useI18n();
  const summary = describeError(t, error);

  return (
    <div
      className={`rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink ${
        className ?? ''
      }`}
    >
      <p>{summary}</p>
      {fieldMessages(t, error.errors)
        .filter((message) => message !== summary)
        .map((message) => (
          <p key={message}>{message}</p>
        ))}
    </div>
  );
};

/** The create panel. A panel rather than a page because the list it lands in is right below it. */
const CreatePanel: React.FC<{ onCreated: (profileId: string) => void; onCancel: () => void }> = ({
  onCreated,
  onCancel,
}) => {
  const { t } = useI18n();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const submit = async (draft: ProfileDraft, picture: File | null) => {
    setBusy(true);
    setError(null);

    try {
      const created = await api.createSpeciesProfile(toCreateBody(draft));

      // The picture is a route of its own, so it is a call of its own, and it is sent *after* the profile exists.
      // The other order has no id to upload to, and a profile without its picture is a state a keeper can fix by
      // editing, while a picture with no profile is a file nothing refers to.
      if (picture) {
        await api.setSpeciesProfileImage(created.id, picture);
      }

      onCreated(created.id);
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
          onSubmit={(draft, picture) => void submit(draft, picture)}
          onCancel={onCancel}
        />
      </div>
    </section>
  );
};

/** One profile: what it holds, its bands, and — for an Admin — the four ways of changing it. */
export const ProfileCard: React.FC<{
  profile: SpeciesProfileItem;
  canWrite: boolean;
  onChanged: (notice: string) => void;
  compact?: boolean;
  terrariumCount?: number | null;
  terrariumCountLoading?: boolean;
}> = ({
  profile,
  canWrite,
  onChanged,
  compact = false,
  terrariumCount = 0,
  terrariumCountLoading = false,
}) => {
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

  const save = (draft: ProfileDraft, picture: File | null) =>
    run(async () => {
      await api.updateSpeciesProfile(profile.id, toUpdateBody(draft));

      // The fields and the picture travel on two routes, so saving a form that changed both is two calls. The
      // fields go first: a picture the server refuses leaves the keeper's edits saved and the refusal naming the
      // file, which is a state they can act on — the reverse would refuse the whole form over a photograph.
      if (picture) {
        await api.setSpeciesProfileImage(profile.id, picture);
      }

      setEditing(false);
      onChanged('speciesProfileUpdated');
    });

  const removePicture = () =>
    run(async () => {
      await api.deleteSpeciesProfileImage(profile.id);
      onChanged('speciesProfilePictureRemoved');
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

  if (compact) {
    return (
      <article className="h-full rounded-2xl border border-hairline bg-surface p-4 transition-colors hover:border-brand/50">
        {!editing && !duplicating && !confirmingDelete ? (
          <Link to={`/species-profiles/${encodeURIComponent(profile.id)}`} className="flex gap-3">
            {profile.imageUrl ? (
              <ProfilePicture
                imageUrl={profile.imageUrl}
                imageUpdatedAt={profile.imageUpdatedAt}
                alt={profile.name}
                className="h-20 w-20 shrink-0 rounded-xl border border-hairline object-cover"
              />
            ) : (
              <div className="flex h-20 w-20 shrink-0 items-center justify-center rounded-xl border border-hairline bg-raised text-muted">
                <Leaf className="h-7 w-7" aria-hidden="true" />
              </div>
            )}
            <div className="min-w-0 flex-1">
              <h2 className="font-heading font-semibold text-ink">{profile.name}</h2>
              <p className="mt-0.5 text-xs italic text-muted">{profile.scientificName || '—'}</p>
              <p className="mt-2 text-xs text-muted">
                {terrariumCountLoading
                  ? t('profileTerrariumCountLoading')
                  : terrariumCount === null
                    ? t('profileTerrariumCountUnavailable')
                    : t('profileTerrariumCount', { count: terrariumCount })}
              </p>
              <span className="mt-2 inline-flex rounded-full border border-hairline px-2 py-0.5 text-[10px] text-muted">
                {profile.isBuiltIn ? t('speciesProfileBuiltIn') : t('speciesProfileMine')}
              </span>
            </div>
          </Link>
        ) : null}
        {canWrite ? (
          <div className="mt-3 flex flex-wrap items-center gap-3 border-t border-hairline pt-3 text-xs">
            {writable ? (
              <button
                type="button"
                disabled={busy}
                onClick={() => {
                  setError(null);
                  setEditing((open) => !open);
                  setConfirmingDelete(false);
                  setDuplicating(false);
                }}
                aria-expanded={editing}
                className="inline-flex items-center gap-1 text-accent hover:underline disabled:opacity-60"
              >
                <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                {t('speciesProfileEdit')}
              </button>
            ) : null}
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                setError(null);
                setEditing(false);
                setConfirmingDelete(false);
                setDuplicating((open) => !open);
              }}
              aria-expanded={duplicating}
              className="inline-flex items-center gap-1 text-accent hover:underline disabled:opacity-60"
            >
              <Copy className="h-3.5 w-3.5" aria-hidden="true" />
              {t('speciesProfileDuplicate')}
            </button>
            {writable ? (
            <button
              type="button"
              disabled={busy}
              onClick={() => {
                setError(null);
                setEditing(false);
                setDuplicating(false);
                setConfirmingDelete((open) => !open);
              }}
              aria-expanded={confirmingDelete}
              className="inline-flex items-center gap-1 text-muted hover:text-critical hover:underline disabled:opacity-60"
            >
              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              {t('speciesProfileDelete')}
            </button>
            ) : null}
          </div>
        ) : null}
        {editing ? (
          <div className="mt-3 border-t border-hairline pt-3">
            <ProfileForm
              idPrefix={`profile-edit-${profile.id}`}
              initial={draftFrom(profile)}
              submitLabel={t('save')}
              busyLabel={t('saving')}
              busy={busy}
              error={error}
              pictureUrl={profile.imageUrl}
              pictureUpdatedAt={profile.imageUpdatedAt}
              onSubmit={(draft, picture) => void save(draft, picture)}
              onCancel={() => setEditing(false)}
            />
          </div>
        ) : null}
        {duplicating ? (
          <div className="mt-3 space-y-2 border-t border-hairline pt-3">
            <label htmlFor={`compact-copy-${profile.id}`} className="text-[11px] text-muted">
              {t('speciesProfileDuplicateName')}
            </label>
            <input
              id={`compact-copy-${profile.id}`}
              value={copyName}
              onChange={(event) => setCopyName(event.target.value)}
              maxLength={80}
              className="w-full rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
            />
            <div className="flex justify-end gap-2">
              <button
                type="button"
                disabled={busy}
                onClick={() => setDuplicating(false)}
                className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink disabled:opacity-60"
              >
                {t('cancel')}
              </button>
              <button
                type="button"
                disabled={busy}
                onClick={() => void duplicate()}
                className="inline-flex items-center gap-1 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white disabled:opacity-60"
              >
                {busy ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                ) : (
                  <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                )}
                {t('speciesProfileDuplicate')}
              </button>
            </div>
          </div>
        ) : null}
        {confirmingDelete ? (
          <div className="mt-3 flex flex-wrap items-center gap-2 rounded-lg border border-critical/30 bg-critical/5 p-3">
            <span className="mr-auto text-xs text-ink">
              {t('speciesProfileDeleteConfirm')}
            </span>
            <button
              type="button"
              disabled={busy}
              onClick={() => void remove()}
              className="inline-flex items-center gap-1 rounded-lg bg-critical px-3 py-2 text-xs font-medium text-white disabled:opacity-60"
            >
              {busy ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
              ) : (
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              {t('speciesProfileDelete')}
            </button>
            <button
              type="button"
              onClick={() => setConfirmingDelete(false)}
              className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink"
            >
              {t('cancel')}
            </button>
          </div>
        ) : null}
        {error ? <RefusalBanner error={error} className="mt-3" /> : null}
      </article>
    );
  }

  return (
    <article className="rounded-2xl border border-hairline bg-surface p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3">
          {profile.imageUrl ? (
            <ProfilePicture
              imageUrl={profile.imageUrl}
              imageUpdatedAt={profile.imageUpdatedAt}
              alt={profile.name}
              className="h-16 w-16 shrink-0 rounded-xl border border-hairline object-cover"
            />
          ) : null}
          <div className="min-w-0">
            <h2 className="font-heading text-lg font-semibold text-ink">{profile.name}</h2>
            <p className="mt-0.5 text-xs text-muted">
              {profile.scientificName ? `${profile.scientificName} · ` : ''}
              {climateZoneLabel(t, profile.climateZone)}
            </p>
          </div>
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

      {error ? <RefusalBanner error={error} className="mt-3" /> : null}

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
            pictureUrl={profile.imageUrl}
            pictureUpdatedAt={profile.imageUpdatedAt}
            onRemovePicture={() => void removePicture()}
            onSubmit={(draft, picture) => void save(draft, picture)}
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
  /** The picture already stored, shown in the section so an edit knows what it is replacing. */
  pictureUrl?: string | null;
  pictureUpdatedAt?: string | null;
  /** Absent for the create form, where there is no stored picture to remove yet. */
  onRemovePicture?: () => void;
  onSubmit: (draft: ProfileDraft, picture: File | null) => void;
  onCancel: () => void;
}> = ({
  idPrefix,
  initial,
  submitLabel,
  busyLabel,
  busy,
  error,
  pictureUrl,
  pictureUpdatedAt,
  onRemovePicture,
  onSubmit,
  onCancel,
}) => {
  const { t } = useI18n();
  const [draft, setDraft] = useState<ProfileDraft>(initial);
  const [picture, setPicture] = useState<File | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  useEffect(() => {
    if (!picture) {
      setPreview(null);

      return;
    }

    const url = URL.createObjectURL(picture);
    setPreview(url);

    return () => URL.revokeObjectURL(url);
  }, [picture]);

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
        onSubmit(draft, picture);
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

      <div className="border-t border-hairline pt-3">
        <div className="flex items-start gap-3">
          <div className="flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-xl border border-hairline bg-raised">
            {preview ? (
              <img src={preview} alt="" className="h-full w-full object-cover" />
            ) : pictureUrl ? (
              <ProfilePicture
                imageUrl={pictureUrl}
                imageUpdatedAt={pictureUpdatedAt ?? null}
                alt={t('profilePicture')}
                className="h-full w-full object-cover"
              />
            ) : (
              <ImagePlus className="h-5 w-5 text-muted" aria-hidden="true" />
            )}
          </div>

          <div className="min-w-0 flex-1">
            <span className="text-[11px] text-muted">{t('profilePicture')}</span>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              {/* The picker itself is the hidden input: a native file control cannot carry this app's styling, and
                  a label bound to it is what a keyboard and a screen reader both reach. */}
              <label
                htmlFor={`${idPrefix}-picture`}
                className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-1.5 text-xs font-medium text-ink transition-colors hover:bg-raised"
              >
                <ImagePlus className="h-4 w-4" aria-hidden="true" />
                <span>{picture || pictureUrl ? t('profilePictureReplace') : t('profilePictureChoose')}</span>
              </label>
              <input
                id={`${idPrefix}-picture`}
                type="file"
                accept={PICTURE_TYPES}
                onChange={(event) => setPicture(event.target.files?.[0] ?? null)}
                aria-invalid={fieldHasError(error, 'file') ? true : undefined}
                aria-describedby={`${idPrefix}-picture-hint`}
                className="sr-only"
              />
              {onRemovePicture && pictureUrl ? (
                <button
                  type="button"
                  // Removing means "this profile ends up with no picture", so a file chosen but not yet saved is
                  // dropped as well — otherwise the keeper would have to save a picture they had just deleted.
                  onClick={() => {
                    setPicture(null);
                    onRemovePicture();
                  }}
                  disabled={busy}
                  className="inline-flex items-center gap-2 rounded-lg border border-hairline px-3 py-1.5 text-xs font-medium text-critical transition-colors hover:bg-raised disabled:opacity-60"
                >
                  <Trash2 className="h-4 w-4" aria-hidden="true" />
                  <span>{t('profilePictureRemove')}</span>
                </button>
              ) : null}
            </div>
            <p id={`${idPrefix}-picture-hint`} className="mt-1 text-[11px] text-muted">
              {picture
                ? `${picture.name} · ${t('profilePictureHint')}`
                : pictureUrl
                  ? t('profilePictureHint')
                  : `${t('profilePictureNone')} · ${t('profilePictureHint')}`}
            </p>
            {fieldMessage(t, error, 'file') ? (
              <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'file')}</p>
            ) : null}
          </div>
        </div>
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

      {error ? <RefusalBanner error={error} /> : null}

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
