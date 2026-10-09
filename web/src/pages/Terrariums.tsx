import React from 'react';
import { Link } from 'react-router-dom';
import { Box, ExternalLink, History as HistoryIcon, Info } from 'lucide-react';
import * as api from '../api/endpoints';
import type { TerrariumList } from '../api/types';
import { EmptyPanel, ErrorPanel, LoadingPanel, UnreachableBanner } from '../components/states';
import { useI18n } from '../i18n';
import { formatDateTime, formatRelative } from '../lib/format';
import { deviceStatusLabel } from '../lib/status';
import { usePolled } from '../lib/usePolled';

/**
 * W2's terrarium list (roadmap 4.16).
 *
 * Create is deliberately absent. `POST /terrariums` exists (2.8) but it takes a `speciesProfileId`, and the route
 * that lists species profiles is specified and unbuilt — so a create form here would need the keeper to type a
 * GUID, and the alternative (hard-coding one of the four seeded profile ids) would put an invented value on the
 * screen. It waits for the catalogue route, which is the roadmap's own rule for 4.20: a screen is wired when its
 * endpoints exist, and not before.
 */

const POLL_MS = 30_000;

export const Terrariums: React.FC = () => {
  const { t, lang } = useI18n();
  const terrariums = usePolled<TerrariumList>(() => api.listTerrariums(), POLL_MS);
  const items = terrariums.data?.items ?? [];

  return (
    <div className="space-y-6">
      <header>
        <h1 className="font-heading text-2xl font-bold text-ink">{t('terrariumsTitle')}</h1>
      </header>

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

      <p className="flex items-start gap-2 text-[11px] text-muted">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
        <span>{t('createTerrariumNotBuilt')}</span>
      </p>
    </div>
  );
};
