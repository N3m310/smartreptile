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

Measured on the development machine (Windows, .NET 10.0.112, Flutter 3.47.4, PlatformIO 6.2.0) on **2026-09-21**:

| Gate | Command / method | Result |
|---|---|---|
| Backend builds, warnings as errors | `dotnet build backend/SmartReptile.sln -c Release` | ✅ Build succeeded, 0 warnings |
| Backend unit tests | `dotnet test backend/SmartReptile.sln -c Release` | ✅ **54 passed**, 0 failed |
| Formatting gate (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | ✅ clean (generated migrations excluded via `.editorconfig`) |
| Schema created by migrations only | `dotnet ef migrations add InitialSchema` | ✅ one migration; `(DeviceId, Sequence)` unique, filtered open-alert unique, one-device-per-terrarium unique, band CHECK constraints all present |
| Liveness | `curl /health/live` | ✅ `200 Healthy`, answers immediately |
| Readiness with the database **down** | `curl /health/ready` | ✅ `503` — `database: Unhealthy`, `mqtt-broker: Healthy`, in ~3 s; the API kept serving (degraded mode, NFR-03) |
| Readiness with the database **up** | `docker compose up -d db` → `/health/ready` | ⬜ not run: the Docker Desktop daemon was stopped on this machine. Run the Quick start above to close this gate |
| Version / metrics / root | `curl /version /metrics /` | ✅ counters + `brokerRunning: true` |
| MQTT rejects anonymous devices | `paho-mqtt` connect without credentials | ✅ `CONNACK: Bad user name or password`, and `mqtt_rejected_connections_total` incremented |
| MQTT verifies the device secret | — | ⬜ **M2 work**: M1 rejects anonymous connections only; the `DeviceCredential` lookup lands with provisioning (FR-05) |
| Firmware builds for the target | `pio run -e esp32dev` | ✅ RAM 13.6% (44 536 B), Flash 20.7% (270 673 B) |
| Firmware host unit tests | `pio test -e native` | ⚠️ 22 cases written; **runs in CI only** — this machine has no host C++ compiler (see `firmware/README.md`) |
| App analyzes + tests | `flutter analyze && flutter test` | ✅ `No issues found!`, **19 tests passed** |
| App release APK | `flutter build apk --release` | ⬜ M6 (release milestone) |
| No committed secrets or build output | `git ls-files` audit | ✅ 127 files tracked; only `.env.example`; no `bin/`, `obj/`, `.dart_tool/`, `.pio/`, keystores |

### What M1 did *not* verify (stated, not hidden)

- Nothing has been deployed to a container yet (Docker daemon was off), so the Compose stack and the SQL Server
  migration apply are unverified on this machine.
- No sensor hardware is connected: `main.cpp` runs with bench mode off and reports placeholder values until M2.
- The MQTT broker accepts *any* username/password today — only *anonymous* connections are refused.
- Firmware host tests are unrun locally (toolchain gap), so their first execution will be CI's.


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
