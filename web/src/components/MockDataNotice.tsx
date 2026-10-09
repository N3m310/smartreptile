import React from 'react';
import { FlaskConical } from 'lucide-react';
import { useI18n } from '../i18n';

/**
 * Honesty notice for the screens that still render `data/mockData.ts` (ADR-017, task 4.14).
 *
 * It is no longer global. Task 4.16 wired the dashboard, the terrarium screens and history to the API, so those
 * screens have nothing to apologise for; Devices, Alerts and Settings cannot be wired yet (roadmap 4.20 — their
 * endpoints do not exist), and the shell renders this notice on exactly those routes. A screen therefore shows it
 * if and only if it is still inventing its numbers.
 *
 * The copy moved into the shared deck rather than staying hard-coded Vietnamese, because a bilingual client cannot
 * carry a string only one of its languages can read.
 */
export const MockDataNotice: React.FC<{ className?: string }> = ({ className = '' }) => {
  const { t } = useI18n();
  return (
    <div
      role="note"
      className={`flex items-start gap-2.5 rounded-xl border border-accent/40 bg-accent/10 px-4 py-2.5 text-[13px] leading-snug text-ink ${className}`}
    >
      <FlaskConical className="mt-0.5 h-4 w-4 shrink-0 text-warning" aria-hidden="true" />
      <p>
        <strong className="font-semibold text-warning">{t('mockDataNoticeTitle')}</strong>{' '}
        {t('mockDataNoticeBody')}
      </p>
    </div>
  );
};
