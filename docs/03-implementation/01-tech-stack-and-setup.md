# 01 — Tech Stack and Setup

Three deliverables in one repository: **firmware**, **backend**, **clients**. Versions are pinned;
unpinned versions are treated as a defect because "it worked last week" is not a design.

## 1. Pinned stack

| Component | Technology | Version | Why |
|---|---|---|---|
| Firmware | C/C++ on Arduino core for ESP32 via **PlatformIO** | `platform = espressif32@6.9.0`, `framework = arduino`, `board = esp32dev` | Reproducible builds; PlatformIO pins the toolchain, not just the source |
| Firmware libs | `WiFiManager`/`WebServer` (portal), `PubSubClient`, `ArduinoJson@7`, `Adafruit SHT31`, `BH1750`, `Adafruit LTR390`, `OneWire` + `DallasTemperature`, `Adafruit SSD1306`, `Preferences` (NVS), `time.h`/`esp_sntp` | pinned in `platformio.ini` | All actively maintained (OWASP I5) |
| Backend | **ASP.NET Core** (`net10.0`) Web API, minimal APIs + controllers for auth | 10.x | Team familiarity, strong EF Core story, matches the mentor's reference stack |
| Data access | **EF Core** + `Microsoft.EntityFrameworkCore.SqlServer` | 10.x | Migrations as the only schema path |
| Database | **SQL Server 2022** (Docker image `mcr.microsoft.com/mssql/server:2022-latest`) | 2022 | Indexing + filtered unique indexes used by DI-01/DI-02/DI-04 |
| Messaging | **MQTTnet** broker hosted in-process for the demo + `MQTTnet.Client` subscriber | 4.x | No extra infrastructure; swappable for Mosquitto/HiveMQ in production |
| Real-time | **SignalR** (`Microsoft.AspNetCore.SignalR`) | 10.x | Push to app + dashboard |
| Push | `FirebaseAdmin` (FCM) | latest stable, pinned | Android push |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.2 (validation) + `System.IdentityModel.Tokens.Jwt` 8.23.0 (issuing) + custom PBKDF2 (`Rfc2898DeriveBytes`) | 10.0.2 / 8.23.0 | No external identity provider needed; the designer of record for the token lifetime and claims is BR-01.3 |
| Logging | `Serilog` + console/file sinks (structured JSON) | latest pinned | Correlation id propagation |
| API docs | `Microsoft.AspNetCore.OpenApi` + `Swagger/OpenAPI` UI | 10.x | Also the contract source for the app's client code |
| Tests (backend) | `xUnit`, `FluentAssertions`, `Testcontainers.MsSql` (integration), `WireMock.Net` (channel fakes) | pinned | Integration tests against a real SQL Server container |
| Mobile app | **Flutter** stable + Dart | Flutter 3.47.x / Dart 3.13.x | Rubric requires a real mobile app |
| App packages | `provider` (state), `http` (REST), `signalr_netcore` or `web_socket_channel` (live), `fl_chart` (charts), `shared_preferences` (non-secret cache), `flutter_secure_storage` (tokens), `firebase_messaging`, `intl`, `flutter_localizations`, `go_router` | pinned in `pubspec.yaml` | `provider` is explicitly on the rubric |
| App tests | `flutter_test`, `mocktail`, `integration_test` | pinned | Widget + unit + E2E |
| Web client (`web/`, TERRAGUARD) | **React 19** + **Vite 6** + **TypeScript 5.7** + **Tailwind CSS v4**, plus `react-router-dom` 7, `recharts` 2, `lucide-react` | pinned in `web/package.json` | **The web surface** (`ADR-019`): the M1 static dashboard was retired on 2026-10-07 (task 4.19) and this client absorbed it — sign-in and both recovery paths, the live dashboard, terrariums, history, the wallboard and the ops diagnosis page all read the API. The API client, the shared ARB key set and the CI job (`tsc`, key parity, build) arrived with 4.15–4.18 |
| Node (web client only) | Node.js 22 LTS + npm | 22.x | `npm run dev` / `npm run build` for `web/`; nothing else in the stack needs Node, and the committed `web/dist/` keeps the nginx demo Node-free |
| Reverse proxy | `nginx:1.27-alpine` serving the dashboard + TLS termination for the demo host | 1.27 | Simple, well understood |
| Orchestration | **Docker Compose** | v2 | NFR-08 |
| CI | GitHub Actions (or local `make ci` if no CI access) | — | `dotnet test`, `flutter test`, `pio run`, `flutter analyze` |

## 2. Repository layout (monorepo)

```
smartreptile/
├── README.md                     # 10-line quick start
├── .env.example                  # documented env vars, NO secrets
├── docker-compose.yml
├── docs/                         # → this doc set (this is its home; the repository is the source of truth)
├── firmware/
│   ├── platformio.ini
│   ├── include/sr_config.h        # pin map, intervals, topic templates
│   └── src/{main.cpp, sensors/*.cpp, net/*.cpp, storage/ringbuffer.cpp, ui/oled.cpp}
├── backend/
│   ├── SmartReptile.sln
│   ├── src/SmartReptile.Domain/          # entities, value objects, no dependencies
│   ├── src/SmartReptile.Application/     # use cases, services, interfaces, validators
│   ├── src/SmartReptile.Infrastructure/  # EF Core, repositories, MQTT, FCM, hashing
│   ├── src/SmartReptile.Api/             # endpoints, auth, SignalR, workers, Program.cs
│   └── tests/SmartReptile.Tests.{Unit,Integration}
├── app/                          # Flutter
│   ├── lib/{core,data,state,screens,widgets,l10n}
│   └── test/  integration_test/
├── scripts/                      # dump-database.ps1 -> regenerates 07-appendices/07 (DB contents, secrets redacted)
└── web/                          # one web surface: the TERRAGUARD client (ADR-019; web/legacy/ retired 2026-10-07)
    ├── index.html package.json package-lock.json vite.config.ts tsconfig.json nginx.conf
    ├── scripts/check-strings.mjs  # key parity + every referenced key defined (TC-I-15), run in CI
    ├── src/{App.tsx, main.tsx, index.css}
    │   ├── api/       # transport, session, DTOs and one function per route
    │   ├── i18n/      # reads app/lib/l10n/app_*.arb directly: one copy deck, two clients
    │   ├── state/     # the session provider the guard reads
    │   ├── lib/       # formatting, status rendering, error-code sentences, polling
    │   ├── components/ pages/
    │   └── data/mockData.ts       # only the three screens 4.20 has no endpoints for
    └── dist/                      # committed build; nginx serves it (no Node at demo time)
    └── dist/                     # committed Vite build of the prototype — deliberate exception (ADR-017)
```

## 3. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.x | `dotnet --list-sdks` |
| Flutter SDK | 3.47.x stable | `flutter --version` |
| Node.js + npm | 22.x LTS (needed for `web/` only — ADR-017) | `node --version` |
| Docker Desktop / Engine + Compose | v2 | `docker compose version` |
| PlatformIO Core | 6.x (standalone, or the VS Code extension) | `pio --version` |
| USB-UART driver | CP210x or CH340 (depends on the dev board) | Device appears as `COMx` |
| JDK for Android builds | 17+ (Android Studio JBR works) | `flutter doctor -v` |
| SQL client (optional) | Azure Data Studio / `sqlcmd` | for migration inspection |

> **Toolchain gotcha (recorded because it already bit this machine once):** the `java` on PATH being
> Java 8 breaks the Android Gradle build. Fix with
> `flutter config --jdk-dir "C:\Program Files\Android\Android Studio\jbr"` before the first Android build.

## 4. Bootstrap commands

### 4.1 Backend + database + broker (Docker Compose)

```bash
cp .env.example .env          # then fill in: MSSQL_SA_PASSWORD, JWT_SIGNING_KEY, PROVISIONING_SHARED_KEY
docker compose up -d db       # start SQL Server first, wait for healthy
docker compose up -d api mqtt web
docker compose logs -f api    # expect: "Applying migrations..." then "Now listening on: http://+:8080"
```

Development without Docker (faster inner loop for the API):

```bash
cd backend
dotnet restore
dotnet ef database update --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
dotnet run --project src/SmartReptile.Api
```

### 4.2 Firmware

```bash
cd firmware
pio run                 # build
pio run -t upload       # flash over USB
pio device monitor -b 115200
```

First boot prints Wi-Fi configuration instructions and, after registration, the claim code.
`firmware/include/sr_config.h` holds the pin map (`07-appendices/04` §2) and must match the wiring. It is
prefixed `sr_` because the ESP32 framework ships its own `config.h`, which would shadow a generically named one.

### 4.3 Flutter app

```bash
cd app
flutter pub get
flutter gen-l10n
flutter run -d android               # or: flutter run -d emulator-5554
flutter build apk --release --split-per-abi
```

`app/lib/core/env.dart` holds the API base URL per build flavour (`dev` → `http://10.0.2.2:8080` for the
Android emulator, `demo` → `https://<host>`); no secrets are compiled into the app.

### 4.4 Web (`web/`) — one surface

The **TERRAGUARD client** is the web surface (`ADR-019`); the M1 static dashboard was retired on 2026-10-07 by
task 4.19, and every screen it had is a React route now (including the ops diagnosis page it used to own as
`health.html`):

```bash
cd web
npm install
npm run dev                          # Vite on http://127.0.0.1:8081
npm run typecheck                    # tsc --noEmit, the first half of the CI job
npm run check:strings                # key parity + every referenced key defined (TC-I-15)
npm run build                        # writes web/dist/, which is committed on purpose (ADR-017)
```

`npm run dev` binds the same port as the compose `web` service, so run one or the other. Compose serves one surface
from one nginx (`web/nginx.conf`): `/` is the committed build, with an `index.html` fallback so `/wallboard` and any
deep route return `200` instead of a 404. **BUG-03** (`05-release/03` §4) stays closed, and its regression check is
the `curl` pair in `05-release/01` §5 — rewritten for one mount when the two-mount pair became obsolete.

The ARB files are the copy deck for **both** clients: `src/i18n/strings.ts` imports
`../../app/lib/l10n/app_{en,vi}.arb` through Vite's `?raw`, so `TC-I-15`'s "identical key sets" is structural rather
than a comparison of two lists. Adding a string means adding it to both ARB files; `npm run check:strings` fails the
build if a screen names a key that no locale defines.

## 5. Configuration reference (`.env.example`)

| Key | Example | Notes |
|---|---|---|
| `MSSQL_SA_PASSWORD` | *(blank)* | Never committed; needed by the `db` service |
| `JWT_SIGNING_KEY` | *(blank)* | ≥ 32 bytes base64 |
| `JWT_ACCESS_MINUTES` / `JWT_REFRESH_DAYS` | `15` / `30` | FR-01 |
| `MQTT_PORT` / `MQTT_TLS_PORT` | `1883` / `8883` | FR-05. Devices connect on the TLS port; the plaintext port is for local debugging |
| `MQTT_PLAINTEXT_HOST` | `127.0.0.1` | BR-05.1: plaintext is loopback-only. Compose overrides it to `0.0.0.0` because a container is not reachable on its own loopback, and restricts the port at the published mapping instead (`127.0.0.1:1883:1883`) |
| `MQTT_ENABLE_TLS` / `MQTT_DISABLE_PLAINTEXT` | `false` / `false` | BR-05.1: the release runbook sets both to serve TLS only |
| `MQTT_SERVER_CERT_PATH` / `MQTT_SERVER_CERT_PASSWORD` | `/certs/server.pfx` / *(blank)* | PFX (certificate **and** private key) mounted read-only — the broker loads it with `X509CertificateLoader`, so a loose `.crt`/`.key` pair is not enough |
| `MQTT_REQUIRE_CLIENT_AUTH` | `true` | FR-05: a connection must carry a device credential |
| `INGEST_HTTP_FALLBACK_ENABLED` | `true` | FR-06 |
| `FCM_SERVICE_ACCOUNT_PATH` | `/secrets/fcm.json` | Optional; push disabled if absent |
| `SMTP_*` | *(blank)* | Optional channel |
| `RETENTION_RAW_DAYS` | `90` | FR-15 |
| `RETENTION_ROLLUP_MONTHS` | `24` | FR-15 |
| `DEFAULT_TIMEZONE` | `Asia/Ho_Chi_Minh` | NFR-10 |
| `EVAL_DWELL_WARN_MINUTES` / `..._CRIT_MINUTES` | `5` / `2` | FR-11 defaults (overridable per threshold row) |

## 6. Definition of "environment works" (gate before Milestone 2)

1. `docker compose up` brings the API to `/health` green and `/ready` shows `db:true, broker:true`.
2. `dotnet test` runs green with at least one Testcontainers integration test.
3. `flutter test` green; app logs in against the local API.
4. `pio run` succeeds and the board prints sensor values over serial.
5. A manual MQTT publish with a fake device credential appears in `/api/v1/terrariums/{id}/readings/latest`.

Only after all five are true does feature work start (`03-implementation/07-implementation-roadmap.md` §2).
