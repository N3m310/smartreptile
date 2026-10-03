# Implementation summary — 2026-10-03

**Branch** `feature/m2-device-auth-and-provisioning` · **Base** `main` @ `641c345` · **Scope** M2 (device
onboarding and the MQTT broker), the FR-01 prerequisite it needed, and the three M4 web follow-ups the
TERRAGUARD re-base created.

This file is the narrative record of one working session: what was built, how it was verified, which defects
were found on the way, and what is deliberately still open. The documents themselves remain the source of truth —
each section below points at the file that now carries the detail.

---

## 1. What was delivered

| Area | Task | State | Evidence |
|---|---|---|---|
| Web serving | **4.12** — nginx serves the prototype at `/` and the M1 pages under `/legacy/` | Done | `web/nginx.conf`, sibling mounts in `docker-compose.yml`, BUG-03 closed |
| Web honesty | **4.14** — the prototype is labelled as mock data and its palette is documented | Done | `web/src/components/MockDataNotice.tsx`, measured contrast ratios in `web/src/index.css` |
| Web direction | **4.13** — decided as **ADR-018**: the prototype stays a UI reference, `web/legacy/` remains the M4 surface | Done | `docs/07-appendices/01-adr-log.md` |
| Backend auth | **FR-01** — registration, sessions, refresh rotation, throttling, rate limiting | Done | `docs/03-implementation/07` §M2, 121 unit tests at the time |
| Backend onboarding | **2.2** — self-register, claim, credentials, rotate, revoke | Done | `docs/03-implementation/07` §M2, 50 live assertions |
| Backend broker | **2.3** — credential check, TLS/plaintext split, topic ACL, revoke-time session kick | Done | `docs/03-implementation/07` §M2, 30 live assertions |

### 1.1 FR-01 — user authentication (`/api/v1/auth`)

Task 2.2's `claim` endpoint is `Owner`-gated and nothing could be an `Owner`, so authentication came first.

| Piece | File |
|---|---|
| Registration, login, refresh, logout, profile, change-password | `backend/src/SmartReptile.Application/Identity/AuthService.cs` |
| HTTP surface and RFC 7807 mapping | `backend/src/SmartReptile.Api/Endpoints/AuthEndpoints.cs` |
| PBKDF2-HMAC-SHA256, 210 000 iterations, cost stored **per user** and upgraded on next login | `backend/src/SmartReptile.Infrastructure/Security/Pbkdf2PasswordHasher.cs` |
| HS256 access tokens (15 min, `sub`/`role`/`iat`/`exp`/`jti`/`ver`) | `backend/src/SmartReptile.Infrastructure/Security/JwtAccessTokenService.cs` |
| Password, identifier and login-throttle rules | `backend/src/SmartReptile.Domain/Identity/` |

Decisions worth remembering: `register` answers identically whether or not the identifiers were free (no
enumeration, and therefore no session — login is the only session issuer); login verifies a dummy hash for
unknown accounts so timing does not enumerate; a consumed refresh token revokes the whole family.

### 1.2 Task 2.2 — device provisioning

| Piece | File |
|---|---|
| Claim code: 31-symbol alphabet, 8 characters, 15-minute TTL, single use, normalised input | `backend/src/SmartReptile.Domain/Devices/ClaimCode.cs` |
| 15-minute code TTL, 10-minute rotation grace, "is this code usable" | `backend/src/SmartReptile.Domain/Devices/DeviceProvisioningRules.cs` |
| Anti-abuse limits, now read from `OnboardingProtection` configuration | `backend/src/SmartReptile.Domain/Devices/OnboardingThrottlePolicy.cs` |
| Self-register, claim, rotate, revoke, credential verification | `backend/src/SmartReptile.Application/Devices/DeviceProvisioningService.cs` |
| 256-bit secret, base32 (52 chars), `SHA-256(secret ‖ 16-byte salt)`, constant-time compare | `backend/src/SmartReptile.Infrastructure/Security/` |
| Routes and role policies (`Owner` for claim/rotate/revoke) | `backend/src/SmartReptile.Api/Endpoints/DeviceEndpoints.cs`, `.../Security/AuthorizationPolicies.cs` |

Design points: routes identify devices by **public id** (`sr-3f9a2c`), never the surrogate key; unknown, expired
and consumed claim codes are one answer (`404 claim_code_invalid`); a foreign terrarium is `404 not_found`, not
`403`, so ids cannot be probed (BR-02.2); a revoked device stops occupying its terrarium because
`IX_Device_TerrariumId` is filtered on `Status <> 3`, so a replacement can be claimed without a rebind.

### 1.3 Task 2.3 — the broker becomes the FR-05 broker

| Piece | File |
|---|---|
| Credential check on CONNECT, TLS and plaintext listeners, publish/subscribe ACL, session kick | `backend/src/SmartReptile.Infrastructure/Mqtt/MqttBrokerHostedService.cs` |
| Topic rules and the per-device ACL (pure, unit-tested) | `backend/src/SmartReptile.Domain/Devices/MqttTopicScheme.cs` |
| Session registry port and its in-process adapter | `backend/src/SmartReptile.Application/Abstractions/IDeviceSessionRegistry.cs`, `backend/src/SmartReptile.Infrastructure/Mqtt/DeviceSessionRegistry.cs` |

The broker now enforces: `username = deviceId` + `password = secret` against a usable `DeviceCredential` (the
rotation grace window included); TLS on `8883` for devices with plaintext on `1883` bound to loopback and
switchable off; publish only on own `telemetry`/`health`/`status`/`events`/`ack` and subscribe only to own `cmd`,
never with a wildcard; and a revoked device's live session is closed by the revoke call itself.

---

## 2. Verification

Everything below is a real run on this machine, not a plan.

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build SmartReptile.sln -c Release` | 0 warnings, 0 errors (`TreatWarningsAsErrors`) |
| Unit tests | `dotnet test SmartReptile.sln -c Release` | **226 passed**, 0 failed — 54 at M1, 121 after FR-01, 186 after 2.2, 226 after 2.3 |
| Formatting (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | exit 0 |
| FR-01 live | HTTP against a live API + SQL Server | register `202`, login `200`, `/me` `200`, refresh `200`, consumed-token reuse `401 token_reused`, wrong password `401`, no token `401`, 11th auth call in a minute `429` |
| 2.2 live | 50 assertions, `curl` against a live API + SQL Server | all green, including `429 rate_limited`, four unusable code shapes collapsing to `404`, foreign terrarium `404 not_found`, `409 terrarium_already_bound`, idempotent `204` revoke, `403 insufficient_role` for a Technician |
| 2.3 live | 30 assertions, `paho-mqtt` over TLS with a self-signed certificate, plus `netstat` | all green: three identical credential refusals; own `cmd` granted while a wildcard and a foreign `cmd` were refused; a foreign publish refused **and** that session dropped; revoke closed the live session in **0.0 s**; in the release shape nothing listened on `1883` |
| Deployed stack | `docker compose build api && docker compose up -d api`, then probe | container healthy, `/health/ready` reports `database` + `mqtt-broker` Healthy, bogus credentials refused on `1883`, new counters visible on `/metrics` |
| CI | pull request #1 → the five jobs of `.github/workflows/ci.yml` | **all five green** on run `37130567454` (`backend` 55 s, `integration` 1 m 9 s, `app` 1 m 7 s, `firmware` 1 m 28 s, `secret scan` 6 s). The secret scan failed first — see defect 9 — and the `integration` job passing against an empty database confirms that the two integration failures seen on this machine are the stale local volume, not the code |

Reproducing the live checks: start the API with `--no-launch-profile` (the launch profile would take port 8080,
which the container holds), point `ConnectionStrings__Default` and `Jwt__SigningKey` at the values in `.env`,
and set `Mqtt__PlaintextHost=127.0.0.1` with `Mqtt__Port`/`Mqtt__TlsPort` away from the container's ports.

---

## 3. Defects found while running it

Each of these was caught by executing the thing rather than reading it.

| # | Defect | Fix |
|---|---|---|
| 1 | The claim-code spec said "32 symbols" while listing the 31 that survive removing `0 O 1 I L` from the 36 alphanumerics — a normative string with a wrong count next to it | Counts and entropy figures corrected to 31 symbols and ≈ 8.5 × 10¹¹ in `docs/07-appendices/03` §2.1, `docs/02-design/02` and `docs/02-design/06` |
| 2 | `OnboardingProtection` configuration was bound at start-up and then ignored: the policy used hard-coded constants | Limits became an `OnboardingLimits` record that the composition root fills from configuration |
| 3 | Binding only `127.0.0.1` for plaintext MQTT still left MQTTnet's IPv6 socket on `[::]`, so "loopback only" was reachable from the LAN over IPv6 | The IPv6 counterpart is bound explicitly (`::1` for a loopback address), proven with `netstat` |
| 4 | `appsettings.Development.json` set `Mqtt:RequireClientAuthentication=false`, so a local run accepted any password — the exact hole 2.3 closes | Stays `true`, with the reason written next to it |
| 5 | `docker-compose.yml` published MQTT plaintext on `0.0.0.0:1883`, contradicting BR-05.1's "loopback only" | Published as `127.0.0.1:1883:1883`; `Mqtt:PlaintextHost` documented in `.env.example` |
| 6 | The M1 broker's TLS switch reused the plaintext endpoint, so enabling TLS would not have produced the two-listener shape the design asks for | Separate encrypted endpoint with its own port and bind address |
| 7 | `docs/03-implementation/01` §5 listed MQTT environment keys (`MQTT_BROKER_PORT_TLS`, `MQTT_TLS_CERT_PATH`/`_KEY_PATH`) that do not exist in `.env.example`, and implied a PEM pair where the broker loads a PFX | Table rewritten to the real keys |
| 8 | The readiness/degraded path had no way to report *why* TLS was missing | The broker records the reason in `LastError` and keeps serving on plaintext instead of failing to start |
| 9 | CI's `gitleaks` job failed on this branch with **eight findings**, all of them invented credentials — two password literals in `AuthServiceTests`, one in `Pbkdf2PasswordHasherTests`, three inline wrong-password literals, and the two `curl` examples in the README | The fixtures changed rather than the scan: a low-entropy `local-demo-N` value that is obviously local, the inline literals collapsed into the existing constant, and the README examples carry the same synthetic value with a note saying why. Allow-listing test or documentation paths would have weakened the one gate that keeps a real key out of the repository. Because that gate walks the **whole history**, a follow-up commit was not enough — the branch was squashed into one commit and force-pushed, then verified by scanning a fresh clone of the pushed branch: "27 commits scanned … no leaks found" |

---

## 4. Deliberately still open

| Item | Why it is open | Where it is recorded |
|---|---|---|
| **No `AuditLog` rows** for claim, rotate, revoke or rejected connections | `BR-18.4` requires them and §3.18 specifies the table, but `InitialSchema` never created it — a schema gap, not a code gap | `docs/03-implementation/07` §M2 (2.2 and 2.3) |
| **Nothing consumes `sr/v1/d/+/telemetry`** | `IngestWorker` and `IngestPipeline` are task 2.4; 2.3 stopped at the transport boundary on purpose | `docs/03-implementation/07` §M2 (2.3) |
| **A lost race on `terrarium_already_bound` returns `500`** | The filtered unique index is the backstop, but `DbUpdateException` is not translated yet (the design in `docs/03-implementation/03` §1 requires it) | `docs/03-implementation/07` §M2 (2.2) |
| **The per-address self-register limit is shared behind one NAT** | That is what `docs/07-appendices/03` §2.2 specifies; worth knowing before demoing two boards | `docs/03-implementation/07` §M2 (2.2) |
| **No hardware exists**, so nothing has been flashed or measured | Tasks 1.5, 1.6, 2.5–2.7 and the 2.10 gate need a board | `docs/03-implementation/07` §M1/M2 |
| **Stale local database volume** (created 2026-09-21) fails 2 integration tests with the old `your-org` seed | Documented local-only drift; CI starts from an empty database | repository `README`, `docs/05-release/03` |

---

## 5. Documentation changes

No document was added or removed: the set is still **36 files** (35 documents + this index), and this summary is
an extra implementation record rather than a new design document.

| Document | Change |
|---|---|
| `docs/README.md` | Five dated revision notes for this work (re-base, follow-ups closed, FR-01, 2.2, 2.3) |
| `docs/03-implementation/07-implementation-roadmap.md` | M2 "progress so far" extended with what each of FR-01, 2.2 and 2.3 delivers, the verification evidence, and the open items above |
| `docs/05-release/03` §5 | One documentation-log row per delivery, with the reason each was built when it was |
| `docs/05-release/01` §6 | New runbook step 3a: prove the broker is TLS-only and `1883` is shut |
| `docs/07-appendices/03` §2.1–§2.3, §3.1 | Alphabet count corrected, endpoint error lists completed, ACL enforcement behaviour written down |
| `docs/02-design/02`, `docs/02-design/06` | Alphabet correction in both |
| `docs/03-implementation/01` §5 | Environment-key table aligned with `.env.example` |
| Repository `README.md` | Verification table gained the provisioning, broker-auth, ACL and plaintext rows; test counts updated; the "broker accepts any password" limitation replaced by what is actually missing (the consumer) |
| `docs/04-quality/*`, `docs/06-report/*`, `docs/02-design/*`, `docs/03-implementation/08`, `docs/07-appendices/01` | The TERRAGUARD re-base and `ADR-017`/`ADR-018` reconciliation carried in this branch |

---

## 6. What comes next

In roadmap order: **2.4** (`IngestWorker` + `IngestPipeline` stages + counters, idempotent on
`(DeviceId, Sequence)`), then **2.8** (readings and terrarium CRUD — a device currently needs a terrarium that
only SQL can create), then **2.9** (SignalR broadcast). The `AuditLog` slice sits in front of 2.3's "audited"
acceptance line and should be taken with it. M3 (3.1–3.7) and the M4 app/web tasks follow; 2.5–2.7 and the 2.10
gate stay blocked on hardware.
