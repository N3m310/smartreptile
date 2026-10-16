import React, { useEffect, useState } from 'react';
import { ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { useI18n } from '../i18n';

/**
 * The two controls every paged administrative list needs, so four lists cannot drift into four behaviours.
 *
 * `useDebounced` is here for the same reason: a search box that fires a request per keystroke is a rate limit
 * waiting to happen, and the API's `search` parameter is a filter rather than a suggestion — the wait is short
 * enough that nobody types through it.
 */

/** The page size the administrative lists ask for, matching the server's own default. */
export const ADMIN_PAGE_SIZE = 50;

export function useDebounced<T>(value: T, delayMs: number): T {
  const [settled, setSettled] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setSettled(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return settled;
}

export const SearchField: React.FC<{
  value: string;
  onChange: (value: string) => void;
  label: string;
  placeholder: string;
}> = ({ value, onChange, label, placeholder }) => (
  <label className="relative block w-full max-w-sm">
    <span className="sr-only">{label}</span>
    <Search
      className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted"
      aria-hidden="true"
    />
    <input
      type="search"
      value={value}
      onChange={(event) => onChange(event.target.value)}
      placeholder={placeholder}
      className="w-full rounded-lg border border-hairline bg-field py-2 pl-9 pr-3 text-xs text-ink outline-none focus:border-brand"
    />
  </label>
);

/**
 * Previous/next over a window, with the window stated. A console that pages without saying "rows 51-100 of 312"
 * leaves an administrator guessing whether the list is complete.
 */
export const Pager: React.FC<{
  total: number;
  skip: number;
  take: number;
  onSkip: (skip: number) => void;
}> = ({ total, skip, take, onSkip }) => {
  const { t } = useI18n();
  const first = total === 0 ? 0 : skip + 1;
  const last = Math.min(skip + take, total);
  const hasPrevious = skip > 0;
  const hasNext = skip + take < total;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-[11px] text-muted">
        {t('adminPagerRange', { from: first, to: last, total })}
      </p>
      <div className="flex items-center gap-2">
        <button
          type="button"
          disabled={!hasPrevious}
          onClick={() => onSkip(Math.max(0, skip - take))}
          className="inline-flex items-center gap-1 rounded-lg border border-hairline px-2.5 py-1.5 text-[11px] text-ink transition-colors hover:bg-raised disabled:opacity-40"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          {t('adminPrevious')}
        </button>
        <button
          type="button"
          disabled={!hasNext}
          onClick={() => onSkip(skip + take)}
          className="inline-flex items-center gap-1 rounded-lg border border-hairline px-2.5 py-1.5 text-[11px] text-ink transition-colors hover:bg-raised disabled:opacity-40"
        >
          {t('adminNext')}
          <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
};
