# 01 — Build and Release

Three artefacts ship: **firmware** (flashed node), **backend** (Docker Compose stack), **app** (signed
release APK + App Bundle). The rubric requires proof of a release build, so every step below produces a
screenshot or a hash.

## 1. Versioning

| Artefact | Scheme | Example | Where it appears |
|---|---|---|---|
| Firmware | SemVer, `-rc` while testing | `1.0.0` | Serial banner, OLED, health payload `fw`, fleet view, report appendix |
| Backend API | SemVer | `1.0.0` | `GET /health` body, container tag `smartreptile/api:1.0.0` |
| App | SemVer + build code | `1.0.0+10` | `pubspec.yaml` `version`, Settings → About, release screenshot |
| DB schema | EF migration name | `20260921_InitialSchema` | `__EFMigrationsHistory` |
| Doc set | same as the shipped release | `v1.0.0` | `README.md` §5 status table |

A single `VERSION` file at the repository root is the source of truth; the build scripts inject it into
firmware `build_info.h`, the API assembly, and the app's `--dart-define=APP_VERSION`.

## 2. Firmware release

```bash
cd firmware
pio test -e native                               # 22 host cases must pass first (see firmware/README.md if no g++)
pio run -e esp32dev                              # release build
pio run -e esp32dev -t size                      # record flash/RAM numbers for the report
pio run -e esp32dev -t upload                    # flash
pio device monitor -b 115200                     # confirm version + first sample
```

> `pio run -e native` is deliberately absent: the `native` environment exists to be *tested*, and a plain
> `pio run -e native` tries to compile `src/main.cpp`, which needs Arduino headers and cannot build for the host.
> On a machine with no host C++ compiler, run the cases through the image in `firmware/Dockerfile.host-tests`
> instead of installing a toolchain — that is how the 22/22 figure below was measured.

Release checklist:

| # | Item | Evidence |
|---|---|---|
| 2.1 | `pio test -e native` green — **22/22** host cases (8 filters + 8 payload + 6 ring buffer), measured 2026-09-21 | terminal output saved |
| 2.2 | Version string matches `VERSION` | serial banner screenshot |
| 2.3 | No secret in the binary or the logs | `pio run -t upload` then grep the serial log for the secret pattern → no match |
| 2.4 | `1883` unused; TLS `8883` verified against the real broker certificate | serial log shows successful TLS handshake |
| 2.5 | Heap/flash figures recorded | `pio run -t size` output |
| 2.6 | Tagged in git (`fw-1.0.0`) with the exact `platformio.ini` used | `git tag -l`, build reproducibility note |

The release firmware is archived together with `platformio.ini` (pinned platform and library versions) so
the build is reproducible (NFR-08). No OTA in v1 — the node is flashed over USB (ADR-008/limitation L-03).

## 3. Backend release

```bash
cp .env.example .env                          # fill secrets; .env is git-ignored
docker compose build --pull
docker compose up -d                          # db + api + web. There is no `mqtt` service: the broker runs
                                              # inside the API process (see the header of docker-compose.yml)
curl -fsS http://localhost:8080/health/live  | jq
curl -fsS http://localhost:8080/health/ready | jq
```

With `Startup__ApplyMigrationsOnStartup=true` (the local default) the `api` service migrates and seeds on
start-up. For a release run set `STARTUP_APPLY_MIGRATIONS=false` and apply the migration as an explicit host
step — the `api` image is a *runtime* image, so it contains neither the SDK nor the `dotnet-ef` tool:

```bash
cd backend
export ConnectionStrings__Default="Server=127.0.0.1,14330;Database=SmartReptile;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True;Encrypt=False"
dotnet ef database update --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
```

The host publishes SQL Server on **127.0.0.1:14330**, not 1433, so this stack coexists with the other course
project on the same machine. Spell it `127.0.0.1`: on Windows `localhost` resolves to IPv6 `::1`, Docker Desktop
does not proxy the published port on `::1`, and the failure appears as a 16 s connect timeout rather than a
fast refusal — which reads like a down database when the database is fine.

Deployment rules:
- Migrations are applied as an **explicit step** in the release runbook, not on API start-up in release config, so a
  broken migration cannot silently take the host down (`03-implementation/03` §2).
- Seeding runs once after migration (`ReferenceDataSeeder`), idempotent by `Code`/`Name`.
- `backend/.dockerignore` is **required**, not an optimisation: without it `COPY src/ src/` overwrites the
  container's Linux restore output with the developer's `obj/project.assets.json`, whose `packageFolders` hard-code
  `C:\Users\<user>\.nuget\packages\`, and the build dies with `NETSDK1064` for a package that is present.
- The container healthcheck probes `/health/live` over bash's `/dev/tcp`, because
  `mcr.microsoft.com/dotnet/aspnet:10.0` ships neither `wget` nor `curl` — a `wget`-based check reports a healthy
  API as `unhealthy`.
- Certificates mounted read-only; FCM service account mounted read-only; secrets only via `.env`
  (NFR-04).
- Before the demo, take a SQL Server backup:

```bash
docker compose exec db mkdir -p /var/opt/mssql/backup        # start-up does not create it
docker compose exec db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "BACKUP DATABASE SmartReptile TO DISK='/var/opt/mssql/backup/demo.bak' WITH INIT"
docker compose cp db:/var/opt/mssql/backup/demo.bak ./backup/demo-$(date +%F).bak
```

> The `mssql-tools18` path is the one that exists in `mcr.microsoft.com/mssql/server:2022-latest`; the
> `.../mssql-tools/bin/sqlcmd` path from older images is absent. The backup/restore drill itself is
> **unverified** — run it once during the M6 rehearsal and record the timings.

Rollback: `docker compose down && cp ./backup/demo.bak ... && docker compose up -d`, documented in the
runbook with a one-line command so it can be executed under pressure.

## 4. Android release build

```bash
cd app
flutter clean
flutter pub get
flutter gen-l10n
flutter test                                   # widget/unit suite green
flutter build appbundle --release --dart-define=API_BASE=https://<host> --dart-define=APP_VERSION=1.0.0+10
flutter build apk --release --split-per-abi
```

Signing (first time only):

```bash
keytool -genkey -v -keystore ~/smartreptile-release.jks -keyalg RSA -keysize 2048 -validity 10000 \
        -alias smartreptile
# create app/android/key.properties (git-ignored):
#   storePassword=…  keyPassword=…  keyAlias=smartreptile  storeFile=<absolute path>/smartreptile-release.jks
```

| # | Item | Evidence |
|---|---|---|
| 4.1 | `flutter test` green | terminal summary |
| 4.2 | `flutter analyze` clean | output |
| 4.3 | Release build produced (not debug) | `build/app/outputs/flutter-apk/app-release.apk` size + timestamp |
| 4.4 | APK signature verified | `apksigner verify --print-certs app-release.apk` output |
| 4.5 | **Release-mode proof**: screenshot of Settings → About showing version + `release` build mode, and the app receiving a real push while installed from the APK (not `flutter run`) | screenshots for the report |
| 4.6 | No secrets compiled into the app (`strings`/grep for keys finds nothing but the API base URL) | grep output |
| 4.7 | Installed on a physical device via the APK file (not via IDE) | installation screenshot |

Per-ABI sizes (`arm64-v8a`, `armeabi-v7a`) are recorded for the report; the universal APK is only used as a
demo fallback.

## 5. Web dashboard release

- No build step. Files are served by nginx from `web/` inside the compose stack.
- `js/api.js` takes the API base URL from a `<meta name="api-base">` tag injected at container start, so the
  same static files work on `localhost` and on the demo host.
- Cache-busting by query string (`app.css?v=1.0.0`) so a stale browser cache cannot show old UI during the
  demo — a small thing that has ruined demos before.

## 6. Release runbook (the order that actually works)

| Step | Command / action | Expected | Time |
|---|---|---|---|
| 1 | `docker compose up -d db` | SQL Server healthy | ~40 s |
| 2 | `docker compose up -d api` (migrations + reference seed run on start-up; set `STARTUP_APPLY_MIGRATIONS=false` and use the §3 `dotnet ef` command for an explicit release step) | logs show `Applying migration '…_InitialSchema'. Done.` | ~30 s |
| 3 | `docker compose up -d web` | `/health/ready` → `{"status":"Healthy","checks":[database Healthy, mqtt-broker Healthy]}`; dashboard on `:8081` | ~5 s |
| 4 | Verify reference data | `SELECT COUNT(*) FROM SpeciesProfile` → 3 profiles; 17 bands in `Threshold` | — |
| 5 | Power the node | OLED shows values; status `online` in the fleet view within 90 s | — |
| 6 | Install/open the release APK | Logged in, live cards populated | — |
| 7 | Open the wallboard on the demo display | Values update silently | — |
| 8 | Send a test alert (induce a 5-min excursion) | Telegram + push within 90 s | ~6 min |

Total cold start ≈ 3 minutes; step 8 accounts for the dwell time and is the reason the demo script budgets
6 minutes for it. A rehearsal must confirm the whole sequence twice.

## 7. Packaging and submission

```bash
# source archive (excludes build artefacts and secrets)
git archive --format=zip --prefix=smartreptile/ -o smartreptile-source-v1.0.0.zip HEAD
```

| Included | Excluded (verify before submitting!) |
|---|---|
| `firmware/`, `backend/`, `app/`, `web/`, `docs/` | `.env`, `android/key.properties`, `*.jks`, `fcm.json` |
| `docker-compose.yml`, `VERSION`, README | `bin/`, `obj/`, `.dart_tool/`, `build/`, `node_modules/` |
| Release artefacts: `app-release.apk`, AAB, report PDF | Sensor raw dumps over 50 MB |

Final checks before submission:
1. Extract the archive to a clean directory and follow `README.md` quick start on a machine that has never
   built the project (or a clean container): the app must build and the API must start.
2. Confirm no secret is in the archive: `git grep -I -E "(BEGIN PRIVATE KEY|TELEGRAM_BOT_TOKEN=.|SA_PASSWORD=.)"`.
3. Report PDF contains the release-build proof, test summaries, coverage, latencies, soak results, the
   traceability matrix and the contribution table.
4. Both `.zip` and `.pdf` submitted (the rubric requires both for the demo to be allowed).

## 8. Post-release (honest housekeeping)

| Item | Action |
|---|---|
| Tag the submission commit | `git tag -a v1.0.0 -m "PRM393 submission"` |
| Archive the evidence | Screenshots, logs, coverage, soak data into `report/evidence/` |
| Write down what broke during the demo | Appended to `05-release/03` §4 (bugs) even if cosmetic — next year's team will thank you |
| Freeze the node firmware | Note the flashed version in the report; do not reflash after the rehearsal |
