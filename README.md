# SmartReptile

IoT environmental monitoring and alerting for reptile terrariums — **v1: measure, store, display, alert.
No AI, no actuation** (see `docs/07-appendices/06-v2-ai-roadmap.md` for what comes next).

> Design documentation for this repository lives in [`../smartreptile-docs/`](../smartreptile-docs/) (34 files:
> product, design, implementation, quality, release, report, appendices). Section references below (`§`) point there.

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
docker compose up -d db       # wait until healthy: docker compose ps
docker compose run --rm api dotnet ef database update --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
docker compose up -d api web
curl -s http://localhost:8080/health/ready | jq
```

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

| Gate | Command | Status |
|---|---|---|
| Backend builds | `dotnet build backend/SmartReptile.sln -c Release` | ✅ |
| Backend unit tests | `dotnet test backend/SmartReptile.sln` | ✅ |
| Formatting gate | `dotnet format backend/SmartReptile.sln --verify-no-changes --severity error` | ✅ |
| Schema created by migrations only | `dotnet ef database update` | ✅ InitialSchema |
| `/health/ready` reports `db` + `broker` | `curl localhost:8080/health/ready` | ✅ |
| App analyses + tests clean | `flutter analyze && flutter test` | ✅ |
| Firmware native tests | `pio test -e native` | ✅ 14 tests |
| Secret scan | CI job | ✅ |

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
