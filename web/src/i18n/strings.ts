/*
 * The copy deck, imported straight from the app's ARB files (ADR-019, task 4.18).
 *
 * `TC-I-15` requires the app's and the web client's i18n key sets to be *identical*, and the old
 * `web/legacy/js/i18n.js` failed it by 36 keys because it kept its own list. The fix is not to compare two
 * hand-maintained lists, it is to have one list — which is what this import does. `app/lib/l10n/app_*.arb`
 * is the project's copy deck; the `app_` prefix is historical and the Flutter side is one of its two
 * readers.
 *
 * They arrive through Vite's `?raw` because `.arb` is JSON that TypeScript will not resolve as a module
 * (`resolveJsonModule` covers `.json` only), and a generated copy under `src/` would reintroduce exactly
 * the drift this is here to remove. The parse is at module load, from a file that ships inside the bundle.
 *
 * A key the deck lacks renders as `undefined`-free silence: `template()` falls back to the key's own name,
 * so a missing translation is visible on screen instead of blank. `web/scripts/check-strings.mjs` — run in
 * CI — refuses both directions: a key used in TSX that no ARB carries, and an ARB whose two locales
 * disagree.
 */

import enRaw from '../../../app/lib/l10n/app_en.arb?raw';
import viRaw from '../../../app/lib/l10n/app_vi.arb?raw';

/** The languages the deck carries, and the default (`02-design/04` §7: vi default, en available). */
export const LANGUAGES = ['vi', 'en'] as const;
export type Language = (typeof LANGUAGES)[number];
export const DEFAULT_LANGUAGE: Language = 'vi';

type Deck = Record<string, string>;

const en = JSON.parse(enRaw) as Deck;
const vi = JSON.parse(viRaw) as Deck;

const decks: Record<Language, Deck> = { en, vi };

/** Every string key the deck defines — `@@locale` and `@key` metadata entries are not strings. */
export type StringKey = string;

/** The raw template for a key, or the key itself when the deck has no such entry. */
export function template(lang: Language, key: StringKey): string {
  return decks[lang]?.[key] ?? decks[DEFAULT_LANGUAGE][key] ?? key;
}

/** Every string key, sorted — the CI check compares the two locales and the TSX usage against this. */
export function allKeys(): string[] {
  return Object.keys(decks[DEFAULT_LANGUAGE])
    .filter((key) => !key.startsWith('@'))
    .sort();
}

/** Raw decks, for the check script's message helpers and for tests. */
export const rawDecks = { en, vi };
