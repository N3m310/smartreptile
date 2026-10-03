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
- Seeding runs once after migration (`ReferenceDataSeeder`), idempotent **by presence, not by content**: it creates
  what is missing and never rewrites a row that already exists, so changed seed data needs a volume reset (§3.1).
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

### 3.1 Changed seed data needs a volume reset, not an update

Because seeding is idempotent by presence rather than by content, editing the seeded profiles or bands does
**not** reach a database that was seeded before the change — not even with a rebuilt image. The stamp, the
source reference and the numbers stay as they were on first seed. This is not hypothetical: the reference
provenance was fixed in the seeder on 2026-09-23, and the running stack went on serving
`PENDING VERIFICATION` with a placeholder source link because its rows already existed.

The supported way to get new reference data is to drop the volume:

```bash
docker compose down -v          # -v deletes the named volume: readings, alerts and audit rows on this host go
                               # with it. Acceptable here because the host holds seeded demo data.
docker compose up -d --build
```

Then prove it landed rather than assuming it did:

```bash
docker compose exec db /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -d SmartReptile -h -1 -W \
  -Q "SELECT COUNT(*) AS bands, SUM(CASE WHEN SourceRef LIKE '%PENDING VERIFICATION%' THEN 1 ELSE 0 END) AS pending FROM dbo.Threshold"
```

`pending` must equal `ReferenceDataSeeder.BandsAwaitingVerification` (14 of 17 bands on 2026-09-23, and it is
meant to fall as checklist rows are signed — compare against the constant, do not copy the number). The same
comparison runs in CI, which is why drift is a local-only surprise: CI always starts from an empty database.

There is **no in-place upgrade path** for seeded content today. On a host whose data is worth keeping, the
change has to be a data migration — an `UPDATE` with a stated reason and an audit entry — rather than a volume
reset. That path does not exist yet, and the demo host does not need it.

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

## 5. Web release (`web/`)

Two surfaces live in this folder and only one of them is a deliverable (`ADR-017`, `ADR-018`).

**The static dashboard (`web/legacy/`) — the one that ships, and the M4 web surface.**

- No build step. Files are served by nginx from `web/legacy/` under `/legacy/` inside the compose stack
  (`web/nginx.conf`, task 4.12).
- `js/api.js` takes the API base URL from a `<meta name="api-base">` tag injected at container start, so the
  same static files work on `localhost` and on the demo host.
- Cache-busting by query string (`app.css?v=1.0.0`) so a stale browser cache cannot show old UI during the
  demo — a small thing that has ruined demos before.
- **How it is served.** Compose mounts the prototype build at `/srv/prototype` and these files at `/srv/legacy`;
  nginx sends `/` to the prototype and `/legacy/` to the dashboard, and only the prototype gets a SPA fallback, so
  a missing dashboard page stays a `404` instead of silently rendering the wrong surface. The two mounts are
  **siblings, not nested** — a nested bind mount needs its mountpoint created inside the read-only parent mount
  and fails with `mkdirat ... read-only file system` (defect 13 in the repository `README`).
- **BUG-03 regression check** (`05-release/03` §4) — run whenever the `web` service or its mounts change:

  ```bash
  docker compose up -d web
  curl -sI http://127.0.0.1:8081/ | head -1                       # HTTP/1.1 200 OK
  curl -sI http://127.0.0.1:8081/legacy/wallboard.html | head -1  # HTTP/1.1 200 OK
  curl -sI http://127.0.0.1:8081/legacy/health.html | head -1     # HTTP/1.1 200 OK
  curl -s  http://127.0.0.1:8081/ | grep -o 'assets/index-[^"]*\.js'   # the hashed bundle, never /src/main.tsx
  ```

  The last line is the one that would have caught BUG-03: the old mount answered `200` on `/` while serving the
  prototype's Vite dev entry, so a status code alone was never enough.

**The prototype (`web/`, TERRAGUARD) — not a deliverable, and not a measure of anything.**

- `npm run build` writes `web/dist/`, which is **committed on purpose** so nginx can serve the prototype without a
  Node toolchain. Rebuild it whenever `src/` changes: a stale `dist/` is invisible until someone demos from it,
  because the entry page then points at asset names that no longer exist.
- Rebuilt on 2026-10-03 after the mock-data notice landed — `tsc && vite build` on a clean `npm ci` produced
  `index-hjjUIssv.js` (766 kB / 214 kB gzip) and `index-DAQDTjmL.css` (40 kB), replacing the
  `index-C7gXR99i.js` / `index-CUm0g7Lt.css` pair.
- It renders invented values and says so in the UI. It is never the source of a number in the report, and
  `ADR-018` settles its fate: it stays a reference while `web/legacy/` carries the M4 DoD.

## 6. Release runbook (the order that actually works)

| Step | Command / action | Expected | Time |
|---|---|---|---|
| 1 | `docker compose up -d db` | SQL Server healthy | ~40 s |
| 2 | `docker compose up -d api` (migrations + reference seed run on start-up; set `STARTUP_APPLY_MIGRATIONS=false` and use the §3 `dotnet ef` command for an explicit release step) | logs show `Applying migration '…_InitialSchema'. Done.` | ~30 s |
| 3 | `docker compose up -d web` | `/health/ready` → `{"status":"Healthy","checks":[database Healthy, mqtt-broker Healthy]}`; dashboard on `:8081` | ~5 s |
| 3a | Confirm the broker is TLS-only: `MQTT_DISABLE_PLAINTEXT=true` in `.env`, then `netstat -ano \| findstr :1883` → **nothing listening**, and `curl`/`paho` over TLS on `:8883` still authenticates a claimed device | BR-05.1: plaintext gone, TLS the only way in. The dev default keeps `1883` reachable **on the host's loopback only** (`127.0.0.1:1883:1883`), never on the LAN | — |
| 4 | Verify reference data | `SELECT COUNT(*) FROM SpeciesProfile` → 3 profiles, 17 bands in `Threshold`, and the count of `SourceRef LIKE '%PENDING VERIFICATION%'` equal to `ReferenceDataSeeder.BandsAwaitingVerification` (14 on 2026-09-23) | — |
| 5 | Power the node | OLED shows values; status `online` in the fleet view within 90 s | — |
| 6 | Install/open the release APK | Logged in, live cards populated | — |
| 7 | Open the wallboard on the demo display | Values update silently | — |
| 8 | Send a test alert (induce a 5-min excursion) | Telegram + push within 90 s | ~6 min |

Total cold start ≈ 3 minutes; step 8 accounts for the dwell time and is the reason the demo script budgets
6 minutes for it. A rehearsal must confirm the whole sequence twice.

**If the reference data changed since the volume was created, step 1 must be preceded by
`docker compose down -v`** — the sequence above is a cold start, not an upgrade. Rebuilding the image is not
enough, because the seeder never rewrites existing rows (§3.1).

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
