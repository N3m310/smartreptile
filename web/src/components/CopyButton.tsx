import React, { useEffect, useRef, useState } from 'react';
import { Check, Copy } from 'lucide-react';

/**
 * Copies a one-time secret to the clipboard.
 *
 * The button exists for the two places the API hands out a device credential exactly once — the claim/rotate answer on
 * the devices screen and a terrarium created with a board bound to it. Both say "shown once, never again", so the
 * screen should not leave selecting the text by hand as the only way to keep it.
 *
 * `navigator.clipboard` needs a secure context, and the dev server is deliberately reachable over plain http on a LAN
 * address (`vite --host`), where the API is absent or refuses with `NotAllowedError`. A hidden textarea and
 * `execCommand('copy')` is the fallback there; a failure is shown rather than reported as success.
 */
export const CopyButton: React.FC<{ value: string }> = ({ value }) => {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');
  const timer = useRef<number | null>(null);

  useEffect(
    () => () => {
      if (timer.current !== null) {
        window.clearTimeout(timer.current);
      }
    },
    []
  );

  const copy = async () => {
    const copied = await writeToClipboard(value);
    setState(copied ? 'copied' : 'failed');

    if (timer.current !== null) {
      window.clearTimeout(timer.current);
    }
    timer.current = window.setTimeout(() => setState('idle'), copied ? 2000 : 4000);
  };

  return (
    <button
      type="button"
      onClick={() => void copy()}
      className={`inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1.5 text-xs font-semibold transition-colors ${
        state === 'failed'
          ? 'border-critical/40 text-critical hover:bg-critical/5'
          : 'border-hairline text-ink hover:bg-raised'
      }`}
    >
      {state === 'copied' ? (
        <Check className="h-3.5 w-3.5" aria-hidden="true" />
      ) : (
        <Copy className="h-3.5 w-3.5" aria-hidden="true" />
      )}
      {state === 'copied' ? 'Đã sao chép' : state === 'failed' ? 'Không sao chép được' : 'Sao chép khóa'}
    </button>
  );
};

/** The Clipboard API first, the textarea fallback second: the second is the only one available on a plain-http origin. */
async function writeToClipboard(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text);
      return true;
    }
  } catch {
    // Present but refused — an insecure context answers NotAllowedError, so fall through rather than give up.
  }

  try {
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.top = '0';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    const copied = document.execCommand('copy');
    document.body.removeChild(area);
    return copied;
  } catch {
    return false;
  }
}
