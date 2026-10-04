# Implementation summary — 2026-10-03 and 2026-10-04

**Branch** `feature/m2-device-auth-and-provisioning` · **Base** `main` @ `641c345` · **Scope** M2 (device
onboarding, the MQTT broker and the ingest pipeline), the FR-01 prerequisite it needed, and the three M4 web
follow-ups the TERRAGUARD re-base created.

This file is the narrative record of the branch's two working sessions — **2026-10-03** and **2026-10-04** — kept
in one place because the second continues the first: what was built, how it was verified, which defects were found
on the way, and what is deliberately still open. The date in the file name is the day the record started. The
documents themselves remain the source of truth — each section below points at the file that now carries the
detail.

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
| Backend ingest | **2.4** — a published batch becomes a stored sample, its readings and a device state update | Done | `docs/03-implementation/07` §M2, 325 unit tests, 15 integration tests, live MQTT run |

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

### 1.4 Task 2.4 — the ingest pipeline (2026-10-04)

M2's goal is "a real sample walks from the terrarium into SQL Server", and 2.3 deliberately stopped at the
transport boundary: after it, a batch that passed the ACL was counted and dropped. This closes that. Each stage is
a separate type, so each rule has one implementation and one test, and the pure ones have no database in front of
them at all.

| Stage | File | Verified by |
|---|---|---|
| Bytes → document | `Infrastructure/Ingest/JsonTelemetryPayloadParser.cs` | adapter tests: tolerance, unknown keys kept for the validator to judge, the 32 KB limit |
| Schema, shape, timing (V-01…V-03, V-07…V-09, V-10) | `Application/Ingest/TelemetryPayloadValidator.cs`, `Domain/Readings/TelemetryIngestRules.cs` | `TC-U-01…04` plus the timing rules |
| Device: match, lifecycle, credential (V-04/V-05) | `Application/Ingest/DeviceAuthenticator.cs` | `TC-U-05` |
| Plausibility, never a refusal (V-06) | `Application/Ingest/PlausibilityGuard.cs` | `TC-U-06…08` |
| Calibration on `Value`, never on `RawValue` (BR-07.4) | `Application/Ingest/{DeviceCalibration,CalibrationApplier}.cs` | `TC-U-09` |
| Dedupe + stage the rows (DI-02/DI-03) | `Application/Ingest/TelemetryWriter.cs` + `Infrastructure/Ingest/EfTelemetryStore.cs` | `TC-I-01…03` |
| Device state: `LastSeenAt`, status, firmware, health denorms | `Application/Ingest/DeviceStateUpdater.cs` | `TC-I-01`, and `TC-I-04`'s health half |
| Transport, back-pressure, counters | `Infrastructure/Ingest/{InProcessTelemetryBus,IngestWorker,IngestOutcomeRecorder}.cs`, broker `OnInterceptingPublishAsync` | the live run in §2 |
| Fan-out after the commit | `Infrastructure/Ingest/PendingFanOut.cs` (**placeholder**) | the recorder tests assert a failed push cannot fail a stored batch |

Two decisions worth reading back, both recorded in `docs/03-implementation/03` §3 as "as built":

- **The transport hop carries raw envelopes, not batches.** The design's sketch deserialises in the reader and
  queues the batch; a payload that is not JSON has to be *counted* under `schema_invalid`, and the writer loop is
  the one place that can count anything — so the queue holds bytes and the parse is the writer's first stage.
- **Back-pressure is the write itself.** The broker is in-process (ADR-012), so MQTTnet sends the QoS 1 PUBACK only
  after `InterceptingPublishAsync` returns; awaiting the bounded channel write *is* the "stop acking so the broker
  holds the messages" rule of §02-design/03 §1. The wait is unbounded on purpose — a timeout would have to choose
  between losing the batch and failing the broker, and "the device retries" is already the right answer.

The fan-out boundary is wired but its two consumers are placeholders: `PendingTelemetryBroadcaster` (2.9) and
`PendingEvaluationQueue` (3.2/3.3). Nothing is broadcast and nothing is evaluated yet, which is *why* `TC-I-03`'s
"zero alerts created" holds today — trivially, not because the evaluator skipped the flagged row. The flag is on
the row, which is what the evaluator will read.


---

## 2. Verification

Everything below is a real run on this machine, not a plan.

| Gate | Command | Result |
|---|---|---|
| Build | `dotnet build SmartReptile.sln -c Release` | 0 warnings, 0 errors (`TreatWarningsAsErrors`) |
| Unit tests | `dotnet test SmartReptile.sln -c Release` | **325 passed**, 0 failed — 54 at M1, 121 after FR-01, 186 after 2.2, 226 after 2.3, **325 after 2.4** |
| Formatting (CI parity) | `dotnet format SmartReptile.sln --verify-no-changes --severity error` | exit 0 — and the same gate against `tests/SmartReptile.Tests.Integration`, which is outside the solution and would otherwise be silently excluded |
| FR-01 live | HTTP against a live API + SQL Server | register `202`, login `200`, `/me` `200`, refresh `200`, consumed-token reuse `401 token_reused`, wrong password `401`, no token `401`, 11th auth call in a minute `429` |
| 2.2 live | 50 assertions, `curl` against a live API + SQL Server | all green, including `429 rate_limited`, four unusable code shapes collapsing to `404`, foreign terrarium `404 not_found`, `409 terrarium_already_bound`, idempotent `204` revoke, `403 insufficient_role` for a Technician |
| 2.3 live | 30 assertions, `paho-mqtt` over TLS with a self-signed certificate, plus `netstat` | all green: three identical credential refusals; own `cmd` granted while a wildcard and a foreign `cmd` were refused; a foreign publish refused **and** that session dropped; revoke closed the live session in **0.0 s**; in the release shape nothing listened on `1883` |
| 2.4 live | `paho-mqtt` publish over a real MQTT listener against a live API + SQL Server, then `curl /metrics` and read the rows back | all green: a two-sample batch published twice stored **2** `TelemetrySample` rows with contiguous sequences `1, 2` and **8** `MetricReading` rows with `Value = 28.750` / `RawValue = 28.900` intact; the device's `LastSeenAt`, `FirmwareVersion = 1.2.0`, `SignalStrengthDbm = -63` and `FreeHeapKb = 142` came from the batch's health block, plus a `DeviceHealthSample` row; `/metrics` read `ingest_samples_total=2`, `ingest_duplicates_total=2`, `ingest_rejected_total=0`. A `tf = 85` sample was **stored** with `QualityFlags = 2`; a 121-sample batch and a malformed body were refused as `payload_too_large` and `schema_invalid`, taking `ingest_rejected_total` to **2** |
| Integration tests | `dotnet test tests/SmartReptile.Tests.Integration` against a real SQL Server | **15 passed**, including `TC-I-01…03`. Run against a database created for the purpose, because the local volume is the stale one below |
| Deployed stack | `docker compose build api && docker compose up -d api`, then probe | container healthy, `/health/ready` reports `database` + `mqtt-broker` Healthy, bogus credentials refused on `1883`, new counters visible on `/metrics` |
| CI | pull request #1 → the five jobs of `.github/workflows/ci.yml` | **all five green** on run `37130567454` (`backend` 55 s, `integration` 1 m 9 s, `app` 1 m 7 s, `firmware` 1 m 28 s, `secret scan` 6 s). The secret scan failed first — see defect 9 — and the `integration` job passing against an empty database confirms that the two integration failures seen on this machine are the stale local volume, not the code |

Reproducing the live checks: start the API with `--no-launch-profile` (the launch profile would take port 8080,
which the container holds), point `ConnectionStrings__Default` and `Jwt__SigningKey` at the values in `.env`,
and set `Mqtt__PlaintextHost=127.0.0.1` with `Mqtt__Port`/`Mqtt__TlsPort` away from the container's ports.

For the 2.4 run specifically, `Mqtt__RequireClientAuthentication=false` was used so the broker takes the MQTT
username as the device id, and the device and terrarium rows were inserted with `sqlcmd`: obtaining a real
credential means going through claim, and claim needs a terrarium that only SQL can create until 2.8. The
credential path itself is covered by `TC-U-05` and 2.2's live run, so what this run was there to prove is the
broker → bus → worker → EF → counter chain, which the switch does not touch.

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
| 10 | The test fixture's "unbound device" helper bound one: `terrariumId ?? Guid.NewGuid()` turned an explicit `null` into a fresh terrarium, so the test for the unbound path exercised the *bound* one and passed without a rule behind it | The helper takes `bound:` and leaves `TerrariumId` null when it is false, so "pass a null id" can no longer mean "make me one" |
| 11 | The ingest timing rules labelled **every** honest ring-buffer back-fill as `ClockUnsynced` and stored the *clamped* skew, so a device whose clock was three hours fast was recorded with `ClockSkewSeconds = 0` — which reads as "no skew at all" and hides the fault the column exists to surface | Back-fill is checked before clock skew (a sample that sat in the buffer has a perfectly good clock), and `ClockSkewSeconds` follows the column's own definition, `ReceivedAt − RecordedAt`, **before** any clamping; rule V-07's clamp still applies to `RecordedAt` |
| 12 | Three test cases asserted things the requirement does not say, and could only be "passed" by writing code that contradicted FR-18 or the schema | Corrected rather than quietly satisfied: `TC-I-01`'s `ingest_samples_total` unit (FR-18's "1 000 samples → +1 000" fixes it as `TelemetrySample` rows, not readings and not batches), `TC-U-05`'s constant-time assertion (a wall-clock spread test measures the runner, so the property is asserted where it is implemented, in `DeviceCredentials.VerifySecret`), and `TC-I-04`, whose `sensor_fault` half needs 3.3 and a table that `InitialSchema` never created |

---

## 4. Deliberately still open

| Item | Why it is open | Where it is recorded |
|---|---|---|
| **No `AuditLog` rows** for claim, rotate, revoke or rejected connections | `BR-18.4` requires them and §3.18 specifies the table, but `InitialSchema` never created it — a schema gap, not a code gap | `docs/03-implementation/07` §M2 (2.2 and 2.3) |
| **The ingest fan-out has no consumer** | `PendingTelemetryBroadcaster` is 2.9 and `PendingEvaluationQueue` is 3.2/3.3; both are wired into the worker and deliberately empty, so a stored sample is pushed to nobody and evaluated by nobody. This is also why `TC-I-03`'s "zero alerts" holds trivially | `docs/03-implementation/07` §M2 (2.4) |
| **`TC-I-04`'s `sensor_fault` half** | A `sensor_fault` arrives on `sr/v1/d/{id}/events`, and "humidity is `Unavailable`" needs the `SensorFault` derived signal of 3.3 — which has no table to record the event in today, the same class of gap as the missing `AuditLog`. The health half is green | `docs/03-implementation/07` §M2 (2.4), `docs/04-quality/02` |
| **`health`, `status` and `events` are not forwarded to ingest** | They are part of the topic scheme but have no consumer yet. Left unforwarded on purpose: accepting a status payload and then ignoring it would look like it worked | `docs/03-implementation/07` §M2 (2.4) |
| **No HTTPS fallback endpoint** (`POST /api/v1/ingest/http`) | The parser, the presented-secret path of `DeviceAuthenticator` and the `IngestSource` column are in place for it; the endpoint belongs with 2.6's firmware work, which is what needs it | `docs/03-implementation/07` §M2 (2.4) |
| **A lost race on `terrarium_already_bound` returns `500`** | The filtered unique index is the backstop, but `DbUpdateException` is not translated yet (the design in `docs/03-implementation/03` §1 requires it). The ingest path *does* translate its own version of this — a lost dedupe race is re-read and reported as a duplicate | `docs/03-implementation/07` §M2 (2.2) |
| **The per-address self-register limit is shared behind one NAT** | That is what `docs/07-appendices/03` §2.2 specifies; worth knowing before demoing two boards | `docs/03-implementation/07` §M2 (2.2) |
| **No hardware exists**, so nothing has been flashed or measured | Tasks 1.5, 1.6, 2.5–2.7 and the 2.10 gate need a board | `docs/03-implementation/07` §M1/M2 |
| **Stale local database volume** (created 2026-09-21) fails 2 integration tests with the old `your-org` seed | Documented local-only drift; CI starts from an empty database. Re-confirmed on 2026-10-04 by running the same suite green (15/15) against a database created for the purpose | repository `README`, `docs/05-release/03` |

---

## 5. Documentation changes

No document was added or removed in either session: the set is still **36 files** (35 documents + this index), and
this summary is an extra implementation record rather than a new design document.

| Document | Change |
|---|---|
| `docs/README.md` | **Six** dated revision notes across the two sessions — re-base, follow-ups closed, FR-01, 2.2, 2.3, and (2026-10-04) 2.4 |
| `docs/03-implementation/07-implementation-roadmap.md` | M2 "progress so far" extended with what each of FR-01, 2.2, 2.3 and 2.4 delivers, the verification evidence, and the open items above. The 2.4 acceptance line was re-scoped to `TC-I-01/02/03` plus `TC-I-04`'s health half, and the stale "2.3 is partly in place" sentence corrected |
| `docs/03-implementation/03` §3 | "As built" note recording the two places the code and the design sketch differ: the transport hop carries envelopes rather than deserialised batches, and fan-out is the worker's, after the outcome is returned |
| `docs/04-quality/02` | Three test cases corrected rather than quietly satisfied — `TC-I-01`'s counter unit, `TC-U-05`'s constant-time assertion, `TC-I-04` split against 3.3 |
| `docs/01-product/05` | The calibration quality bit was written as 16; `QualityFlags.CalibrationApplied` is 32 (the schema reference already said so) |
| `docs/05-release/03` §5 | One documentation-log row per delivery, with the reason each was built when it was — including 2.4, whose row records the three test-case corrections |
| `docs/05-release/01` §6 | New runbook step 3a: prove the broker is TLS-only and `1883` is shut |
| `docs/07-appendices/03` §2.1–§2.3, §3.1 | Alphabet count corrected, endpoint error lists completed, ACL enforcement behaviour written down |
| `docs/02-design/02`, `docs/02-design/06` | Alphabet correction in both |
| `docs/03-implementation/01` §5 | Environment-key table aligned with `.env.example` |
| Repository `README.md` | Verification table gained the provisioning, broker-auth, ACL and plaintext rows, and on 2026-10-04 the ingest row; the integration row now reads 15; test counts updated; the "nothing consumes the telemetry" limitation replaced by what is actually missing (the two fan-out consumers) |
| `docs/04-quality/*`, `docs/06-report/*`, `docs/02-design/*`, `docs/03-implementation/08`, `docs/07-appendices/01` | The TERRAGUARD re-base and `ADR-017`/`ADR-018` reconciliation carried in this branch |

---

## 6. What comes next

In roadmap order: **2.8** (readings and terrarium CRUD — a device needs a terrarium that only SQL can create
today, which is also why the 2.4 live check inserted its rows with `sqlcmd` instead of going through claim), then
**2.9** (SignalR broadcast, replacing `PendingTelemetryBroadcaster` and making the live view real). The
`AuditLog` slice sits in front of 2.3's "audited" acceptance line and should be taken with it; the device-event
storage that `TC-I-04`'s `sensor_fault` half needs belongs with **3.3**'s derived signals rather than with
ingest, since that is where the signal itself lives. M3 (3.1–3.7) and the M4 app/web tasks follow; 2.5–2.7 and
the 2.10 gate stay blocked on hardware — and with the ingest path complete and verified, the firmware half of M2
is now the only thing between the repository and its first real end-to-end run.
