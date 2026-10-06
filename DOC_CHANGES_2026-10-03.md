# Documentation re-base — the TERRAGUARD prototype (2026-10-03)

**Scope of this change:** documentation only. No source, configuration or build file was modified, and nothing was
committed.

**Trigger.** [`docs/TERRAGUARD_SUMMARY.md`](docs/TERRAGUARD_SUMMARY.md) describes a React/Vite prototype of the
keeper's screens, and the repository's own documents still described a single vanilla-JS dashboard living at
`web/`. This change makes the doc set describe what is actually on disk.

---

## 1. What the summary turned out to be

The summary is an **accurate** description of `web/src` — the eight routes, the theme tokens and the libraries all
match. The mismatch was everywhere else.

| Claim in the summary | Verified how | Result |
|---|---|---|
| React 19 + Vite + Tailwind v4 + TypeScript, `react-router-dom` / `recharts` / `lucide-react` | `web/package.json` | ✅ matches |
| 8 screens at `/`, `/dashboard`, `/terrariums`, `/terrariums/:id`, `/devices`, `/alerts`, `/history`, `/settings` | `web/src/App.tsx` | ✅ matches |
| Vietnamese-only UI, friends of `#0b1a0d` / `#4a9e6a` / `#e87f3a` family | `web/index.html`, `web/src/index.css` | ✅ matches |
| Mock data: 3 terrariums, 9 devices, 4 alerts, 24 history points each | `web/src/data/mockData.ts` | ✅ 3 / 9 / 4, and `for (let i = 23; i >= 0; i--)` = 24 points |
| "Không có backend — mọi dữ liệu là mock" | grep for `fetch`, `axios`, `XMLHttpRequest`, `WebSocket`, `EventSource`, `SignalR`, `import.meta.env`, `localStorage` | ✅ zero matches — no request can leave the page |

---

## 2. What was wrong instead

1. **The doc set described one dashboard at `web/`.** The prototype took the root of `web/` and the M1 pages moved
   to `web/legacy/`; every reference still pointed at the old paths.
2. **Compose broke a documented path.** `docker-compose.yml` mounts `./web`, so `http://127.0.0.1:8081/` serves the
   Vite dev entry (`<script type="module" src="/src/main.tsx">`) as a blank page, and `/wallboard.html` is a 404.
   Recorded as **BUG-03**, open, fixed by roadmap task 4.12.
3. **The "no build output in git" rule was quietly violated.** `web/dist/` (3 files) is tracked; the M1 gate row
   claimed "no committed build output".

---

## 3. Decisions recorded

| Id | Decision | Where |
|---|---|---|
| **ADR-017** | `web/` holds the React prototype (mock data, Vietnamese only, outside CI); `web/legacy/` holds the M1 static dashboard (real API, no build step); `web/dist/` is committed on purpose to keep npm out of the demo path | [`docs/07-appendices/01-adr-log.md`](docs/07-appendices/01-adr-log.md) |
| **ADR-003** | Marked **superseded in part** by ADR-017 — the "no build step" half only. Id kept, per the append-only rule | same file |
| **BUG-03** | Compose `web` mount serves the wrong page after the prototype landed — S2, open | [`docs/05-release/03-risks-assumptions-decisions.md`](docs/05-release/03-risks-assumptions-decisions.md) §4 |
| **4.12 / 4.13 / 4.14** | New M4 tasks: fix the serving layout · decide the prototype's fate in an ADR · label the prototype as mock data and reconcile its palette and copy | [`docs/03-implementation/07-implementation-roadmap.md`](docs/03-implementation/07-implementation-roadmap.md) |
| **QA 5.12** | New drill: the served page is a working page and the mock-data label is present | [`docs/04-quality/03-manual-qa-checklist.md`](docs/04-quality/03-manual-qa-checklist.md) §5 |

No existing id was renumbered or reused.

---

## 4. Files changed (18)

| File | Change |
|---|---|
| [`README.md`](README.md) | Component table split into dashboard + prototype; §5 rewritten around the two surfaces; repository conventions record the `web/dist` exception; M1 gate rows annotated with what moved; status paragraph added |
| [`docs/README.md`](docs/README.md) | ADR range → ADR-017; doc-map entry for `03-implementation/05`; revision note with the touched files; doc set is now 36 files |
| [`docs/07-appendices/01-adr-log.md`](docs/07-appendices/01-adr-log.md) | Header → ADR-017; ADR-003 supersession note; full **ADR-017** entry (context → decision → consequences → rejected) |
| [`docs/05-release/03-risks-assumptions-decisions.md`](docs/05-release/03-risks-assumptions-decisions.md) | ADR index rows; **BUG-03**; 2026-10-03 documentation-log row |
| [`docs/03-implementation/01-tech-stack-and-setup.md`](docs/03-implementation/01-tech-stack-and-setup.md) | Pinned-stack rows for the prototype and for Node (22.x, prototype only); `web/` tree now matches disk; §4.4 splits into the two run paths |
| [`docs/03-implementation/02-project-structure-and-conventions.md`](docs/03-implementation/02-project-structure-and-conventions.md) | §4 split: legacy conventions vs prototype conventions ("no API layer", "no engine rules here") |
| [`docs/03-implementation/05-app-state-management-and-realtime.md`](docs/03-implementation/05-app-state-management-and-realtime.md) | §6 split into 6.1 (dashboard: target tree vs what exists today) and 6.2 (prototype: no store, no API client) |
| [`docs/03-implementation/07-implementation-roadmap.md`](docs/03-implementation/07-implementation-roadmap.md) | New **Prototype baseline** section; M4 web-surface note, tasks 4.12–4.14, DoD clause; `DOC` allocation row |
| [`docs/03-implementation/08-work-distribution-w5-w10.md`](docs/03-implementation/08-work-distribution-w5-w10.md) | 4.12/4.13/4.14 absorbed into W5/W6/W7; `DOC` 80 → **90 h**, project 345 → **355 h**; weekly and workstream totals re-balanced; cut list updated |
| [`docs/02-design/01-architecture.md`](docs/02-design/01-architecture.md) | L4 Experience row names both web surfaces |
| [`docs/02-design/04-ui-ux-and-navigation.md`](docs/02-design/04-ui-ux-and-navigation.md) | §1.2 note: what the prototype does and does not implement, and its third palette |
| [`docs/05-release/01-build-and-release.md`](docs/05-release/01-build-and-release.md) | §5 rewritten: the shippable dashboard vs the prototype, BUG-03 caveat, `dist` freshness evidence |
| [`docs/06-report/01-report-outline.md`](docs/06-report/01-report-outline.md) | R6.7 counts both surfaces and forbids quoting a prototype number as a measurement |
| [`docs/06-report/02-contribution-table.md`](docs/06-report/02-contribution-table.md) | Artifact 19 (the prototype); web workstream label; doc set → 36 files |
| [`docs/06-report/snapshots/README.md`](docs/06-report/snapshots/README.md) | Path-move note; reproduce command now mounts `web/legacy` |
| [`docs/04-quality/02-test-cases.md`](docs/04-quality/02-test-cases.md) | TC-I-15 names `web/legacy/js/i18n.js` and records the prototype's exclusion |
| [`docs/04-quality/03-manual-qa-checklist.md`](docs/04-quality/03-manual-qa-checklist.md) | New drill 5.12 |
| [`docs/04-quality/04-requirements-traceability-matrix.md`](docs/04-quality/04-requirements-traceability-matrix.md) | FR-08 module path → `web/legacy/js/live.js` |

### Roadmap re-baseline in numbers

| | Before | After |
|---|---|---|
| Web workstream | 45 h (4.10) | 55 h (4.10, 4.12–4.14) |
| `DOC` total | 80 h | 90 h |
| Project total | 345 h | 355 h |
| `DOC` weekly | 12 · 12 · 14 · 15 · 12 · 15 | 15 · 15 · 18 · 15 · 12 · 15 |

---

## 5. Verification performed

| Check | Result |
|---|---|
| Prototype builds from a clean install | `npm ci` → `tsc && vite build` ✅ 2541 modules, `tsc` clean |
| Committed `web/dist` is current | Rebuild reproduced the same asset names (`index-C7gXR99i.js`, `index-CUm0g7Lt.css`, 765 kB / 213 kB gzip) — now recorded in `05-release/01` §5 |
| Paths referenced in the new text exist | `web/legacy/js/{api,store,i18n}.js`, `web/legacy/js/pages/live.js`, `web/src/pages/*.tsx`, `web/dist/*`, `docs/TERRAGUARD_SUMMARY.md` — all present |
| ADR / task / drill ids do not collide | ADR-017 is new; 4.12–4.14 are new; QA 5.12 is new; nothing renumbered |
| Markdown tables intact | Automated pipe-count check across all 18 changed files → 0 mismatches |
| Doc set file count | 36 `.md` files under `docs/` excluding `snapshots/` — matches the claim |
| Line endings | Edited files normalised to LF, per `.gitattributes` (`* text=auto eol=lf`), so the diff is content-only: **286 insertions / 57 deletions** across 18 files |
| Temporary files | `web/node_modules/` and the temp build directory removed; `git status` shows the 18 modified documents plus this summary file, and nothing else |

---

## 6. Deliberately not done

| Item | Why |
|---|---|
| `docker-compose.yml` unchanged | The choice was "docs first"; the mount fix is now task 4.12 with BUG-03 as its record |
| `.gitignore` unchanged, `web/dist` still tracked | ADR-017 accepts it as a deliberate exception so the nginx demo needs no Node toolchain |
| The prototype's code untouched | The task was to make the docs match the code, not the reverse |
| Nothing committed | No commit was requested |

---

## 7. Open items for the team

1. **4.12** — point compose at the prototype build and keep the M1 pages reachable at `/legacy/*`, then close BUG-03.
2. **4.13** — decide whether the prototype becomes the dashboard (a rewrite of its data layer) or stays a mock-data
   reference; record the answer as an ADR.
3. **4.14** — label the UI as mock data, and either reconcile `web/src/index.css`'s status→colour palette with
   `02-design/04` §3 or write down why it differs; decide the Vietnamese-only copy.
4. The M1 hardware line (`03-implementation/07` tasks 1.5 / 1.6) is untouched and still the only thing keeping M1
   open.
