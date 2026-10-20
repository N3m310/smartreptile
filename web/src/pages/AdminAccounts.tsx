import React, { useState } from 'react';
import { AlertTriangle, KeyRound, Loader2, Plus, Save, ShieldCheck, Trash2, UserCog, UserX } from 'lucide-react';
import * as api from '../api/endpoints';
import { ApiError } from '../api/client';
import { PERMISSION_CODES } from '../api/types';
import type { RoleSummary, UserPage, UserSummary } from '../api/types';
import { ADMIN_PAGE_SIZE, Pager, SearchField, useDebounced } from '../components/AdminList';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDate, formatDateTime } from '../lib/format';
import { describeError, fieldHasError, fieldMessage, fieldMessages } from '../lib/messages';
import { permissionLabel, severityLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';

/**
 * Account management (FR-18) — `/api/v1/users`, gated on `user.read` for the reads and `user.manage` for every
 * write. A deployment that wants an auditor account can grant the first without the second, which is why the two
 * are separate routes and why this screen hides what the caller cannot do rather than offering it and being
 * refused.
 *
 * Two refusals are the API's own rules and the screen is built around them. **The caller's own account** cannot be
 * edited, re-roled, disabled or deleted (`409 self_change_refused`), because locking yourself out is not a feature;
 * the panel for it therefore shows the account and says so instead of rendering five buttons whose only outcome is
 * a refusal. **The last administrator** cannot be stripped of `user.manage` (`409 last_administrator_refused`),
 * which depends on every other account rather than on this one — so that rule cannot be pre-checked here and its
 * sentence is rendered from the code when the server refuses.
 *
 * A delete is a choice of two, not one: the soft delete disables the account and keeps every enclosure, alert and
 * audit row that names it, and the hard delete erases the row and is refused while anything still references it.
 * The form asks which, because "remove this account" means the first one to an auditor and the second to whoever
 * mistyped a username.
 */

const SEVERITIES = ['info', 'warning', 'critical'];

/** The three severity codes as `AlertSeverity` spells them, so the label lookup is the vocabulary one. */
const SEVERITY_ENUM: Record<string, string> = {
  info: 'Info',
  warning: 'Warning',
  critical: 'Critical',
};

export const AdminAccounts: React.FC = () => {
  const { t, lang } = useI18n();
  const { user, hasPermission } = useSession();
  const canManage = hasPermission(PERMISSION_CODES.userManage);

  const [search, setSearch] = useState('');
  const [skip, setSkip] = useState(0);
  const applied = useDebounced(search, 300);

  const accounts = usePolled<UserPage>(
    () => api.listUsers({ search: applied, skip, take: ADMIN_PAGE_SIZE }),
    null,
    [applied, skip]
  );
  // The role list is read once: it is what the create form and the role change are driven from, and it changes
  // only on the roles screen.
  const roles = usePolled<{ items: RoleSummary[] }>(() => api.listRoles(), null);

  const items = accounts.data?.items ?? [];
  const [createOpen, setCreateOpen] = useState(false);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const selected = items.find((item) => item.id === selectedId) ?? null;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('adminAccountsTitle')}</h1>
          <p className="mt-1 max-w-3xl text-xs text-muted">{t('adminSubtitle')}</p>
        </div>
        {canManage ? (
          <button
            type="button"
            onClick={() => setCreateOpen((open) => !open)}
            aria-expanded={createOpen}
            className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            <span>{t('adminAccountNew')}</span>
          </button>
        ) : null}
      </header>

      {notice ? (
        <p role="status" className="rounded-lg border border-brand/40 bg-brand/10 px-3 py-2 text-[11px] text-ink">
          {t(notice)}
        </p>
      ) : null}

      {createOpen && canManage ? (
        <CreateAccountPanel
          roles={roles.data?.items ?? []}
          onCreated={() => {
            setNotice('adminAccountCreated');
            accounts.reload();
          }}
          onCancel={() => setCreateOpen(false)}
        />
      ) : null}

      <SearchField
        value={search}
        onChange={(value) => {
          setSearch(value);
          setSkip(0);
        }}
        label={t('adminAccountsSearch')}
        placeholder={t('adminAccountsSearch')}
      />

      {accounts.error?.isNetworkFailure ? <UnreachableBanner onRetry={() => accounts.reload()} /> : null}
      {!accounts.error?.isNetworkFailure && accounts.error ? (
        <ErrorPanel error={accounts.error} onRetry={() => accounts.reload()} />
      ) : null}
      {accounts.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {!accounts.loading && items.length === 0 && !accounts.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('adminRollupEmpty')} />
      ) : null}

      {items.length > 0 ? (
        <>
          <div className="overflow-x-auto rounded-2xl border border-hairline bg-surface">
            <table className="w-full min-w-[46rem] text-left text-[11px]">
              <thead className="border-b border-hairline text-muted">
                <tr>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminColumnAccount')}</th>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminColumnRole')}</th>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminColumnStatus')}</th>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminColumnCreated')}</th>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminColumnLastLogin')}</th>
                  <th className="whitespace-nowrap px-4 py-3 font-semibold">{t('adminAccountActions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hairline">
                {items.map((account) => (
                  <tr key={account.id} className="hover:bg-raised/40">
                    <td className="px-4 py-3 align-top text-ink">
                      <span className="block font-semibold text-ink">{account.username}</span>
                      <span className="block text-[11px] text-muted">{account.email}</span>
                    </td>
                    <td className="px-4 py-3 align-top font-mono text-ink">{account.role}</td>
                    <td className="px-4 py-3 align-top">
                      <span
                        className={`text-[11px] font-semibold ${
                          account.isActive ? 'text-inrange' : 'text-critical'
                        }`}
                      >
                        {account.isActive ? t('adminBadgeActive') : t('adminBadgeDisabled')}
                      </span>
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 align-top text-muted">
                      {formatDate(account.createdAt, lang)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 align-top text-muted">
                      {formatDateTime(account.lastLoginAt, lang)}
                    </td>
                    <td className="whitespace-nowrap px-4 py-3 align-top">
                      <button
                        type="button"
                        onClick={() => setSelectedId(selectedId === account.id ? null : account.id)}
                        aria-expanded={selectedId === account.id}
                        className="inline-flex items-center gap-1.5 rounded-lg border border-hairline px-2.5 py-1.5 text-[11px] text-ink transition-colors hover:bg-raised"
                      >
                        <UserCog className="h-3.5 w-3.5" aria-hidden="true" />
                        {t('adminAccountEdit')}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager total={accounts.data?.total ?? 0} skip={accounts.data?.skip ?? 0} take={accounts.data?.take ?? 0} onSkip={setSkip} />
        </>
      ) : null}

      {selected ? (
        <AccountPanel
          key={selected.id}
          account={selected}
          roles={roles.data?.items ?? []}
          canManage={canManage}
          isSelf={selected.id === user?.id}
          onChanged={() => accounts.reload()}
          onNotice={setNotice}
        />
      ) : null}
    </div>
  );
};

/** Body of `POST /users`. The password box is the administrator's because this deployment has no delivery channel. */
const CreateAccountPanel: React.FC<{
  roles: RoleSummary[];
  onCreated: () => void;
  onCancel: () => void;
}> = ({ roles, onCreated, onCancel }) => {
  const { t } = useI18n();
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState('Owner');
  const [preferredLanguage, setPreferredLanguage] = useState('vi');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);

    try {
      await api.createUser({ username, email, password, role, preferredLanguage });
      setUsername('');
      setEmail('');
      setPassword('');
      onCreated();
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
    <form onSubmit={submit} className="space-y-3 rounded-2xl border border-hairline bg-surface p-5">
      <h2 className="font-heading text-base font-semibold text-ink">{t('adminAccountNew')}</h2>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <div>
          <label htmlFor="account-username" className="text-[11px] text-muted">
            {t('username')}
          </label>
          <input
            id="account-username"
            value={username}
            onChange={(event) => setUsername(event.target.value)}
            required
            autoComplete="off"
            className={inputClass('username')}
          />
          {fieldMessage(t, error, 'username') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'username')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="account-email" className="text-[11px] text-muted">
            {t('email')}
          </label>
          <input
            id="account-email"
            type="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
            autoComplete="off"
            className={inputClass('email')}
          />
          {fieldMessage(t, error, 'email') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'email')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="account-password" className="text-[11px] text-muted">
            {t('password')}
          </label>
          <input
            id="account-password"
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
            autoComplete="new-password"
            className={inputClass('password')}
          />
          <p className="mt-1 text-[11px] text-muted">{t('passwordHint')}</p>
        </div>

        <div>
          <label htmlFor="account-role" className="text-[11px] text-muted">
            {t('adminFormRole')}
          </label>
          <select
            id="account-role"
            value={role}
            onChange={(event) => setRole(event.target.value)}
            className={inputClass('role')}
          >
            {roles.map((option) => (
              <option key={option.id} value={option.code}>
                {`${option.code} — ${option.name}`}
              </option>
            ))}
          </select>
          {fieldMessage(t, error, 'role') ? (
            <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'role')}</p>
          ) : null}
        </div>

        <div>
          <label htmlFor="account-language" className="text-[11px] text-muted">
            {t('language')}
          </label>
          <select
            id="account-language"
            value={preferredLanguage}
            onChange={(event) => setPreferredLanguage(event.target.value)}
            className={inputClass('preferredLanguage')}
          >
            <option value="vi">{t('languageVi')}</option>
            <option value="en">{t('languageEn')}</option>
          </select>
        </div>
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
 * Everything that can be done to one account, in one panel. One panel rather than a row of dialogs, because the
 * answers to "what does this account hold" and "what may I do to it" belong side by side.
 */
const AccountPanel: React.FC<{
  account: UserSummary;
  roles: RoleSummary[];
  canManage: boolean;
  isSelf: boolean;
  onChanged: () => void;
  onNotice: (notice: string) => void;
}> = ({ account, roles, canManage, isSelf, onChanged, onNotice }) => {
  const { t, lang } = useI18n();
  const [username, setUsername] = useState(account.username);
  const [email, setEmail] = useState(account.email);
  const [preferredLanguage, setPreferredLanguage] = useState(account.preferredLanguage);
  const [minNotifySeverity, setMinNotifySeverity] = useState(account.minNotifySeverity);
  const [quietHoursStart, setQuietHoursStart] = useState(account.quietHoursStart ?? '');
  const [quietHoursEnd, setQuietHoursEnd] = useState(account.quietHoursEnd ?? '');
  const [role, setRole] = useState(account.role);
  const [newPassword, setNewPassword] = useState('');
  const [purge, setPurge] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

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
          <h2 className="font-heading text-base font-semibold text-ink">{account.username}</h2>
          <p className="mt-0.5 text-[11px] text-muted">{account.email}</p>
        </div>
        <span className={`text-[11px] font-semibold ${account.isActive ? 'text-inrange' : 'text-critical'}`}>
          {account.isActive ? t('adminBadgeActive') : t('adminBadgeDisabled')}
        </span>
      </header>

      <dl className="grid grid-cols-1 gap-2 text-[11px] sm:grid-cols-3">
        <Detail label={t('adminFormRole')} value={account.role} />
        <Detail label={t('adminFormTimeZone')} value={account.timeZoneId} />
        <Detail label={t('adminColumnCreated')} value={formatDate(account.createdAt, lang)} />
        <Detail label={t('adminColumnLastLogin')} value={formatDateTime(account.lastLoginAt, lang)} />
        <Detail label={t('adminBadgeDisabled')} value={formatDateTime(account.disabledAt, lang)} />
      </dl>

      <div>
        <h3 className="text-[11px] font-medium text-muted">{t('adminRolePermissions')}</h3>
        <ul className="mt-1.5 flex flex-wrap gap-1.5">
          {account.permissions.map((code) => (
            <li key={code} className="rounded-full border border-hairline px-2.5 py-1 text-[11px] text-ink">
              {permissionLabel(t, code)}
            </li>
          ))}
        </ul>
      </div>

      {error ? (
        <div className="rounded-lg border border-critical/40 bg-critical/10 px-2.5 py-2 text-[11px] text-ink">
          <p>{describeError(t, error)}</p>
          {fieldMessages(t, error.errors).map((message) => (
            <p key={message}>{message}</p>
          ))}
        </div>
      ) : null}

      {!canManage ? (
        <p className="flex items-start gap-2 rounded-lg border border-hairline px-2.5 py-2 text-[11px] text-muted">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          {t('adminReadOnlyHint')}
        </p>
      ) : null}

      {/* The API refuses every write against the caller's own account, so the panel does not offer one. */}
      {canManage && isSelf ? (
        <p className="flex items-start gap-2 rounded-lg border border-warning/40 bg-warning/10 px-2.5 py-2 text-[11px] text-ink">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          {t('adminErrorSelfChange')}
        </p>
      ) : null}

      {canManage && !isSelf ? (
        <>
          <form
            onSubmit={(event) => {
              event.preventDefault();
              void run(
                () =>
                  api.updateUser(account.id, {
                    username,
                    email,
                    preferredLanguage,
                    minNotifySeverity,
                    // Both ends always travel: they are a pair, and an empty one clears the window rather than
                    // leaving the other end alone (`400 quiet_hours_incomplete` otherwise).
                    quietHoursStart,
                    quietHoursEnd,
                  }),
                'adminAccountSaved'
              );
            }}
            className="space-y-3 border-t border-hairline pt-4"
          >
            <h3 className="text-[11px] font-medium text-muted">{t('adminAccountEdit')}</h3>

            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div>
                <label htmlFor={`account-${account.id}-username`} className="text-[11px] text-muted">
                  {t('username')}
                </label>
                <input
                  id={`account-${account.id}-username`}
                  value={username}
                  onChange={(event) => setUsername(event.target.value)}
                  className={inputClass('username')}
                />
                {fieldMessage(t, error, 'username') ? (
                  <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'username')}</p>
                ) : null}
              </div>

              <div>
                <label htmlFor={`account-${account.id}-email`} className="text-[11px] text-muted">
                  {t('email')}
                </label>
                <input
                  id={`account-${account.id}-email`}
                  type="email"
                  value={email}
                  onChange={(event) => setEmail(event.target.value)}
                  className={inputClass('email')}
                />
                {fieldMessage(t, error, 'email') ? (
                  <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'email')}</p>
                ) : null}
              </div>

              <div>
                <label htmlFor={`account-${account.id}-language`} className="text-[11px] text-muted">
                  {t('language')}
                </label>
                <select
                  id={`account-${account.id}-language`}
                  value={preferredLanguage}
                  onChange={(event) => setPreferredLanguage(event.target.value)}
                  className={inputClass('preferredLanguage')}
                >
                  <option value="vi">{t('languageVi')}</option>
                  <option value="en">{t('languageEn')}</option>
                </select>
              </div>

              <div>
                <label htmlFor={`account-${account.id}-severity`} className="text-[11px] text-muted">
                  {t('adminFormMinNotifySeverity')}
                </label>
                <select
                  id={`account-${account.id}-severity`}
                  value={minNotifySeverity}
                  onChange={(event) => setMinNotifySeverity(event.target.value)}
                  className={inputClass('minNotifySeverity')}
                >
                  {SEVERITIES.map((severity) => (
                    <option key={severity} value={severity}>
                      {severityLabel(t, SEVERITY_ENUM[severity])}
                    </option>
                  ))}
                </select>
                {fieldMessage(t, error, 'minNotifySeverity') ? (
                  <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'minNotifySeverity')}</p>
                ) : null}
              </div>

              <div>
                <label htmlFor={`account-${account.id}-quiet-start`} className="text-[11px] text-muted">
                  {t('adminFormQuietHoursStart')}
                </label>
                <input
                  id={`account-${account.id}-quiet-start`}
                  type="time"
                  value={quietHoursStart}
                  onChange={(event) => setQuietHoursStart(event.target.value)}
                  className={inputClass('quietHoursStart')}
                />
                {fieldMessage(t, error, 'quietHoursStart') ? (
                  <p className="mt-1 text-[11px] text-critical">{fieldMessage(t, error, 'quietHoursStart')}</p>
                ) : null}
              </div>

              <div>
                <label htmlFor={`account-${account.id}-quiet-end`} className="text-[11px] text-muted">
                  {t('adminFormQuietHoursEnd')}
                </label>
                <input
                  id={`account-${account.id}-quiet-end`}
                  type="time"
                  value={quietHoursEnd}
                  onChange={(event) => setQuietHoursEnd(event.target.value)}
                  className={inputClass('quietHoursEnd')}
                />
              </div>
            </div>

            <p className="text-[11px] text-muted">{t('adminFormQuietHoursHint')}</p>

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
          </form>

          <form
            onSubmit={(event) => {
              event.preventDefault();
              void run(() => api.setUserRole(account.id, role), 'adminRoleAssigned');
            }}
            className="space-y-2 border-t border-hairline pt-4"
          >
            <h3 className="text-[11px] font-medium text-muted">{t('adminAccountChangeRole')}</h3>
            <div className="flex flex-wrap items-end gap-2">
              <label htmlFor={`account-${account.id}-role`} className="text-[11px] text-muted">
                <span className="mb-1 block">{t('adminFormRole')}</span>
                <select
                  id={`account-${account.id}-role`}
                  value={role}
                  onChange={(event) => setRole(event.target.value)}
                  className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
                >
                  {roles.map((option) => (
                    <option key={option.id} value={option.code}>
                      {`${option.code} — ${option.name}`}
                    </option>
                  ))}
                </select>
              </label>
              <button
                type="submit"
                disabled={busy || role === account.role}
                className="inline-flex items-center gap-2 rounded-lg border border-hairline px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised disabled:opacity-50"
              >
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
                {t('adminAccountChangeRole')}
              </button>
            </div>
            <p className="text-[11px] text-muted">{t('adminAccountRoleChangeNote')}</p>
          </form>

          <form
            onSubmit={(event) => {
              event.preventDefault();
              void run(
                async () => {
                  await api.resetUserPassword(account.id, newPassword);
                  setNewPassword('');
                },
                'adminPasswordReset'
              );
            }}
            className="space-y-2 border-t border-hairline pt-4"
          >
            <h3 className="text-[11px] font-medium text-muted">{t('adminAccountResetPassword')}</h3>
            <div className="flex flex-wrap items-end gap-2">
              <label htmlFor={`account-${account.id}-new-password`} className="text-[11px] text-muted">
                <span className="mb-1 block">{t('adminNewPassword')}</span>
                <input
                  id={`account-${account.id}-new-password`}
                  type="password"
                  value={newPassword}
                  onChange={(event) => setNewPassword(event.target.value)}
                  required
                  autoComplete="new-password"
                  className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
                />
              </label>
              <button
                type="submit"
                disabled={busy || newPassword === ''}
                className="inline-flex items-center gap-2 rounded-lg border border-hairline px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised disabled:opacity-50"
              >
                <KeyRound className="h-3.5 w-3.5" aria-hidden="true" />
                {t('adminAccountResetPassword')}
              </button>
            </div>
          </form>

          <div className="space-y-2 border-t border-hairline pt-4">
            <h3 className="text-[11px] font-medium text-muted">{t('adminColumnState')}</h3>
            <button
              type="button"
              disabled={busy}
              onClick={() =>
                void run(
                  () => api.setUserDisabled(account.id, account.isActive),
                  account.isActive ? 'adminAccountDisabled' : 'adminAccountEnabled'
                )
              }
              className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-medium transition-colors hover:bg-raised disabled:opacity-50 ${
                account.isActive ? 'border-warning/40 text-warning' : 'border-hairline text-ink'
              }`}
            >
              {account.isActive ? (
                <UserX className="h-3.5 w-3.5" aria-hidden="true" />
              ) : (
                <ShieldCheck className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              {account.isActive ? t('adminAccountDisable') : t('adminAccountEnable')}
            </button>
          </div>

          <div className="space-y-2 border-t border-hairline pt-4">
            <h3 className="text-[11px] font-medium text-muted">{t('adminAccountDelete')}</h3>

            {/* Two modes rather than one with a flag: "remove this account" means "keep the history" to an
                auditor and "erase the row" to whoever mistyped a username, and the API behaves differently. */}
            <div className="space-y-1.5">
              <label className="flex items-start gap-2 text-[11px] text-ink">
                <input
                  type="radio"
                  name={`delete-mode-${account.id}`}
                  checked={!purge}
                  onChange={() => setPurge(false)}
                  className="mt-0.5 h-3.5 w-3.5 accent-brand"
                />
                <span>
                  <span className="block">{t('adminAccountDeleteSoft')}</span>
                  <span className="block text-muted">{t('adminAccountDeleteSoftHint')}</span>
                </span>
              </label>
              <label className="flex items-start gap-2 text-[11px] text-ink">
                <input
                  type="radio"
                  name={`delete-mode-${account.id}`}
                  checked={purge}
                  onChange={() => setPurge(true)}
                  className="mt-0.5 h-3.5 w-3.5 accent-brand"
                />
                <span>
                  <span className="block">{t('adminAccountDeleteHard')}</span>
                  <span className="block text-muted">{t('adminAccountDeleteHardHint')}</span>
                </span>
              </label>
            </div>

            {confirmDelete ? (
              <div className="flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  disabled={busy}
                  onClick={() => void run(() => api.deleteUser(account.id, purge), 'adminAccountRemoved')}
                  className="inline-flex items-center gap-2 rounded-lg bg-critical px-3 py-2 text-xs font-medium text-white transition-colors hover:opacity-90 disabled:opacity-70"
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                  {t('adminAccountDeleteTitle', { username: account.username })}
                </button>
                <button
                  type="button"
                  onClick={() => setConfirmDelete(false)}
                  className="rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised"
                >
                  {t('cancel')}
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                className="inline-flex items-center gap-2 rounded-lg border border-critical/40 px-3 py-2 text-xs font-medium text-critical transition-colors hover:bg-critical/10"
              >
                <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                {t('adminAccountDelete')}
              </button>
            )}
          </div>
        </>
      ) : null}
    </section>
  );
};

/** One read-only fact, named and valued — the panel states what the server said rather than paraphrasing it. */
const Detail: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div>
    <dt className="text-muted">{label}</dt>
    <dd className="text-ink">{value}</dd>
  </div>
);
