# Implementation summary — 2026-10-13

**Branch** `main` · **Base** `main` @ `d0ab306` · **Scope** M3 task 3.9 (the species-profile catalogue), the defect
the report behind it exposed (`BUG-11`), the Swagger defect that preceded it (`BUG-10`), and the web create form
that 4.16 had been waiting for. The commit also carries the two M3 pieces finished earlier the same day —
3.1's threshold write half and the audit surface (§7).

This file is the narrative record of the session: what was built, how it was verified, which defects were found on
the way and which of them were fixed. The documents remain the source of truth — each section points at the file
that now carries the detail.

---

## 1. What was delivered

| Area | Task | State | Evidence |
|---|---|---|---|
| Species profiles | **3.9** — `GET`/`POST`/`POST {id}/duplicate`/`PATCH {id}`/`DELETE {id}` on `/api/v1/species-profiles` | Done | 19 unit cases, five routes exercised live, `docs/07-appendices/03` §4.3 (as built) |
| Create path | **BUG-11** — a valid token naming an account this database does not have answered `500` | Fixed | `TerrariumServiceTests.Create_refuses_a_caller_that_is_not_an_account_of_this_database`, live 500 → 401 |
| Swagger | **BUG-10** — the login response was undocumented, so nothing said which token to paste | Fixed | `.Produces<AuthSession>` on login/refresh, `$ref` verified in the live document |
| Web | **4.16** — the create form, filled from the catalogue | Done | Wired and driven in a real browser; `web/dist` rebuilt |
| Web | **4.21** — the catalogue's own screen: list, create, edit, duplicate, delete | Done, later the same day (§11) | Driven in a real browser against the API and SQL Server; `403 builtin_immutable` and `409 profile_in_use` rendered from their codes; 54 deck keys, 270 total |
| Docs | The four documents that claimed the catalogue was unbuilt, the roadmap row for 3.9, the new problem code | Updated | §8 |

## 2. The report that started it

A keeper testing from Swagger reported the same symptom twice: after signing in, an authenticated call came back
`401 unauthenticated` (the first time because no operation declared the bearer requirement — `BUG-08`; the second
because the login response named no token field — `BUG-10`), and then that `POST /terrariums` answered
`500 internal_error` for a body whose `speciesProfileId` was real. The screenshots showed the pair that identified
the third defect: the write failed while `GET /terrariums` answered `200 {"items":[]}` to the same caller.

None of the three was a missing feature. The report also asked the question that produced the feature: *how does a
client learn a `speciesProfileId` at all?* Nothing exposed one. The web client had already refused to wire a create
form rather than hard-code a seeded id (roadmap 4.16, `04-quality/02` B3), and the only way to find one was to open
SSMS.

## 3. Task 3.9 — the species-profile catalogue (FR-10)

The five routes, built as one catalogue:

| Piece | File |
|---|---|
| Contracts, the outcome type and the field-level refusal shape | `backend/src/SmartReptile.Application/Species/SpeciesProfileContracts.cs` |
| The use cases: list, create, duplicate, patch, delete | `backend/src/SmartReptile.Application/Species/SpeciesProfileService.cs` |
| The persistence port | `backend/src/SmartReptile.Application/Abstractions/ISpeciesProfileStore.cs` |
| The EF adapter | `backend/src/SmartReptile.Infrastructure/Species/EfSpeciesProfileStore.cs` |
| The HTTP surface and its code → status mapping | `backend/src/SmartReptile.Api/Endpoints/SpeciesProfileEndpoints.cs` |
| The audit verbs (`profile.created`/`updated`/`deleted`/`duplicated`) and the `SpeciesProfile` entity name | `backend/src/SmartReptile.Domain/Auditing/` |

Readings the build settles, all recorded in `07-appendices/03` §4.3 because a client written against either reading
would break:

- **The list carries the bands, and is unpaged.** Four built-ins plus whatever the account authored; the picker in
  `POST /terrariums` needs all of them at once.
- **Create and duplicate answer `200` with the profile rather than `201` with a `Location`** — there is no read-by-id
  route to point one at, the reading the silence routes took. A duplicate is named `"<source> (copy)"` when the body
  names nothing and keeps its source's `sourceRef`, because a copy whose numbers lost their citation would be a
  second, quieter way of shipping an unsourced range.
- **`PATCH` replaces the band set when `bands` is sent**; omitted members are left alone and an empty string clears a
  text field. Half a band cannot be validated, so merging is not offered.
- **A built-in is `403 builtin_immutable`, a foreign profile is `404`, a profile in use is `409 profile_in_use`** —
  distinguished because the client does something different with each (offer a copy, reload, point at the
  terrariums). Soft-deleted terrariums count towards "in use": their rows still hold the foreign key.
- **Band validation is the domain's `ThresholdBand.Validate`**, the same call the override write path makes, so the
  rules live in one place and are enforced a second time by the table's CHECK constraints.
- **`BR-10.5`'s climate-zone warning stays the client's job**: the API ships `climateZone` for it to judge with.

One defect was found by running it rather than by reading it. Replacing the band set on a *tracked* profile made EF
emit `UPDATE [Threshold] … WHERE [Id] = …`, which matched zero rows and failed the whole save with a
`DbUpdateConcurrencyException` — the trap 3.1 had already recorded for `ThresholdOverride`, because a band carries a
client-assigned key. The fix is the same shape as that one: the rows are staged through the adapter
(`ISpeciesProfileStore.ReplaceBands` → `db.Thresholds.Add`), not by refilling the loaded collection. The unit suite
cannot see this class of defect; only a real database can.

## 4. BUG-11 — the create path reported an absent account as a server fault

`POST /terrariums` is the only write that keys a new row on the caller's own subject, and it passed
`ClaimsPrincipal.GetUserId() ?? Guid.Empty` straight into the insert. A token this server signed can still name an
account **this** database does not have — one minted against another database, or before that database was rebuilt —
and `Terrarium.UserId`'s foreign key then refused the row as a `DbUpdateException`, which the global handler reports
as `500 internal_error`. `GET /terrariums` answering `200 {"items":[]}` to the same caller was the tell.

`ITerrariumStore.UserExistsAsync` and a check at the top of `TerrariumService.CreateAsync` now answer
`401 unauthenticated` ("The access token names an account this server does not have. Sign in again."), and
`TerrariumEndpoints.Problem` maps that code to 401. Deliberately unchanged: account-scoped reads still answer an
empty list for such a caller, because there is nothing to expose and `200` is the honest answer.

**Not fully closed.** Which database the reporter's Swagger was talking to is still unknown from here: the
`SpeciesProfile` rows in the screenshots are not the ones on this machine's SQL Server Express (`Arid (desert)` is
`8CACDF0E…` there and `40A53A34…` locally, whose last sign-in was 04:13). The exception line in the API console
answers it: `FK_Terrarium_User_UserId` means "sign in again against the database you are writing to", and
`Invalid column name` would mean a missing migration instead.

## 5. BUG-10 — Swagger accepted a token and told nobody which one

The document declared the `Bearer` scheme (so the *Authorize* dialog appeared) but described `/auth/login`'s `200` as
a bare `OK`: the auth endpoints return anonymous bodies, so ASP.NET Core infers no response type and Swashbuckle has
nothing to document — and `GenerateDocumentationFile` is `false`, so the XML comments that do describe `AuthSession`
never reach the document either. The one operation that hands out the credential was the only one that named no
field of it.

`/auth/login` and `/auth/refresh` now declare `.Produces<AuthSession>(StatusCodes.Status200OK)` and `/auth/me`
declares `.Produces<UserProfile>`, generating a schema that matches the wire body field for field; the scheme, login
and refresh descriptions say which field the dialog wants, that Swagger UI adds the prefix itself, and that nothing
on the page rotates a 15-minute token.

## 6. 4.16's create form (web)

`web/src/pages/Terrariums.tsx` gains an Owner-only create panel — name, a species picker filled from
`GET /species-profiles`, location — that reloads the list and reports the created terrarium in place. Errors render
twice on purpose: next to the field that was refused and as a summary, through the existing code → sentence table.
`api/endpoints.ts` gains `createTerrarium` and `listSpeciesProfiles`, `api/types.ts` the catalogue types, and
`lib/messages.ts` the six field codes the create path can return. Seven deck keys were added to both locales and
`createTerrariumNotBuilt` was deleted — the sentence "creating a terrarium waits for the species-profile catalogue"
stopped being true the moment 3.9 landed, and a screen explaining a constraint that no longer exists is worse than
no note at all. `flutter gen-l10n` was re-run and `web/dist/` rebuilt, because the committed bundle is what nginx
serves.

## 7. Also carried in this commit (finished earlier the same day)

Recorded here because the commit carries it, not because it came from this session:

- **3.1's write half** — `PUT /api/v1/terrariums/{id}/thresholds` and `DELETE …/thresholds/{metric}`,
  `ThresholdSnapshot` and migration `20261013025001_AddThresholdSnapshots`, with the readings that build settled
  (all-or-nothing positional validation, a no-op when nothing changes, the snapshot resolving the set *after* the
  change). `docs/03-implementation/07` and `05-release/03` §5 carry the detail.
- **The audit surface** — `AuditLog`/`AuditAction`/`AuditEntity`, `AuditService`, `GET /audit` and its adapter,
  with the tests that keep the closed entity vocabulary and the writers in step.

## 8. Verification

- **Backend gates:** `dotnet build SmartReptile.sln` — 0 warnings; **698 unit tests pass**, which is +1 for `BUG-11`
  and +19 for the catalogue over the 678 the suite had when this session's work began; the integration project
  (deliberately outside the solution, BUG-09) builds in Release and runs **31 cases, 30 green** against a real SQL
  Server — the one failure is `AlertLifecycleTests.The_cursor_pages_newest_first_without_repeating_or_skipping`,
  which fails identically on a fresh database and is the pre-existing defect §5 of the ADR log already records.
- **`dotnet format --verify-no-changes --severity error` is clean on every file this session touched.** The
  `ENDOFLINE` failures that remain are in files nobody edited today (`AuditEndpoints.cs`, `AuditQueryTests.cs`,
  `AuditService.cs`, `AuditContracts.cs`, `IAuditStore.cs`, `EfAuditStore.cs`, `AuditFakes.cs`,
  `AuditServiceTests.cs`, `AuditEntityTests.cs`): they are CRLF in this working tree while `.editorconfig` asks for
  LF, and they were left alone rather than rewritten.
- **Live, against the real API and SQL Server** (each defect reproduced before it was fixed, on a scratch database
  that was dropped afterwards): the three minted subjects — unknown, absent and non-Guid — that answered `500` now
  answer `401`; a freshly registered account still answers `201`. The catalogue was exercised end to end — list,
  create, duplicate (citation carried over), a patch that replaced the band set, `403 builtin_immutable`, `404` for a
  foreign profile, `409 profile_name_taken`, `409 profile_in_use`, `204` once the terrarium moved to another
  profile, and `401` for an orphaned token — and `GET /audit?entity=SpeciesProfile` returned the new rows, which is
  the evidence that the vocabulary reached the filter as well as the writer.
- **OpenAPI document:** 43 operations, **30 carrying the bearer requirement and 13 anonymous**, every authenticated
  success documented with a schema — the check `BUG-10` exists to make.
- **Web:** `tsc --noEmit` clean, `check-strings.mjs` passes (184 keys, identical in both locales, every referenced
  key defined), `vite build` passes. The create form was then driven in a real browser against the running API:
  signed in, opened the panel, saw the four seeded profiles by name and scientific name, created one, and the card
  appeared in the list while the row landed in SQL Server with the chosen `SpeciesProfileId` and the deployment's
  default zone.

## 9. Found, recorded, not fixed

- **Foreign-key-shaped 500s are still opaque for writes other than this one.** The guard was added where the
  reported failure happened; a general "the subject of this token is not an account of this database" check belongs
  in the authentication pipeline, and that is a decision about cost per request, not a patch.
- **`insufficient_role` has no sentence** in `web/src/lib/messages.ts`, so a Viewer who calls a Technician route
  sees the code itself. Pre-existing, and no screen reaches it today because the writes are role-gated in the UI
  too.
- **The operation counts in older, dated rows** (`35 operations, 22 secured` in `04-quality/02`, `37` in
  `03-implementation/07`'s 3.1 row) no longer match the document. They are records of the day they were written,
  and rewriting them would edit history rather than correct a claim.

## 10. Deliberately still open

- **No screen manages species profiles.** *Closed later the same day — see §11.* What stays open is the mobile
  app's equivalent, and the fact that a screen is now the only way to author a profile: nothing imports or exports
  one, and `sourceUrl` (below) still has no writer. A profile-editing screen (and the mobile app's equivalent) is the next thing this unblocks, together
  with 4.20's threshold editor, which can now preview and author bands against a real catalogue.
- **`sourceUrl` is unused.** Custom bands carry `sourceRef`; nothing writes a URL, so the field stays reserved for a
  citation someone has actually opened.
- **`SystemDefault`,** the third tier of `BR-10.3`, still has no values anywhere in the doc set and is therefore
  still unimplemented.

## 11. Later the same day — the two screens the day's work unblocked

Both were built after this file was first written, for the same reason: the API had just landed and had no caller.

- **4.20's threshold editor** (commit `eb127bd`). `TerrariumDetail.tsx` gains a `ThresholdsSection` that reads
  `GET /terrariums/{id}/thresholds` and, for an Owner, writes `PUT /terrariums/{id}/thresholds` and
  `DELETE /terrariums/{id}/thresholds/{metric}`; every row renders the band, its `source`, the phase in force and
  `capturedAt`. `Settings.tsx`'s mock threshold form was deleted in the same change — it accepted edits, reported
  that they had been saved and never called the API, which beside a real editor is a screen that teaches the
  opposite of what the editor does — and `mockData.ts` lost the 130 lines that form was the last consumer of
  (`Terrarium`, `ThresholdConfig`, `HistoryDataPoint`, `initialTerrariums`, `generateHistoryData`).
- **4.21, the catalogue's screen.** `SpeciesProfiles.tsx`: the five routes of 3.9 with a caller at last, Owner-only
  for the writes, with a built-in offering only *Duplicate*. Its three client-side readings, its evidence and the
  3.16 walkthrough are in the roadmap's 4.21 row, `04-quality/03` §3 and the §5 log of `05-release/03`.
