import React from 'react';
import { Activity, CheckCircle2, Server, XCircle } from 'lucide-react';
import * as api from '../api/endpoints';
import type { HealthReport, MetricsSnapshot, VersionInfo } from '../api/types';
import { ErrorPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime } from '../lib/format';
import { usePolled } from '../lib/usePolled';

/**
 * The diagnosis page — what the retired `web/legacy/health.html` did, so nothing is lost by the retirement.
 *
 * It is deliberately outside the app's authenticated screens' data flow: `/health/ready`, `/version` and
 * `/metrics` are unauthenticated (FR-18), which means this page still works when the reason you are looking at it
 * is that signing in does not.
 *
 * Two rules from `02-design/04` §6 decide the copy. "Never report an unbuilt feature as an outage" is why a 404
 * with no problem code reads as *not built yet* rather than as a failure — the transport tells the two apart, and
 * this page prints the distinction instead of flattening it. And a failing check is named: "not ready" on its own
 * sends the keeper hunting, so the broker and the database each get their own sentence.
 */
export const System: React.FC = () => {
  const { t, lang } = useI18n();

  const readiness = usePolled<HealthReport>(() => api.readiness(), 30_000);
  const version = usePolled<VersionInfo>(() => api.version(), null);
  const metrics = usePolled<MetricsSnapshot>(() => api.metrics(), 30_000);

  const report = readiness.data;
  const healthy = report?.status === 'Healthy';

  const checkSentence = (name: string, error: string | null, description: string | null): string => {
    const haystack = `${name} ${description ?? ''} ${error ?? ''}`.toLowerCase();
    if (haystack.includes('broker') || haystack.includes('mqtt')) {
      return t('brokerDown');
    }
    if (haystack.includes('db') || haystack.includes('database') || haystack.includes('sql')) {
      return t('databaseDown');
    }
    return error ?? description ?? name;
  };

  return (
    <div className="space-y-6">
      <header>
        <h1 className="font-heading text-2xl font-bold text-ink">{t('systemTitle')}</h1>
      </header>

      {readiness.error?.isNetworkFailure || version.error?.isNetworkFailure ? (
        <UnreachableBanner onRetry={() => readiness.reload()} />
      ) : null}
      {version.error && !version.error.isNetworkFailure ? (
        <ErrorPanel error={version.error} onRetry={version.reload} />
      ) : null}

      <section className="rounded-2xl border border-hairline bg-surface p-5">
        <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
          {t('readiness')}
        </h2>

        {report ? (
          <>
            <p
              className={`mt-2 inline-flex items-center gap-2 font-heading text-lg ${
                healthy ? 'text-inrange' : 'text-critical'
              }`}
            >
              {healthy ? (
                <CheckCircle2 className="h-5 w-5" aria-hidden="true" />
              ) : (
                <XCircle className="h-5 w-5" aria-hidden="true" />
              )}
              {healthy ? t('ready') : t('notReady')}
            </p>
            <p className="mt-1 text-xs text-muted">
              {t('checkedAt')} {formatDateTime(report.timestamp, lang)} · {report.totalDurationMs} ms
            </p>

            <ul className="mt-4 space-y-2">
              {report.checks.map((check) => (
                <li
                  key={check.name}
                  className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-hairline bg-field px-4 py-2.5 text-xs"
                >
                  <span className="inline-flex items-center gap-2">
                    <Server className="h-3.5 w-3.5 text-muted" aria-hidden="true" />
                    <span className="font-mono text-ink">{check.name}</span>
                  </span>
                  <span
                    className={check.status === 'Healthy' ? 'text-inrange' : 'text-critical'}
                  >
                    {check.status === 'Healthy'
                      ? t('ready')
                      : checkSentence(check.name, check.error, check.description)}
                  </span>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <p className="mt-2 text-xs text-muted">{t('signingIn')}</p>
        )}
      </section>

      <section className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="rounded-2xl border border-hairline bg-surface p-5">
          <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
            {t('apiVersion')}
          </h2>
          {version.data ? (
            <dl className="mt-3 space-y-1.5 text-xs">
              <div className="flex justify-between gap-3">
                <dt className="text-muted">{t('apiVersion')}</dt>
                <dd className="font-mono text-ink">{version.data.api}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-muted">{t('schemaVersion')}</dt>
                <dd className="font-mono text-ink">{version.data.schema}</dd>
              </div>
              <div className="flex justify-between gap-3">
                <dt className="text-muted">{t('minFirmware')}</dt>
                <dd className="font-mono text-ink">{version.data.minFirmware}</dd>
              </div>
            </dl>
          ) : (
            <p className="mt-2 text-xs text-muted">{t('signingIn')}</p>
          )}
        </div>

        <div className="rounded-2xl border border-hairline bg-surface p-5">
          <h2 className="font-heading text-sm font-semibold uppercase tracking-wider text-muted">
            {t('metricsCounters')}
          </h2>
          {metrics.data ? (
            <>
              <p className="mt-3 inline-flex items-center gap-2 text-xs text-muted">
                <Activity className="h-3.5 w-3.5" aria-hidden="true" />
                {t('brokerRunning')}:{' '}
                <span className={metrics.data.brokerRunning ? 'text-inrange' : 'text-critical'}>
                  {metrics.data.brokerRunning ? t('ready') : t('notReady')}
                </span>
                {!metrics.data.brokerRunning ? ` — ${t('brokerDown')}` : ''}
              </p>
              <dl className="mt-3 space-y-1.5 text-xs">
                {Object.entries(metrics.data.counters).map(([name, value]) => (
                  <div key={name} className="flex justify-between gap-3">
                    <dt className="truncate font-mono text-muted">{name}</dt>
                    <dd className="font-mono text-ink">{value}</dd>
                  </div>
                ))}
              </dl>
            </>
          ) : (
            <p className="mt-2 text-xs text-muted">{t('signingIn')}</p>
          )}
        </div>
      </section>
    </div>
  );
};
