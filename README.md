# SmartReptile

IoT environmental monitoring and alerting for reptile terrariums — **v1: measure, store, display, alert.
No AI, no actuation** (see `docs/07-appendices/06-v2-ai-roadmap.md` for what comes next).

> Design documentation for this repository lives in [`docs/`](docs/) — 34 files covering product, design,
> implementation, quality, release, report and the appendices. Section references below (`§`) point there.

| Component | Path | Stack |
|---|---|---|
| Firmware (sensor node) | `firmware/` | ESP32 / Arduino via PlatformIO (`esp32dev` + `native` for host tests) |
| Backend | `backend/` | ASP.NET Core 10, EF Core 10, SQL Server 2022, in-process MQTT broker (MQTTnet), SignalR |
| Mobile app | `app/` | Flutter (Android primary), `provider` |
| Web dashboard | `web/` | Static HTML/CSS/JS + Chart.js (no build step) |

## Quick start

### 1. Backend + database

```bash
cp .env.example .env          # fill in MSSQL_SA_PASSWORD and JWT_SIGNING_KEY
docker compose up -d          # db, api, web — api waits for db to be healthy
curl -s http://localhost:8080/health/ready | jq
```

The `api` service applies EF migrations and seeds reference data on startup
(`Startup__ApplyMigrationsOnStartup`, `Startup__SeedReferenceDataOnStartup`), so there is no separate migration
step. To apply the migration *explicitly* from the host instead — which is how M1 was verified:

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
flutter run -d android     # or: flutter run -d emulator-5554
flutter test
```

### 5. Web dashboard

```bash
cd web
python -m http.server 8081     # or the `web` service in docker compose (nginx)
```

## Verification gates for Milestone M1 (`docs/03-implementation/07-implementation-roadmap.md`)

Measured on the development machine (Windows, .NET 10.0.112, Flutter 3.47.4, PlatformIO 6.2.0) on **2026-09-21**:

| Gate | Command / method | Result |
|---|---|---|
| Backend builds, warnings as errors | `dotnet build backend/SmartReptile.sln -c Release` | ✅ Build succeeded, 0 warnings |
| Backend unit tests | `dotnet test backend/SmartReptile.sln -c Release` | ✅ **54 passed**, 0 failed |
| Formatting gate (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | ✅ clean (generated migrations excluded via `.editorconfig`) |
| Schema created by migrations only | `dotnet ef migrations add InitialSchema` | ✅ one migration; `(DeviceId, Sequence)` unique, filtered open-alert unique, one-device-per-terrarium unique, band CHECK constraints all present |
| Docker image builds | `docker compose build api` | ✅ builds from a clean context (needed `backend/.dockerignore` — defect 8 below) |
| Compose stack comes up | `docker compose up -d` | ✅ `db`, `api`, `web` all Up; `db` + `api` report `healthy`, 0 container restarts |
| Migration applies to a **real** SQL Server | `dotnet ef database update` against 127.0.0.1:14330 | ✅ `Applying migration '20260921091523_InitialSchema' ... Done.` — 13 tables created |
| Invariants exist in SQL, not only in C# | `sys.indexes`, `sys.check_constraints`, `sys.foreign_keys` | ✅ filtered unique `IX_Alert_DedupeKey ([State]<>(2))`, `IX_Device_TerrariumId ([TerrariumId] IS NOT NULL AND [Status]<>(3))`, 5 `CK_Threshold*` / `CK_ThresholdOverride*` ordering constraints |
| Cascade risk does not fire | read the FK delete rules back | ✅ `Device → Terrarium` is `SET_NULL`, so `TelemetrySample` has a single cascade path — this was the specific shape that was flagged as a risk before the schema existed |
| Reference data seeds on startup | `SELECT COUNT(*) FROM SpeciesProfile` | ✅ 3 profiles (`Arid (desert)`, `Leopard gecko (semi-desert)`, `Tropical (humid forest)`) and 17 threshold bands |
| Dashboard served, and the browser accepts the API | nginx `web` on `:8081` + real browser `fetch` to `:8080` | ✅ `index`/`wallboard`/`health` all `200`; browser fetch of `/version` returns `200` with `Access-Control-Allow-Origin: http://127.0.0.1:8081` (+`204` preflight) |
| Liveness | `curl /health/live` | ✅ `200 Healthy`, answers immediately |
| Readiness with the database **up** | `curl /health/ready` | ✅ `200 Healthy` — `database: Healthy` ("Database is reachable") + `mqtt-broker: Healthy`, 7 ms |
| Readiness with the database **down** | `docker stop smartreptile-db` → `curl /health/ready` | ✅ `503` in **3.0 s** — `database: Unhealthy` ("Database probe timed out after 3 s"), `mqtt-broker: Healthy`; `/health/live`, `/version` and `/metrics` all still `200` (degraded mode, NFR-03). Before the probe was bounded this took 16.1 s — defect 10 below |
| Recovery without an API restart | `docker start smartreptile-db` → `curl /health/ready` | ✅ back to `200 Healthy` in 7 ms; api `RestartCount` was 0 across the whole drill |
| Version / metrics / root | `curl /version /metrics /` | ✅ counters + `brokerRunning: true` |
| MQTT rejects anonymous devices | `paho-mqtt` connect without credentials | ✅ `CONNACK: Bad user name or password`, and `mqtt_rejected_connections_total` incremented |
| MQTT verifies the device secret | — | ⬜ **M2 work**: M1 rejects anonymous connections only; the `DeviceCredential` lookup lands with provisioning (FR-05) |
| Firmware builds for the target | `pio run -e esp32dev` | ✅ RAM 13.6% (44 536 B), Flash 20.7% (270 673 B) |
| Firmware host unit tests | `docker run --rm -v "$PWD/firmware:/firmware" smartreptile-fw-test` (image from `firmware/Dockerfile.host-tests`; plain `pio test -e native` wherever a host compiler exists) | ✅ **22/22 passed** — 8 filters + 8 payload + 6 ring buffer, ~18 s. First ever execution, and it caught defect 11 |
| App analyzes + tests | `flutter analyze && flutter test` | ✅ `No issues found!`, **19 tests passed** |
| App release APK | `flutter build apk --release` | ⬜ M6 (release milestone) |
| No committed secrets or build output | `git ls-files` audit | ✅ 164 files tracked; only `.env.example`; no `bin/`, `obj/`, `.dart_tool/`, `.pio/`, keystores |
| **CI runs, and passes** | push to `master` → `gh run watch` | ✅ **all five jobs green** (`backend`, `integration`, `app`, `firmware`, `secret-scan`), ~2 min wall clock. The firmware job is where the 22 host tests execute in CI |
| Integration tests against a **real SQL Server** | `dotnet test backend/tests/SmartReptile.Tests.Integration` (CI: the `integration` job with a SQL Server 2022 service container) | ✅ **8 passed** — migrations applied, 3 profiles + 17 bands seeded, re-seeding duplicates nothing, and the SQL-level invariants reject what they should |

### What M1 still does *not* verify (stated, not hidden)

- **No end-to-end, soak or chaos coverage.** The database paths are enforced by the `integration` job now, but
  nothing drives the full chain (device → broker → threshold engine → alert → notification), no test runs for hours,
  and no failure drill has been executed against a live stack — those are M5 work by plan. The integration tests
  also run against a single SQL Server that is already up, so they say nothing about a database that disappears
  mid-run.
- **The REST surface does not exist yet.** The API exposes ops endpoints only (`/health`, `/version`, `/metrics`,
  the SignalR hub), so the dashboard's live view requests `/api/v1/terrariums`, receives `404`, and degrades to
  its empty state showing `(http_404)` — by design, not by accident; those endpoints are M2/M3 work.
- **No sensor hardware is connected**: `main.cpp` runs with bench mode off and reports placeholder values until
  M2, and no board has ever been flashed from this repository.
- **The MQTT broker accepts *any* username/password today** — only *anonymous* connections are refused; the
  `DeviceCredential` lookup lands with provisioning (FR-05).
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
Windows `localhost` resolves to IPv6 `::1` first, Docker Desktop does not proxy a container's published port on
`::1`, and a refused IPv6 attempt is followed by a **connect timeout** rather than a quick fallback — so use
`127.0.0.1` in every host-side connection string.


## Repository conventions

- **Do not commit `.env`**, keystores, or generated build output — see `.gitignore`.
- All timestamps are UTC in storage; local-day bucketing happens only in the rollup/summary layer (§`ADR-015`).
- Schema changes go through EF Core migrations only (§`03-implementation/03` §2).
- Interfaces in `Application`, implementations in `Infrastructure`, no EF Core types in `Domain`
  (§`02-design/01` §4).
- Version source of truth: the `VERSION` file at the repository root.

## Status

Milestone **M1 (foundations)** scaffolded: solution + layered backend with health/readiness, in-process MQTT
broker, initial EF migration, unit-tested domain logic, firmware skeleton with host tests, Flutter app shell,
web dashboard shell, CI. Next: **M2 — telemetry pipeline** (provisioning, ingest, readings API).
