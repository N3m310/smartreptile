import React, { useState } from 'react';
import { ArrowLeft, ChevronDown, ChevronUp } from 'lucide-react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import * as api from '../api/endpoints';
import type { Coverage, RangeSeries, SpeciesProfileList, TerrariumItem, TerrariumList } from '../api/types';
import { ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, metricLabel, metricKeys } from '../lib/format';
import { describeError } from '../lib/messages';
import { usePolled } from '../lib/usePolled';
import { useSession } from '../state/session';
import { ProfileCard } from './SpeciesProfiles';

export const SpeciesProfileDetail: React.FC = () => {
  const { id = '' } = useParams();
  const { t, lang } = useI18n();
  const navigate = useNavigate();
  const { hasPermission } = useSession();
  const profiles = usePolled<SpeciesProfileList>(() => api.listSpeciesProfiles(), null, [id]);
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), null, [id]);
  const profile = profiles.data?.items.find((item) => item.id === id);
  const matchingTerrariums = (terrariums.data?.items ?? []).filter((item) => item.speciesProfileId === id);

  const refresh = () => {
    profiles.reload();
    terrariums.reload();
  };

  return (
    <div className="space-y-5">
      <button type="button" onClick={() => navigate('/species-profiles')} className="inline-flex items-center gap-2 text-sm text-accent hover:underline">
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />{t('profileBack')}
      </button>
      {profiles.error?.isNetworkFailure ? <UnreachableBanner onRetry={refresh} /> : null}
      {profiles.error && !profiles.error.isNetworkFailure ? <ErrorPanel error={profiles.error} onRetry={refresh} /> : null}
      {terrariums.error && <p role="alert" className="rounded-lg border border-critical/30 bg-critical/5 p-3 text-sm text-critical">{describeError(t, terrariums.error)}</p>}
      {profiles.loading && !profiles.data ? <LoadingPanel label={t('signingIn')} /> : null}
      {!profiles.loading && !profiles.error && !profile ? (
        <div className="rounded-xl border border-hairline bg-surface p-6">
          <h1 className="font-heading text-lg font-semibold text-ink">{t('errorProfileNotFound')}</h1>
          <Link to="/species-profiles" className="mt-2 inline-block text-sm text-accent hover:underline">{t('profileBack')}</Link>
        </div>
      ) : null}
      {profile && (
        <>
          <ProfileCard
            profile={profile}
            canWrite={hasPermission('species.write')}
            onChanged={refresh}
          />
          <section className="space-y-3">
            <div>
              <h2 className="font-heading text-lg font-semibold text-ink">{t('profileTerrariumsTitle')}</h2>
              <p className="mt-1 text-xs text-muted">{t('profileTerrariumsCaption')}</p>
            </div>
            {terrariums.loading && !terrariums.data ? <LoadingPanel label={t('signingIn')} /> : null}
            {matchingTerrariums.length === 0 && terrariums.data ? (
              <p className="rounded-xl border border-hairline bg-surface p-5 text-sm text-muted">{t('profileNoTerrariums')}</p>
            ) : null}
            <ul className="space-y-3">
              {matchingTerrariums.map((terrarium) => <li key={terrarium.id}><TerrariumHistory terrarium={terrarium} lang={lang} /></li>)}
            </ul>
          </section>
        </>
      )}
    </div>
  );
};

const TerrariumHistory: React.FC<{ terrarium: TerrariumItem; lang: string }> = ({ terrarium, lang }) => {
  const { t } = useI18n();
  const [expanded, setExpanded] = useState(false);
  return (
    <article className="rounded-xl border border-hairline bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div><h3 className="font-medium text-ink">{terrarium.name}</h3><p className="mt-1 text-xs text-muted">{terrarium.location || '—'} · {t('openAlertsCount', { count: terrarium.openAlertCount })}</p></div>
        <div className="flex items-center gap-3">
          <Link to={`/terrariums/${encodeURIComponent(terrarium.id)}`} className="text-xs text-accent hover:underline">{t('navTerrariums')}</Link>
          <button type="button" aria-expanded={expanded} onClick={() => setExpanded((value) => !value)} className="inline-flex items-center gap-1 rounded-lg border border-hairline px-3 py-2 text-xs text-ink">
            {expanded ? <>{t('profileHistoryHide')}<ChevronUp className="h-3.5 w-3.5" /></> : <>{t('profileHistoryShow')}<ChevronDown className="h-3.5 w-3.5" /></>}
          </button>
        </div>
      </div>
      {expanded && <HistoryPanel terrarium={terrarium} lang={lang} />}
    </article>
  );
};

const HistoryPanel: React.FC<{ terrarium: TerrariumItem; lang: string }> = ({ terrarium, lang }) => {
  const { t } = useI18n();
  const [metric, setMetric] = useState('tempC');
  const [hours, setHours] = useState(24);
  const [to] = useState(() => new Date());
  const from = new Date(to.getTime() - hours * 60 * 60 * 1000);
  const fromUtc = from.toISOString();
  const toUtc = to.toISOString();
  const series = usePolled<RangeSeries>(() => api.readings(terrarium.id, metric, fromUtc, toUtc), null, [terrarium.id, metric, fromUtc, toUtc]);
  const coverage = usePolled<Coverage>(() => api.coverage(terrarium.id, fromUtc, toUtc), null, [terrarium.id, fromUtc, toUtc]);

  return (
    <div className="mt-4 space-y-3 border-t border-hairline pt-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="text-xs text-muted">{t('historyMetric')}<select value={metric} onChange={(event) => setMetric(event.target.value)} className="mt-1 block rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink">{metricKeys.map((key) => <option key={key} value={key}>{metricLabel(t, key)}</option>)}</select></label>
        <label className="text-xs text-muted">{t('historyWindow')}<select value={hours} onChange={(event) => setHours(Number(event.target.value))} className="mt-1 block rounded-lg border border-hairline bg-canvas px-3 py-2 text-sm text-ink"><option value={24}>24 giờ</option><option value={168}>7 ngày</option></select></label>
        {coverage.data && <p className="text-xs text-muted">{t('profileCoverage', { percent: coverage.data.coveragePct })}</p>}
      </div>
      {series.error && <p role="alert" className="text-sm text-critical">{t('errorRequestFailed')} ({series.error.code})</p>}
      {coverage.error && <p role="alert" className="text-xs text-muted">{t('profileCoverageUnavailable')}</p>}
      {series.loading && !series.data ? <p className="text-xs text-muted">{t('thresholdsLoading')}</p> : null}
      {series.data && series.data.points.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border border-hairline">
          <table className="w-full text-left text-xs">
            <thead className="bg-raised text-muted"><tr><th className="px-3 py-2">{t('historyTime')}</th><th className="px-3 py-2">{t('historyMinimum')}</th><th className="px-3 py-2">{t('historyAverage')}</th><th className="px-3 py-2">{t('historyMaximum')}</th><th className="px-3 py-2">{t('historySamples')}</th></tr></thead>
            <tbody className="divide-y divide-hairline">{series.data.points.map((point) => <tr key={point.t}><td className="px-3 py-2 text-muted">{formatDateTime(point.t, lang === 'vi' ? 'vi' : 'en')}</td><td className="px-3 py-2 text-ink">{point.min ?? '—'}</td><td className="px-3 py-2 text-ink">{point.avg ?? '—'}</td><td className="px-3 py-2 text-ink">{point.max ?? '—'}</td><td className="px-3 py-2 text-muted">{point.count}</td></tr>)}</tbody>
          </table>
        </div>
      ) : series.data ? <p className="text-xs text-muted">{t('profileHistoryEmpty')}</p> : null}
    </div>
  );
};
