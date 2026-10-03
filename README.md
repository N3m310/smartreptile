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
| Web dashboard | `web/legacy/` | Static HTML/CSS/JS + Chart.js (no build step) — the only web surface with a real API behind it, and the M4 web surface (`ADR-018`) |
| Web UI prototype | `web/` | React 19 + Vite + TypeScript + Tailwind CSS v4 — mock data, labelled as such, Vietnamese only, **not** the deliverable (`ADR-017`, `ADR-018`) |

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

### 5. Web (`web/` — two surfaces, see `ADR-017` and `ADR-018`)

Compose serves both surfaces from one nginx (`web/nginx.conf`, task 4.12): `http://127.0.0.1:8081/` is the
committed prototype build, and `http://127.0.0.1:8081/legacy/` is the dashboard that talks to the API.

The dashboard — the surface that ships (no build step):

```bash
cd web/legacy
python -m http.server 8081     # the same files compose serves under /legacy/
```

The TERRAGUARD UI prototype (mock data, no network calls, **not** the deliverable):

```bash
cd web
npm install
npm run dev                    # Vite on http://127.0.0.1:8081 — the same port as compose, so run one, not both
npm run build                  # refresh the committed web/dist/ that compose serves at /
```

## Verification gates for Milestone M1 (`docs/03-implementation/07-implementation-roadmap.md`)

Measured on the development machine (Windows, .NET 10.0.112, Flutter 3.47.4, PlatformIO 6.2.0) on **2026-09-21**:

| Gate | Command / method | Result |
|---|---|---|
| Backend builds, warnings as errors | `dotnet build backend/SmartReptile.sln -c Release` | ✅ Build succeeded, 0 warnings |
| Backend unit tests | `dotnet test backend/SmartReptile.sln -c Release` | ✅ **226 passed**, 0 failed (54 at M1, 121 after FR-01, 186 after device provisioning, 226 after broker auth/ACL) |
| Formatting gate (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | ✅ clean (generated migrations excluded via `.editorconfig`) |
| Schema created by migrations only | `dotnet ef migrations add InitialSchema` | ✅ one migration; `(DeviceId, Sequence)` unique, filtered open-alert unique, one-device-per-terrarium unique, band CHECK constraints all present |
| Docker image builds | `docker compose build api` | ✅ builds from a clean context (needed `backend/.dockerignore` — defect 8 below) |
| Compose stack comes up | `docker compose up -d` | ✅ `db`, `api`, `web` all Up; `db` + `api` report `healthy`, 0 container restarts |
| Migration applies to a **real** SQL Server | `dotnet ef database update` against 127.0.0.1:14330 | ✅ `Applying migration '20260921091523_InitialSchema' ... Done.` — 13 tables created |
| Invariants exist in SQL, not only in C# | `sys.indexes`, `sys.check_constraints`, `sys.foreign_keys` | ✅ filtered unique `IX_Alert_DedupeKey ([State]<>(2))`, `IX_Device_TerrariumId ([TerrariumId] IS NOT NULL AND [Status]<>(3))`, 5 `CK_Threshold*` / `CK_ThresholdOverride*` ordering constraints |
| Cascade risk does not fire | read the FK delete rules back | ✅ `Device → Terrarium` is `SET_NULL`, so `TelemetrySample` has a single cascade path — this was the specific shape that was flagged as a risk before the schema existed |
| Reference data seeds on startup | `SELECT COUNT(*) FROM SpeciesProfile` | ✅ 3 profiles (`Arid (desert)`, `Leopard gecko (semi-desert)`, `Tropical (humid forest)`) and 17 threshold bands |
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
| Device provisioning over HTTP | `curl` against a live API + SQL Server (50 assertions) | ✅ **2.2 done 2026-10-03**: self-register `201`/`429`/`400`, claim `200`/`404 claim_code_invalid`/`404 not_found`/`409 terrarium_already_bound`, rotate `200` with a 10-minute grace, revoke `204` and idempotent — with `Owner`-only gating (`Technician` → `403 insufficient_role`) |
| Firmware builds for the target | `pio run -e esp32dev` | ✅ RAM 13.6% (44 536 B), Flash 20.7% (270 673 B) |
| Firmware host unit tests | `docker run --rm -v "$PWD/firmware:/firmware" smartreptile-fw-test` (image from `firmware/Dockerfile.host-tests`; plain `pio test -e native` wherever a host compiler exists) | ✅ **22/22 passed** — 8 filters + 8 payload + 6 ring buffer, ~18 s. First ever execution, and it caught defect 11 |
| App analyzes + tests | `flutter analyze && flutter test` | ✅ `No issues found!`, **19 tests passed** |
| App release APK | `flutter build apk --release` | ⬜ M6 (release milestone) |
| No committed secrets or build output | `git ls-files` audit | ✅ at M1: 164 files tracked; only `.env.example`; no `bin/`, `obj/`, `.dart_tool/`, `.pio/`, keystores. **Re-audited 2026-10-03:** 247 files and still no secrets — the increase is the TERRAGUARD prototype, including the three `web/dist/` build artefacts that `ADR-017` accepts deliberately |
| **CI runs, and passes** | push to `master` → `gh run watch` | ✅ **all five jobs green** (`backend`, `integration`, `app`, `firmware`, `secret-scan`) in run `35602903951`, ~2 min wall clock. The firmware job is where the 22 host tests execute in CI |
| Integration tests against a **real SQL Server** | `dotnet test backend/tests/SmartReptile.Tests.Integration` (CI: the `integration` job with a SQL Server 2022 service container) | ✅ **8 passed** — migrations applied, 3 profiles + 17 bands seeded, re-seeding duplicates nothing, and the SQL-level invariants reject what they should |

### What M1 still does *not* verify (stated, not hidden)

- **No end-to-end, soak or chaos coverage.** The database paths are enforced by the `integration` job now, but
  nothing drives the full chain (device → broker → threshold engine → alert → notification), no test runs for hours,
  and no failure drill has been executed against a live stack — those are M5 work by plan. The integration tests
  also run against a single SQL Server that is already up, so they say nothing about a database that disappears
  mid-run.
- **The domain REST surface is partly there.** The API serves the ops endpoints (`/health`, `/version`,
  `/metrics`, the SignalR hub), `/api/v1/auth` (register, login, refresh, logout, me, change-password — added
  2026-10-03) and `/api/v1/devices` (self-register, claim, rotate-secret, revoke — added the same day, task 2.2),
  so the dashboard's live view still requests `/api/v1/terrariums`, receives `404`, and degrades to its empty
  state showing `(http_404)` — by design, not by accident. A device can only be claimed into a terrarium that
  exists, and terrariums CRUD is task 2.8, so today that row has to be created through SQL.
  The terrarium, reading, threshold and alert endpoints are M2/M3 work.
- **No sensor hardware is connected**: `main.cpp` runs with bench mode off and reports placeholder values until
  M2, and no board has ever been flashed from this repository.
- **The MQTT broker verifies device credentials** (task 2.3, 2026-10-03): `username = deviceId`, `password =
  secret`, validated against the current `DeviceCredential` with the rotation grace window applied, plus a
  per-device topic ACL and a revoke-time session kick. What is still missing is the *consumer*: nothing reads
  `sr/v1/d/+/telemetry` yet, so a published batch is accepted and counted but not stored — `IngestWorker` is
  task 2.4.
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
invalidates every credential. All of it is gated by the role policies — `Owner` for claim, rotate and revoke, so a
`Technician` gets `403 insufficient_role`. Run end to end against the same live stack, 50 assertions green;
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
accepts arrives with task 2.4.

The whole session — the TERRAGUARD re-base and its three follow-ups, FR-01, and tasks 2.2 and 2.3 — is recorded
build by build in [`IMPLEMENTATION_SUMMARY_2026-10-03.md`](IMPLEMENTATION_SUMMARY_2026-10-03.md), including the
nine defects that only showed up once the code and its gates were run.

```bash
# The password below is a throwaway value for a local demo account. Keep it obviously synthetic: CI's secret
# scanner rejects realistic-looking credentials wherever they appear, test fixtures and documentation included,
# and allow-listing those paths is exactly how a real key gets committed unnoticed.
curl -s -X POST http://localhost:8080/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"keeper","email":"keeper@example.com","password":"local-demo-1"}'
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"usernameOrEmail":"keeper","password":"local-demo-1"}'
```
