import React from 'react';
import { useI18n } from '../i18n';
import { statusBarClass, statusLabel, statusTextClass } from '../lib/status';

/**
 * The status chip. Always the server's value, always with its label — colour is never the only signal
 * (`02-design/04` §3/NFR-06), which is also why an unknown status renders grey and shows its raw code.
 */
export const StatusChip: React.FC<{ status: string }> = ({ status }) => {
  const { t } = useI18n();
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded border border-hairline px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider ${statusTextClass(
        status
      )}`}
    >
      <span className={`h-2 w-2 rounded-full ${statusBarClass(status)}`} aria-hidden="true" />
      {statusLabel(t, status)}
    </span>
  );
};
