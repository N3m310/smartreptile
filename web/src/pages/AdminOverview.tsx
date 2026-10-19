import React, { useState } from 'react';
import { AlertTriangle, Box, Cpu, Download, Leaf, RefreshCw, Users } from 'lucide-react';
import * as api from '../api/endpoints';
import type {
  AdminAlertView,
  AdminDeviceView,
  AdminOverview as AdminOverviewData,
  AdminPage,
  AdminTerrariumView,
} from '../api/types';
import { ADMIN_PAGE_SIZE, Pager, SearchField, useDebounced } from '../components/AdminList';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import type { Language } from '../i18n/strings';
import { formatDate, formatDateTime, formatRelative, metricLabel } from '../lib/format';
import {
  alertFamilyLabel,
  alertSourceLabel,
  alertStateLabel,
  alertStateTextClass,
  deviceLifecycleLabel,
  deviceLifecycleTextClass,
  severityLabel,
  severityTextClass,
  terrariumStatusLabel,
} from '../lib/status';
import { usePolled } from '../lib/usePolled';

/**
 * The administrator's get-all dashboard (FR-18) — `/api/v1/admin/*`, every route of which names `data.read.any`.
 *
 * Three things are on this screen and they answer three different questions. The header answers "how much is
 * there"; the families answer "what is wrong, and of which kind"; the get-all tables answer "who has what". All
 * three render the API's own figures — the header is one `overview` call rather than eight list calls, and nothing
 * here counts anything itself, because a number the client computed is a number that can disagree with the one on
 * the other screen.
 *
 * **The two families are kept apart because the product keeps them apart.** `enclosure` holds humidity, light and
 * the board's own silences and faults — what the enclosure and its hardware answer for; `species` holds temperature
 * and UV, judged against the species profile. The metric keys of each family arrive with it, so this screen labels
 * them without carrying a list of its own that could fall out of date.
 *
 * The three paged lists load one at a time, with the tab that shows them: a console that fetched every table to
 * render one of them would make the dashboard's first paint wait for data nobody asked to see.
 */

const POLL_MS = 60_000;

/** The get-all tables, one tab each — the lists `/api/v1/admin/*` publishes beside the overview. */
type Tab = 'accounts' | 'terrariums' | 'devices' | 'alerts';

/** Each tab's label, so the four tabs are one list rather than four conditionals. */
const TAB_LABEL_KEY: Record<Tab, string> = {
  accounts: 'adminAccountsEntry',
  terrariums: 'adminTabTerrariums',
  devices: 'adminTabDevices',
  alerts: 'adminTabAlerts',
};

/** One pane's current question: its filter and how far down the list it has paged. */
interface Pane {
  search: string;
  skip: number;
}

const EMPTY_PANE: Pane = { search: '', skip: 0 };

/** The table skin, so four lists cannot disagree about a header, a hairline or a cell's padding. */
const TABLE_WRAP = 'overflow-x-auto rounded-2xl border border-hairline bg-surface';
const TABLE = 'w-full min-w-[46rem] text-left text-[11px]';
const TH = 'whitespace-nowrap px-4 py-3 font-semibold';
const TD = 'px-4 py-3 align-top text-ink';

export const AdminOverview: React.FC = () => {
  const { t, lang } = useI18n();
  const overview = usePolled<AdminOverviewData>(() => api.adminOverview(), POLL_MS);
  const counts = overview.data?.counts ?? null;
  const families = overview.data?.families ?? [];

  const [tab, setTab] = useState<Tab>('accounts');
  // Every pane keeps its own filter and position: a search that survived a look at another table is the
  // difference between a console and four screens in a trench coat.
  const [enclosures, setEnclosures] = useState<Pane>(EMPTY_PANE);
  const [boards, setBoards] = useState<Pane>(EMPTY_PANE);
  const [alerts, setAlerts] = useState<{ family: string; state: string; skip: number }>({
    family: '',
    state: '',
    skip: 0,
  });

  const networkProblem = overview.error?.isNetworkFailure;

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="font-heading text-2xl font-bold text-ink">{t('adminOverviewEntry')}</h1>
          <p className="mt-1 max-w-3xl text-xs text-muted">{t('adminSubtitle')}</p>
        </div>
        <button
          type="button"
          onClick={() => overview.reload()}
          className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised"
        >
          <RefreshCw className="h-4 w-4" aria-hidden="true" />
          {t('retry')}
        </button>
      </header>

      {networkProblem ? <UnreachableBanner onRetry={() => overview.reload()} /> : null}
      {!networkProblem && overview.error ? (
        <ErrorPanel error={overview.error} onRetry={() => overview.reload()} />
      ) : null}
      {overview.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {counts ? (
        <section aria-label={t('adminSectionCounts')} className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-5">
          <CountTile
            icon={<Users className="h-4 w-4 shrink-0 text-brand" aria-hidden="true" />}
            label={t('adminCountAccounts')}
            value={counts.accounts}
            details={[{ label: t('adminCountAccountsActive'), value: counts.accountsActive }]}
          />
          <CountTile
            icon={<Box className="h-4 w-4 shrink-0 text-brand" aria-hidden="true" />}
            label={t('adminCountTerrariums')}
            value={counts.terrariums}
            details={[{ label: t('adminCountTerrariumsWithDevice'), value: counts.terrariumsWithDevice }]}
          />
          <CountTile
            icon={<Cpu className="h-4 w-4 shrink-0 text-brand" aria-hidden="true" />}
            label={t('adminCountDevices')}
            value={counts.devices}
            details={[{ label: t('adminCountDevicesOnline'), value: counts.devicesOnline }]}
          />
          <CountTile
            icon={<AlertTriangle className="h-4 w-4 shrink-0 text-warning" aria-hidden="true" />}
            label={t('adminCountAlertsOpen')}
            value={counts.alertsOpen}
            details={[
              { label: alertFamilyLabel(t, 'enclosure'), value: counts.alertsOpenEnclosure },
              { label: alertFamilyLabel(t, 'species'), value: counts.alertsOpenSpecies },
              {
                label: t('adminCountAlertsCritical'),
                value: counts.alertsCritical,
                emphasis: counts.alertsCritical > 0,
              },
            ]}
          />
          <CountTile
            icon={<Leaf className="h-4 w-4 shrink-0 text-brand" aria-hidden="true" />}
            label={t('adminCountSpeciesProfiles')}
            value={counts.speciesProfiles}
            details={[]}
          />
        </section>
      ) : null}

      {families.length > 0 ? (
        <section className="space-y-3">
          <h2 className="font-heading text-lg font-semibold text-ink">{t('adminSectionFamilies')}</h2>
          <p className="max-w-3xl text-xs text-muted">{t('adminFamiliesCaption')}</p>
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            {families.map((family) => (
              <article key={family.key} className="rounded-2xl border border-hairline bg-surface p-5">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h3 className="font-heading text-base font-semibold text-ink">
                      {alertFamilyLabel(t, family.key)}
                    </h3>
                    <p className="mt-0.5 text-[11px] text-muted">
                      {family.key === 'enclosure'
                        ? t('adminFamilyEnclosureDetail')
                        : family.key === 'species'
                          ? t('adminFamilySpeciesDetail')
                          : null}
                    </p>
                  </div>
                  <span className="shrink-0 font-mono text-sm font-semibold text-warning">
                    {t('adminFamilyOpen', { count: family.alertsOpen })}
                  </span>
                </div>

                <p className="mt-3 text-[11px] text-muted">
                  {t('adminFamilyMetrics', {
                    metrics: family.metrics.map((metric) => metricLabel(t, metric)).join(', '),
                  })}
                </p>

                <button
                  type="button"
                  onClick={() => {
                    setAlerts({ family: family.key, state: 'Open', skip: 0 });
                    setTab('alerts');
                  }}
                  className="mt-4 inline-flex items-center gap-2 rounded-lg border border-hairline px-3 py-1.5 text-[11px] font-medium text-ink transition-colors hover:bg-raised"
                >
                  {t('adminTabAlerts')}
                </button>
              </article>
            ))}
          </div>
        </section>
      ) : null}

      <section className="space-y-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="font-heading text-lg font-semibold text-ink">{t('adminListsTitle')}</h2>
            <p className="mt-1 max-w-3xl text-xs text-muted">{t('adminListsEntryCaption')}</p>
          </div>

          <div className="flex flex-wrap items-center gap-1 rounded-xl border border-hairline bg-field p-1 text-[11px]">
            {(['accounts', 'terrariums', 'devices', 'alerts'] as const).map((option) => (
              <button
                key={option}
                type="button"
                onClick={() => setTab(option)}
                aria-pressed={tab === option}
                className={`rounded-lg px-3 py-1.5 font-medium transition-colors ${
                  tab === option ? 'bg-raised text-ink' : 'text-muted hover:text-ink'
                }`}
              >
                {t(TAB_LABEL_KEY[option])}
              </button>
            ))}
          </div>
        </div>

        {tab === 'accounts' ? <AccountRollupTable accounts={overview.data?.accounts ?? []} /> : null}

        {tab === 'terrariums' ? (
          <TerrariumTable
            pane={enclosures}
            onSearch={(search) => setEnclosures({ search, skip: 0 })}
            onSkip={(skip) => setEnclosures((current) => ({ ...current, skip }))}
            lang={lang}
          />
        ) : null}

        {tab === 'devices' ? (
          <DeviceTable
            pane={boards}
            onSearch={(search) => setBoards({ search, skip: 0 })}
            onSkip={(skip) => setBoards((current) => ({ ...current, skip }))}
            lang={lang}
          />
        ) : null}

        {tab === 'alerts' ? (
          <AlertTable
            query={alerts}
            onFilter={(next) => setAlerts({ ...next, skip: 0 })}
            onSkip={(skip) => setAlerts((current) => ({ ...current, skip }))}
            lang={lang}
          />
        ) : null}
      </section>
    </div>
  );
};

/**
 * One figure of the header, with the second numbers of a pair under it — active accounts under accounts, online
 * boards under boards. A header of eleven equal tiles is a header nobody reads, and a label beside its value needs
 * no sentence invented for it.
 */
const CountTile: React.FC<{
  icon: React.ReactNode;
  label: string;
  value: number;
  details: { label: string; value: number; emphasis?: boolean }[];
}> = ({ icon, label, value, details }) => (
  <article className="rounded-2xl border border-hairline bg-surface px-5 py-4">
    <header className="flex items-start justify-between gap-3">
      <h3 className="text-[11px] font-semibold uppercase tracking-wider text-muted">{label}</h3>
      {icon}
    </header>
    <p className="mt-2 font-mono text-3xl font-semibold text-ink">{value}</p>
    {details.length > 0 ? (
      <dl className="mt-1 space-y-0.5 text-[11px]">
        {details.map((detail) => (
          <div key={detail.label} className="flex items-baseline justify-between gap-2">
            <dt className="text-muted">{detail.label}</dt>
            <dd className={`font-mono ${detail.emphasis ? 'text-critical' : 'text-ink'}`}>{detail.value}</dd>
          </div>
        ))}
      </dl>
    ) : null}
  </article>
);

/**
 * The get-all table the overview call already carries: every account with its holdings. It is not paged because the
 * answer is not a page — `overview.accounts` is the whole rollup, which is what makes "who has what" one screen.
 */
const AccountRollupTable: React.FC<{ accounts: AdminOverviewData['accounts'] }> = ({ accounts }) => {
  const { t, lang } = useI18n();
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState('');
  const [stateFilter, setStateFilter] = useState('');
  const roles = [...new Set(accounts.map((account) => account.role))].sort((left, right) =>
    left.localeCompare(right)
  );
  const query = search.trim().toLocaleLowerCase();
  const filteredAccounts = accounts.filter((account) => {
    const matchesSearch =
      !query ||
      account.username.toLocaleLowerCase().includes(query) ||
      account.email.toLocaleLowerCase().includes(query);
    const matchesRole = !roleFilter || account.role === roleFilter;
    const matchesState =
      !stateFilter ||
      (stateFilter === 'active' ? account.isActive : !account.isActive);
    return matchesSearch && matchesRole && matchesState;
  });

  if (accounts.length === 0) {
    return <EmptyPanel title={t('emptyStateTitle')} body={t('adminRollupEmpty')} />;
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end gap-3">
        <SearchField
          value={search}
          onChange={setSearch}
          label={t('adminSearchAccountsRollup')}
          placeholder={t('adminSearchAccountsRollup')}
        />
        <label className="text-[11px] text-muted">
          <span className="mb-1 block">{t('adminColumnRole')}</span>
          <select
            value={roleFilter}
            onChange={(event) => setRoleFilter(event.target.value)}
            className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
          >
            <option value="">{t('adminFilterRoleAll')}</option>
            {roles.map((role) => (
              <option key={role} value={role}>{role}</option>
            ))}
          </select>
        </label>
        <label className="text-[11px] text-muted">
          <span className="mb-1 block">{t('adminColumnStatus')}</span>
          <select
            value={stateFilter}
            onChange={(event) => setStateFilter(event.target.value)}
            className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
          >
            <option value="">{t('adminFilterAccountStateAll')}</option>
            <option value="active">{t('adminBadgeActive')}</option>
            <option value="disabled">{t('adminBadgeDisabled')}</option>
          </select>
        </label>
        <button
          type="button"
          disabled={filteredAccounts.length === 0}
          onClick={() =>
            downloadAccountsCsv(
              [
                t('adminColumnUsername'),
                t('adminColumnEmail'),
                t('adminColumnRole'),
                t('adminColumnState'),
                t('adminColumnTerrariums'),
                t('adminColumnDevices'),
                t('adminColumnAlertsOpen'),
                t('adminColumnCreated'),
                t('adminColumnLastLogin'),
              ],
              filteredAccounts.map((account) => [
                account.username,
                account.email,
                account.role,
                account.isActive ? t('adminBadgeActive') : t('adminBadgeDisabled'),
                account.terrariums,
                account.devices,
                account.alertsOpen,
                formatDate(account.createdAt, lang),
                formatDateTime(account.lastLoginAt, lang),
              ])
            )
          }
          className="inline-flex items-center gap-2 rounded-lg border border-hairline bg-surface px-3 py-2 text-xs font-medium text-ink transition-colors hover:bg-raised disabled:cursor-not-allowed disabled:opacity-40"
        >
          <Download className="h-3.5 w-3.5" aria-hidden="true" />
          {t('adminExportCsv')}
        </button>
      </div>
      <p className="text-[11px] text-muted">
        {t('adminFilterResults', { count: filteredAccounts.length, total: accounts.length })}
      </p>

      {filteredAccounts.length === 0 ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('adminNoRows')} />
      ) : (
        <div className={TABLE_WRAP}>
          <table className={TABLE}>
            <thead className="border-b border-hairline text-muted">
              <tr>
                <th className={TH}>{t('adminColumnAccount')}</th>
                <th className={TH}>{t('adminColumnRole')}</th>
                <th className={TH}>{t('adminColumnState')}</th>
                <th className={TH}>{t('adminColumnTerrariums')}</th>
                <th className={TH}>{t('adminColumnDevices')}</th>
                <th className={TH}>{t('adminColumnAlertsOpen')}</th>
                <th className={TH}>{t('adminColumnCreated')}</th>
                <th className={TH}>{t('adminColumnLastLogin')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-hairline">
              {filteredAccounts.map((account) => (
                <tr key={account.userId} className="hover:bg-raised/40">
                  <td className={TD}>
                    <span className="block font-semibold text-ink">{account.username}</span>
                    <span className="block text-[11px] text-muted">{account.email}</span>
                  </td>
                  <td className={`${TD} font-mono`}>{account.role}</td>
                  <td className={TD}>
                    <AccountState active={account.isActive} />
                  </td>
                  <td className={`${TD} font-mono`}>{account.terrariums}</td>
                  <td className={`${TD} font-mono`}>{account.devices}</td>
                  <td className={`${TD} font-mono ${account.alertsOpen > 0 ? 'text-warning' : ''}`}>
                    {account.alertsOpen}
                  </td>
                  <td className={`${TD} whitespace-nowrap text-muted`}>{formatDate(account.createdAt, lang)}</td>
                  <td className={`${TD} whitespace-nowrap text-muted`}>
                    {formatDateTime(account.lastLoginAt, lang)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

function downloadAccountsCsv(headers: string[], rows: (string | number)[][]): void {
  const escapeCell = (value: string | number): string => {
    let text = String(value);
    if (/^[\t\r ]*[=+\-@]/.test(text)) {
      text = `'${text}`;
    }
    return `"${text.replace(/"/g, '""')}"`;
  };

  const csv = `\uFEFF${[headers, ...rows]
    .map((row) => row.map(escapeCell).join(','))
    .join('\r\n')}`;
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = `admin-accounts-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 0);
}

/** Enabled or disabled, in words — a soft-deleted account *is* a disabled one, and the list is where that shows. */
const AccountState: React.FC<{ active: boolean }> = ({ active }) => {
  const { t } = useI18n();
  return (
    <span className={`text-[11px] font-semibold ${active ? 'text-inrange' : 'text-critical'}`}>
      {active ? t('adminBadgeActive') : t('adminBadgeDisabled')}
    </span>
  );
};

/** Every account's enclosures, newest first. */
const TerrariumTable: React.FC<{
  pane: Pane;
  onSearch: (search: string) => void;
  onSkip: (skip: number) => void;
  lang: Language;
}> = ({ pane, onSearch, onSkip, lang }) => {
  const { t } = useI18n();
  const search = useDebounced(pane.search, 300);
  const page = usePolled<AdminPage<AdminTerrariumView>>(
    () => api.listAdminTerrariums({ search, skip: pane.skip, take: ADMIN_PAGE_SIZE }),
    null,
    [search, pane.skip]
  );

  return (
    <div className="space-y-3">
      <SearchField
        value={pane.search}
        onChange={onSearch}
        label={t('adminSearchTerrariums')}
        placeholder={t('adminSearchTerrariums')}
      />

      {page.error ? <ErrorPanel error={page.error} onRetry={() => page.reload()} /> : null}
      {page.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {page.data && page.data.items.length === 0 && !page.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('adminNoRows')} />
      ) : null}

      {page.data && page.data.items.length > 0 ? (
        <>
          <div className={TABLE_WRAP}>
            <table className={TABLE}>
              <thead className="border-b border-hairline text-muted">
                <tr>
                  <th className={TH}>{t('adminColumnTerrarium')}</th>
                  <th className={TH}>{t('adminColumnOwner')}</th>
                  <th className={TH}>{t('adminColumnSpeciesProfile')}</th>
                  <th className={TH}>{t('adminColumnStatus')}</th>
                  <th className={TH}>{t('adminColumnBoard')}</th>
                  <th className={TH}>{t('adminColumnLastSeen')}</th>
                  <th className={TH}>{t('adminColumnAlertsOpen')}</th>
                  <th className={TH}>{t('adminColumnCreated')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hairline">
                {page.data.items.map((row) => (
                  <tr key={row.id} className="hover:bg-raised/40">
                    <td className={TD}>
                      <span className="block font-semibold text-ink">{row.name}</span>
                      <span className="block font-mono text-[11px] text-muted">{row.id}</span>
                    </td>
                    <td className={TD}>{row.ownerUsername}</td>
                    <td className={TD}>{row.speciesProfileName}</td>
                    <td className={`${TD} whitespace-nowrap`}>{terrariumStatusLabel(t, row.status)}</td>
                    <td className={TD}>
                      {row.devicePublicId ? (
                        <>
                          <span className="block font-mono text-[11px]">{row.devicePublicId}</span>
                          <span className={`block text-[11px] ${deviceLifecycleTextClass(row.deviceStatus)}`}>
                            {deviceLifecycleLabel(t, row.deviceStatus)}
                          </span>
                        </>
                      ) : (
                        <span className="text-muted">{t('adminNoBoard')}</span>
                      )}
                    </td>
                    <td className={`${TD} whitespace-nowrap text-muted`}>
                      {row.deviceLastSeenAt ? (
                        <>
                          <span className="block">{formatDateTime(row.deviceLastSeenAt, lang)}</span>
                          <span className="block text-[11px]">{formatRelative(t, row.deviceLastSeenAt)}</span>
                        </>
                      ) : (
                        t('never')
                      )}
                    </td>
                    <td className={`${TD} font-mono ${row.openAlerts > 0 ? 'text-warning' : ''}`}>
                      {row.openAlerts}
                    </td>
                    <td className={`${TD} whitespace-nowrap text-muted`}>{formatDate(row.createdAt, lang)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager total={page.data.total} skip={page.data.skip} take={page.data.take} onSkip={onSkip} />
        </>
      ) : null}
    </div>
  );
};

/**
 * Every board in the deployment, unclaimed ones included — an administrator looking for a board that was never set
 * up would not find it in a list of bound ones.
 */
const DeviceTable: React.FC<{
  pane: Pane;
  onSearch: (search: string) => void;
  onSkip: (skip: number) => void;
  lang: Language;
}> = ({ pane, onSearch, onSkip, lang }) => {
  const { t } = useI18n();
  const search = useDebounced(pane.search, 300);
  const page = usePolled<AdminPage<AdminDeviceView>>(
    () => api.listAdminDevices({ search, skip: pane.skip, take: ADMIN_PAGE_SIZE }),
    null,
    [search, pane.skip]
  );

  return (
    <div className="space-y-3">
      <SearchField
        value={pane.search}
        onChange={onSearch}
        label={t('adminSearchDevices')}
        placeholder={t('adminSearchDevices')}
      />

      {page.error ? <ErrorPanel error={page.error} onRetry={() => page.reload()} /> : null}
      {page.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {page.data && page.data.items.length === 0 && !page.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('adminNoRows')} />
      ) : null}

      {page.data && page.data.items.length > 0 ? (
        <>
          <div className={TABLE_WRAP}>
            <table className={TABLE}>
              <thead className="border-b border-hairline text-muted">
                <tr>
                  <th className={TH}>{t('adminColumnDeviceName')}</th>
                  <th className={TH}>{t('adminColumnStatus')}</th>
                  <th className={TH}>{t('adminColumnOwner')}</th>
                  <th className={TH}>{t('adminColumnTerrarium')}</th>
                  <th className={TH}>{t('adminColumnFirmware')}</th>
                  <th className={TH}>{t('adminColumnLastSeen')}</th>
                  <th className={TH}>{t('adminColumnSignal')}</th>
                  <th className={TH}>{t('adminColumnBattery')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hairline">
                {page.data.items.map((row) => (
                  <tr key={row.id} className="hover:bg-raised/40">
                    <td className={TD}>
                      <span className="block font-semibold text-ink">
                        {row.deviceName || t('adminUnknown')}
                      </span>
                      <span className="block font-mono text-[11px] text-muted">{row.publicId}</span>
                    </td>
                    <td className={`${TD} whitespace-nowrap ${deviceLifecycleTextClass(row.status)}`}>
                      {deviceLifecycleLabel(t, row.status)}
                    </td>
                    <td className={TD}>{row.ownerUsername ?? '—'}</td>
                    <td className={TD}>{row.terrariumName ?? '—'}</td>
                    <td className={`${TD} whitespace-nowrap`}>{row.firmwareVersion || '—'}</td>
                    <td className={`${TD} whitespace-nowrap text-muted`}>
                      {row.lastSeenAt ? (
                        <>
                          <span className="block">{formatDateTime(row.lastSeenAt, lang)}</span>
                          <span className="block text-[11px]">{formatRelative(t, row.lastSeenAt)}</span>
                        </>
                      ) : (
                        t('never')
                      )}
                    </td>
                    <td className={`${TD} font-mono`}>
                      {row.signalStrengthDbm === null ? '—' : `${row.signalStrengthDbm} dBm`}
                    </td>
                    <td className={`${TD} font-mono`}>
                      {row.batteryPct === null ? '—' : `${Math.round(row.batteryPct)}%`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager total={page.data.total} skip={page.data.skip} take={page.data.take} onSkip={onSkip} />
        </>
      ) : null}
    </div>
  );
};

/**
 * Both families' alerts across the deployment, which is the point of the two-family split: an enclosure that went
 * quiet and an animal that is too cold are different problems with different owners, and one list that mixes them
 * hides that.
 */
const AlertTable: React.FC<{
  query: { family: string; state: string; skip: number };
  onFilter: (query: { family: string; state: string }) => void;
  onSkip: (skip: number) => void;
  lang: Language;
}> = ({ query, onFilter, onSkip, lang }) => {
  const { t } = useI18n();
  const page = usePolled<AdminPage<AdminAlertView>>(
    () =>
      api.listAdminAlerts({
        family: query.family || undefined,
        state: query.state || undefined,
        skip: query.skip,
        take: ADMIN_PAGE_SIZE,
      }),
    null,
    [query.family, query.state, query.skip]
  );

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-3">
        <label className="text-[11px] text-muted">
          <span className="mb-1 block">{t('adminColumnFamily')}</span>
          <select
            value={query.family}
            onChange={(event) => onFilter({ family: event.target.value, state: query.state })}
            className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
          >
            <option value="">{t('adminFilterFamilyAll')}</option>
            <option value="enclosure">{alertFamilyLabel(t, 'enclosure')}</option>
            <option value="species">{alertFamilyLabel(t, 'species')}</option>
          </select>
        </label>

        <label className="text-[11px] text-muted">
          <span className="mb-1 block">{t('adminColumnState')}</span>
          <select
            value={query.state}
            onChange={(event) => onFilter({ family: query.family, state: event.target.value })}
            className="rounded-lg border border-hairline bg-field px-3 py-2 text-xs text-ink outline-none focus:border-brand"
          >
            <option value="">{t('adminFilterStateAll')}</option>
            <option value="Open">{alertStateLabel(t, 'Open')}</option>
            <option value="Acknowledged">{alertStateLabel(t, 'Acknowledged')}</option>
            <option value="Resolved">{alertStateLabel(t, 'Resolved')}</option>
          </select>
        </label>
      </div>

      {page.error ? <ErrorPanel error={page.error} onRetry={() => page.reload()} /> : null}
      {page.loading ? <LoadingPanel label={t('signingIn')} /> : null}

      {page.data && page.data.items.length === 0 && !page.error ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('adminNoRows')} />
      ) : null}

      {page.data && page.data.items.length > 0 ? (
        <>
          <div className={TABLE_WRAP}>
            <table className={TABLE}>
              <thead className="border-b border-hairline text-muted">
                <tr>
                  <th className={TH}>{t('adminColumnFamily')}</th>
                  <th className={TH}>{t('adminColumnSeverity')}</th>
                  <th className={TH}>{t('adminColumnState')}</th>
                  <th className={TH}>{t('adminColumnSource')}</th>
                  <th className={TH}>{t('adminColumnMetric')}</th>
                  <th className={TH}>{t('adminColumnTerrarium')}</th>
                  <th className={TH}>{t('adminColumnOwner')}</th>
                  <th className={TH}>{t('adminColumnTriggeredAt')}</th>
                  <th className={TH}>{t('adminColumnResolvedAt')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-hairline">
                {page.data.items.map((row) => (
                  <tr key={row.id} className="hover:bg-raised/40">
                    <td className={`${TD} whitespace-nowrap`}>{alertFamilyLabel(t, row.family)}</td>
                    <td className={`${TD} whitespace-nowrap font-semibold ${severityTextClass(row.severity)}`}>
                      {severityLabel(t, row.severity)}
                    </td>
                    <td className={`${TD} whitespace-nowrap ${alertStateTextClass(row.state)}`}>
                      {alertStateLabel(t, row.state)}
                    </td>
                    <td className={`${TD} whitespace-nowrap`}>{alertSourceLabel(t, row.source)}</td>
                    <td className={TD}>{row.metric ? metricLabel(t, row.metric) : '—'}</td>
                    <td className={TD}>{row.terrariumName}</td>
                    <td className={TD}>{row.ownerUsername}</td>
                    <td className={`${TD} whitespace-nowrap text-muted`}>
                      <span className="block">{formatDateTime(row.triggeredAt, lang)}</span>
                      <span className="block text-[11px]">{formatRelative(t, row.triggeredAt)}</span>
                    </td>
                    <td className={`${TD} whitespace-nowrap text-muted`}>
                      {row.resolvedAt ? formatDateTime(row.resolvedAt, lang) : t('alertStateOpen')}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager total={page.data.total} skip={page.data.skip} take={page.data.take} onSkip={onSkip} />
        </>
      ) : null}
    </div>
  );
};
