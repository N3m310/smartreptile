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
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer`, custom PBKDF2 (`Rfc2898DeriveBytes`) | 10.x | No external identity provider needed |
| Logging | `Serilog` + console/file sinks (structured JSON) | latest pinned | Correlation id propagation |
| API docs | `Microsoft.AspNetCore.OpenApi` + `Swagger/OpenAPI` UI | 10.x | Also the contract source for the app's client code |
| Tests (backend) | `xUnit`, `FluentAssertions`, `Testcontainers.MsSql` (integration), `WireMock.Net` (channel fakes) | pinned | Integration tests against a real SQL Server container |
| Mobile app | **Flutter** stable + Dart | Flutter 3.47.x / Dart 3.13.x | Rubric requires a real mobile app |
| App packages | `provider` (state), `http` (REST), `signalr_netcore` or `web_socket_channel` (live), `fl_chart` (charts), `shared_preferences` (non-secret cache), `flutter_secure_storage` (tokens), `firebase_messaging`, `intl`, `flutter_localizations`, `go_router` | pinned in `pubspec.yaml` | `provider` is explicitly on the rubric |
| App tests | `flutter_test`, `mocktail`, `integration_test` | pinned | Widget + unit + E2E |
| Web dashboard | Static HTML + CSS + vanilla JS + **Chart.js** | Chart.js 4.x pinned | Matches the mentor's reference ("nhẹ, có thể mở rộng lên React"); no build step to break at demo time |
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
│   ├── src/SmartReptile.Infrastructure/  # EF Core, repositories, MQTT, FCM, Telegram, hashing
│   ├── src/SmartReptile.Api/             # endpoints, auth, SignalR, workers, Program.cs
│   └── tests/SmartReptile.Tests.{Unit,Integration}
├── app/                          # Flutter
│   ├── lib/{core,data,state,screens,widgets,l10n}
│   └── test/  integration_test/
└── web/                          # dashboard
    ├── index.html wallboard.html alerts.html thresholds.html devices.html report.html admin.html
    ├── css/app.css
    └── js/{api.js, i18n.js, live.js, charts.js, pages/*.js}
```

## 3. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.x | `dotnet --list-sdks` |
| Flutter SDK | 3.47.x stable | `flutter --version` |
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
cp .env.example .env          # then fill in: MSSQL_SA_PASSWORD, JWT_SIGNING_KEY, TELEGRAM_BOT_TOKEN
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

### 4.4 Web dashboard

```bash
cd web
python -m http.server 8081           # or served by nginx in compose
```

## 5. Configuration reference (`.env.example`)

| Key | Example | Notes |
|---|---|---|
| `MSSQL_SA_PASSWORD` | *(blank)* | Never committed; needed by the `db` service |
| `JWT_SIGNING_KEY` | *(blank)* | ≥ 32 bytes base64 |
| `JWT_ACCESS_MINUTES` / `JWT_REFRESH_DAYS` | `15` / `30` | FR-01 |
| `MQTT_BROKER_PORT_TLS` / `..._PLAINTEXT` | `8883` / `1883` (loopback only) | FR-05 |
| `MQTT_TLS_CERT_PATH` / `..._KEY_PATH` | `/certs/server.crt` / `.key` | Mounted read-only |
| `INGEST_HTTP_FALLBACK_ENABLED` | `true` | FR-06 |
| `FCM_SERVICE_ACCOUNT_PATH` | `/secrets/fcm.json` | Optional; push disabled if absent |
| `TELEGRAM_BOT_TOKEN` | *(blank)* | Optional channel |
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
