import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { DEFAULT_LANGUAGE, LANGUAGES, type Language, type StringKey, template } from './strings';

/**
 * `t()` — the only way a user-facing string reaches a screen (`02-design/04` §7).
 *
 * Two constructs are resolved, and only two, because only two appear in the deck:
 *   - `{count, plural, =1 {…} other {…}}` — Flutter's gen-l10n wants ICU, so the relative-time strings use
 *     it and the web has to read them. `=N` wins when the count matches, `other` otherwise.
 *   - `{name}` — plain substitution. A parameter that was not supplied stays visible as `{name}` rather
 *     than rendering as `undefined`, because a visible placeholder is a bug report and `undefined` is not.
 */
export type TParams = Record<string, string | number>;

/** The `{key …}` branches of a plural body, by balanced-brace scan — `other {{count} seconds ago}` nests. */
function branchesOf(body: string): Record<string, string> {
  const out: Record<string, string> = {};
  const keyRe = /(=\d+|\w+)\s*\{/g;
  let match: RegExpExecArray | null;

  while ((match = keyRe.exec(body)) !== null) {
    const start = match.index + match[0].length;
    let depth = 1;
    let end = start;
    while (end < body.length && depth > 0) {
      if (body[end] === '{') {
        depth += 1;
      } else if (body[end] === '}') {
        depth -= 1;
      }
      end += 1;
    }
    out[match[1]] = body.slice(start, end - 1);
    keyRe.lastIndex = end;
  }

  return out;
}

/**
 * Resolves `{count, plural, =1 {…} other {…}}`.
 *
 * The branches are found by counting braces rather than by one regex, because the strings are nested:
 * `other {{count} seconds ago}` closes two braces, and a pattern that stops at the first `}` silently returns
 * the whole template — which is exactly what the first version of this function did, and the dashboard rendered
 * `{count, plural, =1 {1 minute ago} other {2 minutes ago}}` on every card to prove it. A visible placeholder is
 * at least loud; the fix is to scan properly.
 */
function resolvePlural(text: string, params: TParams): string {
  let out = '';
  let index = 0;

  while (index < text.length) {
    const head = text[index] === '{' ? /^\{(\w+),\s*plural,\s*/.exec(text.slice(index)) : null;
    if (!head) {
      out += text[index];
      index += 1;
      continue;
    }

    const bodyStart = index + head[0].length;
    let depth = 1;
    let end = bodyStart;
    while (end < text.length && depth > 0) {
      if (text[end] === '{') {
        depth += 1;
      } else if (text[end] === '}') {
        depth -= 1;
      }
      end += 1;
    }

    const whole = text.slice(index, end);
    const count = Number(params[head[1]]);
    const branches = branchesOf(text.slice(bodyStart, end - 1));
    out += Number.isFinite(count) ? (branches[`=${count}`] ?? branches.other ?? whole) : whole;
    index = end;
  }

  return out;
}

/** Applies the deck's two constructs to one template. Exported because formatting is testable without React. */
export function format(templateText: string, params?: TParams): string {
  let text = params ? resolvePlural(templateText, params) : templateText;
  if (params) {
    text = text.replace(/\{(\w+)\}/g, (whole, name: string) =>
      name in params ? String(params[name]) : whole
    );
  }
  return text;
}

const LANGUAGE_KEY = 'terraguard.language';

function readStoredLanguage(): Language {
  const stored = localStorage.getItem(LANGUAGE_KEY);
  return LANGUAGES.includes(stored as Language) ? (stored as Language) : DEFAULT_LANGUAGE;
}

interface I18nValue {
  lang: Language;
  setLang: (lang: Language) => void;
  t: (key: StringKey, params?: TParams) => string;
}

const I18nContext = createContext<I18nValue | null>(null);

export const I18nProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [lang, setLangState] = useState<Language>(readStoredLanguage);

  // The document language drives hyphenation, font fallback and the screen reader's voice, so it follows
  // the switch rather than the `lang="vi"` the prototype hard-coded.
  useEffect(() => {
    document.documentElement.lang = lang;
  }, [lang]);

  const setLang = useCallback((next: Language) => {
    localStorage.setItem(LANGUAGE_KEY, next);
    setLangState(next);
  }, []);

  const t = useCallback(
    (key: StringKey, params?: TParams) => format(template(lang, key), params),
    [lang]
  );

  const value = useMemo(() => ({ lang, setLang, t }), [lang, setLang, t]);
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
};

export function useI18n(): I18nValue {
  const value = useContext(I18nContext);
  if (!value) {
    throw new Error('useI18n must be used inside I18nProvider');
  }
  return value;
}
