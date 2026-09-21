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

## 5. Ordered evaluation queue

Alerts must be evaluated in `RecordedAt` order per device or a late sample could resolve an episode before
its cause was processed.

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
| `/api/v1/auth` | `register`, `login`, `refresh`, `logout`, `me`, `change-password` | Anonymous + `[Authorize]` |
| `/api/v1/terrariums` | `GET/POST`, `GET/PATCH/DELETE {id}`, `GET {id}/thresholds`, `PUT {id}/thresholds`, `POST {id}/silences`, `GET {id}/coverage`, `GET {id}/readings/latest`, `GET {id}/readings`, `GET {id}/summaries`, `POST {id}/exports` | Owner-scoped via `TerrariumAccessRequirement` |
| `/api/v1/devices` | `GET`, `GET {id}`, `POST self-register`, `POST claim`, `PATCH {id}`, `POST {id}/rebind`, `POST {id}/rotate-secret`, `POST {id}/revoke`, `POST {id}/calibration`, `POST {id}/commands`, `POST {id}/snapshots` | `self-register` anonymous + rate-limited |
| `/api/v1/ingest` | `POST http` | Device-auth header; the HTTP fallback path |
| `/api/v1/alerts` | `GET`, `GET {id}`, `POST {id}/ack`, `POST {id}/resolve` | Role-gated ack/resolve |
| `/api/v1/species-profiles` | `GET`, `POST`, `PATCH {id}`, `DELETE {id}`, `POST {id}/duplicate` | Built-ins immutable |
| `/api/v1/notifications` | `GET`, `POST {id}/read`, `POST read-all` | In-app inbox |
| `/api/v1/exports` | `GET {jobId}`, `GET {jobId}/download` | Token-based download |
| `/api/v1/audit` | `GET` | Owner-scoped |
| ops | `/health`, `/ready`, `/metrics` | Unauthenticated, non-sensitive |

Implementation notes:
- Minimal APIs grouped per file with `RequireAuthorization()` + `AddEndpointFilter<ValidationFilter>()`.
- `ProblemDetails` (RFC 7807) with `code`, `title`, `detail`, `traceId` and optional `errors[]` per field.
- Pagination: `?cursor=&pageSize=` (default 50, max 200); responses always include `nextCursor`.
- `Idempotency-Key` support on `POST` mutations (24 h cache table) — matters on flaky mobile networks.
- CORS: allow-list of the dashboard origins; the Flutter app is not affected by CORS.

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

## 8. Background workers

| Worker | Schedule | Idempotency | Failure behaviour |
|---|---|---|---|
| `IngestWorker` | continuous | `(deviceId, seq)` unique index | Stop acking → broker holds → device retries |
| `EvaluatorWorker` | continuous (ordered queue) | `EvaluationState` per key | Log + counter, next sample retries; never blocks ingest |
| `NotificationWorker` | continuous | notification job id | 3 retries with backoff, then `Failed` |
| `RollupWorker` | every minute + nightly 48 h recompute | Upsert on `(terrarium, metric, hour)` | Recompute later; raw data still present |
| `SummaryWorker` | local midnight + 5 min per terrarium timezone | Upsert on `(terrarium, localDate)` | Recompute on late data |
| `RetentionSweeperWorker` | nightly 02:00 local | Batched deletes ≤ 10 000 rows/tx | Partial progress is safe; resumes next run |
| `DeviceSilenceWatchdog` | every 30 s | One alert per terrarium/device | Creates `DeviceSilent` alerts; escalates at 30 min |

All workers derive from `BackgroundService`, resolve scoped services from a scope factory, and log with a
correlation id. Each exposes a counter so a stuck worker is visible on `/metrics` (NFR-12).

## 9. Repository and query patterns

- Repositories exist only where a query is non-trivial or reused (readings range, alert inbox, coverage).
  Simple CRUD goes through `DbContext` in the `Application` layer via a small `IUnitOfWork` — adding a
  repository for every entity is noise, and this choice is stated in the report rather than hidden.
- All range queries use `AsNoTracking()` and explicit projections (`Select` into DTOs) — never
  materialise `TelemetrySample` graphs for a chart.
- Coverage query (FR-07 BR-07.5):

```csharp
public async Task<CoverageDto> GetCoverageAsync(Guid terrariumId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
{
    var interval = TimeSpan.FromSeconds(await db.Devices.Where(d => d.TerrariumId == terrariumId)
                                                        .Select(d => d.SamplingIntervalSec).FirstAsync(ct));
    var expected = (int)Math.Max(1, (to - from) / interval);
    var received = await db.TelemetrySamples.CountAsync(s => s.TerrariumId == terrariumId &&
                                                             s.RecordedAt >= from && s.RecordedAt < to, ct);
    var first = await db.TelemetrySamples.Where(s => s.TerrariumId == terrariumId)
                                         .MinAsync(s => (DateTimeOffset?)s.RecordedAt, ct);
    return new CoverageDto(expected, received, 100.0 * received / expected, first, /* gaps computed client-side or in SQL */ null);
}
```

## 10. Performance defence (the part that is easy to get wrong)

| Risk | Mitigation | Verified by |
|---|---|---|
| Chart query scanning 90 days of raw rows | Bucket selection by range (raw ≤ 6 h, 5-min 6–48 h, hourly rollup > 48 h) | `TC-I-11`, `TC-I-10` |
| `latest` endpoint becoming a sort of the whole table | `(TerrariumId, RecordedAt DESC)` index + `TAKE 1` per metric via `ROW_NUMBER()` | Execution plan captured in `05-release/02` §3 |
| N+1 when rendering a terrarium list | Single projection query with `Include` limited to the device row | `TC-I-11` |
| Alert inbox scan | `(TerrariumId, State, TriggeredAt DESC)` index | `TC-I-11` |
| Daily summary recomputation across all history | Summary worker only recomputes days touched by late data (targeted by sample `RecordedAt` range) | `TC-I-06` |
