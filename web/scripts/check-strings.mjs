#!/usr/bin/env node
/*
 * Key-parity check for the shared copy deck (roadmap 4.18; `TC-I-15`'s i18n half).
 *
 * `TC-I-15` asks that "the i18n key sets of the app and of the web client are identical". Both clients now read
 * `app/lib/l10n/app_{en,vi}.arb`, so the sets are identical by construction — which moves the failure mode from
 * "two lists have drifted" to "a key is missing or misspelled". That is what this script refuses:
 *
 *   1. the two locales must define exactly the same keys (a string translated in one language only);
 *   2. every key a screen names must exist in the deck — `t('foo')` anywhere under `src/`;
 *   3. every deck key named by a *table* (`lib/messages.ts`, `lib/status.ts`, `lib/format.ts`) must exist too,
 *      because those tables are how codes become sentences and a typo there renders the key itself on screen.
 *
 * It runs in CI next to `tsc` and `vite build`. Exit code 1 on any problem, with the offending key printed: a
 * silent fallback to the key's own name is visible in the browser but invisible in a pipeline log.
 */

import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const webRoot = join(here, '..');
const repoRoot = join(webRoot, '..');
const l10nDir = join(repoRoot, 'app', 'lib', 'l10n');

/** Files whose object literals map codes to deck keys, and therefore cite keys that must exist. */
const TABLE_FILES = ['src/lib/messages.ts', 'src/lib/status.ts', 'src/lib/format.ts'];

/** Values in those tables that are not deck keys (markup, class names, vocabulary). */
const TABLE_ALLOW = new Set([
  'inline-flex',
  'min-w-0',
  'online',
  'offline',
  'maintenance',
  'InRange',
  'OutOfRange',
  'Critical',
  'Unavailable',
  'Maintenance',
]);

function keysOf(file) {
  const arb = JSON.parse(readFileSync(file, 'utf8'));
  return Object.keys(arb).filter((key) => !key.startsWith('@'));
}

function walk(dir) {
  const out = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      out.push(...walk(full));
    } else if (/\.tsx?$/.test(entry)) {
      out.push(full);
    }
  }
  return out;
}

const en = keysOf(join(l10nDir, 'app_en.arb'));
const vi = keysOf(join(l10nDir, 'app_vi.arb'));
const deck = new Set(en);
const problems = [];

for (const key of en.filter((key) => !vi.includes(key))) {
  problems.push(`locale mismatch: "${key}" is in en but not vi`);
}
for (const key of vi.filter((key) => !en.includes(key))) {
  problems.push(`locale mismatch: "${key}" is in vi but not en`);
}

const sources = walk(join(webRoot, 'src'));

// 1 + 3: keys named by the code, either as a `t('…')` literal or as a value in one of the mapping tables.
for (const file of sources) {
  const text = readFileSync(file, 'utf8');
  // Normalised to forward slashes: `relative()` returns backslashes on Windows, and a table file that silently
  // fails to match is a check that silently passes.
  const shown = relative(webRoot, file).replaceAll('\\', '/');

  for (const match of text.matchAll(/\bt\(\s*'([^']+)'/g)) {
    if (!deck.has(match[1])) {
      problems.push(`${shown}: t('${match[1]}') is not a deck key`);
    }
  }

  const inTable = TABLE_FILES.some((table) => shown.endsWith(table));
  if (inTable) {
    for (const match of text.matchAll(/:\s*'([a-z][A-Za-z0-9]*)'/g)) {
      const value = match[1];
      if (!deck.has(value) && !TABLE_ALLOW.has(value)) {
        problems.push(`${shown}: table value "${value}" is not a deck key`);
      }
    }
  }
}

if (problems.length > 0) {
  console.error(`i18n check failed (${problems.length}):`);
  for (const problem of problems) {
    console.error(`  - ${problem}`);
  }
  process.exit(1);
}

console.log(`i18n check passed: ${en.length} keys, identical in vi and en, every referenced key defined.`);
