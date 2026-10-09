import React, { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Download } from 'lucide-react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import * as api from '../api/endpoints';
import { METRIC_CODES, type RangeSeries, type TerrariumList } from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, metricLabel } from '../lib/format';
import { usePolled } from '../lib/usePolled';

/**
 * W3 — history (roadmap 4.16).
 *
 * The chart is gap-aware because the API is: an empty bucket arrives as `{count: 0, min: null, …}` rather than as
 * an interpolated value, and `connectNulls={false}` is what makes that visible instead of drawing a straight line
 * across an outage. That is FR-09 BR-09.5 rendered rather than re-implemented.
 *
 * The bucket width is the server's choice (BR-09.1) and is displayed as sent — the screen does not decide that a
 * 24-hour window is "5min", it asks for the window and prints what came back. Coverage is requested for the same
 * window and shown beside the chart, because §6 refuses a summary without it.
 */

const RANGES = [
  { key: 'range6h', hours: 6 },
  { key: 'range24h', hours: 24 },
  { key: 'range7d', hours: 24 * 7 },
  { key: 'range30d', hours: 24 * 30 },
] as const;

const BUCKET_KEY: Record<string, string> = {
  raw: 'bucketRaw',
  '5min': 'bucket5min',
  hourly: 'bucketHourly',
};

export const History: React.FC = () => {
  const { t, lang } = useI18n();
  const [params, setParams] = useSearchParams();
  const [hours, setHours] = useState<number>(24);
  const [metric, setMetric] = useState<string>(METRIC_CODES[0]);

  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), 60_000);
  const items = terrariums.data?.items ?? [];
  const fromQuery = params.get('terrarium');
  const selectedId =
    (fromQuery && items.some((item) => item.id === fromQuery) ? fromQuery : null) ?? items[0]?.id ?? null;

  // Keep the URL honest: the selected terrarium is linkable, so a screenshot of a chart names its terrarium.
  useEffect(() => {
    if (selectedId && selectedId !== fromQuery) {
      const next = new URLSearchParams(params);
      next.set('terrarium', selectedId);
      setParams(next, { replace: true });
    }
  }, [selectedId, fromQuery, params, setParams]);

  const window = useMemo(() => {
    const to = new Date();
    const from = new Date(to.getTime() - hours * 60 * 60 * 1000);
    return { from: from.toISOString(), to: to.toISOString() };
  }, [hours]);

  const series = usePolled<RangeSeries | null>(
    () => (selectedId ? api.readings(selectedId, metric, window.from, window.to) : Promise.resolve(null)),
    60_000,
    [selectedId, metric, window.from, window.to]
  );

  const data = series.data;
  const points = data?.points ?? [];
  const withValues = points.filter((point) => point.avg !== null);

  /** The CSV the retired dashboard offered, built from what the API sent — gaps included as empty buckets. */
  const downloadCsv = () => {
    if (!data) {
      return;
    }
    const rows = [
      ['t', 'min', 'max', 'avg', 'count'].join(','),
      ...data.points.map((point) =>
        [point.t, point.min ?? '', point.max ?? '', point.avg ?? '', point.count].join(',')
      ),
    ];
    const blob = new Blob([rows.join('\n')], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `${metric}-${data.bucket}-${data.fromUtc.slice(0, 10)}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="font-heading text-2xl font-bold text-ink">{t('historyTitle')}</h1>

        <div className="flex flex-wrap items-end gap-4">
          <label className="text-xs uppercase tracking-wider text-muted">
            <span className="mb-1 block">{t('terrariumsTitle')}</span>
            <select
              value={selectedId ?? ''}
              onChange={(event) => setParams({ terrarium: event.target.value })}
              className="rounded-xl border border-hairline bg-field px-3 py-2 text-sm normal-case tracking-normal text-ink outline-none focus:border-brand"
            >
              {items.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>

          <label className="text-xs uppercase tracking-wider text-muted">
            <span className="mb-1 block">{t('metricLabel')}</span>
            <select
              value={metric}
              onChange={(event) => setMetric(event.target.value)}
              className="rounded-xl border border-hairline bg-field px-3 py-2 text-sm normal-case tracking-normal text-ink outline-none focus:border-brand"
            >
              {METRIC_CODES.map((code) => (
                <option key={code} value={code}>
                  {metricLabel(t, code)}
                </option>
              ))}
            </select>
          </label>

          <div className="flex items-center gap-1">
            {RANGES.map((range) => (
              <button
                key={range.key}
                type="button"
                onClick={() => setHours(range.hours)}
                aria-pressed={hours === range.hours}
                className={`rounded-lg border px-3 py-2 text-xs transition-colors ${
                  hours === range.hours
                    ? 'border-brand text-brand'
                    : 'border-hairline text-muted hover:text-ink'
                }`}
              >
                {t(range.key)}
              </button>
            ))}
          </div>

          <button
            type="button"
            onClick={downloadCsv}
            disabled={!data || withValues.length === 0}
            className="inline-flex items-center gap-1.5 rounded-lg border border-hairline px-3 py-2 text-xs text-ink transition-colors hover:bg-raised disabled:opacity-40"
          >
            <Download className="h-3.5 w-3.5" aria-hidden="true" />
            {t('downloadCsv')}
          </button>
        </div>
      </header>

      {terrariums.error?.isNetworkFailure || series.error?.isNetworkFailure ? (
        <UnreachableBanner onRetry={() => series.reload()} />
      ) : null}

      {items.length === 0 && !terrariums.loading ? (
        <EmptyPanel title={t('emptyStateTitle')} body={t('emptyStateBody')} />
      ) : null}

      {series.error && !series.error.isNetworkFailure ? (
        <ErrorPanel error={series.error} onRetry={series.reload} />
      ) : null}

      {series.loading && !data ? <LoadingPanel label={t('signingIn')} /> : null}

      {data ? (
        <section className="rounded-2xl border border-hairline bg-surface p-5">
          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <h2 className="font-heading text-lg font-semibold text-ink">
              {metricLabel(t, data.metric)}
              <span className="ml-2 text-sm font-normal text-muted">{data.unit}</span>
            </h2>
            <p className="text-xs text-muted">
              {t('band')}: {BUCKET_KEY[data.bucket] ? t(BUCKET_KEY[data.bucket]) : data.bucket} ·{' '}
              {formatDateTime(data.fromUtc, lang)} → {formatDateTime(data.toUtc, lang)}
            </p>
          </div>

          {withValues.length === 0 ? (
            <div className="py-6">
              <EmptyPanel title={t('emptyHistoryTitle')} body={t('emptyHistoryBody')} />
            </div>
          ) : (
            <div className="mt-4 h-80 w-full">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={points} margin={{ top: 8, right: 12, bottom: 8, left: 0 }}>
                  <CartesianGrid stroke="#1e3825" strokeDasharray="3 3" vertical={false} />
                  <XAxis
                    dataKey="t"
                    tick={{ fill: '#8e9e8f', fontSize: 11 }}
                    tickFormatter={(value: string) => formatDateTime(value, lang)}
                    minTickGap={40}
                    stroke="#1e3825"
                  />
                  <YAxis
                    tick={{ fill: '#8e9e8f', fontSize: 11 }}
                    domain={['auto', 'auto']}
                    stroke="#1e3825"
                    width={48}
                  />
                  <Tooltip
                    contentStyle={{
                      background: '#112016',
                      border: '1px solid #1e3825',
                      borderRadius: 12,
                      fontSize: 12,
                    }}
                    labelFormatter={(value) => formatDateTime(String(value), lang)}
                  />
                  {/* connectNulls={false}: an empty bucket is a gap, not a value (BR-09.5). */}
                  <Line
                    type="monotone"
                    dataKey="avg"
                    name={metricLabel(t, data.metric)}
                    stroke="#4a9e6a"
                    strokeWidth={2}
                    dot={false}
                    connectNulls={false}
                  />
                </LineChart>
              </ResponsiveContainer>
            </div>
          )}

          <p className="mt-3 text-[11px] text-muted">{t('chartGapNote')}</p>
        </section>
      ) : null}
    </div>
  );
};
