import React from 'react';
import { AlertTriangle, Loader2, RefreshCw } from 'lucide-react';
import { ApiError } from '../api/client';
import { useI18n } from '../i18n';
import { describeError } from '../lib/messages';

/**
 * The honest states, in one place: loading, empty, refused, unreachable.
 *
 * `02-design/04` §6 is why these are separate components rather than one `if` in each screen — "never report an
 * unbuilt feature as an outage" means a 404 with no code, a 404 with a code, and a dead connection have to render
 * three different sentences, and an empty account is a fourth state that is not a failure at all.
 */

export const LoadingPanel: React.FC<{ label: string }> = ({ label }) => (
  <div className="flex items-center gap-3 rounded-2xl border border-hairline bg-surface px-5 py-6 text-sm text-muted">
    <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
    <span>{label}</span>
  </div>
);

export const EmptyPanel: React.FC<{
  title: string;
  body: string;
  action?: React.ReactNode;
}> = ({ title, body, action }) => (
  <div className="rounded-2xl border border-dashed border-hairline bg-surface/60 px-6 py-10 text-center">
    <p className="font-heading text-lg font-semibold text-ink">{title}</p>
    <p className="mx-auto mt-2 max-w-md text-sm text-muted">{body}</p>
    {action ? <div className="mt-5 flex justify-center">{action}</div> : null}
  </div>
);

/** A refused call: the server answered, and the answer is rendered from its code (roadmap 4.15). */
export const ErrorPanel: React.FC<{ error: ApiError; onRetry?: () => void }> = ({ error, onRetry }) => {
  const { t } = useI18n();
  return (
    <div
      role="alert"
      className="flex items-start gap-3 rounded-2xl border border-critical/40 bg-critical/10 px-5 py-4"
    >
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-critical" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="text-sm text-ink">{describeError(t, error)}</p>
        {error.detail ? <p className="mt-1 text-xs text-muted">{error.detail}</p> : null}
      </div>
      {onRetry ? (
        <button
          type="button"
          onClick={onRetry}
          className="flex shrink-0 items-center gap-1.5 rounded-lg border border-hairline px-3 py-1.5 text-xs text-ink transition-colors hover:bg-raised"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          {t('retry')}
        </button>
      ) : null}
    </div>
  );
};

/**
 * The banner for a value that is still on screen while the server is unreachable. It exists because
 * `02-design/04` §6 refuses the alternative: freezing the last value without saying so makes a stale reading look
 * like a live one.
 */
export const UnreachableBanner: React.FC<{ onRetry?: () => void }> = ({ onRetry }) => {
  const { t } = useI18n();
  return (
    <div
      role="status"
      className="flex items-center gap-3 rounded-xl border border-warning/40 bg-warning/10 px-4 py-2.5 text-[13px] text-ink"
    >
      <AlertTriangle className="h-4 w-4 shrink-0 text-warning" aria-hidden="true" />
      <span className="flex-1">{t('errorBackendUnreachable')}</span>
      {onRetry ? (
        <button
          type="button"
          onClick={onRetry}
          className="rounded-lg border border-hairline px-2.5 py-1 text-xs transition-colors hover:bg-raised"
        >
          {t('retry')}
        </button>
      ) : null}
    </div>
  );
};
