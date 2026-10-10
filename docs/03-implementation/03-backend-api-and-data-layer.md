# 03 — Backend API and Data Layer

Stack: ASP.NET Core 10 · EF Core 10 · SQL Server 2022 · MQTTnet · SignalR. This document is the
implementation counterpart of `02-design/03` (ingest + engine) and `07-appendices/02` (schema).

## 1. `DbContext` and entity configuration

```csharp
public sealed class SmartReptileDbContext(DbContextOptions<SmartReptileDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Terrarium> Terrariums => Set<Terrarium>();
    public DbSet<SpeciesProfile> SpeciesProfiles => Set<SpeciesProfile>();
    public DbSet<Threshold> Thresholds => Set<Threshold>();
    public DbSet<ThresholdOverride> ThresholdOverrides => Set<ThresholdOverride>();
    public DbSet<ThresholdSnapshot> ThresholdSnapshots => Set<ThresholdSnapshot>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<Metric> Metrics => Set<Metric>();
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();
    public DbSet<MetricReading> MetricReadings => Set<MetricReading>();
    public DbSet<DeviceHealthSample> DeviceHealthSamples => Set<DeviceHealthSample>();
    public DbSet<TelemetryHourlyRollup> HourlyRollups => Set<TelemetryHourlyRollup>();
    public DbSet<DailyEnvironmentalSummary> DailySummaries => Set<DailyEnvironmentalSummary>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<MetricSilence> Silences => Set<MetricSilence>();
    public DbSet<NotificationLog> Notifications => Set<NotificationLog>();
    public DbSet<EvaluationState> EvaluationStates => Set<EvaluationState>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
}
```

Configurations that carry real meaning (not just column mapping):

```csharp
// DI-02: the idempotency guarantee for ingest.
builder.Entity<TelemetrySample>()
       .HasIndex(s => new { s.DeviceId, s.Sequence }).IsUnique();

// BR-09.1 / FR-09: range + latest queries.
builder.Entity<TelemetrySample>()
       .HasIndex(s => new { s.TerrariumId, s.RecordedAt })
       .IsDescending(false, true)
       .IncludeProperties(s => s.Id);

// DI-01: at most one OPEN alert per dedupe key, enforced by the database, not by code.
builder.Entity<Alert>()
       .HasIndex(a => a.DedupeKey)
       .IsUnique()
       .HasFilter("[State] <> 2");

// DI-04: a terrarium holds at most one active device.
builder.Entity<Device>()
       .HasIndex(d => d.TerrariumId)
       .IsUnique()
       .HasFilter("[TerrariumId] IS NOT NULL AND [Status] <> 3");

// DI-05: band ordering is a database rule as well as a domain rule.
builder.Entity<Threshold>().ToTable(t =>
{
    t.HasCheckConstraint("CK_Threshold_TargetOrder", "[TargetMin] < [TargetMax]");
    t.HasCheckConstraint("CK_Threshold_CriticalOrder",
        "[CriticalMin] IS NULL OR ([CriticalMin] <= [TargetMin] AND [CriticalMax] >= [TargetMax])");
});

// UC-03 A3: optimistic concurrency on authored configuration.
builder.Entity<SpeciesProfile>().Property(p => p.RowVersion).IsRowVersion();
```

**Why filtered unique indexes matter here.** DI-01 and DI-04 protect invariants that a background worker
and a REST endpoint could otherwise violate concurrently (evaluator raising an alert while an ack
arrives; two claims racing for one terrarium). Putting them in the database means the invariant holds even
if a future code path forgets the rule. The API catches `DbUpdateException` on these indexes and maps it
to the correct problem code (`alert_duplicate`, `terrarium_already_bound`) rather than returning a 500.

## 2. Migrations and seeding

```bash
dotnet ef migrations add InitialSchema   --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
dotnet ef database update                --project src/SmartReptile.Infrastructure --startup-project src/SmartReptile.Api
```

Seeding is done in code, idempotently, and runs after migration:

```csharp
public sealed class ReferenceDataSeeder(SmartReptileDbContext db)
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await SeedMetricsAsync(ct);          // keyed by Code  → see 02-design/02 §3.8
        await SeedSpeciesProfilesAsync(ct);  // keyed by Name  → see 07-appendices/05
        // Built-in profiles arrive with SourceRef on every threshold row (brief §6, FR-10).
    }
}
```

Migration policy: auto-apply in `Development` only; in the release runbook it is an explicit step
(`05-release/01` §3) so a failed migration cannot take the demo host down silently. Destructive
migrations require a comment stating the historical consequence (migration rule 4 in `02-design/02` §7).

## 3. Ingest pipeline implementation

```csharp
public sealed class IngestWorker(
    IServiceScopeFactory scopes,
    IMqttSubscriber subscriber,
    Channel<TelemetryBatch> channel,
    ILogger<IngestWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await subscriber.SubscribeAsync("sr/v1/d/+/telemetry", qos: 1, ct);

        var reader = Task.Run(async () =>
        {
            await foreach (var msg in subscriber.ReadAllAsync(ct))
            {
                // Bounded channel: when full, we stop acking so the broker holds messages
                // instead of us dropping them (NFR-03).
                await channel.Writer.WriteAsync(Deserialize(msg.Payload), ct);
                await subscriber.AckAsync(msg, ct);
            }
        }, ct);

        var writer = Task.Run(async () =>
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(ct))
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider
                    .GetRequiredService<IngestPipeline>()
                    .ProcessAsync(batch, ct);
                IngestMetrics.Record(result); // counters exposed on /metrics (FR-18)
            }
        }, ct);

        await Task.WhenAll(reader, writer);
    }
}
```

`IngestPipeline.ProcessAsync` implements the stages from `02-design/03` §1 in order, and each stage is a
separate class so it can be unit-tested without a broker:

| Stage | Class | Tested by |
|---|---|---|
| Schema/shape | `TelemetryPayloadValidator` | `TC-U-01…04` |
| Credential check | `DeviceAuthenticator` | `TC-U-05`, `TC-I-05` |
| Plausibility | `PlausibilityGuard` | `TC-U-06…08` |
| Calibration | `CalibrationApplier` | `TC-U-09` |
| Dedupe + persist | `TelemetryWriter` (single transaction) | `TC-I-01…03` |
| Status/health update | `DeviceStateUpdater` | `TC-I-04` |
| Fan-out | `TelemetryBroadcaster` (SignalR) + `EvaluationQueue` | `TC-I-11` |

Fan-out happens **after** the commit; a SignalR failure must never roll back a stored sample.

**As built (2026-10-04, task 2.4).** Two of the details above differ from this sketch, and the differences are
deliberate:

- **The channel carries raw envelopes, not batches.** An unparseable payload has to be counted under
  `schema_invalid`, and the writer loop is the only place that can count; deserialising in the reader would either
  lose that diagnostic or duplicate the counting. So `IngestWorker`'s reader enqueues the topic's device id, the
  bytes and the arrival time, and the parse is the writer's first stage.
- **Fan-out is done by the worker after `IngestPipeline` returns, not inside it.** `IngestOutcome.Stored` carries
  the committed samples, so the push cannot be mistaken for part of the transaction it follows: a failed broadcast
  is logged as a lost push by `IngestOutcomeRecorder`, never as a refused batch.

**As built (2026-10-09) — the other three channels, and the evaluator behind the queue.** The stage table above is
the telemetry path. A publication on `health`, `status` or `events` takes the same hop but a different pipeline,
because the three have no samples to judge and no batch to validate:

| Channel | Pipeline | Writes |
|---|---|---|
| `telemetry` | `IngestPipeline` | `TelemetrySample` + `MetricReading` + device state |
| `health` | `DeviceChannelPipeline` | device denorms + `DeviceHealthSample` |
| `status` | `DeviceChannelPipeline` | `Device.Status`, and the `statusChanged` push |
| `events` | `DeviceChannelPipeline` | `DeviceEvent` |

`DeviceChannelPipeline` shares the gate the sample path uses — the payload must name the device the transport
authenticated (rule V-04), and the device must exist, be unrevoked and be bound — so a device cannot reach the
database on one topic that it could not reach on another. `JsonDeviceChannelParser` is the tolerant half and
`Application/Ingest/DeviceChannelContracts.cs` the rules, split the same way as the telemetry parser and validator.
An out-of-vocabulary `status` word or event `type` is refused as `schema_invalid`, so a firmware typo is counted
rather than quietly becoming a new category. `ack` is still forwarded to nobody: command results are FR-14's, and
accepting one would acknowledge a command this build cannot issue.

`IEvaluationQueue` is now `InProcessEvaluationQueue` — a bounded channel drained by `EvaluatorWorker` into
`SampleEvaluator` (`Application/Evaluation/`), which applies §02-design/03 §4.2's "faulted or implausible: return"
and advances an `EvaluationState` watermark per `(terrarium, metric, phase)`. Unlike `InProcessTelemetryBus` it does
**not** back-pressure: ingest's acknowledgement may wait for the database, but it must never wait for a derived
opinion, so a full queue drops the batch and logs an error as the defect it is.

The silence watchdog (roadmap 3.3, `02-design/03` §4.3) is the one worker that is timer-driven rather than
queue-driven, for the same reason in reverse: its input is the *absence* of messages, so there is nothing to
subscribe to and nothing arrives to trigger a pass. The other two signals of that task are the opposite case and
live in the pipelines that receive their evidence: the sensor fault is decided in the events channel from the
parsed payload's failure count, and the clock-skew notice on the sample path from V-09's quality bit — each staged
into the same unit of work as the row that evidenced it, so a stored fault is always a signalled one. None of them
sends anything: delivery stays with the dispatcher.

The stage classes below are the ones that exist; `TelemetryPayloadValidator` and `PlausibilityGuard` live in the
Application layer and `TelemetryWriter`/`DeviceStateUpdater` are application stages over an `ITelemetryStore` port,
which is what keeps them unit-testable without a broker or a database.

## 4. Threshold evaluation implementation

```csharp
public sealed class ThresholdEvaluator(
    IThresholdResolver resolver,       // effective bands, cached + invalidated on change
    IEvaluationStateStore states,      // EvaluationState per (terrarium, metric, phase)
    IAlertRepository alerts,
    IClock clock)
{
    public async Task EvaluateAsync(TelemetrySample sample, IReadOnlyList<MetricReading> readings, CancellationToken ct)
    {
        foreach (var reading in readings)
        {
            if ((sample.QualityFlags & QualityFlags.NotEvaluable) != 0) continue;   // BR-11.1

            var band = await resolver.ResolveAsync(sample.TerrariumId, reading.MetricId, sample.RecordedAt, ct);
            if (band is null || !band.Enabled) continue;

            var state = await states.GetAsync(sample.TerrariumId, reading.MetricId, band.Phase, ct);
            var decision = ThresholdDecision.Decide(band, reading.Value, sample.RecordedAt, state, clock.UtcNow);

            switch (decision.Kind)
            {
                case DecisionKind.None: break;
                case DecisionKind.Open:      await alerts.OpenAsync(band, sample, reading, decision, ct); break;
                case DecisionKind.Escalate:  await alerts.EscalateAsync(state.OpenAlertId!.Value, reading, ct); break;
                case DecisionKind.Touch:     await alerts.TouchAsync(state.OpenAlertId!.Value, reading, ct); break;
                case DecisionKind.Resolve:   await alerts.AutoResolveAsync(state.OpenAlertId!.Value, ct); break;
            }

            await states.SaveAsync(state.With(decision), ct);
        }
    }
}
```

`ThresholdDecision.Decide` is a **pure static function** — no database, no clock, no logger. That is the
single most important testability decision in the backend: the dwell/hysteresis/escalation matrix can be
exhaustively tested in milliseconds (`TC-U-10…20`), and the algorithm is reviewable by reading one method.

```csharp
public static ThresholdDecision Decide(EffectiveBand band, decimal value, DateTimeOffset observedAt,
                                       EvaluationState state, DateTimeOffset nowUtc)
{
    var hot  = value > band.TargetMax;
    var cold = value < band.TargetMin;
    var crit = value > band.CriticalMax || value < band.CriticalMin;

    if (hot || cold)
    {
        state.FirstOutOfBandAt ??= observedAt;                     // triggers are back-dated (02-design/03 §4.2)
        if (crit) state.CriticalSinceAt ??= observedAt; else state.CriticalSinceAt = null;

        var sustained     = nowUtc - state.FirstOutOfBandAt >= TimeSpan.FromMinutes(band.DwellWarnMinutes);
        var critSustained = crit && nowUtc - state.CriticalSinceAt  >= TimeSpan.FromMinutes(band.DwellCritMinutes);

        state.ConsecutiveRecoveryTicks = 0;
        if (state.OpenAlertId is null && sustained) return ThresholdDecision.Open(crit);
        if (state.OpenAlertId is not null && critSustained && !state.IsCritical) return ThresholdDecision.Escalate();
        if (state.OpenAlertId is not null) return ThresholdDecision.Touch(value);
        return ThresholdDecision.None;
    }

    var recovered = value >= band.TargetMin + band.RecoveryMargin && value <= band.TargetMax - band.RecoveryMargin;
    state.ConsecutiveRecoveryTicks = recovered ? state.ConsecutiveRecoveryTicks + 1 : 0;
    if (state.OpenAlertId is not null && state.ConsecutiveRecoveryTicks >= 3) return ThresholdDecision.Resolve();
    return ThresholdDecision.None;
}
```

`IClock` (`Clock.Inject`) is injected so time can be advanced in tests without `Thread.Sleep` — the same
pattern the Flutter core uses (`03-implementation/05`).

**As built (2026-10-09, roadmap 3.2).** The sketch above is the shipped algorithm, in
`Domain/Evaluation/ThresholdDecision.cs`, with five differences worth naming — each one a place where the sketch
left something undecided:

| Sketch | Built | Why |
|---|---|---|
| `EffectiveBand band` | `ThresholdBand band` | `ThresholdBand` is the domain's own value object (with generated/validated ordering helpers); `EffectiveBand` was a name for a type that never needed to exist |
| `!state.IsCritical` | `AlertSeverity? openAlertSeverity` parameter | `EvaluationState` has no severity column and should not grow one (§07-appendices/02 §3.9): the fact lives on the alert row, so the caller passes it in |
| `alerts.OpenAsync(...)` / `alerts.TouchAsync(...)` | `ThresholdAlertWriter.Open/Touch/Escalate/Resolve` | four field-level writes with no I/O, so a test asserts a row without a store; sending is 3.5's, and `alertChanged` is 3.4's |
| `states.SaveAsync(state.With(decision))` | the same entity mutated in place, saved once per pass | the state row is loaded per batch and mutated by the decision, so `With` would allocate a second truth |
| *(nothing about the alert's values)* | `TriggeringValue`/`PeakValue` read back from the stored readings of the dwell window | both describe readings the engine judged before the alert existed; the readings are the evidence, so no column is added for them |

**One write ordering is deliberate.** When a decision opens an alert, the alert is committed *before* the state
row points at it (`state.OpenAlertId = alert.Id` needs the identity the insert assigned). So an interruption
between the two leaves an open alert that no state row names, rather than a state naming a row that was never
written — and the next pass repairs the first case by adopting the open alert it finds for that key. The same
adoption is what makes a human's resolve through the lifecycle API (3.4) visible to the evaluator without a second
mechanism: a resolved row is not open, so the pointer is cleared and the next excursion is a new episode.

**What is not built here.** The reorder window of §5 below is still a sketch: evaluation runs in ingest order (see
`02-design/03` §4.2's as-built note and the open item in `03-implementation/07`), and `Message` is left null on
purpose because §02-design/05 §7 composes the notification text from the alert's fields and localises it per user
(ADR-013).

## 5. Ordered evaluation queue

Alerts must be evaluated in `RecordedAt` order per device or a late sample could resolve an episode before
its cause was processed.

> **Not built (2026-10-09).** The window below is still a sketch. What runs today evaluates batches in ingest
> order, which is per-device publish order and therefore `RecordedAt` order for a single node — the common case —
> but a second publisher or a broker redelivery can still reach the engine out of order. The `EvaluationState`
> watermark cannot catch that: it is an id watermark, and a back-filled sample gets a *higher* id than the samples
> already evaluated even though its `RecordedAt` is older. Recorded as open in `03-implementation/07` (roadmap
> 3.2) with the reason it was not built now: a 30-second buffer delays every evaluation by 30 seconds, which the
> pipeline's own live checks and the M3 demo timing would have to absorb, and no test case in `04-quality/02`
> demands it yet.

```csharp
// Keyed by device; a small reorder window absorbs out-of-order back-fill.
var queue = new DeviceOrderedQueue(reorderWindow: TimeSpan.FromSeconds(30), maxBuffer: 2_000);
await queue.EnqueueAsync(sample, ct);   // yields samples in RecordedAt order
```

On API restart, each device's queue restarts from `EvaluationState.LastEvaluatedSampleId`, so nothing is
skipped and nothing is evaluated twice.

## 6. REST endpoint groups (contract detail in `07-appendices/03` §4)

| Group | Endpoints (abbreviated) | Notes |
|---|---|---|
| `/api/v1/auth` | `register`, `login`, `refresh`, `logout`, `me`, `change-password`, `recover`, `forgot-password`, `reset-password` | Anonymous + `[Authorize]`; the three recovery routes are anonymous and share the credential throttle (`auth`, 10 / min / address), while `me`, `refresh` and `logout` carry the larger `authSession` budget because a signed-in client calls them as housekeeping. A password change ends every **other** session of the account (`ADR-022`) |

Password recovery (FR-01, BR-01.5) is the group's largest deliberate piece of design, and one file owns it:

- **Two credentials, two endpoints, one non-disclosure rule — with two recorded exceptions.** `recover` spends the
  backup code issued at registration; `forgot-password` + `reset-password` spend a code the server issues. Every
  *code* failure on both paths is the same opaque `401` (`invalid_recovery_code` / `invalid_reset_code`) whether the
  code is spent, expired or belongs to someone else. What the identifier resolves to is disclosed on two paths only
  (`ADR-020`): registration answers `409 registration_conflict` naming `email_taken` / `username_taken`, and
  `forgot-password` answers `404 identifier_unknown` when no account uses the identifier — a keeper who mistyped
  their address is told so rather than left waiting for a code that was never generated.
- **A failing delivery channel cannot change the answer.** `AuthService.ForgotPasswordAsync` commits the code and
  then calls `IPasswordResetNotifier` inside a guard that swallows a throwing implementation: a `500` where the code
  exists would be indistinguishable from a broken deployment and would tell the keeper nothing about the code they
  were just issued. The port is still required not to throw — the guard is the second line, not the first.
- **Hashing follows what the secret is used for.** The backup code is salted (`Sha256SecretHasher`, shared with
  device credentials) because it is verified after the account is known. The reset code is **unsalted** SHA-256 via
  `ISecretGenerator.Sha256`, like a refresh token, because the row has to be *found* by the value presented; both
  are high-entropy and short-lived, so there is nothing to brute-force. Passwords remain PBKDF2.
- **One live code per account.** Issuing stamps `ConsumedAt` on any outstanding row for the user, so asking again
  replaces the previous code instead of leaving two working ways in; spending does the same for the row it used.
- **Delivery is a port with a log implementation.** `LogPasswordResetNotifier` writes the code when
  `PasswordReset:LogCode` is true (development only) and otherwise warns that it reached nobody — a production log
  holding a live credential is a leak, so the default is off and the release note says so (limitation L-02).
| `/api/v1/terrariums` | **built:** `GET`, `POST`, `GET {id}`, `PATCH {id}`, `DELETE {id}`, `GET {id}/readings/latest`, `GET {id}/readings`, `GET {id}/coverage`, `GET {id}/thresholds`, `POST {id}/silences`, `GET {id}/silences`, `DELETE {id}/silences/{silenceId}` · **planned:** `PUT {id}/thresholds`, `GET {id}/summaries`, `POST {id}/exports` | Ownership is a query parameter (`TerrariumService` / `ITerrariumStore`), not a filter applied afterwards |
| `/api/v1/devices` | `GET`, `GET {id}`, `POST self-register`, `POST claim`, `PATCH {id}`, `POST {id}/rebind`, `POST {id}/rotate-secret`, `POST {id}/revoke`, `POST {id}/calibration`, `POST {id}/commands`, `POST {id}/snapshots` | `self-register` anonymous + rate-limited |
| `/api/v1/ingest` | **built:** `POST http` | Device-auth header (`Device {id}.{secret}`); the HTTP fallback path, 6 requests/min/device |
| `/api/v1/alerts` | **built:** `GET`, `GET {id}`, `GET {id}/timeline`, `POST {id}/ack`, `POST {id}/resolve` | Ack and resolve name the `Technician` policy (Owner or Technician), so a Viewer reads the same list and cannot change it; a foreign alert is a `404`, a closed one a `409 alert_not_open` |
| `/api/v1/species-profiles` | `GET`, `POST`, `PATCH {id}`, `DELETE {id}`, `POST {id}/duplicate` | Built-ins immutable |
| `/api/v1/notifications` | `GET`, `POST {id}/read`, `POST read-all` | In-app inbox |
| `/api/v1/exports` | `GET {jobId}`, `GET {jobId}/download` | Token-based download |
| `/api/v1/audit` | `GET` | Owner-scoped |
| ops | `/health`, `/ready`, `/metrics` | Unauthenticated, non-sensitive |

Implementation notes:
- Minimal APIs grouped per file with `RequireAuthorization()` + `AddEndpointFilter<ValidationFilter>()`.
- `ProblemDetails` (RFC 7807) with `code`, `title`, `detail`, `traceId` and optional `errors[]` per field.
- Pagination: `?cursor=&pageSize=` (default 50, max 200); responses always include `nextCursor`. As built,
  `GET /alerts` is the only endpoint that implements it — it is the first list in the product that grows without
  bound — and the cursor is `triggeredAt|id` in base64, so paging over a table that keeps receiving rows neither
  repeats nor skips one.
- `Idempotency-Key` support on `POST` mutations (24 h cache table) — matters on flaky mobile networks.
- CORS: allow-list of the dashboard origins; the Flutter app is not affected by CORS.

**The alert lifecycle as built (roadmap 3.4, 2026-10-10).** `Application/Alerts/` holds two services over two ports,
and the split is by feature rather than by table: an alert can be acknowledged with no silence in sight, and a silence
outlives every alert it suppresses.

- **One class owns the rules, and it is pure.** `AlertLifecycle` (domain) decides whether a transition is allowed and
  writes the four fields it touches; `AlertService` does the ordering around it — look the alert up *scoped to the
  caller*, apply, stage one audit row, commit, then push. `Acknowledge` succeeds only from `Open`, and a refusal is
  `alert_not_open` for the second acknowledgement and for a resolved alert alike: in both cases the request changed
  nothing.
- **Resolution re-arms the dwell key.** `IAlertStore.FindExcursionStateAsync` finds the `EvaluationState` row of
  `(terrarium, metric, phase)` and `EvaluationState.Rearm()` clears the excursion window without touching the
  watermark — samples before this instant have been judged and must not be judged twice. That is what stops a value
  that never returned inside its band from re-opening the alert on the next sample; a longer quiet period is the
  silence window's job (`ADR-023`).
- **A device-level alert has no key to re-arm**, which is why the rule is a domain predicate
  (`AlertLifecycle.RearmsItsKey`) rather than a branch in the service.
- **The audit row is the note's home.** The alert row keeps `ResolvedReason` (machine-readable, grouped by the
  report) and has no column for prose; the keeper's `note` is written into the `alert.resolved` entry and read back by
  the timeline.
- **Silences follow §02-design/05 §5, and the read side is what 3.5 needs.**
  `MetricSilenceService.CreateAsync` validates through `MetricSilencePolicy` (reason required, window in the future,
  at most 24 h) and stages the window with its audit row; `CancelAsync` is idempotent, because a `DELETE` that failed
  on a retry would leave a keeper believing a suppression was still in force. The dispatcher of 3.5 reads the same
  `IsActiveAt`/`Covers` pair the list endpoint renders — a window with no metric covers every metric of the
  terrarium, device-level alerts included.
- **Nothing in either service can reach another account's data.** Reads start from the terrarium's owner
  (`EfAlertStore`'s joins, `IMetricSilenceStore.IsMemberAsync`), so a foreign id answers `404 not_found` exactly like
  a missing one; the only `403` in the group is the policy refusing a Viewer before a handler runs.

## 7. SignalR hub

```csharp
public sealed class TelemetryHub : Hub
{
    public Task JoinTerrarium(Guid terrariumId) { /* authorise membership, then */ return Groups.AddToGroupAsync(Context.ConnectionId, $"terrarium:{terrariumId}"); }
    public Task LeaveTerrarium(Guid terrariumId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"terrarium:{terrariumId}");
}
```

Events pushed: `readingAdded {sampleId, recordedAt, metrics[]}`, `statusChanged {deviceStatus, lastSeenAt}`,
`alertChanged {alertId, state, severity}`. Membership is authorised on join — a group is never joined
before the `TerrariumAccessRequirement` check, otherwise SignalR would become a cross-tenant data leak.

**As built (task 2.9, 2026-10-06).** Three things the sketch does not say, all of them decisions rather than
details:

- **Membership is the ownership query the read surface already makes.** `TerrariumService.IsMemberAsync` asks
  `ITerrariumStore.FindOwnedAsync`, so the hub answers "not yours" and "does not exist" identically, exactly as
  `GET /terrariums/{id}` does (BR-02.2) — the hub cannot be used to enumerate ids. A connection with no subject is
  refused rather than trusted; `[Authorize]` should make that unreachable, and the safe direction to be wrong in
  costs nothing here.
- **The push is an Application port implemented in the API project.** `SignalRTelemetryBroadcaster` sits beside the
  hub because `IHubContext<TelemetryHub>` is not visible from Infrastructure; the ingest pipeline still depends only
  on `ITelemetryBroadcaster`. The event body is `ReadingAddedPayload`, in the application layer so its shape is
  testable without a hub, and one event is sent per sample rather than per batch — a client handler then treats
  every arrival the same way instead of unwrapping a batch that may be one long. Metrics are keyed by the metric
  dictionary's `ApiKey`, the same string `readings/latest` answers with, and the per-metric `qualityFlags` is the
  *sample's* bitmask, because quality is recorded per sample.
- **The token travels in the query string** (`?access_token=…`), because a browser cannot set a header on the
  WebSocket handshake. Accepted on `/hubs` only: a token in a URL reaches logs, proxies and referrer headers, which
  is worth paying for the one route that cannot avoid it and nowhere else.

`statusChanged` and `alertChanged` are both written now: `statusChanged` since 2.9 and `alertChanged` since 3.4 (see
the 2.9 and 3.4 blocks in `07-implementation-roadmap`).

**`alertChanged` as built (roadmap 3.4).** One event per move, to the terrarium's group, after the commit that wrote
it — by whichever layer owns that commit: `EvaluatorWorker` (threshold alerts), `SilenceWatchdogWorker` (silence),
`IngestOutcomeRecorder` (the clock-skew entry and the sensor fault, which the pipelines stage and this class pushes
once the commit has given them identities) and `AlertService` itself for ack and resolve. The producers do not depend
on the hub: each outcome carries `AlertChange` records and the fan-out step turns them into
`AlertChangedPayload`. A touch is not announced — the documented event reports lifecycle moves — and a failed push is
logged and dropped, never retried into a failed request, which is the same rule the sample push follows.

## 8. Background workers

| Worker | Schedule | Idempotency | Failure behaviour |
|---|---|---|---|
| `IngestWorker` | continuous | `(deviceId, seq)` unique index | Stop acking → broker holds → device retries |
| `EvaluatorWorker` | continuous (ordered queue) | `EvaluationState` per key | Log + counter, next sample retries; never blocks ingest. Pushes `alertChanged` for every alert the pass opened, escalated or resolved, after the commit |
| `NotificationWorker` | continuous | notification job id | 3 retries with backoff, then `Failed` |
| `RollupWorker` | every minute + nightly 48 h recompute | Upsert on `(terrarium, metric, hour)` | Recompute later; raw data still present |
| `SummaryWorker` | local midnight + 5 min per terrarium timezone | Upsert on `(terrarium, localDate)` | Recompute on late data |
| `RetentionSweeperWorker` | nightly 02:00 local | Batched deletes ≤ 10 000 rows/tx | Partial progress is safe; resumes next run |
| `SilenceWatchdogWorker` | every 30 s | One open alert per device | Creates `DeviceSilent` alerts, moves the device `Offline` and escalates at 30 min; a failed sweep is logged and the next tick retries. Pushes `alertChanged` after its own commit |

All workers derive from `BackgroundService`, resolve scoped services from a scope factory, and log with a
correlation id. Each exposes a counter so a stuck worker is visible on `/metrics` (NFR-12).

## 9. Repository and query patterns

- Ports exist only where a set of queries is non-trivial or reused. `ITerrariumStore`
  (`Application/Abstractions`) with its `EfTerrariumStore` adapter (`Infrastructure/Persistence`) is the read path of
  FR-03/FR-08/FR-09 (task 2.8). It follows `IProvisioningStore`: **one port per use-case family**, and no repository
  per entity, which is why there is no `ITerrariumRepository`/`IDeviceRepository` pair.
- **Built — ownership is a query parameter.** Every terrarium query filters by owner in SQL
  (`Where(t => t.Id == id && t.UserId == owner)`) instead of loading a row and discarding it, so a foreign terrarium
  is never materialised and a later refactor cannot leak one (BR-02.2). The soft-delete filter is an EF query filter
  on the model, so it applies to all of them without being repeated.
- **Built — grouped queries, not N+1.** A list of twenty terrariums resolves the newest sample and the open-alert
  count for all of them with one `GroupBy` each (`SummariseActivityAsync`), and `readings/latest` reads a bounded
  window of 50 samples rather than one query per metric.
- **Built — a filtered include for a series.** `readings` loads samples carrying only the requested metric
  (`Include(s => s.Readings.Where(r => r.Metric == metric))`), so drawing one chart does not pull four metrics.
- All range queries project with `AsNoTracking()` where the caller cannot mutate, and never materialise
  `TelemetrySample` graphs for a chart. The read path above is the deliberate exception: the aggregates it loads (a
  terrarium, its device, a window of samples) are small, and `readings/latest` has to walk several samples anyway
  because a metric the newest one lacks lives in an earlier one.
- **Built — a fourth port, for the delivery channel.** `IPasswordResetNotifier` (`Application/Abstractions`) exists
  so the identity use cases can issue a reset code without knowing how it travels; `LogPasswordResetNotifier`
  (`Infrastructure/Security`) is the implementation the demo ships, and the SMTP sender of the next phase replaces
  the registration rather than the call site. It is the same shape as `ITerrariumStore` and `IProvisioningStore`:
  one port per use-case family, no repository per entity. `IUserStore` gained the three reset-code queries
  (`AddPasswordResetCode`, `FindPasswordResetCodeAsync`, `InvalidateOutstandingResetCodesAsync`) rather than a new
  port, because they belong to the account aggregate that port already owns.
- **Built — the lookup key is the query, not a scan.** `FindPasswordResetCodeAsync` matches on the SHA-256 index
  `IX_PasswordResetCode_CodeHash` (unique), so verifying a presented code is one seek. A salted hash could not do
  this: it would have to load every outstanding code and compare each one.
- Coverage (FR-07 BR-07.5): `expected = ⌊(to − from) ÷ samplingIntervalSec⌋`, `received` a `COUNT` over the window,
  `coveragePct = min(100, received ÷ expected × 100)`. `TerrariumService.CoverageAsync` answers `409
  device_not_bound` rather than dividing by a null device, and takes the interval from the bound device so the
  expectation is that device's own cadence rather than a system default.

## 10. Performance defence (the part that is easy to get wrong)

| Risk | Mitigation | Verified by |
|---|---|---|
| Chart query scanning 90 days of raw rows | Bucket selection by range (raw ≤ 6 h, 5-min 6–48 h, hourly rollup > 48 h) | `TC-I-11`, `TC-I-10` |
| `latest` endpoint becoming a sort of the whole table | `(TerrariumId, RecordedAt DESC)` index + `TAKE 1` per metric via `ROW_NUMBER()` | Execution plan captured in `05-release/02` §3 |
| N+1 when rendering a terrarium list | Single projection query with `Include` limited to the device row | `TC-I-11` |
| Alert inbox scan | `(TerrariumId, State, TriggeredAt DESC)` index | `TC-I-11` |
| Daily summary recomputation across all history | Summary worker only recomputes days touched by late data (targeted by sample `RecordedAt` range) | `TC-I-06` |
