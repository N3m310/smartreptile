# Implementation summary — 2026-10-17

**Branch** `main` · **Base** `main` @ `70c9690` · **Commit** `a87829d` (227 files) · **Scope** two workstreams:
M3 task **3.5** (the notification layer, 2026-10-16) and the **role/permission + alert-family re-framing** with the
administrator's dashboard (2026-10-17). Both were requested in this session, and both are in the one commit because
the earlier work had never been committed.

This file is the narrative record of the session: what was built, how it was verified, which defects were found on
the way, and what is still open. The documents remain the source of truth — each section points at the file that now
carries the detail.

---

## 1. What was delivered

| Area | Task | State | Evidence |
|---|---|---|---|
| Notifications | **3.5** — policy, cadence, dispatcher, two channels, inbox, Mailpit | Done | 37 new unit cases + 5 integration cases; a live sweep wrote exactly one row and one `notified` timeline entry |
| Identity | The permission matrix as data: `Role`, `Permission`, `RolePermission`, `User.RoleId` | Done | Migration `20261016161825_AddRolesAndPermissions`; 25 `RoleAdminServiceTests`, 4 `IdentitySeederTests` |
| Alerts | Two families (`enclosure`, `species`) as a property of the metric | Done | Migration `20261016162814_AddAlertFamily`; 24 `AlertFamilyTests`; `?family=` verified live |
| Accounts | Full CRUD at `/api/v1/users` with the two guards | Done | 21 `UserAdminServiceTests`; the live round trip below |
| Dashboard | `/api/v1/admin/*` — the cross-account get-all surface | Done | 13 `AdminServiceTests`, 6 `AdminStoreTests`; the live overview |
| Matrix API | `/admin/roles`, `/admin/permissions` — the matrix as an editable screen | Done | `RoleEndpoints.cs`; live create → edit → delete, `409` for a system role |
| Web | `AdminOverview`, `AdminAccounts`, `AdminRoles` + permission gating | Done | `tsc`, `check:strings`, `vite build`; `web/dist` rebuilt |
| App | The same surface on the phone, 17 new files | Done | `flutter analyze` clean, `flutter test` 182 passed |
| Docs | 20 documents + `docs/README.md`, both READMEs and the ADR log | Updated | §7 |

## 2. M3 task 3.5 — the notification layer

M3's definition of done is one induced excursion producing *an alert **and** a notification*. The alert half had been
real since 3.2; nothing delivered anything, so an engine that could raise an alarm could tell nobody.

Built as two pure functions plus one sweep, because "why was I not told" is a question about rules and the sweep is
the only part that can be wrong in a way rules cannot see:

- `NotificationPolicy.Judge` — §02-design/05 §4's whole matrix as one static function (recovery notice, silence,
  maintenance, severity floor, quiet hours with Critical bypassing them, the cap).
- `NotificationCadence` — the 60-minute repeat cooldown, the 10-per-hour cap, BR-13.5's 5 s / 30 s / 2 min backoff,
  §6's 30 / 60 / 120-minute escalation ladder, and the digest's summarising sentence.
- `NotificationLog` — one row per dispatch attempt, which is the delivery record *and* the in-app inbox row.
- `NotificationDispatcher` — a 15-second sweep over the alerts that could owe somebody a message: verdict, step,
  channel, retry, digest.
- Two channels behind `INotificationChannel`: FCM HTTP v1 with a service-account assertion, and SMTP.
- Three inbox routes, and the `notified` entry `02-design/05` §6 was written for on the alert timeline.
- **Mailpit** added to `docker-compose.yml`, which settles the out-of-band-channel decision left open on 2026-10-07;
  `R-13` no longer leans on the inbox alone.

Six readings are recorded where they belong rather than here (`02-design/05`, the roadmap's 3.5 block): the sweep
instead of a subscription, one row per attempt, the `NotificationLog.Step` column the sketch lacked, the hourly cap
rationing the noise and not the record, quiet hours judged in the recipient's zone with a zero-length window treated
as none, and the digest identified by the instant it records.

## 3. The re-framing — three parts, two of them model changes

The request was one sentence in three parts: two separate alert families, a `Role` table saying which data each role
reaches, and an administrator dashboard with get-all, account CRUD and the API wired into it.

### 3.1 The permission matrix became data

`UserRole` and `User.Role` are gone. A role is a row in `Role`, a permission is a row in `Permission`, and the grant
between them is a row in `RolePermission`. The **catalogue** — the sixteen things the software can be asked to do —
stays in code (`Permissions.cs`) because a permission the software does not implement cannot be granted by inserting
a row; only the *mapping* moved into data.

A **policy name is a permission code**: `AuthorizationPolicies.AddPermissionPolicies()` registers one policy per
catalogue entry and every route names the code it needs, so `Owner`, `Technician` and the composite `OwnerOrAdmin`
are deleted and a role name appears nowhere in the API. `Admin`'s seed is a superset of `Owner`'s by construction,
which is what retired the "Admin is not a fourth level" argument `02-design/06` §3 had been qualifying since
2026-10-13.

Cross-account reach is two permissions — `data.read.any` / `data.write.any` — held by `Admin` alone. That is what
"Admin = toàn bộ dữ liệu" means as a property of a permission set rather than a branch in a service. Ownership is
still `Terrarium.UserId`: there is no membership table, and the cross-account reads live in a separate store with no
owner parameter, so a keeper's scoped query cannot be widened by a flag.

Permissions travel in the access token (one `perm` claim per code, `ver` = 2) and `GET /auth/me` returns `roleId`
plus `permissions`, so both clients gate screens on codes. Staleness is bounded and documented: a role change, a
password reset and a delete end the account's refresh tokens; an edit to a role's *permission set* does not, and
takes effect within one access-token lifetime.

### 3.2 Alerts split into two families

An excursion has two possible subjects, so an alert is either `enclosure` — the enclosure's own humidity, light, and
the board in it (silence, sensor fault, clock skew) — or `species` — the animal's temperature, UV and basking
surface, judged against its species profile.

The split is a property of the **metric** (`MetricDefinition.Family`), persisted on the row (`Alert.Family`,
back-filled from the metric), published on every alert in REST and on the `alertChanged` payload, and filterable with
`?family=enclosure|species` on both the keeper's list and the administrator's. It is stored rather than derived for
the same reason `BandMin`/`BandMax` are, and for one more: a device-level alert has no metric to derive it from.

### 3.3 The administrator's surface

`/api/v1/admin/*` (overview with the deployment counts, every account with its holdings, and both families with their
metric keys; enclosures; boards; alerts), `/admin/roles` + `/admin/permissions` for the matrix, and the account CRUD
at `/users`: create, edit, reset password, change role, disable, enable, soft delete, and `?purge=true` when nothing
references the account. Two guards: `409 self_change_refused`, and `409 last_administrator_refused` — evaluated by
counting accounts whose role grants `user.manage`, not by comparing role names.

Both clients gained the surface: three web pages behind a `RequirePermission` guard, and five Flutter screens behind
`hasAnyPermission`, with the three old `role === 'Owner'` gates replaced by permission checks.

### 3.4 The migrations

`20261016161825_AddRolesAndPermissions` creates the three tables, seeds the sixteen permissions, the four roles and
their thirty-nine grants, adds `User.RoleId` **nullable**, back-fills it from the old enum (`0 Owner`, `1 Technician`,
`2 Viewer`, `3 Admin`, anything else `Owner`), then tightens it and drops the old column. `20261016162814_AddAlertFamily`
adds `Alert.Family` with an enclosure default and back-fills it from the metric.

Both were applied from scratch, reverted and re-applied against SQL Server Express, and a restored copy of the
pre-change database proved the back-fill survives a `Down`/`Up` round trip with the accounts unchanged.

## 4. An incident I caused, and repaired

While bulk-editing four endpoint files with a PowerShell script, a nested-array bug made `$pair[0]` return a *string's
first character* instead of the first element, so every `A` became `u` and every `P` became `e` in
`TerrariumEndpoints.cs`, `AlertEndpoints.cs`, `SpeciesProfileEndpoints.cs` and `AuditEndpoints.cs`.

Diagnosed by comparing character histograms against `git HEAD` (which proved those two letters were the only
substitutions and that the transform is not injective, so a blind inverse was impossible), then repaired with a
vocabulary-verified inverse: a token is restored only when exactly one `A`/`P` combination reproduces a word of the
project's vocabulary. `AlertEndpoints.cs` and `AuditEndpoints.cs` came back **byte-identical to `git HEAD`** — the
strongest available proof of exact restoration. The eight `*.repaired*` scratch copies were deleted before the push.

## 5. Defects found and fixed

| Where found | Defect | Fix |
|---|---|---|
| 3.5's live run | The dispatcher's *first* sweep ran before `DatabaseInitializer` applied migrations and logged a failed query | The first tick waits one interval, the shape the silence watchdog already uses |
| The re-framing's live run | The account list answered **zero permissions for every row** — `EfUserStore` loaded `Role` but not its grants, which a client reads as "this account can do nothing" | The three account reads load both; `AccountReadTests` pins it against real SQL Server |
| The re-framing's live run | The role list reported **0 holders** for every role (the count was never read) | `IUserStore.CountUsersByRoleAsync` reads them in one grouped query |
| Writing the docs | `silence.write` was granted to three roles but named by **no route** — a permission that does nothing | The two silence writes require `silence.write` |
| Writing the docs | 23 OpenAPI descriptions still said "Requires the Owner role." / "Requires the Technician role or above." | They name the permission the route enforces; the 403 body's title says a permission is missing |
| Writing the docs | `TerrariumService`'s cross-account flag was still called `isAdmin`, and `02-design/06` named a parameter that did not exist | Renamed `mayReachAnyAccount`; the docs follow |
| The suite | `AlertLifecycleTests`' cursor-paging case had been red since 2026-10-13 | The *expectation* was wrong about its own fixture (four alerts seeded, one asserted on page two); it now asserts the invariant directly |
| Both clients | Cross-account EF projections ordered or filtered the response record itself, which the provider cannot translate | Every such query selects an anonymous type and maps after materialisation — recorded in `EfAdminStore`'s header |

## 6. Verification

| Gate | Result |
|---|---|
| `dotnet build SmartReptile.sln -c Release` | 0 warnings, 0 errors |
| Unit tests | **947 passed** (from 882 at 3.5: `RoleAdminServiceTests`, `AdminServiceTests`, `AlertFamilyTests`, `AccountReadTests`, plus additions to `UserAdminServiceTests`, `JwtAccessTokenServiceTests`, `AuthServiceTests`, `AuditEntityTests`, `SampleEvaluatorTests`) |
| Integration tests | **55 passed, 0 failed** (from 42 with one permanently red) |
| `dotnet format --verify-no-changes --severity error` | Clean, for the solution and for the integration project separately |
| `flutter analyze` / `flutter test` | No issues; **182 passed** (from 19) |
| `tsc` / `check:strings` / `vite build` | Exit 0; **509 keys** identical in both locales; bundle rebuilt |
| CI (`main`) | All six jobs green — the first green `main` run since 2026-10-13 |

**Live run 1 (2026-10-16, the notification layer).** A registered `Owner`, a terrarium and a seeded excursion
produced exactly one dispatch row and one inbox entry — `"Linh's gecko box · Nhiệt độ quá cao"`, the peak rather than
the trigger, with the band — plus a `notified` timeline entry beside the `opened` one, `notifications_sent_total: 1`,
`204` on read (and `204` again on a second call), `404` for an id that is not the caller's, and one row left after a
second sweep.

**Live run 2 (2026-10-17, the re-framing).** On a fresh database that applied all **14** migrations and seeded the
administrator from configuration: the administrator's token carried **16** permission claims against a registered
keeper's **11**, and `/auth/me` returned `roleId` plus the same list. `/admin/overview` answered
`accounts 3, terrariums 1 (1 with a board), devices 1 online, alertsOpen 2 (1 enclosure + 1 species), 1 critical,
speciesProfiles 4`, with each account's holdings and both families' metric keys. `/admin/terrariums`, `/admin/devices`
and `/admin/alerts?family=` returned the owner, the board's health and the correctly split rows, and `?family=habitat`
was `400 family_invalid`. A keeper's token got **`403` on all five administrative routes**. The account surface
round-tripped: create → `201` with the role's permission set, `PATCH role` → the set changed, an unknown code →
`400 role_invalid`, disable → login `401`, enable → login `200`, reset-password → the new password worked, soft
delete → `200`, `?purge=true` → `200` and a later read `404`, self-edit → `409 self_change_refused`. The role surface:
create → `201`, edit → `200` with the grant set replaced, a system role → `409`, a custom role → `200` then `404`.

## 7. Documentation

**28 files**, of which the substance changed in three: `02-design/06` §3 was rewritten as the authority on the model
(the catalogue, the role→code table, which data each role maps to, the scope decision, enforcement, guards, and the
seeder's asymmetry), **FR-02** was rewritten on permissions, and the new **FR-19** was added for the administrator's
account management and deployment dashboard.

Also updated: `01-product/01`, `01-product/02` (RBAC, permission, cross-account and alert-family entries),
`01-product/05`, `02-design/01`, `02-design/02` (the ERD, the `Role`/`Permission`/`RolePermission` section,
`Alert.Family`, the metric families), `02-design/04` (the two families as screens, the administrator's pages on both
clients), `02-design/05` §2 (the families and the `Family` column on the source matrix), `03-implementation/03`,
`03-implementation/05`, `03-implementation/07` (the re-framing block: piece table, decisions, the EF projection
lesson, evidence), `04-quality/01`, `04-quality/02` (TC-U-33, TC-U-36, TC-W-12), `04-quality/03` (the account surface
and two new rows for the matrix and the families), `04-quality/04` (FR-02, FR-12, FR-19), `05-release/01`,
`05-release/03` (a new log row plus "superseded in part" markers on the two entries this change qualifies),
`06-report/01`, `07-appendices/01` (**ADR-024** and **ADR-025**), `07-appendices/02` (§3.16–§3.18, `User.RoleId`,
`Alert.Family`, the DDL of both migrations, the seed table), `07-appendices/03` (the permission column on every
route, the `family` filter, §4.7 rewritten and §4.8 added), both READMEs, `docs/README.md`'s seventeenth pass,
`scripts/dump-database.ps1` and `TASKS_2026-10-14.md`.

## 8. What is not verified, and what is left open

- **FCM push is still unverified** — it needs a Google service-account file and a device registration token, neither
  of which exists on this machine. The path is covered by the assertion test and the fake-channel cases only.
- **The new administrative screens were never clicked.** The API behind them was driven end to end with `curl`, and
  the web client had been driven in a browser before these three pages existed; the Flutter screens have not been
  run against a live API at all.
- **GitHub raises four advisory annotations** on the backend job ("possible null reference argument for `source`")
  from the `!`-suppression style the test suite already uses. Warnings, not failures.
- ~~**`.github/workflows/ci.yml` opens with "One pipeline, five gates"** and lists five, but there are six jobs —
  `web` is missing from that list, and the repository README's CI row inherited the same omission.~~ **Closed after
  this commit** by `0e19744 docs(ci): the pipeline has six gates, not five` — the workflow header and the README's CI
  row now say six and name `web`.
- **`species.write` is `Admin`-only**, so a self-registered `Owner` cannot author the shared species catalogue. That
  is the design (the catalogue is data every terrarium is judged against), but it is one line in `Role.cs` to change
  if keepers should author profiles too.
