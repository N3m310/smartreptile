# SmartReptile

IoT environmental monitoring and alerting for reptile terrariums — **v1: measure, store, display, alert.
No AI, no actuation** (see `docs/07-appendices/06-v2-ai-roadmap.md` for what comes next).

> Design documentation for this repository lives in [`docs/`](docs/) — 36 files covering product, design,
> implementation, quality, release, report and the appendices. Section references below (`§`) point there.

| Component | Path | Stack |
|---|---|---|
| Firmware (sensor node) | `firmware/` | ESP32 / Arduino via PlatformIO (`esp32dev` + `native` for host tests) |
| Backend | `backend/` | ASP.NET Core 10, EF Core 10, SQL Server 2022, in-process MQTT broker (MQTTnet), SignalR |
| Mobile app | `app/` | Flutter (Android primary), `provider` |
| Web client | `web/` | React 19 + Vite + TypeScript + Tailwind CSS v4 — **the web surface** (`ADR-019`, wired to the API 2026-10-07; the M1 static dashboard it replaced is deleted). `npm run dev` for it, `npm run build` to refresh the committed `web/dist/` that nginx serves |

## Quick start

### 1. Backend + database

```bash
cp .env.example .env          # fill in MSSQL_SA_PASSWORD and JWT_SIGNING_KEY
docker compose up -d          # db, api, web — api waits for db to be healthy
curl -s http://localhost:8080/health/ready | jq
```

The `api` service applies EF migrations and seeds reference data on startup
(`Startup__ApplyMigrationsOnStartup`, `Startup__SeedReferenceDataOnStartup`), so there is no separate migration
step.

It also creates the **system administrator** when `ADMIN_USERNAME`/`ADMIN_EMAIL`/`ADMIN_PASSWORD` are set in `.env`
(`Admin__*` in the container). That account holds the seeded `Admin` role — every one of the sixteen permission
codes — which is what lets it author the species-profile catalogue
(`POST`/`PATCH`/`DELETE /api/v1/species-profiles`, where the built-in bands and any custom profile come from), manage
accounts at `/api/v1/users`, edit the role matrix at `/api/v1/admin/roles`, and read every account's data through
`/api/v1/admin/*`. Registration always produces an `Owner` and can never produce an `Admin`, so the two codes that
reach past an account (`data.read.any`, `data.write.any`) are held by the seeded account alone
(`docs/02-design/06` §3). Leaving `ADMIN_USERNAME` empty is the default and means nobody can author the catalogue or
manage accounts; readings and the per-terrarium threshold overrides are unaffected either way. Running the API
outside Docker instead, the development placeholder account is `admin` / `Admin#2026`
(`appsettings.Development.json`), and the password belongs to the configuration, so changing it there and restarting
rotates it. To apply the migration
*explicitly* from the host instead — which is how M1 was verified:

```bash
cd backend
export ConnectionStrings__Default="Server=127.0.0.1,14330;Database=SmartReptile;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True;Encrypt=False"
dotnet ef database update --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
```

Do **not** reach for `docker compose run --rm api dotnet ef ...`: the api image is a runtime image with no SDK and
no `dotnet-ef` tool, and the arguments are appended to the image entrypoint, so that command silently boots a
second API instance and never exits (verified — it hung until killed).

SQL Server is published on **127.0.0.1:14330**, not the default 1433, so this stack can run at the same time as
the other course project on the same machine (the aeroponic-iot stack binds 1433). Inside the Compose network
the API still reaches the database on `db:1433`. Use `127.0.0.1` and not `localhost` in host-side connection
strings: on Windows `localhost` resolves to IPv6 `::1` first, Docker Desktop does not proxy `::1` for the
published port, and the result is a 16 s timeout rather than a fast connection refusal.

Expected: `{"status":"Healthy", ... "checks":[{"name":"database","status":"Healthy"},{"name":"mqtt-broker","status":"Healthy"}]}`

### 2. Run the API without Docker (faster inner loop)

```bash
cd backend
dotnet restore
dotnet run --project src/SmartReptile.Api          # http://localhost:8080, Swagger in Development
```

### 3. Firmware

```bash
cd firmware
pio run                    # release build for esp32dev
pio test -e native         # host unit tests (filters, ring buffer) — no hardware needed
pio run -t upload          # flash over USB
pio device monitor -b 115200
```

### 4. Mobile app

```bash
cd app
flutter pub get
flutter gen-l10n
flutter run -d android --dart-define=API_BASE=http://10.0.2.2:8080   # emulator reaches the host's API this way
flutter test
```

`flutter run` opens the app's own VIVARIUMGUARD shell. Add `--dart-define=PROTOTYPE=true` to run the clickable
prototype instead — fake data with the real rule kernel, the Rule Lab and the scenario walkthroughs
(`app/PROTOTYPE.md`); the two are one binary with a compile-time switch, and the release build passes neither
define, so the artifact a keeper installs is the app.

### 5. Web (`web/` — one surface since 2026-10-07: the VIVARIUMGUARD client, see `ADR-019`)

Compose serves the committed build from one nginx (`web/nginx.conf`): `http://127.0.0.1:8081/` is the wired
VIVARIUMGUARD client, and `/wallboard`, `/history` or any other deep route returns `200` through the SPA fallback.
The M1 static dashboard that used to answer under `/legacy/` is deleted, and with it the second mount. (The brand
was renamed from TERRAGUARD on 2026-10-09; `ADR-019` and the dated entries below keep the old name because they
record what was decided then.)

```bash
cd web
npm install
npm run dev                    # Vite on http://127.0.0.1:8081 — the same port as compose, so run one, not both
npm run typecheck              # tsc --noEmit (CI)
npm run check:strings          # i18n key parity against app/lib/l10n/app_*.arb (CI)
npm run build                  # refresh the committed web/dist/ that compose serves at /
```

Sign in with any account on the API; the [quick-start](#1-backend--database) backend serves it. A fresh account
shows the empty state, because a terrarium has to be created through `POST /api/v1/terrariums`
(`07-appendices/03` §4.2) — the web form that would do it waits for the species-profile catalogue route, and says
so rather than inventing a profile id.

## Verification gates for Milestone M1 (`docs/03-implementation/07-implementation-roadmap.md`)

Measured on the development machine (Windows, .NET 10.0.112, Flutter 3.47.4, PlatformIO 6.2.0) on **2026-09-21**:

| Gate | Command / method | Result |
|---|---|---|
| Backend builds, warnings as errors | `dotnet build backend/SmartReptile.sln -c Release` | ✅ Build succeeded, 0 warnings |
| Backend unit tests | `dotnet test backend/SmartReptile.sln -c Release` | ✅ **947 passed**, 0 failed (54 at M1, 121 after FR-01, 186 after device provisioning, 226 after broker auth/ACL, 325 after the ingest pipeline, 386 after the terrarium read surface, 422 after password recovery, 453 after the disclosed-conflict change, 455 after the password-policy change, 497 after the M2 close-out and the M3 scaffolding, 513 after 3.1's read half, 545 after the threshold engine (3.2), 655 after 3.1's write half, 700 after the administrator role that now owns the species-profile catalogue, 717 after the profile picture, 845 after the 2026-10-13 review's remaining items — ownership's three answers, the administrator's reach, the status column, the MAC pairing, the account and board surfaces, and the time-zone catalogue — both 2026-10-14), 882 after the notification layer (3.5, 2026-10-16), **947 after the role/permission and alert-family re-framing (2026-10-17)** |
| Formatting gate (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | ✅ clean (generated migrations excluded via `.editorconfig`) |
| Web client type-checks and builds | `npm run typecheck && npm run build` in `web/` | ✅ **clean** — `tsc --noEmit` exit 0, `vite build` produced `index-CGs5KiNg.js` (932 kB / 253 kB gzip) + `index-HAqBhTgA.css` (42.1 kB), refreshed **2026-10-17** with the administrator's three pages |
| Web i18n key parity (`TC-I-15`) | `npm run check:strings` in `web/` | ✅ **509 keys**, identical in vi and en, every key referenced by `src/` defined — and the check was verified to *fail* on a deliberate typo before it was trusted. The count grew from 164 because the web client now reads the Flutter **app's** deck (`app/lib/l10n/app_{en,vi}.arb`) through `strings.ts` instead of keeping a second catalogue of its own, so one deck is the vocabulary for both clients |
| Schema created by migrations only | `dotnet ef migrations add InitialSchema` | ✅ one migration; `(DeviceId, Sequence)` unique, filtered open-alert unique, one-device-per-terrarium unique, band CHECK constraints all present |
| Docker image builds | `docker compose build api` | ✅ builds from a clean context (needed `backend/.dockerignore` — defect 8 below) |
| Compose stack comes up | `docker compose up -d` | ✅ `db`, `api`, `web` all Up; `db` + `api` report `healthy`, 0 container restarts |
| Migration applies to a **real** SQL Server | `dotnet ef database update` against 127.0.0.1:14330 | ✅ `Applying migration '20260921091523_InitialSchema' ... Done.` — 13 tables created |
| Invariants exist in SQL, not only in C# | `sys.indexes`, `sys.check_constraints`, `sys.foreign_keys` | ✅ filtered unique `IX_Alert_DedupeKey ([State]<>(2))`, `IX_Device_TerrariumId ([TerrariumId] IS NOT NULL AND [Status]<>(3))`, 5 `CK_Threshold*` / `CK_ThresholdOverride*` ordering constraints |
| Cascade risk does not fire | read the FK delete rules back | ✅ `Device → Terrarium` is `SET_NULL`, so `TelemetrySample` has a single cascade path — this was the specific shape that was flagged as a risk before the schema existed |
| Reference data seeds on startup | `SELECT COUNT(*) FROM SpeciesProfile` | ✅ 4 profiles (`Arid (desert)`, `Arid-cool (bearded dragon, ambient)`, `Leopard gecko (semi-desert)`, `Tropical (humid forest)`) and 19 threshold bands |
| Dashboard served, and the browser accepts the API | nginx `web` on `:8081` + real browser `fetch` to `:8080` | ✅ `index`/`wallboard`/`health` all `200`; browser fetch of `/version` returns `200` with `Access-Control-Allow-Origin: http://127.0.0.1:8081` (+`204` preflight). *(The three pages now live in `web/legacy/` after the prototype took the root of `web/` — `ADR-017`.)* **Re-verified 2026-10-03 after 4.12:** `:8081/` now serves the prototype build and `:8081/legacy/index.html` / `wallboard.html` / `health.html` all return `200`; BUG-03 is closed, with the `curl` pair written down in `docs/05-release/01` §5 |
| Liveness | `curl /health/live` | ✅ `200 Healthy`, answers immediately |
| Readiness with the database **up** | `curl /health/ready` | ✅ `200 Healthy` — `database: Healthy` ("Database is reachable") + `mqtt-broker: Healthy`, 7 ms |
| Readiness with the database **down** | `docker stop smartreptile-db` → `curl /health/ready` | ✅ `503` in **3.0 s** — `database: Unhealthy` ("Database probe timed out after 3 s"), `mqtt-broker: Healthy`; `/health/live`, `/version` and `/metrics` all still `200` (degraded mode, NFR-03). Before the probe was bounded this took 16.1 s — defect 10 below |
| Recovery without an API restart | `docker start smartreptile-db` → `curl /health/ready` | ✅ back to `200 Healthy` in 7 ms; api `RestartCount` was 0 across the whole drill |
| Version / metrics / root | `curl /version /metrics /` | ✅ counters + `brokerRunning: true` |
| MQTT rejects anonymous devices | `paho-mqtt` connect without credentials | ✅ `CONNACK: Bad user name or password`, and `mqtt_rejected_connections_total` incremented |
| MQTT verifies the device secret | `paho-mqtt` over TLS with a real credential, a wrong one and none | ✅ **2.3 done 2026-10-03**: anonymous, wrong-secret and unknown-device connects all answered `Bad user name or password`; a claimed device connected over TLS *and* over plaintext; wildcard and cross-device subscriptions refused; a publish under another device's prefix refused and that session dropped; `POST /devices/{id}/revoke` closed the live session in **0.0 s** and the credential could not reconnect |
| MQTT topic ACL | `paho-mqtt` subscribe/publish attempts outside the device's own prefix | ✅ **2.3**: own `cmd` granted; `sr/v1/d/+/telemetry` and another device's `cmd` refused; counters `mqtt_refused_subscriptions_total` / `mqtt_refused_publications_total` on `/metrics` |
| MQTT plaintext is loopback-only | `netstat -ano` with TLS on, then again with `Mqtt:DisablePlaintextEndpoint=true` | ✅ **2.3**: `127.0.0.1:1883` **and** `[::1]:1883` only (no wildcard bind — binding the IPv4 address alone leaves the IPv6 socket open); in the release shape nothing listens on `1883` at all and TLS kept authenticating devices |
| Ingest: broker → bus → worker → SQL Server | `paho-mqtt` publish over a real MQTT listener against a live API + SQL Server, then `curl /metrics` and read the rows back | ✅ **2.4 done 2026-10-04**: a two-sample batch published twice stored **2** `TelemetrySample` rows (sequences `1, 2`), **8** `MetricReading` rows with `Value = 28.750` / `RawValue = 28.900` intact, set `LastSeenAt` / `FirmwareVersion` / `SignalStrengthDbm` / `FreeHeapKb` and wrote a `DeviceHealthSample`; `/metrics` read `ingest_samples_total=2`, `ingest_duplicates_total=2`, `ingest_rejected_total=0`. A `tf = 85` sample was stored with `QualityFlags = 2`; a 121-sample batch was `payload_too_large` and a malformed body `schema_invalid`, taking `ingest_rejected_total` to **2** |
| The alert lifecycle over HTTP (M3 task 3.4) | the API started headlessly with **no** SQL Server — which is what makes a *route* check possible without a database: `curl` with a minted Viewer token, a Technician token and no token at all (14 assertions, all green) | ✅ **2026-10-10**: no token → `401 unauthenticated`; **Viewer** → `403 insufficient_role` on `POST /alerts/1/ack`, `POST /alerts/1/resolve`, `POST /terrariums/{id}/silences` and `DELETE …/silences/{id}` (the RBAC half of `TC-U-36`, measured); `?state=Nonsense`, `?cursor=not-a-cursor` and an unknown resolve reason → `400` (`validation_failed`, `invalid_cursor`, `validation_failed`) **before** any database call; with a Technician token every handler ran and failed only on the absent SQL Server (`500 internal_error`, not the `404` a missing route would give); and `swagger.json` carried **35 operations, 22 requiring the bearer scheme** — the 14 that already did plus all eight new ones. The three silence routes and five alert routes are also what `GET /` advertises. The *data* half (`TC-I-07` over HTTP: ack → resolve → second ack refused, read back through the API) still needs SQL Server |
| Integration tests against a **real SQL Server** | `dotnet test backend/tests/SmartReptile.Tests.Integration` (CI: the `integration` job with a SQL Server 2022 service container) | ✅ **55 cases, all green** — 19 green as of 2026-10-09: migrations applied, 4 profiles + 19 bands seeded, re-seeding duplicates nothing, the SQL-level invariants reject what they should, and the ingest pipeline stores, dedupes and flags against the real database (`TC-I-01…03`). The three added 2026-10-09 assert what only SQL Server can: a stale `rowversion` refuses an update, `PK_EvaluationState` refuses a second state row for one key, and a `DeviceEvent` stores with a null terrarium. The five added 2026-10-10 (task 3.4) assert what a fake cannot answer: an alert of another account is invisible to both the list and the lookup, the soft-delete filter on `Terrarium` reaches an alert through its foreign key, the cursor predicate translates and pages without repeating a row, one transaction leaves the resolved row + the re-armed `EvaluationState` + the audit entry together, and a silence window round-trips through create, list and cancel. The two added 2026-10-13 (3.1's write half) assert what the schema and the adapter have to answer: an override change and its `ThresholdSnapshot` commit together, the evaluator's own band source then resolves the new band while an alert already open keeps its denormalised `BandMin`/`BandMax`, and a revert deletes the override row rather than disabling it. The five added 2026-10-16 (3.5) cover the notification store's joins, window and writes; the **thirteen added 2026-10-17** cover the identity seed (`IdentitySeederTests`: the migration left the catalogue complete, a second pass is a no-op, a grant an administrator removed is *not* restored, a missing system role returns with its defaults), the account reads (`AccountReadTests`: the list and the single-account reads load the role's grants, and the last-administrator count goes through the join) and the administrator's cross-account store (`AdminStoreTests`: the enclosure with its owner and open-alert count, the owner search, a board no account has claimed still appearing, the `?family=` filter returning only that family, the overview's counts moving by exactly what was seeded, and the per-account rollup). **All 55 are green**: `AlertLifecycleTests.The_cursor_pages…` used to be the one red case — it expected one alert fewer than its own fixture creates (four alerts, not three) — and on 2026-10-17 it was corrected to assert the invariant directly rather than patched to pass |
| Terrarium update and delete over HTTP | `curl` a live API + SQL Server: create, `PATCH` with the current `ETag`, then a stale one, then none, then `DELETE` with and without the flag (26 assertions, all green) | ✅ **2.8's update/delete halves done 2026-10-09**: `201` + `ETag` on create, `200` and a **new** `ETag` on a matching `If-Match`, `412 precondition_failed` on a stale one with the row provably not overwritten, `428 precondition_required` with no header, `If-Match: *` accepted, `400 validation_failed` on a bad body, `409 conflict_device_bound` while a live device is bound, `204` with `?allowUnboundDevice=true` (the board returns to `Provisioning` with its credentials intact), and `404 not_found` after |
| The version an update needs is readable from the body, and Swagger shows the `If-Match` box | `curl` a live API + SQL Server on a fresh database: create, read, `PATCH` with the body's `rowVersion` (plain and quoted), then a stale one, then none, then `*` — 19 assertions, all green | ✅ **2026-10-14, closing the 2026-10-13 review's item 4**: the create answer's `ETag` equals the `rowVersion` in its body, `GET /terrariums/{id}` repeats the pair, a **list** item carries no `rowVersion` at all, `PATCH` answers `200` with a **new** token in both forms, the same token afterwards is `412 precondition_failed` (name unchanged) and no header is `428 precondition_required`, with both messages naming `GET /api/v1/terrariums/{id}` and the two places it carries the value; `If-Match: *` accepted; an empty `name` still `400 validation_failed` with `errors[0].field = "name"` and nothing written. `swagger.json` carries the `If-Match` header parameter on `PATCH /api/v1/terrariums/{terrariumId}` — `in: header`, `required: true`, with the description naming where the value comes from — which is what Swagger UI renders as an input box. The parameter is bound as nullable, because a non-nullable one would make the framework answer a missing header with its own `400` before the handler could answer the documented `428`; `RequiredIfMatchOperationFilter` corrects the document so the two agree. Verified against a scratch database — `SmartReptileVerify` — so no dev data was touched |
| A species profile's picture (FR-10, `BR-10.6`) | `curl` a live API + SQL Server + a real file on disk: create a profile, upload a PNG, read it back, replace it with a JPEG, then remove it (27 assertions, all green) — then the same flow driven in a real browser | ✅ **built 2026-10-14**: `POST`/`GET`/`DELETE /api/v1/species-profiles/{id}/image` with the bytes under `Storage:RootPath`, the first byte storage in the repository. The read-back is byte-identical with `Content-Type: image/png` and `Cache-Control: private, max-age=300`; a second account's read is `404`, an anonymous one `401`, a non-admin write `403`; a text file labelled `image/png` is `400 image_invalid`, a PNG labelled `text/html` is `400 image_type_unsupported`, 2 MB + 10 bytes is `400 image_too_large` and a body with no `file` part is `400 image_required`, with the stored picture surviving all four; a built-in is `403 builtin_immutable`; a replacement is served as `image/jpeg` and leaves **one** file — the old one discarded; `DELETE` clears the row and the file and a second `DELETE` is still `200`; the audit trail holds exactly two `profile.updated` rows for a set-and-replace; and `swagger.json` documents the upload as `multipart/form-data`. Driven in a browser against that API: create with a picture (the new card fetched and showed it), replace it, a refused `.txt` rendered at the picker with the field edits still saved, and *Xoá hình* cleared both the stored picture and one chosen but not yet saved. Verified on a scratch database — `SmartReptileVerify` — so no dev data was touched |
| MQTT device channels are consumed, not dropped | `paho-mqtt` publishing `status`, `health`, `events` and `telemetry` over loopback from a **scripted** node (no hardware), then reading the rows back | ✅ **2026-10-09**: `flags=1.2.0`, `rssi=-64`, `heap=140`, `bat=87.00` on the `Device` row; **2** `DeviceHealthSample` rows (one from the `health` topic, one from a batch's health block); **1** `DeviceEvent` (a `sensor_fault` on `humidityPct`) with an unknown event type refused as `schema_invalid`; **2** `TelemetrySample` rows; and **2** `EvaluationState` rows written by the evaluator worker **for the keys the profile actually bands** (`TempC`/Night and `HumidityPct`/Any, watermark 20) |
| The threshold engine raises and closes an alert (M3 task 3.2) | `paho-mqtt` publishing a scripted excursion over a real broker against a live API + SQL Server, then reading `Alert` and `EvaluationState` back — 48 assertions, all green | ✅ **2026-10-09**: a faulted out-of-band reading opened nothing and created no state row; an out-of-band reading opened nothing while the dwell window was open; when it elapsed **exactly one Warning opened, back-dated to the first out-of-band reading**, with `TriggeringValue` = that reading and `PeakValue` = the window's worst reading (`32.6 °C` opened it, `33.0 °C` was the peak — a value only the stored readings know); a critical reading short of its own dwell touched the row without escalating; when that dwell elapsed the **same** alert escalated once (`Severity` and `DedupeKey` → Critical, id and `TriggeredAt` unchanged); three readings inside the band by the margin resolved it (`Recovered`, pointer and excursion cleared, three ticks counted); a later excursion opened a **second** row while the first stayed resolved; and `/metrics` moved by exactly the two openings and the ten published samples, with no duplicate. The run used a one-minute dwell through an override (the seeded leopard gecko band asks for five) because FR-10's write route is 3.1's remainder |
| Effective thresholds over HTTP (M3 task 3.1's read half) | `curl` a live API + SQL Server: terrariums on the seeded Tropical profile in a zone inside the photoperiod and one outside it, with an override inserted per phase (`sqlcmd`, because the write half of 3.1 does not exist yet) — 34 assertions, all green | ✅ **2026-10-09**: an anonymous read `401`; **both phases observed**, the phase computed from each terrarium's own zone — `Europe/London` (local 15:00) listed four metrics with the day band `24–28 °C`, `Pacific/Auckland` (local 03:00) listed two with the night band `20–24 °C`; `humidityPct` reported `phase: "any"` / `source: "profile"` with the profile's `60–80` and critical `40–95` in both; a **day-only** override correctly *silent* at night (the profile's night band came back); an override for the night phase reported `source: "override"` with its own bounds while the other metrics kept `source: "profile"` and the metric list was unchanged; a foreign/missing id `404 not_found`; `readings/latest` still `200` beside the new route. The same run is what pins the two readings of `03-implementation/06` §1 that the document left ambiguous |
| The role matrix, the two alert families and the administrator's dashboard (2026-10-14 review items 1, 12 and 13) | `curl` a live API + SQL Server Express, database created by the run itself (14 migrations applied from scratch); 4 accounts, 1 enclosure, 1 board and 3 alerts seeded through the API and `sqlcmd` | ✅ **2026-10-17**: `/auth/register` produced an `Owner` whose token carried **11** `perm` claims against the administrator's **16**; `/auth/me` returned `roleId` + the same list; `/admin/overview` answered `accounts 3, terrariums 1 (1 with a board), devices 1 online, alertsOpen 2 (1 enclosure + 1 species), 1 critical, speciesProfiles 4` with each account's holdings and both families' metric keys; `/admin/terrariums`, `/admin/devices` and `/admin/alerts?family=` answered the owner, the board's health and the split rows (`?family=habitat` → `400 family_invalid`); a keeper's token got **`403` on all five administrative routes**; the account surface round-tripped — create `201` with the role's permission set, `PATCH role` → the set changed, an unknown code `400 role_invalid`, disable → login `401`, enable → login `200`, reset-password → the new password worked, soft delete `200`, `?purge=true` `200` and a later read `404`, self-edit `409 self_change_refused`; the role surface: create `201`, edit `200` with the grant set replaced, a system role `409`, a custom role `200` then `404`. A restored pre-change database proved the back-fill (`0→Owner … 3→Admin`) and a `Down`/`Up` round trip left the accounts identical. **Two defects found and fixed:** the account list reported zero permissions for every row (the read loaded `Role` but not its grants) and the role list reported 0 holders for every role |
| Device provisioning over HTTP | `curl` against a live API + SQL Server (50 assertions) | ✅ **2.2 done 2026-10-03**: self-register `201`/`429`/`400`, claim `200`/`404 claim_code_invalid`/`404 not_found`/`409 terrarium_already_bound`, rotate `200` with a 10-minute grace, revoke `204` and idempotent — gated `Owner`-only at the time (`Technician` → `403 insufficient_role`), which is the `device.provision` permission since 2026-10-17 |
| Firmware builds for the target | `pio run -e esp32dev` | ✅ RAM 13.6% (44 536 B), Flash 20.7% (270 673 B) |
| Firmware host unit tests | `docker run --rm -v "$PWD/firmware:/firmware" smartreptile-fw-test` (image from `firmware/Dockerfile.host-tests`; plain `pio test -e native` wherever a host compiler exists) | ✅ **22/22 passed** — 8 filters + 8 payload + 6 ring buffer, ~18 s. First ever execution, and it caught defect 11 |
| App analyzes + tests | `flutter analyze && flutter test` | ✅ `No issues found!`, **182 tests passed** (19 at M1; the suite grew with the client's own screens, and the 2026-10-17 re-framing added the permission-gating widget cases — the administration tab is absent without the administrative codes, present with them, and each of the two groups appears only with its own permission) |
| App release APK | `flutter build apk --release` | ⬜ M6 (release milestone) |
| No committed secrets or build output | `git ls-files` audit | ✅ at M1: 164 files tracked; only `.env.example`; no `bin/`, `obj/`, `.dart_tool/`, `.pio/`, keystores. **Re-audited 2026-10-03:** 247 files and still no secrets — the increase is the TERRAGUARD prototype, including the three `web/dist/` build artefacts that `ADR-017` accepts deliberately |
| **CI runs, and passes** | push to `master` → `gh run watch` | ✅ **all five jobs green** (`backend`, `integration`, `app`, `firmware`, `secret-scan`) in run `35602903951`, ~2 min wall clock. The firmware job is where the 22 host tests execute in CI |

### What M1 still does *not* verify (stated, not hidden)

- **No end-to-end, soak or chaos coverage.** The database paths are enforced by the `integration` job now, but
  nothing drives the full chain (device → broker → threshold engine → alert → notification), no test runs for hours,
  and no failure drill has been executed against a live stack — those are M5 work by plan. The integration tests
  also run against a single SQL Server that is already up, so they say nothing about a database that disappears
  mid-run.
- **The domain REST surface is partly there.** The API serves the ops endpoints (`/health`, `/version`,
  `/metrics`, the SignalR hub), `/api/v1/auth` (register, login, refresh, logout, me, change-password — added
  2026-10-03 — plus recover, forgot-password and reset-password, added 2026-10-06), `/api/v1/devices`
  (self-register, claim, rotate-secret, revoke — added the same day, task 2.2) and `/api/v1/terrariums` (list,
  create, detail, `readings/latest`, `readings`, `coverage` — added 2026-10-06 — plus `PATCH`, `DELETE` and
  `thresholds`, added 2026-10-09), so the dashboard's live view signs in and renders measured values rather than
  degrading to its empty state; the empty state now means an account with no terrarium, and one can be created,
  renamed and removed over HTTP. `GET .../thresholds` reports the effective band per metric with the layer it came
  from, so the threshold editor (M4's 4.20) can be built against it and show *why* a limit is what it is.
  **Thresholds are writable since 2026-10-13** (3.1's write half), so the editor can author and not only preview:
  `PUT /terrariums/{id}/thresholds` upserts the bands the body names and
  `DELETE /terrariums/{id}/thresholds/{metric}` reverts one metric to its profile — both requiring
  `terrarium.write` (they were `Owner`-gated until the 2026-10-17 re-framing), both
  answering with the resulting effective set, and every change recording a `ThresholdSnapshot` so an alert already
  open keeps the band it was judged against.
  Alerts have a full API since 2026-10-10 (task 3.4): `/api/v1/alerts` lists them newest-first with
  `?cursor=&pageSize=` and, since 2026-10-17, `?family=enclosure|species`, and `GET /api/v1/alerts/{id}` carries the
  band in force with an excerpt of the values the episode was judged on; `POST /alerts/{id}/ack` and
  `POST /alerts/{id}/resolve` close the response loop for any account holding `alert.write` (Owner, Technician and
  Admin; not Viewer), `GET /alerts/{id}/timeline` shows what happened to one alert and who did it, and
  `/api/v1/terrariums/{id}/silences` silences a metric — or the whole box — for at most 24 hours with a reason on
  record. Summaries and exports remain 3.6 and the export task.
- **Alerts are pushed but not delivered.** An alert that opens, escalates, is acknowledged or is resolved is pushed
  to the terrarium's group as `alertChanged`, and every lifecycle action writes its audit row in the same
  transaction — so a dashboard badge can update live. What is still absent is *delivery*: no FCM push, no email, no
  inbox row, because the notification dispatcher is 3.5, and a silence today suppresses what 3.5 will send rather
  than anything the system does now. The per-device **reorder window** of
  `docs/02-design/03` §7 is also unbuilt: samples are evaluated in ingest order, which for one node *is*
  `RecordedAt` order, but a second publisher or a broker redelivery could arrive out of order and the
  `EvaluationState` watermark is an id watermark, so it cannot detect that.
- **No sensor hardware is connected**: `main.cpp` runs with bench mode off and reports placeholder values until
  M2, and no board has ever been flashed from this repository.
- **The MQTT broker verifies device credentials** (task 2.3, 2026-10-03): `username = deviceId`, `password =
  secret`, validated against the current `DeviceCredential` with the rotation grace window applied, plus a
  per-device topic ACL and a revoke-time session kick. The consumer landed with task 2.4 on 2026-10-04: a
  telemetry publication now becomes a `TelemetrySample`, its `MetricReading` rows and a device state update, and
  the counters move. As of 2026-10-09 the other three channels are consumed too — `health` updates the fleet
  figures and writes a health row, `status` sets the lifecycle state and pushes `statusChanged`, `events` is stored
  as a `DeviceEvent` — so only `ack` is still forwarded to nobody, because command results are FR-14's. The HTTPS
  fallback endpoint exists; its firmware half arrives with the device work of 2.6.
- **No load, soak, or backup testing**, and the retention/rollup jobs have never run against real data.
- **Nothing is deployed**: this is a local Compose stack, not a host with TLS, backups, or monitoring (M6).

### Defects found by running it (all fixed)

Twelve issues surfaced only because each gate was executed rather than assumed:

1. **The firmware would not compile** — the ESP32 framework's own `config.h` silently shadowed ours, leaving every
   `SR_*` macro undefined without any "file not found" error. Renamed to `sr_config.h` plus `-Iinclude`.
2. **A pinned platform version that does not exist** (`espressif32@6.9.0`); now pinned to 6.13.0 with a comment.
3. **The format gate would have broken on the next migration** — EF emits CRLF+BOM on Windows; migration files are
   now marked `generated_code` in `.editorconfig`.
4. **The dashboard rendered nothing** — `js/pages/live.js` imported `./api.js`, which resolved to
   `js/pages/api.js` and 404'd.
5. **The API blocked its own port for ~50 s** while waiting for the database, which is indistinguishable from an
   outage; it now starts listening first and initialises afterwards.
6. **Quality flags were unreachable from the tests** (macros in a header that library consumers never include);
   moved to a `sr::payload::QualityFlag` enum as the single source of truth.
7. **Compose never expands `${...}` inside `.env`**, so a nested connection string would have reached the
   container literally; it is now built in `docker-compose.yml`, where interpolation works.
8. **`docker compose build` failed with `NETSDK1064`** — with no `backend/.dockerignore`, `COPY src/ src/`
   overwrote the container's Linux restore with the host's `obj/project.assets.json`, which hard-codes
   `C:\Users\<user>\.nuget\packages\` paths (read straight out of the file to confirm).
9. **The container healthcheck could never pass** — `mcr.microsoft.com/dotnet/aspnet:10.0` ships neither `wget`
   nor `curl`, so a perfectly healthy API was reported `unhealthy` (`/bin/sh: 1: wget: not found`). Now probes over
   bash's `/dev/tcp`, which needs no extra package.
10. **`/health/ready` took 16.1 s with the database down** despite a 3 s cancellation token. SqlClient ignores the
    token during pre-login and caps at its own 15 s timeout, *and* `CanConnectAsync` blocks its caller
    synchronously before returning a task — so racing it against a delay started the timer too late. The probe now
    runs on a thread-pool thread and answers in 3.0 s.
11. **The firmware's payload-budget guard refused its own configured batch.** `isPayloadWithinBudget` estimated
    5000 bytes for 5 metrics × 20 samples against a 4096-byte budget, so the guard rejected exactly the batch
    `SR_BACKFILL_BATCH_MAX` is documented to keep *under* that budget. Only visible once the 22 host cases were
    actually **executed** rather than compiled. The estimate is now calibrated against the measured §3.2 payload
    (~1.6 KB typical, ~2.8 KB worst case with the optional `raw` object) and the test asserts against the
    configured constants instead of copies of them.
12. **The secret-scan job failed on its very first run** — `gitleaks-action` computed the range
    `<root-commit>^..HEAD`, and `^` cannot resolve for a commit that has no parent, so it exited 1 after scanning
    **0 bytes** with nothing to find (verified: the same repo scans clean locally). Only reachable by executing
    the pipeline. The step now installs a pinned gitleaks and scans the full history, which also catches secrets
    committed and later removed.
13. **The first fix for BUG-03 could not start at all** — mounting `web/legacy` *inside* the read-only prototype
    mount failed with `mountpoint ... read-only file system`, because a nested bind mount needs its mountpoint
    created inside the parent mount. Making the parent writable instead would have created `web/dist/legacy/` in
    the working tree. The two surfaces are now mounted as **siblings** (`/srv/prototype`, `/srv/legacy`) with
    per-location `root` directives, which is also what keeps a missing `/legacy/` page a `404` rather than a SPA
    fallback into the wrong surface.
Windows `localhost` resolves to IPv6 `::1` first, Docker Desktop does not proxy a container's published port on
`::1`, and a refused IPv6 attempt is followed by a **connect timeout** rather than a quick fallback — so use
`127.0.0.1` in every host-side connection string.


## Repository conventions

- **Do not commit `.env`**, keystores, or generated build output — see `.gitignore`. The one recorded exception is
  `web/dist/`, the prototype's Vite build, committed so the nginx demo needs no Node toolchain (`ADR-017`).
- All timestamps are UTC in storage; local-day bucketing happens only in the rollup/summary layer (§`ADR-015`).
- Schema changes go through EF Core migrations only (§`03-implementation/03` §2).
- Interfaces in `Application`, implementations in `Infrastructure`, no EF Core types in `Domain`
  (§`02-design/01` §4).
- Version source of truth: the `VERSION` file at the repository root.

## Status

Milestone **M1 (foundations)** scaffolded: solution + layered backend with health/readiness, in-process MQTT
broker, initial EF migration, unit-tested domain logic, firmware skeleton with host tests, Flutter app shell,
web dashboard shell, CI. Next: **M2 — telemetry pipeline** (provisioning, ingest, readings API).

**Added 2026-10-03.** A TERRAGUARD web UI prototype (React + Vite + Tailwind, eight mock-data screens,
Vietnamese-only) landed in `web/`, which pushed the M1 dashboard pages into `web/legacy/`. The decision and its
consequences are `ADR-017`; the three follow-ups it created are closed — **4.12** fixed the compose mount (BUG-03),
**4.13** is decided as `ADR-018` (the prototype stays a mock-data reference, `web/legacy/` remains the web
surface), and **4.14** labelled the prototype and settled its palette and copy. See
[`docs/03-implementation/07-implementation-roadmap.md`](docs/03-implementation/07-implementation-roadmap.md) for
the M4 status table.

**Added 2026-10-03 (backend, M2).** User authentication (FR-01) is implemented, because task 2.2's `claim` endpoint
is `Owner`-gated and there was nothing to be `Owner` with: `/api/v1/auth/{register,login,refresh,logout,me,change-password}`,
PBKDF2-HMAC-SHA256 at 210 000 iterations **stored per user** and upgraded on the next login, 15-minute HS256 access
tokens (`sub`/`role`/`iat`/`exp`/`jti`/`ver`), single-use refresh rotation that revokes a whole family on reuse,
failed-login throttling, and a 10-per-minute per-address limit on the group. Verified against a real SQL Server and
a real HTTP surface.

**Added 2026-10-03 (backend, M2, task 2.2).** Device provisioning is implemented:
`/api/v1/devices/{self-register,claim,{deviceId}/rotate-secret,{deviceId}/revoke}`. A board self-registers against
its chip id and receives an 8-character claim code from a 31-symbol alphabet (15-minute TTL, single use, one code
live at a time); an `Owner` claims it for a terrarium and receives a 256-bit secret in base32 **once**, stored as
`SHA-256(secret ‖ 16-byte salt)`; rotating puts the previous secret on a 10-minute grace window; revoking
invalidates every credential. All of it is gated by `device.provision` (the `Owner` policy until 2026-10-17), so an
account without it gets `403`. Run end to end against the same live stack, 50 assertions green;
**186 backend unit tests pass** (was 121).

```bash
curl -s -X POST http://localhost:8080/api/v1/devices/self-register \
  -H 'Content-Type: application/json' \
  -d '{"chipId":"A0B1C2D3E4F5","macAddress":"A0:B1:C2:D3:E4:F5","firmwareVersion":"1.0.0"}'
curl -s -X POST http://localhost:8080/api/v1/devices/claim \
  -H "Authorization: Bearer $ACCESS_TOKEN" -H 'Content-Type: application/json' \
  -d '{"claimCode":"K7M2-QP4T","terrariumId":"6f1c…"}'
```

**Added 2026-10-03 (backend, M2, task 2.3).** The MQTT broker is now the FR-05 broker rather than a listener that
refused only *anonymous* clients: a connection needs `username = deviceId` and a password matching a usable
`DeviceCredential` (the rotation grace window included), TLS `8883` and a loopback-only plaintext `1883` are
separate listeners with the plaintext one switchable off entirely, a device may publish only on its own
`telemetry`/`health`/`status`/`events`/`ack` topics and subscribe only to its own `cmd` — anything else is refused
and, for a publish, the session is dropped — and revoking a device closes its live session so BR-05.4's
60-second budget is met in about zero. Checked with `paho-mqtt` against the real TLS listener (30 assertions, all
green) and by `netstat` in both shapes; **226 backend unit tests pass** (was 186). The consumer of what the broker
accepts arrived with task 2.4, recorded just above.

**Added 2026-10-04 (backend, M2, task 2.4).** A published batch is no longer counted and dropped: the broker's
publish interceptor hands accepted telemetry to an in-process bounded queue, and `IngestWorker` runs each batch
through the design's stages — schema/shape/timing (rules V-01…V-03, V-07…V-09, V-10), device authentication,
plausibility (flag and store, never refuse), calibration (`Value` corrected, `RawValue` untouched), dedupe on
`(DeviceId, Sequence)` and persistence in one transaction with the device's `LastSeenAt`, status, firmware and
health denorms. Checked by publishing over a real MQTT listener with `paho-mqtt` and reading the rows back
(see the gate table above); **325 backend unit tests pass** (was 226) and the integration project passes 15 of
15 against a fresh database. Two things are wired but deliberately empty — the SignalR broadcast (2.9) and the
evaluation queue (3.2/3.3) — so `TC-I-03`'s "zero alerts" holds because nothing evaluates yet, not because the
evaluator skipped the flagged row.

The whole session — the TERRAGUARD re-base and its three follow-ups, FR-01, and tasks 2.2 and 2.3 — is recorded
build by build in [`IMPLEMENTATION_SUMMARY_2026-10-03.md`](IMPLEMENTATION_SUMMARY_2026-10-03.md), including the
nine defects that only showed up once the code and its gates were run.

**Added 2026-10-06 (backend, M2 task 2.8 — the terrarium read surface).** Six routes both clients were already
written against: `GET`/`POST /api/v1/terrariums`, `GET /{id}`, `GET /{id}/readings/latest`,
`GET /{id}/readings` (raw and bucketed, gap-aware, with the 720-point ceiling enforced) and `GET /{id}/coverage`,
taking the backend from 16 to 22 routes. Ownership is a query parameter on every store call rather than a filter
applied afterwards, so a foreign terrarium is never materialised (BR-02.2), and `readings/latest` walks back at
most 50 samples per metric rather than issuing one query per metric. `web/legacy` gained sign-in, a session that
rotates once on a `401`, and the calls above. Checked over real HTTP against a real SQL Server (register `202`,
login `200`, terrarium created `201`, a foreign id `404 not_found`, an unaligned 30-day window exactly 720 points,
`coverage` reporting `expected=60, received=3`) and in a browser, which rendered the real values; **386 backend
unit tests pass** (was 325) and the integration project passes 15 of 15 against a fresh database. One defect came
out of the live run rather than the tests — an unaligned `from` produced 721 hourly points, breaking the
documented budget, so the bucket loop is now clamped to it.

**Added 2026-10-06 (backend, FR-01 — password recovery).** A forgotten password no longer means recreating the
account. Registration returns a 20-character **backup recovery code** from the claim-code alphabet (≈ 99 bits),
stored as `SHA-256(code ‖ 16-byte salt)` and shown once; `POST /api/v1/auth/recover` spends it, rotates it and ends
every session. A keeper who no longer has it can ask for a **server-issued code** instead:
`POST /api/v1/auth/forgot-password` mints a single-use, 30-minute code and `POST /api/v1/auth/reset-password`
spends it, rotates the backup code and ends every session. The issued code is stored as an **unsalted** SHA-256 —
like a refresh token, and unlike the backup code — because the row has to be found *by* the value presented. The
reset paths answer identically for an unknown identifier, a spent code and another account's code, which is the
point: the failure code on both is one opaque `401`. (`forgot-password` itself stopped being identical for all
callers on 2026-10-07 — see **ADR-020** below.) Delivery is the `IPasswordResetNotifier` port; the implementation
this deployment ships writes the code to the server log when `PasswordReset:LogCode` is true (false in
`appsettings.json`, true in `appsettings.Development.json` only) and otherwise says plainly that it reached nobody,
which is all that is left of limitation **L-02** — the transport, not the flow. Checked over real HTTP (the
lower-case, separator-laden code accepted once and refused after rotation, the old password `401` and the new one
`200`, a pre-reset refresh token `401` afterwards, the rotated backup code working through `/recover`) and in the
browser, where the recovery form now offers both paths; **422 backend unit tests pass** (was 386).

**Decided 2026-10-06 — `ADR-019` promotes TERRAGUARD to the M4 web surface and retires `web/legacy/`, revoking
`ADR-018` in full.** The move is `ADR-018`'s own escape clause being invoked: it said the decision would be revisited if
extending the static dashboard to W1–W8 became the bottleneck, and it has — the endpoints the wiring needs now exist
(FR-01 and task 2.8), while extending `web/legacy/` means re-deciding eight screens' visual design in vanilla JS that
the TERRAGUARD client already carries. It is a superseding ADR rather than an edit, because `ADR-018`'s surface
analysis is what the revocation was decided *on*. Promotion carries the obligations the prototype was excused from,
which is the substance of the decision rather than a footnote: real API data (4.15, 4.16), verdicts from the server
instead of `value > max` in a screen (4.17 — the ADR-005 rule), vi+en with a key set shared with the app (4.18),
`02-design/04` §3 tokens at ≥ 4.5:1 (4.18), CI coverage (4.18), and the wallboard rebuilt in React (4.19), because
the M4 DoD names a wallboard on a second screen and the wallboard is a legacy page. The remaining screens are wired
as their endpoints land (4.20), and it is staged by the backend rather than by preference: **only three of the eight
screens have endpoints today** (`Login`, `Dashboard`, `History`), which is why 4.15–4.17 can start now and 4.20
cannot. Until a screen is wired it keeps its mock-data notice, and the notice goes when the mock data does. See
[docs/03-implementation/07-implementation-roadmap.md](docs/03-implementation/07-implementation-roadmap.md).

**Completed 2026-10-07 (web — the promotion, tasks 4.15–4.19).** TERRAGUARD is now the web surface and the M1
static dashboard (`web/legacy/`) is deleted, with its compose mount and its nginx location. What that took:
`web/src/api/{client,endpoints,types}.ts` (base URL, stored session, single-flight refresh-on-401, `ApiError`
carrying `code`/`status`/`detail`/`errors[]`); a session provider whose role comes from `GET /auth/me` rather than
from `localStorage`; four auth modes on one `Login.tsx` (sign in, register, backup-code recovery, server-issued
reset code); a route guard; the dashboard, terrariums, terrarium detail and history wired to `/terrariums`,
`readings/latest`, the bucketed range route and `coverage`; the wallboard rebuilt as a React kiosk route (W1 was
never one of the prototype's eight screens) and the retired `health.html` diagnosis rehomed at `/system` so the
retirement did not lose a function; §3's four status roles as the only palette, with contrast measured per surface
and recorded; `tsc`, `vite build` and key parity in a new CI `web` job; and one copy deck for both clients — the
client reads `app/lib/l10n/app_{en,vi}.arb` directly, so `TC-I-15`'s "identical key sets" is structural rather than
a comparison of two lists. Verified live: a terrarium created through `POST /terrariums`, a device claimed and fed
through `/ingest/http`, and the served build rendering five metric cards with the *server's* statuses plus coverage
`0.42%` (6 of 1440 expected) — then the same through the real nginx config: `/`, `/wallboard` and a deep route all
`200`. `Devices`, `Alerts` and `Settings` still render mock data with the notice, because their endpoints do not
exist; that is 4.20, not an oversight.

**Added 2026-10-07 (auth — the password policy the brief asks for, and the two endpoints that now disclose).**
`PasswordPolicy` requires **≥ 8 characters with at least one letter, one digit, one upper-case letter and one
special character** (a symbol that is not a letter or a digit — whitespace does not count), keeps the ≤ 128 cap and
the embedded common-password deny-list, and reports one code per rule, so the dashboard states the rule under both
new-password fields and names the rule a refusal broke instead of saying only that the password was refused. The
Live page also gained *Đổi mật khẩu* / *Change password*: the endpoint had existed since FR-01 with 67 unit tests
behind it and no client that could reach it. `POST /register` answers `409 registration_conflict` (with `errors[]`
naming `email_taken` / `username_taken`) instead of `202` plus a recovery code that could never verify, and
`POST /forgot-password` answers `404 identifier_unknown` for an identifier nobody holds — both deliberate
exceptions to the non-disclosure rule, recorded in **ADR-020** together with their cost. Verified live: `202` then
`409` for one email, `404` for an unknown address, `400 password_policy_violation` naming the rule that failed, and
the change-password flow end to end in the browser; **455 backend unit tests pass** (was 422, 453 after the
disclosure change) and the integration project passes 16 of 16.

**Added 2026-10-09 (backend, M3 tasks 3.1 and 3.2 — the threshold engine).** M3 has started, and the system now
decides things about the data it stores. **3.1's read half**: the override → profile resolution order of
`docs/03-implementation/06` §1 became `ThresholdResolver`, one pure function, and
`GET /api/v1/terrariums/{id}/thresholds` answers with the effective band per metric plus the layer it came from —
the same call the card's band comes from, so an editor and a card cannot disagree. **3.2**: the decision procedure
of `docs/02-design/03` §4.2 became `ThresholdDecision.Decide` — dwell, recovery hysteresis and escalation as one
static function with no dependency at all — and `SampleEvaluator` acts on it, writing a new alert (back-dated to the
first out-of-band reading), a touch, an escalation of the same row, or a recovery. Five readings of the design were
settled in the process and are recorded in the documents rather than only in the code (dwell measured against the
evaluation instant; a reading back inside the band ends the excursion; escalation once per episode using the
severity the alert already carries; a consecutive critical window; and the alert's triggering value and peak read
back from the stored readings of its dwell window). **What is deliberately absent**: delivery — an alert is a row
and a counter, because the dispatcher is 3.5 — and the per-device reorder window of §7. The lifecycle API that
makes alerts readable, acknowledgeable, resolvable and silenceable landed as task 3.4 on 2026-10-10 and pushes
`alertChanged` to the clients watching the terrarium. Verified live by a **scripted node** driving one excursion
through a real broker, the ingest pipeline, the evaluator and SQL Server (48 assertions, listed in the gates above):
one back-dated Warning, no escalation under the critical dwell, one escalation of that same alert, `Recovered` after
three ticks, and a second episode as a separate row.

```bash
# The password below is a throwaway value for a local demo account, and the recovery codes are alphabet
# patterns rather than captured values, for the same reason: CI's secret scanner rejects realistic-looking
# credentials wherever they appear, test fixtures and documentation included, and allow-listing those paths is
# exactly how a real key gets committed unnoticed. A real code from a live run is a credential until it expires.
# It is a documented policy value too (BR-01.2: ≥ 8 characters, one letter, one digit, one upper-case letter and
# one special character) — an example the server refuses is worse than no example.
curl -s -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"keeper","email":"keeper@example.com","password":"Local-demo-1!"}'
# -> 202 {"status":"accepted","recoveryCode":"DEM2DEM2DEM2DEM2DEM2"}   # store it: shown once, never retrievable
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"usernameOrEmail":"keeper","password":"Local-demo-1!"}'
# Already taken? /register answers 409 registration_conflict with errors[] naming email_taken or username_taken.
# Forgot it instead? Ask for a code; an identifier nobody holds answers 404 identifier_unknown (ADR-020), and in
# a development environment the code itself is in the API log (PasswordReset:LogCode).
curl -s -X POST http://localhost:8080/api/v1/auth/forgot-password \
  -H 'Content-Type: application/json' -d '{"usernameOrEmail":"keeper"}'
curl -s -X POST http://localhost:8080/api/v1/auth/reset-password \
  -H 'Content-Type: application/json' \
  -d '{"usernameOrEmail":"keeper","resetCode":"DEM3DEM3DEM3DEM3DEM3","newPassword":"Local-demo-2!"}'
# -> 200 {"recoveryCode":"DEM4DEM4DEM4DEM4DEM4"}   # the backup code rotated too, so take the new one

# With a terrarium and a sample in place, the read surface the dashboard uses:
curl -s "http://localhost:8080/api/v1/terrariums/$ID/readings/latest"
curl -s "http://localhost:8080/api/v1/terrariums/$ID/readings?metric=tempC&from=2026-10-06T00:00:00Z&to=2026-10-07T00:00:00Z"

# The effective band per metric, with the layer each one came from:
curl -s "http://localhost:8080/api/v1/terrariums/$ID/thresholds"
# -> {"terrariumId":"…","capturedAtUtc":"…","timeZoneId":"Asia/Ho_Chi_Minh",
#     "effectiveThresholds":[{"metric":"tempC","unit":"°C","phase":"night","source":"profile", …}]}

# Writing one records a snapshot of the new set, so an alert already open keeps the band it was judged against while
# the next sample is judged against this one; reverting a metric gives the profile's band back (needs terrarium.write):
curl -s -X PUT -H "Authorization: ******" -H 'Content-Type: application/json' \
     -d '{"overrides":[{"metric":"tempC","phase":"any","targetMin":28,"targetMax":30,"note":"summer"}]}' \
     "http://localhost:8080/api/v1/terrariums/$ID/thresholds"
# -> 200 …{"metric":"tempC","unit":"°C","phase":"any","source":"override","targetMin":28,"targetMax":30,…}
curl -s -X DELETE -H "Authorization: ******" "http://localhost:8080/api/v1/terrariums/$ID/thresholds/tempC"
# -> 200 with source "profile" again; a metric with no override answers 200 too (idempotent)

# The engine's output: alerts are raised, escalated, acknowledged, resolved and pushed to the clients watching the
# terrarium. Delivery (M3 task 3.5, built 2026-10-16) is the dispatcher's, and the alert is read back over the API:
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/alerts?state=Open&pageSize=5&family=species"
# -> {"items":[{"id":812,"family":"species","severity":"Warning","state":"Open","metric":"tempC","bandMin":26.0,"bandMax":32.0, …}],"nextCursor":null}
#    ?family= takes enclosure|species; leave it out for both, and an unknown key is 400 family_invalid

# What was delivered for one of them, with its channel, its escalation step and its read state:
curl -s "http://localhost:8080/api/v1/notifications?unreadOnly=true"
# -> {"items":[{"id":1,"channel":"InApp","title":"Linh's gecko box · Nhiệt độ quá cao",
#              "body":"34,20 °C (ngưỡng 28,00–32,00 °C), vượt ngưỡng 10 phút","deepLink":"smartreptile://alerts/1",
#              "step":0,"isRead":false, …}],"unread":1}
curl -s -X POST "http://localhost:8080/api/v1/notifications/1/read"    # -> 204, and 204 again on a second call
curl -s -X POST "http://localhost:8080/api/v1/notifications/read-all"  # -> {"markedRead":0} once nothing is unread

# What happened to one of them, who did it, and what the dispatcher did about it:
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/alerts/812/timeline"

# Closing the loop, and telling the system you already know about a condition (needs alert.write — Owner,
# Technician or Admin; a Viewer gets 403):
curl -s -X POST -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/alerts/812/resolve" \
     -H "Content-Type: application/json" -d '{"reason":"Accepted","note":"lamp on purpose"}'
curl -s -X POST -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/v1/terrariums/$ID/silences" \
     -H "Content-Type: application/json" -d '{"metric":"tempC","untilUtc":"2026-10-11T09:00:00Z","reason":"lamp replacement"}'
# The counters behind it (alerts_opened_total counts openings, not escalations):
curl -s http://localhost:8080/metrics
```

**Added 2026-10-13 (backend, M3 task 3.1's write half — the override write path and `ThresholdSnapshot`).** 3.1 is
complete, and the server side the threshold editor (M4's 4.20) was waiting for now exists.
`PUT /api/v1/terrariums/{id}/thresholds` upserts the band for each `(metric, phase)` the body names, and
`DELETE /api/v1/terrariums/{id}/thresholds/{metric}` reverts one metric to its profile — both `Owner`-gated, both
answering with the resulting effective set in the same shape the read route returns. Every entry is validated
against the band rules before anything is written, reported field by field and addressed by position
(`overrides[1].targetMin`), so one incoherent band changes nothing at all; a band byte-for-byte identical to the
stored one is a no-op; and a real change writes one `ThresholdSnapshot` of the set now in force, in the same unit of
work — the table `docs/02-design/02` §3.13 promised and the change path 3.2/3.4 were waiting for. That is what makes
`TC-U-26` hold: an alert already open keeps its denormalised band for the record while the next sample is judged
against the new one, and the ordinary recovery path is what closes it if the reading is back in range. Recorded in
the roadmap (the task's write-half block), `07-appendices/03` §4.2, `04-quality/02` B3 and the design's §3.13;
**655 backend unit tests pass** (was 638, 17 new), and the suite is 26 integration cases of which 25 are green.

Two things from this task are worth the ink rather than a line in a table. **The unit suite could not have caught the
first cut's defect**: staging a new override by adding it to the loaded terrarium's collection made EF treat the
client-assigned key as an existing row, so it emitted `UPDATE [ThresholdOverride] … WHERE [Id] = …`, affected zero
rows and threw `DbUpdateConcurrencyException` — only a real database said so, which is why new rows now go through
`AddThresholdOverride` and its fixture mirrors the adapter. And **`AlertLifecycleTests.The_cursor_pages…` fails
independently of this change**, verified by reverting these edits and watching it fail the same way on pristine
`main`: it expects one alert fewer than its own fixture creates, and it is recorded here rather than quietly fixed
inside a task about thresholds. (**Corrected 2026-10-17:** it was the *expectation* that was wrong — the fixture
seeds four alerts and the case asserted one row on the second page where two remain. The case now asserts the
invariant directly (both pages together, strictly descending, no id twice, four rows in total), and the suite is
green; see the integration row above.)

**Added 2026-10-16 (backend, M3 task 3.5 — the notification layer, and M3's demo line).** M3's definition of done is
one induced excursion producing "exactly one alert **and one notification**"; the alert half has been real since 3.2
and nothing delivered, so the milestone could raise an alarm and tell nobody. That half is now built, and it was
built as two pure functions plus one sweep, because "why was I not told" is a question about rules and the sweep is
the only part that can be wrong in a way rules cannot see. `Domain/Notifications/NotificationPolicy.cs` holds
§02-design/05 §4's whole matrix (`Judge` — recovery notice, silence, maintenance, severity floor, quiet hours with
Critical bypassing them, the hourly cap) as one static function over a `NotificationRequest`, and
`NotificationCadence` holds the arithmetic: the 60-minute repeat cooldown, the 10-per-hour cap, BR-13.5's
5 s / 30 s / 2 min retry backoff and §6's 30 / 60 / 120 minute escalation ladder. `NotificationDispatcher` is the
sweep: it asks the database which alert could owe somebody a message, decides, renders, delivers, records and stages
— all through the two seams (`INotificationStore`, `INotificationChannel`) that keep SQL Server and the network away
from the policy. The inbox is `GET /api/v1/notifications`, `POST /api/v1/notifications/{id}/read` and
`POST /api/v1/notifications/read-all`, and `GET /alerts/{id}/timeline` gained the `notified` entry §02-design/05 §6
was written for. Recorded in `02-design/05` §3/§4/§5/§6 (as built), `07-appendices/02` §3.10 (as built),
`07-appendices/03` §4.5, `03-implementation/03` §6.2/§8, the roadmap's 3.5 row and block, `04-quality/02` B6,
`05-release/03` §5, `docs/README.md` and the schema reference's summary table; **882 backend unit tests pass**
(was 845, 37 new), 5 new integration cases run against real SQL Server (41 of the suite's 42 green at the time — the
red one was the pre-existing cursor-paging case this file already records, **corrected 2026-10-17**), and migration
`20261016143949_AddNotificationLog`
was applied to a scratch database by the application's own `DatabaseInitializer`.

Six readings of the design are worth the ink. **The dispatcher sweeps rather than subscribing**: four writers move an
alert — the evaluator, the silence watchdog, the signal recorder and the lifecycle API — and they live in three
layers, so an event bus would mean all four learning about notifications; the database is the one place that sees
them, and a sweep is idempotent where an event is not (whatever a crash lost, the next pass re-derives). **The table
is one row per attempt**, which is what makes BR-13.5 visible as three rows five and thirty seconds apart rather than
a counter that says the retries happened somewhere. **`NotificationLog.Step` is a column the design sketch lacked**
and it is deliberate: without it a 30-minute escalation and an hourly repeat are both "a second `Sent` row", so
neither §6's timeline nor the ladder itself could be built out of them. **The hourly cap rations the noise, not the
record** — the eleventh notification of an hour loses its push and its email and keeps its inbox entry, because §5
says the alert stays visible and a budget spent by rows nobody can hear is not a budget. **Quiet hours are judged in
the recipient's zone** and a window whose two ends are equal is no window at all: the profile route already refuses
half a pair, so `22:00–22:00` can only be a typo, and reading a typo as "never notify" would silence a keeper's
alarm system. And **the digest's identity is the instant it records** (the top of the hour it covers), so "already
digested" is an equality test rather than a second table.

**It also closes the decision that was left open on 2026-10-07.** With Telegram dropped by `ADR-021`, 3.5's note
deliberately deferred the choice between adding a local SMTP sink and accepting the inbox as the demo's only proof.
`docker-compose.yml` now runs **Mailpit** beside the API (UI on `127.0.0.1:8025`, SMTP on `mailpit:1025`), which is
what `TC-I-12` means by "a local SMTP sink" and what makes §6's 120-minute escalation demonstrable offline; FCM is
real code and stays off until a service-account file is mounted read-only at `/secrets`.

**The live run found a defect, which is the reason to run it.** The API came up headless against SQL Server Express,
applied all twelve pending migrations and started four workers — including `Notification dispatcher started;
sweeping every 15s` — and a seeded excursion (33.4 °C triggering, 34.2 °C peak, band 28–32 °C) produced exactly one
Vietnamese inbox entry within one sweep: `"Linh's gecko box · Nhiệt độ quá cao"` /
`"34,20 °C (ngưỡng 28,00–32,00 °C), vượt ngưỡng 10 phút"` / `smartreptile://alerts/1`, which is §7's contract
measured rather than asserted. `GET /alerts/1/timeline` carried the `notified` entry beside `opened`, `/metrics`
reported `notifications_sent_total: 1`, a second full sweep left the table at **one row**, `POST
/notifications/1/read` answered `204` twice, and an id that is not the caller's answered `404 not_found`. The first
attempt of that run also logged a failed query against a `Alert` table that did not exist yet: the worker's *first*
sweep ran before `DatabaseInitializer` had applied its migrations, so the first tick now waits one interval — the
shape the silence watchdog already used, and the reason its own sweep has never shown that error.
**Still open, stated rather than implied:** a real FCM push (no Google service-account file and no device
registration token exist on this machine) and therefore `TC-E2E-03`; escalation steps 2 and 3 reach the owner only,
because the product holds one account per enclosure and §6's Technician joins at the 30-minute step; and a recovery
notice is always withheld with `preference`, because v1 has no opt-in column for one — the record says a notice was
withheld rather than pretending it was attempted.
