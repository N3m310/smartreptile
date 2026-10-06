import React from 'react';
import { FlaskConical } from 'lucide-react';

/**
 * Honesty notice for the TERRAGUARD prototype (ADR-017, task 4.14).
 *
 * Every value this app renders comes from `data/mockData.ts`: no request leaves the page, so none of
 * it is a measurement. The notice is rendered on both entry points — the login screen, which is what
 * `http://127.0.0.1:8081/` serves, and the app shell behind it — so that no screenshot of this
 * prototype can present invented numbers as real ones (the standing "never fake a number" rule).
 *
 * The copy is Vietnamese-only on purpose; see the localisation note in `docs/02-design/04` §1.2.
 */
export const MockDataNotice: React.FC<{ className?: string }> = ({ className = '' }) => (
  <div
    role="note"
    className={`flex items-start gap-2.5 rounded-xl border border-[#c87f3a]/40 bg-[#c87f3a]/10 px-4 py-2.5 text-[13px] leading-snug text-[#dcd5c4] ${className}`}
  >
    <FlaskConical className="w-4 h-4 shrink-0 mt-0.5 text-[#e8a832]" aria-hidden="true" />
    <p>
      <strong className="font-semibold text-[#e8a832]">Dữ liệu mô phỏng.</strong> Nguyên mẫu giao diện
      (mock data) — không kết nối API, không hiển thị số đo thật.
    </p>
  </div>
);
