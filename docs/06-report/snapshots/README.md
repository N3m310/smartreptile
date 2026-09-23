# Prototype snapshots — M1 (captured 2026-09-22)

Evidence for the report, taken from the running stack rather than drawn: `docker compose up -d` on the
development machine, then headless Chromium 153 at a 1440 × 1200 viewport.

| File | What it shows |
|---|---|
| `01-health-ready.json` | `/health/ready` — `database` and `mqtt-broker` both report Healthy |
| `02-version.json` | `/version` — API version plus the schema name the database reports (`InitialSchema`) |
| `03-metrics.json` | `/metrics` — pipeline counters and `brokerRunning: true` |
| `04-dashboard-live.png` | Dashboard "Live" page |
| `05-dashboard-wallboard.png` | Dashboard wallboard view |
| `06-dashboard-health.png` | Dashboard "Health" page, showing the two JSON responses **fetched by a browser** |

## Read this before putting 04 or 05 in the report

The Live and Wallboard pages show their empty state behind an `http_404` banner. That is the honest M1 state, not
a broken snapshot: the API exposes operations endpoints only (`/health`, `/version`, `/metrics`, the SignalR hub),
so the dashboard's request for `/api/v1/terrariums` returns 404 and the page falls back to "no terrarium yet".
Those REST endpoints are M2 work (roadmap tasks 2.2 and 2.8).

`06-dashboard-health.png` is the snapshot that demonstrates a complete path today:
browser → nginx → API → SQL Server + MQTT broker.

Two things this exercise surfaced, both worth fixing rather than hiding:

1. The banner text called a 404 "cannot reach the server" (Vietnamese original above it). The server *did*
   answer; it simply has no such endpoint yet. **Fixed on 2026-09-23.** `web/js/api.js` now classifies every
   failed call in one function, `failureKind(error)`:

   | Kind | Trigger | Banner |
   |---|---|---|
   | `unreachable` | no answer at all (`network_unreachable`) | error: "Cannot reach the server. Showing the last known values." |
   | `missing-endpoint` | 404 **with no problem body** — the route is not built | warning: "This feature is not on the server yet — that part is not built. (http_404)" |
   | `not-found` | 404 **with** a problem code — the API understood and refused (ownership is hidden as 404, BR-02.2) | warning: "Not found, or you do not have access to it. (not_found)" |
   | `failed` | any other non-2xx (401, 500, …) | error: "The server answered but refused the request. (…)" |

   The `missing-endpoint` / `not-found` split exists because collapsing them would repeat the original mistake
   in the other direction once M2 ships: "someone else's terrarium" would be reported as "not built yet".
   `getReadiness()` distinguishes a missing `/health/ready` from an unhealthy database the same way, and the
   Health page prints `missing-endpoint: http_404` instead of calling every failure *unreachable*.
   Verified in a browser on 2026-09-23 against a stub that can serve a bare 404, a problem-coded 404 and no
   `/health/ready` at all, plus a dead port for the real unreachable case — four messages, four situations.
2. `health.html` never populated its `API:` label — only `js/pages/live.js` set it, and that file is not loaded by
   the health page, so the header showed a bare em dash. Fixed on 2026-09-22; this snapshot shows the fix.

## How these were produced

The dashboard is static HTML, so a headless browser needs the pages *and* the API reachable:

- the pages were served **unmodified from `web/`** over `http://localhost:8081` — nothing was rewritten for the
  screenshot, so what is shown is what the container serves;
- the API was proxied into the capture container (`socat TCP-LISTEN:8080 → host.docker.internal:8080`) instead of
  editing the `api-base` meta tag, which keeps the header label honest;
- `localhost:8081` is also one of the origins the API's CORS configuration already allows, so the browser made the
  same cross-origin calls it makes in normal use.

Reproduce with the stack up:

```bash
docker run --rm \
  -v "$PWD/docs/06-report/snapshots:/out" -v "$PWD/web:/web:ro" \
  --add-host host.docker.internal:host-gateway debian:bookworm-slim bash -c '
    apt-get update -qq && apt-get install -y -qq chromium python3 socat fonts-dejavu-core >/dev/null 2>&1
    socat TCP-LISTEN:8080,fork,reuseaddr TCP:host.docker.internal:8080 &
    python3 -m http.server 8081 --directory /web &
    sleep 3
    for p in index wallboard health; do
      chromium --headless --no-sandbox --disable-gpu --hide-scrollbars \
        --window-size=1440,1200 --virtual-time-budget=6000 \
        --screenshot="/out/dashboard-$p.png" "http://localhost:8081/$p.html"
    done'
```
