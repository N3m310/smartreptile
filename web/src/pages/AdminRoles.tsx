import React, { useMemo, useState } from 'react';
import { AlertTriangle, Loader2, Plus, Save, ShieldCheck, Trash2, Users } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import { PERMISSION_CODES } from '../api/types';
import type { PermissionSummary, RoleSummary } from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { describeError, fieldHasError, fieldMessage, fieldMessages } from '../lib/messages';
import { permissionDescription, permissionGroupLabel, permissionLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

/**
 * The permission matrix (FR-18) — `/api/v1/admin/roles` and `/admin/permissions`, gated on `user.read` to read and
 * `user.manage` to change anything.
 *
 * **A role is a named set of permissions, and nothing else.** No screen decides what a role may do: the four the
 * product ships are seeded, the catalogue is compiled into the API, and this screen's only job is to move codes
 * between the two. That is why the matrix is checkboxes over the catalogue rather than a list of role names an
 * administrator would have to translate — granting `species.write` says exactly what it does, and the label the
 * API sends for it is kept as a fallback so a code this build does not know stays visible rather than wrong.
 *
 * What the screen refuses to offer, because the API refuses it: deleting a shipped role
 * (`409 system_role_immutable` — the next start-up would recreate it and silently undo whatever was tuned) and
 * deleting a role accounts still hold (`409 role_in_use`). The account count travels with each role, so the
 * delete button appears exactly where it can succeed.
 *
 * One rule cannot be checked here at all: removing the *last* grant of `user.manage` is refused with
 * `last_administrator_refused`, and whether a grant is the last one depends on every other role and account in the
 * deployment. The editor says so instead of pretending to know.
 */

export const AdminRoles: React.FC = () => {
  const { t } = useI18n();
  const { hasPermission } = useSession();
  const canManage = hasPermission(PERMISSION_CODES.userManage);

  const roles = usePolled<{ items: RoleSummary[] }>(() => api.listRoles(), null);
  const catalog = usePolled<{ items: PermissionSummary[] }>(() => api.listPermissions(), null);

  const items = roles.data?.items ?? [];
  const groups = useMemo(() => groupCatalog(catalog.data?.items ?? []), [catalog.data]);

  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const selected = items.find((role) => role.id === selectedId) ?? null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('adminRolesTitle')}</h1>
          <p className="mt-1 max-w-3xl text-xs text-muted">{t('adminRolePick')}</p>
        </div>
        {canManage ? (
          <button
            type="button"
            onClick={() => setCreateOpen((open) => !open)}
            aria-expanded={createOpen}
            className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span>{t('adminRoleNew')}</span>
          </button>
        ) : null}
      </header>

      {notice ? (
        <p role="status" className="rounded-lg border border-brand/40 bg-brand/10 px-3 py-2 text-[11px] text-ink">
          {t(notice)}
        </p>
      ) : null}

      {createOpen && canManage ? (
        <CreateRolePanel
          groups={groups}
          onCreated={() => {
            setNotice('adminRoleCreated');
            roles.reload();
            setCreateOpen(false);
          }}
          onCancel={() => setCreateOpen(false)}
        />
      ) : null}

      {roles.error?.isNetworkFailure ? <UnreachableBanner onRetry={() => roles.reload()} /> : null}
      {!roles.error?.isNetworkFailure && roles.error ? (
        <ErrorPanel error={roles.error} onRetry={() => roles.reload()} />
      ) : null}
      {catalog.error ? <ErrorPanel error={catalog.error} onRetry={() => catalog.reload()} /> : null}
      {roles.loading || catalog.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {!roles.loading && items.length === 0 && !roles.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('emptyStateBody')} />
      ) : null}

      <ul className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {items.map((role) => (
          <li
            key={role.id}
            className="flex flex-col justify-between rounded-2xl border border-hairline bg-surface p-5"
          >
            <div>
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="font-heading text-base font-semibold text-ink">{role.name}</h2>
                  <p className="mt-0.5 font-mono text-[11px] text-muted">{role.code}</p>
                </div>
                <span
                  className={`shrink-0 rounded-full border px-2.5 py-1 text-[11px] ${
                    role.isSystem ? 'border-brand/40 text-brand' : 'border-hairline text-muted'
                  }`}
                >
                  {role.isSystem ? t('adminRoleSystem') : t('adminRoleCustom')}
                </span>
              </div>

              {role.description ? <p className="mt-2 text-[11px] text-muted">{role.description}</p> : null}

              <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5 text-[11px] text-muted">
                <li className="inline-flex items-center gap-1.5">
                  <Users className="h-3.5 w-3.5" aria-hidden="true" />
                  {t('adminRoleHolders', { count: role.userCount })}
                </li>
                <li className="inline-flex items-center gap-1.5">
                  <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  {t('adminRoleGrants', { count: role.permissions.length })}
                </li>
                <li className="inline-flex items-center gap-1.5">
                  {t('adminRoleRank')}
                  <span className="font-mono text-ink">{role.rank}</span>
                </li>
              </ul>

            </div>

            <div className="mt-4 flex items-center gap-2">
              <button
                type="button"
                onClick={() => setSelectedId(selectedId === role.id ? null : role.id)}
                aria-expanded={selectedId === role.id}
                className="inline-flex items-center gap-1.5 rounded-lg border border-hairline px-2.5 py-1.5 text-[11px] text-ink transition-colors hover:bg-raised"
              >
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                {t('adminRolePermissions')}
              </button>
            </div>
          </li>
        ))}
      </ul>

      {selected ? (
        <RolePanel
          key={selected.id}
          role={selected}
          groups={groups}
          canManage={canManage}
          onChanged={() => roles.reload()}
          onNotice={setNotice}
        />
      ) : null}
    </div>
  );
};

/** The catalogue, grouped in the order the API publishes it — the grouping is the server's, not this file's. */
function groupCatalog(items: PermissionSummary[]): { group: string; items: PermissionSummary[] }[] {
  const groups: { group: string; items: PermissionSummary[] }[] = [];

  for (const item of items) {
    const existing = groups.find((candidate) => candidate.group === item.group);
    if (existing) {
      existing.items.push(item);
    } else {
      groups.push({ group: item.group, items: [item] });
    }
  }

  return groups;
}

/**
 * The catalogue as checkboxes. The code is what travels; the labels are the client's and the API's own name and
 * description are the fallback, so a permission added by a later build is readable before this client knows it.
 */
const PermissionPicker: React.FC<{
  groups: { group: string; items: PermissionSummary[] }[];
  selected: Set<string>;
  onToggle: (code: string) => void;
  disabled?: boolean;
}> = ({ groups, selected, onToggle, disabled = false }) => {
  const { t } = useI18n();

  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <fieldset key={group.group}>
          <legend className="text-[11px] font-medium uppercase tracking-wider text-muted">
            {permissionGroupLabel(t, group.group)}
          </legend>
          <div className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-2">
            {group.items.map((permission) => (
              <label
                key={permission.code}
                className={`flex items-start gap-2 rounded-lg border p-2.5 text-[11px] ${
                  selected.has(permission.code) ? 'border-brand/40 bg-brand/5' : 'border-hairline'
                }`}
              >
                <input
                  type="checkbox"
                  checked={selected.has(permission.code)}
                  disabled={disabled}
                  onChange={() => onToggle(permission.code)}
                  className="mt-0.5 h-3.5 w-3.5 accent-brand"
                />
                <span className="min-w-0">
                  <span className="block font-medium text-ink">
                    {permissionLabel(t, permission.code, permission.name)}
                  </span>
                  <span className="block text-muted">
                    {permissionDescription(t, permission.code, permission.description)}
                  </span>
                  <span className="mt-0.5 block font-mono text-muted">{permission.code}</span>
                  {permission.crossesAccounts ? (
                    <span className="mt-1 inline-block rounded-full border border-warning/40 px-2 py-0.5 text-warning">
                      {t('adminRoleCrossAccount')}
                    </span>
                  ) : null}
                </span>
              </label>
            ))}
          </div>
        </fieldset>
      ))}
    </div>
  );
};

/** Body of `POST /admin/roles`. A custom role starts below the shipped four unless an administrator says otherwise. */
const CreateRolePanel: React.FC<{
  groups: { group: string; items: PermissionSummary[] }[];
  onCreated: () => void;
  onCancel: () => void;
}> = ({ groups, onCreated, onCancel }) => {
  const { t } = useI18n();
  const [code, setCode] = useState('');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [rank, setRank] = useState('100');
  const [granted, setGranted] = useState<Set<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const toggle = (permission: string) =>
    setGranted((current) => {
      const next = new Set(current);
      if (next.has(permission)) {
        next.delete(permission);
      } else {
        next.add(permission);
      }
      return next;
    });

  const inputClass = (field: string) =>
    `mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline'
    }`;

  return (
    <form
      onSubmit={(event) => {
        event.preventDefault();
        setBusy(true);
        setError(null);
        void api
          .createRole({
            code,
            name,
            description,
            permissions: [...granted],
            rank: rank === '' ? null : Number(rank),
          })
          .then(() => {
            setCode('');
            setName('');
            setDescription('');
            setGranted(new Set());
            onCreated();
          })
          .catch((cause: unknown) => {
            setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
          })
          .finally(() => setBusy(false));
      }}
      className="space-y-4 rounded-2xl border border-hairline bg-surface p-5"
    >
      <h2 className="font-heading text-base font-semibold text-ink">{t('adminRoleNew')}</h2>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label htmlFor="role-code" className="text-[11px] text-muted">
            {t('adminRoleCode')}
          </label>
          <input
            id="role-code"
            value={code}
            onChange={(event) => setCode(event.target.value)}
            required
            className={inputClass('code')}
          />
          <p className="mt-1 text-[11px] text-muted">{t('adminFormRequired')}</p>
          {fieldMessage(t, error, 'code') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'code')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="role-name" className="text-[11px] text-muted">
            {t('adminRoleName')}
          </label>
          <input
            id="role-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
            className={inputClass('name')}
          />
          {fieldMessage(t, error, 'name') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'name')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="role-description" className="text-[11px] text-muted">
            {t('adminRoleDescription')}
          </label>
          <input
            id="role-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            className={inputClass('description')}
          />
          {fieldMessage(t, error, 'description') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'description')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="role-rank" className="text-[11px] text-muted">
            {t('adminRoleRank')}
          </label>
          <input
            id="role-rank"
            type="number"
            value={rank}
            onChange={(event) => setRank(event.target.value)}
            className={inputClass('rank')}
          />
          <p className="mt-1 text-[11px] text-muted">{t('adminRoleRankHint')}</p>
        </div>
      </div>

      <PermissionPicker groups={groups} selected={granted} onToggle={toggle} />

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
            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
          )}
          <span>{busy ? t('creating') : t('create')}</span>
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

/**
 * One role's grants, edited as a set. `permissions` replaces what the role holds — granting is checking a box and
 * revoking is clearing one — because a merge would make a permission impossible to take away.
 */
const RolePanel: React.FC<{
  role: RoleSummary;
  groups: { group: string; items: PermissionSummary[] }[];
  canManage: boolean;
  onChanged: () => void;
  onNotice: (notice: string) => void;
}> = ({ role, groups, canManage, onChanged, onNotice }) => {
  const { t } = useI18n();
  const [name, setName] = useState(role.name);
  const [description, setDescription] = useState(role.description);
  const [rank, setRank] = useState(String(role.rank));
  const [granted, setGranted] = useState<Set<string>>(new Set(role.permissions));
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const toggle = (permission: string) =>
    setGranted((current) => {
      const next = new Set(current);
      if (next.has(permission)) {
        next.delete(permission);
      } else {
        next.add(permission);
      }
      return next;
    });

  // A no-op writes nothing server-side and records no audit row, so the button stays disabled until something
  // actually differs — the set comparison is on codes, which is the vocabulary the write sends.
  const changed =
    name !== role.name ||
    description !== role.description ||
    rank !== String(role.rank) ||
    granted.size !== role.permissions.length ||
    role.permissions.some((code) => !granted.has(code));

  const run = async (action: () => Promise<unknown>, notice: string) => {
    setBusy(true);
    setError(null);
    try {
      await action();
      onNotice(notice);
      onChanged();
    } catch (cause) {
      setError(cause instanceof ApiError ? cause : new ApiError('unknown', 0, null));
    } finally {
      setBusy(false);
    }
  };

  const inputClass = (field: string) =>
    `mt-1 w-full rounded-lg border bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand ${
      fieldHasError(error, field) ? 'border-critical' : 'border-hairline'
    }`;

  return (
    <section className="space-y-4 rounded-2xl border border-hairline bg-surface p-5">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-heading text-base font-semibold text-ink">{role.name}</h2>
          <p className="mt-0.5 font-mono text-[11px] text-muted">{role.code}</p>
        </div>
        <span
          className={`rounded-full border px-2.5 py-1 text-[11px] ${
            role.isSystem ? 'border-brand/40 text-brand' : 'border-hairline text-muted'
          }`}
        >
          {role.isSystem ? t('adminRoleSystem') : t('adminRoleCustom')}
        </span>
      </header>

      {role.isSystem ? <p className="text-[11px] text-muted">{t('adminErrorSystemRole')}</p> : null}

      {error ? (
        <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          <p>{describeError(t, error)}</p>
          {fieldMessages(t, error.errors).map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

      {!canManage ? <p className="text-[11px] text-muted">{t('adminReadOnlyHint')}</p> : null}

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div>
          <label htmlFor={`role-${role.id}-name`} className="text-[11px] text-muted">
            {t('adminRoleName')}
          </label>
          <input
            id={`role-${role.id}-name`}
            value={name}
            disabled={!canManage}
            onChange={(event) => setName(event.target.value)}
            className={inputClass('name')}
          />
          {fieldMessage(t, error, 'name') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'name')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor={`role-${role.id}-description`} className="text-[11px] text-muted">
            {t('adminRoleDescription')}
          </label>
          <input
            id={`role-${role.id}-description`}
            value={description}
            disabled={!canManage}
            onChange={(event) => setDescription(event.target.value)}
            className={inputClass('description')}
          />
          {fieldMessage(t, error, 'description') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'description')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor={`role-${role.id}-rank`} className="text-[11px] text-muted">
            {t('adminRoleRank')}
          </label>
          <input
            id={`role-${role.id}-rank`}
            type="number"
            value={rank}
            disabled={!canManage}
            onChange={(event) => setRank(event.target.value)}
            className={inputClass('rank')}
          />
        </div>
      </div>

      <PermissionPicker groups={groups} selected={granted} onToggle={toggle} disabled={!canManage} />

      <p className="text-[11px] text-muted">{t('adminRoleCrossCaption')}</p>

      <p className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning/10 px-2.5 py-2 text-[11px] text-ink">
        <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        {t('adminRoleRevokeNote')}
      </p>

      {canManage ? (
        <div className="flex flex-wrap items-center gap-2 border-t border-hairline pt-4">
          <button
            type="button"
            disabled={busy || !changed}
            onClick={() =>
              void run(
                () =>
                  api.updateRole(role.id, {
                    name,
                    description,
                    permissions: [...granted],
                    rank: rank === '' ? null : Number(rank),
                  }),
                'adminRoleSaved'
              )
            }
            className="inline-flex items-center gap-2 rounded-lg bg-brand px-3 py-2 text-xs font-medium text-white transition-colors hover:bg-brand-dark disabled:opacity-50"
          >
            {busy ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
            ) : (
              <Save className="h-3.5 w-3.5" aria-hidden="true" />
            )}
            <span>{busy ? t('saving') : t('save')}</span>
          </button>

          {/* Only where it can succeed: a shipped role and a role accounts still hold are both refused. */}
          {!role.isSystem && role.userCount === 0 ? (
            confirmDelete ? (
              <span className="flex items-center gap-2">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void run(() => api.deleteRole(role.id), 'adminRoleDeleted')}
                  className="inline-flex items-center gap-2 rounded-lg bg-critical px-3 py-2 text-xs font-medium text-white transition-colors hover:opacity-90 disabled:opacity-70"
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  {t('adminRoleDeleteTitle', { name: role.name })}
                </button>
                <button
                  type="button"
                  onClick={() => setConfirmDelete(false)}
                  className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised"
                >
                  {t('cancel')}
                </button>
              </span>
            ) : (
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                className="inline-flex items-center gap-2 rounded-lg border border-critical/40 px-3 py-2 text-xs font-medium text-critical transition-colors hover:bg-critical/10"
              >
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                {t('adminRoleDelete')}
              </button>
            )
          ) : (
            <p className="text-[11px] text-muted">
              {role.isSystem
                ? t('adminErrorSystemRole')
                : t('adminRoleDeleteHint')}
            </p>
          )}
        </div>
      ) : null}
    </section>
  );
};
