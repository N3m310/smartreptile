using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Readings;
using SmartReptile.Application.Reference;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Terrariums;

/// <summary>
/// The terrarium read surface plus creation (FR-03, FR-08, FR-09 — roadmap task 2.8). It answers four questions —
/// what enclosures does this account have, what are their latest values, what does a window of history look like,
/// and how complete is that history — and it is the only place that decides what a metric's
/// <see cref="ReadingStatus"/> is on a read.
/// </summary>
/// <remarks>
/// Three decisions worth defending:
/// <list type="number">
/// <item>
/// <b>Ownership is a query parameter, and the refusal says which kind it is.</b> A missing terrarium is
/// <c>404</c>, a deleted one is <c>410</c> and another account's is <c>403</c> — for callers other than the
/// administrator, who reaches any account's enclosure. This narrows BR-02.2's uniform <c>404</c> to this one
/// resource, so a keeper can tell a mistyped id from someone else's enclosure; the other resources keep the
/// uniform answer, and the decision log records why (TASKS 2026-10-14, items 3 and 5).
/// </item>
/// <item>
/// <b>Status is instantaneous, alerts are not.</b> The value of a reading is judged against the effective band
/// here, because a card has to be coloured — but dwell, hysteresis, dedupe and alert rows belong to the
/// evaluator (M3, FR-11) and are deliberately absent.
/// </item>
/// <item>
/// <b>Bucketing is pure arithmetic.</b> <see cref="RangeQueryRules"/> owns it, so TC-I-10 can assert the bucket
/// for every offered range without a database.
/// </item>
/// </list>
/// </remarks>
public sealed class TerrariumService(
    ITerrariumStore store,
    ITimeZoneStore timeZones,
    DeviceProvisioningService provisioning,
    IClock clock,
    TerrariumSettings settings)
{
    /// <summary>
    /// How far back the latest-readings query walks for a metric the newest sample did not carry. Fifty samples is
    /// a little under an hour at the 60 s default and well over an hour at the fastest allowed interval, which is
    /// longer than any single metric is expected to be absent.
    /// </summary>
    private const int LatestSampleLookback = 50;

    private const int NameMaxLength = 60;
    private const int LocationMaxLength = 120;
    private const int DescriptionMaxLength = 1000;
    private const int NoteMaxLength = 300;
    private const int DefaultDwellWarnMinutes = 5;
    private const int DefaultDwellCritMinutes = 2;
    private const decimal DefaultRecoveryMargin = 0.5m;

    /// <summary>
    /// Snapshot JSON is written in the wire's camelCase, so a stored snapshot reads like the response it was
    /// captured from and the same shape can be handed to a client without a second projection.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly Dictionary<MetricCode, MetricDefinition> Definitions =
        MetricDictionary.All.ToDictionary(definition => definition.Code);

    /// <summary>The caller's terrariums, each with its device state and activity summary.</summary>
    /// <param name="ownerUserId">Account whose terrariums are wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account read permission, which sees every account's enclosures rather than only their own
    /// (TASKS 2026-10-14, item 5). Each item then carries <see cref="TerrariumSummary.OwnerUserId"/>, because a
    /// list of rows belonging to unknown accounts is not usable.
    /// </param>
    /// <param name="speciesProfileId">
    /// When set, only the enclosures assigned to that species profile — the query a profile page makes to list the
    /// terrariums judged against it. It narrows the set the caller may already see and never widens it, so a
    /// profile the caller cannot read still answers with an empty list rather than naming its terrariums.
    /// </param>
    public async Task<TerrariumOutcome> ListAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false,
        Guid? speciesProfileId = null)
    {
        var terrariums = mayReachAnyAccount
            ? await store.ListAllAsync(cancellationToken)
            : await store.ListOwnedAsync(ownerUserId, cancellationToken);

        if (speciesProfileId is { } profileId)
        {
            terrariums = [.. terrariums.Where(terrarium => terrarium.SpeciesProfileId == profileId)];
        }

        var activity = await store.SummariseActivityAsync([.. terrariums.Select(t => t.Id)], cancellationToken);

        // The administrator's list can hold rows belonging to accounts they did not name, so each one is labelled
        // with whose enclosure it is. One query for the whole page, not one per row; an unowned enclosure has no
        // account to name and is left blank.
        var owners = mayReachAnyAccount
            ? await store.AccountEmailsAsync(
                [.. terrariums.Select(terrarium => terrarium.UserId).OfType<Guid>().Distinct()],
                cancellationToken)
            : EmptyOwners;

        return TerrariumOutcome.Listed([.. terrariums.Select(terrarium => Summarise(
            terrarium,
            activity,
            mayReachAnyAccount,
            OwnerEmailOf(terrarium, owners)))]);
    }

    /// <summary>
    /// One terrarium. Missing, deleted and another account's are three different answers: <c>not_found</c>,
    /// <c>terrarium_deleted</c> and <c>forbidden_terrarium</c> respectively — except for the administrator, who may
    /// read any account's.
    /// </summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation.</param>
    public async Task<TerrariumOutcome> GetAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;
        var activity = await store.SummariseActivityAsync([terrariumId], cancellationToken);

        var owners = mayReachAnyAccount && terrarium.UserId is { } ownerId
            ? await store.AccountEmailsAsync([ownerId], cancellationToken)
            : EmptyOwners;

        return TerrariumOutcome.Found(
            Summarise(terrarium, activity, mayReachAnyAccount, OwnerEmailOf(terrarium, owners)),
            terrarium.RowVersion);
    }

    /// <summary>
    /// Whether the caller may receive this terrarium's live updates (FR-08). Ownership is the only membership the
    /// model has, so this is the same query as <see cref="GetAsync"/> without the summarising — and a foreign
    /// terrarium answers <c>false</c> exactly like a missing one, so the realtime hub cannot be used to probe for
    /// ids either (BR-02.2).
    /// </summary>
    /// <param name="terrariumId">Terrarium the caller wants updates for.</param>
    /// <param name="userId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account read permission, which may follow any account's terrarium for the same reason
    /// they may read it (TASKS 2026-10-14, item 5). The hub's answer still says <c>false</c> for a deleted
    /// terrarium, so a removed enclosure does not start streaming.
    /// </param>
    public async Task<bool> IsMemberAsync(
        Guid terrariumId,
        Guid userId,
        CancellationToken cancellationToken = default,
        bool mayReachAnyAccount = false) =>
        (await LocateAsync(terrariumId, userId, mayReachAnyAccount, cancellationToken)).Refusal is null;

    /// <summary>
    /// The band in force for every metric this terrarium has one for, with the layer it came from (FR-10, BR-10.3 —
    /// roadmap 3.1's <c>effectiveThresholds</c> read).
    /// </summary>
    /// <remarks>
    /// This is the same <see cref="ResolveEffective"/> call the band on a reading card comes from, deliberately: an
    /// editor that previewed one set of bands while the cards were judged against another would be worse than no
    /// editor. Only the metrics that resolve <i>right now</i> appear — a metric configured for Day alone is absent
    /// at night, because that is the truth the evaluator will act on.
    /// </remarks>
    /// <param name="terrariumId">Terrarium the bands belong to.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> EffectiveThresholdsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var capturedAt = clock.UtcNow;

        return TerrariumOutcome.Resolved(DescribeThresholds(located.Terrarium!, capturedAt));
    }

    /// <summary>
    /// Upserts this terrarium's band overrides (UC-03, FR-10 — roadmap 3.1's write half). Every entry is validated
    /// against the band rules of `03-implementation/06` §2 before anything is written, so a body carrying one
    /// incoherent band changes nothing at all; a valid body writes the overrides and one
    /// <see cref="ThresholdSnapshot"/> of the set now in force, in the same unit of work.
    /// </summary>
    /// <remarks>
    /// Two semantics worth stating because they are choices rather than consequences. A band identical to the one
    /// the terrarium already holds is <b>not</b> a change: it writes no snapshot and does not touch the row, so
    /// re-saving an unchanged editor page is not recorded as history. And the request is a <b>patch over the override
    /// set</b>, not a replacement — a metric the body does not mention keeps whatever override it had.
    /// </remarks>
    /// <param name="terrariumId">Terrarium the bands belong to.</param>
    /// <param name="request">The bands to upsert.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> UpsertThresholdsAsync(
        Guid terrariumId,
        UpdateThresholdsRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        var inputs = request.Overrides ?? [];
        var violations = new List<IdentityViolation>();
        var candidates = new List<OverrideCandidate>();
        var seen = new HashSet<(MetricCode Metric, ThresholdPhase Phase)>();

        for (var index = 0; index < inputs.Count; index++)
        {
            if (ParseOverride(inputs[index], index, violations) is not { } candidate)
            {
                continue;
            }

            // Two rows for one (metric, phase) would violate the table's unique index; the second is refused here,
            // where the caller can be told which entry is the duplicate.
            if (!seen.Add((candidate.Metric, candidate.Phase)))
            {
                violations.Add(new IdentityViolation(
                    $"overrides[{index}].metric",
                    "threshold_duplicate",
                    "A metric and phase may appear only once: a band is a single row per metric and phase."));
            }

            candidates.Add(candidate);
        }

        if (violations.Count > 0)
        {
            return TerrariumOutcome.Invalid(violations);
        }

        var capturedAt = clock.UtcNow;
        var changed = false;

        foreach (var candidate in candidates)
        {
            changed |= ApplyOverride(terrarium, candidate, ownerUserId, capturedAt);
        }

        if (changed)
        {
            await SaveWithSnapshotAsync(terrarium, capturedAt, cancellationToken);
        }

        return TerrariumOutcome.Resolved(DescribeThresholds(terrarium, capturedAt));
    }

    /// <summary>
    /// Reverts one metric to its profile band (FR-10 — roadmap 3.1's write half) by removing every override row the
    /// terrarium holds for it, whichever phase, and snapshotting the set that is left.
    /// </summary>
    /// <remarks>
    /// Idempotent: a metric that carries no override is not an error, because a delete that failed on a retry would
    /// leave a client believing an override was still in force — the same reasoning the silence cancel follows. An
    /// unknown metric key is refused, though, since that is a client mistake rather than a no-op.
    /// </remarks>
    /// <param name="terrariumId">Terrarium the override belongs to.</param>
    /// <param name="metricKey">Metric key from the dictionary, e.g. <c>tempC</c>.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> DeleteThresholdOverrideAsync(
        Guid terrariumId,
        string metricKey,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        if (!MetricDictionary.TryParseApiKey(metricKey, out var metric))
        {
            return TerrariumOutcome.Invalid(
            [
                new IdentityViolation("metric", "metric_invalid", $"'{metricKey}' is not a metric key: {MetricKeys}."),
            ]);
        }

        var capturedAt = clock.UtcNow;
        var removed = terrarium.ThresholdOverrides.Where(row => row.Metric == metric).ToList();

        if (removed.Count > 0)
        {
            foreach (var row in removed)
            {
                terrarium.ThresholdOverrides.Remove(row);
                store.RemoveThresholdOverride(row);
            }

            await SaveWithSnapshotAsync(terrarium, capturedAt, cancellationToken);
        }

        return TerrariumOutcome.Resolved(DescribeThresholds(terrarium, capturedAt));
    }

    /// <summary>
    /// Creates a terrarium, optionally binding a board to it in the same unit of work (FR-03, FR-04 —
    /// TASKS 2026-10-14, item 2).
    /// </summary>
    /// <param name="request">The enclosure's fields, plus an optional board to bind.</param>
    /// <param name="ownerUserId">Authenticated caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="actor">Request provenance for the binding's audit row, when one is written.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account write permission. An enclosure created that way starts
    /// <b>unowned</b> (2026-10-23): an administrator works from the whole deployment, and guessing an owner for them
    /// would put a keeper's name on an enclosure that keeper never asked for. The owner is assigned afterwards, on
    /// the update route, which is also the only route that can change it.
    /// </param>
    public async Task<TerrariumOutcome> CreateAsync(
        CreateTerrariumRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        AuditActor? actor = null,
        bool mayReachAnyAccount = false)
    {
        // The caller's own account is checked first, because it is the one thing the body cannot fix. A token this
        // server signed can still name an account it does not have — one minted against another database, or before
        // this database was rebuilt — and without this check the id goes straight into the insert and the foreign
        // key answers `500 internal_error`, which reads as a server fault instead of "sign in again" (BUG-11).
        if (!await store.UserExistsAsync(ownerUserId, cancellationToken))
        {
            return TerrariumOutcome.Failed(
                "unauthenticated",
                "The access token names an account this server does not have. Sign in again.");
        }

        var violations = new List<IdentityViolation>();

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            violations.Add(new IdentityViolation("name", "terrarium_name_required", "A name is required."));
        }
        else if (name.Length > NameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "name",
                "terrarium_name_too_long",
                $"A name may be at most {NameMaxLength} characters."));
        }

        var location = Trimmed(request.Location);
        if (location is { Length: > LocationMaxLength })
        {
            violations.Add(new IdentityViolation(
                "location",
                "terrarium_location_too_long",
                $"A location may be at most {LocationMaxLength} characters."));
        }

        var description = Trimmed(request.Description);
        if (description is { Length: > DescriptionMaxLength })
        {
            violations.Add(new IdentityViolation(
                "description",
                "terrarium_description_too_long",
                $"A description may be at most {DescriptionMaxLength} characters."));
        }

        var timeZoneId = Trimmed(request.TimeZoneId) ?? settings.DefaultTimeZoneId;

        // The catalogue is the authority, not a bare provider lookup (TASKS 2026-10-14, item 11): the id has to be
        // one the picker could have offered, so a caller cannot store a zone the clients cannot render — or one the
        // platform cannot resolve, which the phase resolver would silently degrade to UTC. The stored value is the
        // catalogue's spelling, so a body that said `asia/ho_chi_minh` lands as `Asia/Ho_Chi_Minh`.
        if (await Reference.ReferenceService.FindAsync(timeZones, timeZoneId, cancellationToken) is not { } zone)
        {
            violations.Add(new IdentityViolation(
                "timeZoneId",
                "terrarium_timezone_invalid",
                $"'{timeZoneId}' is not one of the catalogue's time zones; read the list from "
                + "GET /api/v1/time-zones."));
        }
        else
        {
            timeZoneId = zone.IanaId;
        }

        if (violations.Count > 0)
        {
            return TerrariumOutcome.Invalid(violations);
        }

        if (request.SpeciesProfileId is { } profileId
            && !await store.SpeciesProfileExistsAsync(profileId, cancellationToken))
        {
            // A 400, not a 404: the profile is part of the body the caller sent, so this is a field the client got
            // wrong rather than a resource someone else owns. A null profile is not that mistake — an enclosure
            // without a species is a valid enclosure (2026-10-25).
            return TerrariumOutcome.Invalid(
                [new IdentityViolation("speciesProfileId", "species_profile_not_found", "No such species profile.")]);
        }

        // Resolved before anything is written, so a request naming a board that cannot be used creates nothing at
        // all — the same all-or-nothing rule the species-profile check above follows, and the reason a keeper never
        // ends up with an empty enclosure they did not want plus a board that is still unbound.
        var (device, deviceViolations) = await provisioning.ResolveClaimableDeviceAsync(
            request.DeviceId,
            request.MacAddress,
            cancellationToken);

        if (deviceViolations.Count > 0)
        {
            return TerrariumOutcome.Invalid(deviceViolations);
        }

        var now = clock.UtcNow;
        var terrarium = new Terrarium
        {
            // An administrator creates from the deployment's side, so the enclosure starts unowned and is assigned
            // on the update route; a keeper's own creation is theirs by definition.
            UserId = mayReachAnyAccount ? null : ownerUserId,
            Name = name,
            SpeciesProfileId = request.SpeciesProfileId,
            Location = location,
            Description = description,
            TimeZoneId = timeZoneId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        store.AddTerrarium(terrarium);

        // The board is bound before the single commit, not after it: the enclosure and its binding are one act, so
        // a failure here would leave either a terrarium whose board nobody can use or a board pointing at an
        // enclosure that was never written. Staging it also means the credential, the device row and the audit entry
        // are committed by the same SaveChanges as the terrarium (TASKS 2026-10-14, item 2).
        var deviceSecret = device is null
            ? null
            : provisioning.StageBinding(
                device,
                terrarium.Id,
                // An administrator's enclosure starts unowned, so the board falls back to the claimer rather than
                // inheriting `null` and landing in nobody's fleet (2026-10-25).
                terrarium.UserId ?? ownerUserId,
                now,
                method: request.DeviceId is { Length: > 0 } ? "device_id" : "mac_address",
                actor);

        await store.SaveChangesAsync(cancellationToken);

        // Re-read so the response carries the profile name; a client that just created a terrarium should not need
        // a second call to render it. An administrator's creation is unowned, so the owner-scoped read cannot find
        // it — that path reads unscoped, exactly as the administrator's next detail request will.
        var created = (mayReachAnyAccount
                ? await store.FindAnyAsync(terrarium.Id, cancellationToken)
                : await store.FindOwnedAsync(terrarium.Id, ownerUserId, cancellationToken))
            ?? terrarium;

        return TerrariumOutcome.Found(
            Summarise(created, new Dictionary<Guid, TerrariumActivity>(), mayReachAnyAccount),
            created.RowVersion,
            deviceSecret);
    }

    /// <summary>
    /// Updates the mutable fields of a terrarium (FR-03). Concurrency is the caller's responsibility: the request
    /// must carry the <c>rowversion</c> it read, and a row that has moved on since is refused as
    /// <c>precondition_failed</c> rather than overwritten.
    /// </summary>
    /// <param name="terrariumId">Terrarium to update.</param>
    /// <param name="request">The fields to change; omitted members are left alone.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="expectedRowVersion">
    /// The concurrency token the caller holds. An empty array means <c>If-Match: *</c> — existence is enough and
    /// the token is not compared.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account write permission, which may edit another account's enclosure
    /// (TASKS 2026-10-14, item 5). Such an edit is recorded in the audit trail; an owner's own edit is not.
    /// </param>
    /// <param name="actor">Request provenance for the audit row, when one is written.</param>
    public async Task<TerrariumOutcome> UpdateAsync(
        Guid terrariumId,
        UpdateTerrariumRequest request,
        Guid ownerUserId,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false,
        AuditActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);

        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        // Checked before validating the body: a stale client is told to reload, not handed a validation report
        // about a version of the terrarium it is not looking at.
        if (expectedRowVersion.Length > 0
            && (terrarium.RowVersion is null
                || !expectedRowVersion.AsSpan().SequenceEqual(terrarium.RowVersion)))
        {
            return TerrariumOutcome.PreconditionFailed();
        }

        var crossAccount = mayReachAnyAccount && terrarium.UserId != ownerUserId;

        // Assignment is the change that must always reach the trail: it moves an enclosure — and the board in it —
        // from one account to another, which is exactly what a later reader will ask about.
        var ownerChange = request.OwnerUserId is { } candidateOwner && candidateOwner != terrarium.UserId;

        var beforeJson = crossAccount || ownerChange ? DescribeTerrarium(terrarium) : null;

        var violations = new List<IdentityViolation>();

        var name = Trimmed(request.Name);
        if (request.Name is not null)
        {
            if (name is null)
            {
                violations.Add(new IdentityViolation("name", "terrarium_name_required", "A name is required."));
            }
            else if (name.Length > NameMaxLength)
            {
                violations.Add(new IdentityViolation(
                    "name",
                    "terrarium_name_too_long",
                    $"A name may be at most {NameMaxLength} characters."));
            }
        }

        if (Trimmed(request.Location) is { Length: > LocationMaxLength })
        {
            violations.Add(new IdentityViolation(
                "location",
                "terrarium_location_too_long",
                $"A location may be at most {LocationMaxLength} characters."));
        }

        if (Trimmed(request.Description) is { Length: > DescriptionMaxLength })
        {
            violations.Add(new IdentityViolation(
                "description",
                "terrarium_description_too_long",
                $"A description may be at most {DescriptionMaxLength} characters."));
        }

        var timeZoneId = Trimmed(request.TimeZoneId);

        if (request.TimeZoneId is not null)
        {
            // Same catalogue as the create path, and for the same reason (TASKS 2026-10-14, item 11).
            if (await Reference.ReferenceService.FindAsync(timeZones, timeZoneId, cancellationToken) is not { } zone)
            {
                violations.Add(new IdentityViolation(
                    "timeZoneId",
                    "terrarium_timezone_invalid",
                    $"'{request.TimeZoneId}' is not one of the catalogue's time zones; read the list from "
                    + "GET /api/v1/time-zones."));
            }
            else
            {
                timeZoneId = zone.IanaId;
            }
        }

        if (request.SpeciesProfileId is { } profileId
            && profileId != Guid.Empty
            && !await store.SpeciesProfileExistsAsync(profileId, cancellationToken))
        {
            violations.Add(new IdentityViolation(
                "speciesProfileId",
                "species_profile_not_found",
                "No such species profile."));
        }

        if (request.OwnerUserId is { } newOwnerUserId)
        {
            if (!mayReachAnyAccount)
            {
                violations.Add(new IdentityViolation(
                    "ownerUserId",
                    "owner_change_forbidden",
                    "Only an administrator may give an enclosure to another account."));
            }
            else if (!await store.UserExistsAsync(newOwnerUserId, cancellationToken))
            {
                violations.Add(new IdentityViolation(
                    "ownerUserId",
                    "account_not_found",
                    "No account with that id exists."));
            }
        }

        if (violations.Count > 0)
        {
            return TerrariumOutcome.Invalid(violations);
        }

        if (name is not null)
        {
            terrarium.Name = name;
        }

        if (request.SpeciesProfileId is { } newProfileId)
        {
            // The all-zero GUID is this record's "empty": it detaches the profile so the enclosure can be set up
            // before its species is known (2026-10-25). Null still means "leave the current one alone", which is
            // what every other omitted member of a patch means.
            var resolvedProfileId = newProfileId == Guid.Empty ? (Guid?)null : newProfileId;

            if (terrarium.SpeciesProfileId != resolvedProfileId)
            {
                terrarium.SpeciesProfileId = resolvedProfileId;

                // The loaded navigation is dropped on purpose: it still points at the profile the enclosure no
                // longer uses, and an already-loaded navigation is not overwritten by the re-read's `Include`, so
                // the answer would name the previous species (BUG-20).
                terrarium.SpeciesProfile = null;
            }
        }

        // Present means "set this", and an empty string is how a client clears a free-text field — the one thing
        // a record bound from JSON cannot express as a null.
        if (request.Location is not null)
        {
            terrarium.Location = Trimmed(request.Location);
        }

        if (request.Description is not null)
        {
            terrarium.Description = Trimmed(request.Description);
        }

        if (timeZoneId is not null)
        {
            terrarium.TimeZoneId = timeZoneId;
        }

        if (request.OwnerUserId is { } assignedOwner)
        {
            terrarium.UserId = assignedOwner;

            // The board follows the enclosure. It stays where it is — the reading history and the DI-04 slot are
            // untouched — but the new keeper's fleet has to list it, or the enclosure would show a board nobody can
            // configure.
            if (terrarium.Device is { } boundDevice)
            {
                boundDevice.UserId = assignedOwner;
            }
        }

        terrarium.UpdatedAt = clock.UtcNow;

        // Stages the trail entry for a cross-account edit or an ownership change. It goes in before the commit for
        // the same reason every audited change does: a change that commits without its entry reads as "nothing
        // happened" (BR-18.4). One row per request — an assignment that also renames is still one act.
        if (ownerChange)
        {
            store.AddAuditEntry(AuditLog.ForTerrarium(
                AuditAction.TerrariumOwnerChanged,
                terrarium.Id,
                ownerUserId,
                actor ?? AuditActor.Unknown,
                clock.UtcNow,
                beforeJson: beforeJson,
                afterJson: DescribeTerrarium(terrarium)));
        }
        else if (crossAccount)
        {
            store.AddAuditEntry(AuditLog.ForTerrarium(
                AuditAction.TerrariumUpdated,
                terrarium.Id,
                ownerUserId,
                actor ?? AuditActor.Unknown,
                clock.UtcNow,
                beforeJson: beforeJson,
                afterJson: DescribeTerrarium(terrarium)));
        }

        if (!await store.TrySaveChangesAsync(cancellationToken))
        {
            return TerrariumOutcome.PreconditionFailed();
        }

        // Re-read for the profile name, and to pick up the rowversion the update generated so the response's ETag
        // is the one the next request must send. Read without the owner scope, because an administrator's edit of
        // someone else's enclosure has to come back with its new token too.
        var updated = await store.FindAnyAsync(terrariumId, cancellationToken) ?? terrarium;
        var activity = await store.SummariseActivityAsync([terrariumId], cancellationToken);

        // The answer names the owner on the same terms as the detail read: a cross-account caller is the one who
        // learns whose enclosure it now is, and an assignment that answers without it would leave the administrator's
        // screen saying the enclosure belongs to nobody (2026-10-24).
        var owners = mayReachAnyAccount && updated.UserId is { } updatedOwnerId
            ? await store.AccountEmailsAsync([updatedOwnerId], cancellationToken)
            : EmptyOwners;

        return TerrariumOutcome.Found(
            Summarise(updated, activity, mayReachAnyAccount, OwnerEmailOf(updated, owners)),
            updated.RowVersion);
    }

    /// <summary>
    /// Soft-deletes a terrarium (FR-03). Its readings, alerts and summaries survive for audit; only the terrarium
    /// disappears from reads, because a query filter on the entity keeps it out of every query.
    /// </summary>
    /// <param name="terrariumId">Terrarium to remove.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="allowUnboundDevice">
    /// Required when a live device is bound. The name counts the device, not the terrarium: it is the caller's
    /// explicit permission to detach a board that is still reporting, which would otherwise keep publishing into
    /// an enclosure nobody can see.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">
    /// True when the caller holds the cross-account write permission, which may delete another account's enclosure
    /// (TASKS 2026-10-14, item 5). Such a delete is recorded in the audit trail; an owner's own delete is not.
    /// </param>
    /// <param name="actor">Request provenance for the audit row, when one is written.</param>
    public async Task<TerrariumOutcome> DeleteAsync(
        Guid terrariumId,
        Guid ownerUserId,
        bool allowUnboundDevice,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false,
        AuditActor? actor = null)
    {
        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        // The row's state as it was, captured now because the very next lines overwrite it, and only when the
        // delete is a cross-account one: that is the change this route gained the ability to make.
        var crossAccount = mayReachAnyAccount && terrarium.UserId != ownerUserId;
        var beforeJson = crossAccount ? DescribeTerrarium(terrarium) : null;

        // A revoked device has already released the slot (the DI-04 filtered index excludes it), so it does not
        // make the caller ask twice to remove an enclosure it no longer reports into.
        var bound = terrarium.Device is { } device && device.Status != DeviceStatus.Revoked ? device : null;

        if (bound is not null && !allowUnboundDevice)
        {
            return TerrariumOutcome.DeviceBound();
        }

        if (bound is not null)
        {
            // Back to Provisioning, not Revoked: the board is fine, it just has no enclosure. Credentials and the
            // owner are kept, so the same account can still rotate or revoke it and re-register it later; ingest
            // refuses an unbound device, so it stops producing data immediately either way.
            bound.TerrariumId = null;
            bound.Status = DeviceStatus.Provisioning;
            bound.ProvisionedAt = null;
            bound.ClaimCode = null;
            bound.ClaimCodeExpiresAt = null;
        }

        // The state column is what marks the delete; the timestamp beside it only records when. See
        // Terrarium.Status for why the two are not redundant.
        terrarium.Status = TerrariumStatus.Deleted;
        terrarium.DeletedAt = clock.UtcNow;

        // Recorded only for the administrator acting on an account's enclosure: that is the change this route
        // gained the ability to make, and the one the trail has to be able to answer for (TASKS 2026-10-14,
        // item 5). A keeper deleting their own terrarium is the ordinary use, unchanged.
        if (crossAccount)
        {
            store.AddAuditEntry(AuditLog.ForTerrarium(
                AuditAction.TerrariumDeleted,
                terrarium.Id,
                ownerUserId,
                actor ?? AuditActor.Unknown,
                clock.UtcNow,
                beforeJson: beforeJson,
                afterJson: DescribeTerrarium(terrarium)));
        }

        await store.SaveChangesAsync(cancellationToken);

        return TerrariumOutcome.Deleted();
    }

    /// <summary>
    /// The newest reading per metric, with the band each is judged against and the device state behind them.
    /// </summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> LatestReadingsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        var samples = await store.RecentSamplesAsync(terrariumId, LatestSampleLookback, cancellationToken);
        var lastSampleAt = samples.Count > 0 ? samples[0].RecordedAt : (DateTimeOffset?)null;

        var metrics = new List<LatestMetric>();
        var reported = new HashSet<MetricCode>();

        // Samples arrive newest first, so the first reading seen for a metric is that metric's latest — which is
        // also how a metric the newest sample did not carry (light after dark) still gets its own card.
        foreach (var sample in samples)
        {
            foreach (var reading in sample.Readings)
            {
                if (!reported.Add(reading.Metric) || !Definitions.TryGetValue(reading.Metric, out var definition))
                {
                    continue;
                }

                metrics.Add(DescribeMetric(terrarium, sample, reading, definition));
            }
        }

        // Ordered by metric key so the cards keep their places between polls.
        metrics.Sort((left, right) => string.CompareOrdinal(left.Code, right.Code));

        return TerrariumOutcome.Snapshot(new LatestReadings(
            terrariumId,
            lastSampleAt,
            terrarium.Device is null ? null : DescribeDevice(terrarium.Device),
            metrics));
    }

    /// <summary>A bucketed history series for one metric. Gaps are null points, never interpolated (BR-09.5).</summary>
    /// <param name="terrariumId">Terrarium to read.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="metric">Metric whose series is wanted.</param>
    /// <param name="fromUtc">Start of the window.</param>
    /// <param name="toUtc">End of the window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> ReadingsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        if (!RangeQueryRules.TryValidateWindow(
                fromUtc,
                toUtc,
                RangeQueryRules.MaxChartWidth,
                "30 days",
                out var code,
                out var message))
        {
            return TerrariumOutcome.Failed(code!, message!);
        }

        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var samples = await store.SamplesInRangeAsync(terrariumId, metric, fromUtc, toUtc, cancellationToken);

        var bucket = RangeQueryRules.ChooseBucket(toUtc - fromUtc);

        // Raw is a point per sample, so a device sampling faster than the raw window allows could blow the
        // documented 720-point budget (NFR-02). It degrades to five-minute buckets rather than returning a series
        // the client would have to truncate.
        if (bucket == ReadingBucket.Raw && samples.Count > RangeQueryRules.MaxBucketPoints)
        {
            bucket = ReadingBucket.FiveMinutes;
        }

        var points = bucket == ReadingBucket.Raw
            ? RawPoints(samples, metric)
            : BucketedPoints(samples, metric, fromUtc, toUtc, RangeQueryRules.BucketSize(bucket)!.Value);

        var definition = Definitions[metric];

        return TerrariumOutcome.Charted(new ReadingSeries(
            definition.ApiKey,
            definition.Unit,
            RangeQueryRules.BucketName(bucket),
            fromUtc,
            toUtc,
            points,
            // Alert overlays need the evaluator's rows (BR-09.3, M3). Empty is the honest answer until then: an
            // absent overlay says "no alerts recorded", it does not say "no excursions happened".
            []));
    }

    /// <summary>Expected versus received samples for a window, so a chart can be labelled with its own coverage.</summary>
    /// <param name="terrariumId">Terrarium to measure.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium unless they are an administrator.</param>
    /// <param name="fromUtc">Start of the window.</param>
    /// <param name="toUtc">End of the window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation (TASKS 2026-10-14, item 5), which since 2026-10-17 is a permission rather than a role.</param>
    public async Task<TerrariumOutcome> CoverageAsync(
        Guid terrariumId,
        Guid ownerUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken,
        bool mayReachAnyAccount = false)
    {
        if (!RangeQueryRules.TryValidateWindow(
                fromUtc,
                toUtc,
                RangeQueryRules.MaxCoverageWidth,
                "90 days",
                out var code,
                out var message))
        {
            return TerrariumOutcome.Failed(code!, message!);
        }

        var located = await LocateAsync(terrariumId, ownerUserId, mayReachAnyAccount, cancellationToken);

        if (located.Refusal is { } refusal)
        {
            return refusal;
        }

        var terrarium = located.Terrarium!;

        if (terrarium.Device is null)
        {
            // Coverage is a statement about a device's reporting, so there is no answer to give without one.
            return TerrariumOutcome.Failed("device_not_bound", "No device is bound to this terrarium.");
        }

        var interval = Math.Max(1, terrarium.Device.SamplingIntervalSec);
        var expected = (int)Math.Floor((toUtc - fromUtc).TotalSeconds / interval);
        var received = await store.CountSamplesAsync(terrariumId, fromUtc, toUtc, cancellationToken);

        var coveragePct = expected <= 0
            ? 100m
            : Math.Min(100m, Math.Round(received * 100m / expected, 2));

        return TerrariumOutcome.Measured(new CoverageReport(
            terrariumId,
            fromUtc,
            toUtc,
            interval,
            expected,
            received,
            coveragePct));
    }

    /// <summary>One point per stored sample, in time order.</summary>
    private static IReadOnlyList<ReadingPoint> RawPoints(IReadOnlyList<TelemetrySample> samples, MetricCode metric)
    {
        var points = new List<ReadingPoint>(samples.Count);

        foreach (var sample in samples)
        {
            if (FindReading(sample, metric) is not { } reading)
            {
                continue;
            }

            points.Add(new ReadingPoint(sample.RecordedAt, reading.Value, reading.Value, reading.Value, 1));
        }

        return points;
    }

    /// <summary>
    /// Bucket-aligned aggregates covering the whole window. Every bucket is emitted, so an interval with no data
    /// becomes a null point with a zero count and the chart draws a hole rather than a straight line (BR-09.5).
    /// </summary>
    private static IReadOnlyList<ReadingPoint> BucketedPoints(
        IReadOnlyList<TelemetrySample> samples,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        TimeSpan size)
    {
        var aggregate = new Dictionary<long, (decimal Min, decimal Max, decimal Sum, int Count)>();

        foreach (var sample in samples)
        {
            if (FindReading(sample, metric) is not { } reading)
            {
                continue;
            }

            var key = FloorToBucket(sample.RecordedAt, size).UtcTicks;

            aggregate[key] = aggregate.TryGetValue(key, out var current)
                ? (Math.Min(current.Min, reading.Value),
                   Math.Max(current.Max, reading.Value),
                   current.Sum + reading.Value,
                   current.Count + 1)
                : (reading.Value, reading.Value, reading.Value, 1);
        }

        var points = new List<ReadingPoint>();
        var start = FloorToBucket(fromUtc, size);

        // Bucket starts sit on a global grid, so an unaligned "from" can place the first start just before the
        // window and add a bucket to the count — enough to turn a 30-day hourly series into 721 points. The budget
        // is a hard ceiling (NFR-02, TC-I-10), so the series stops at the last bucket that fits.
        var budgetEnd = start.AddTicks(size.Ticks * RangeQueryRules.MaxBucketPoints);

        // Half-open window: 30 days of hourly buckets is exactly 720 points, the documented ceiling.
        for (var bucketStart = start; bucketStart < toUtc && bucketStart < budgetEnd; bucketStart = bucketStart.Add(size))
        {
            points.Add(aggregate.TryGetValue(bucketStart.UtcTicks, out var agg)
                ? new ReadingPoint(bucketStart, agg.Min, agg.Max, Math.Round(agg.Sum / agg.Count, 3), agg.Count)
                : new ReadingPoint(bucketStart, null, null, null, 0));
        }

        return points;
    }

    private static DateTimeOffset FloorToBucket(DateTimeOffset instant, TimeSpan size) =>
        new(instant.UtcTicks - (instant.UtcTicks % size.Ticks), TimeSpan.Zero);

    private static MetricReading? FindReading(TelemetrySample sample, MetricCode metric) =>
        sample.Readings.FirstOrDefault(reading => reading.Metric == metric);

    private LatestMetric DescribeMetric(
        Terrarium terrarium,
        TelemetrySample sample,
        MetricReading reading,
        MetricDefinition definition)
    {
        var band = EffectiveBand(terrarium, reading.Metric, sample.RecordedAt);

        return new LatestMetric(
            definition.ApiKey,
            reading.Value,
            definition.Unit,
            sample.RecordedAt,
            StatusFor(reading, sample, band, terrarium.Device),
            (int)sample.QualityFlags,
            band is null ? null : new MetricBand(band.TargetMin, band.TargetMax));
    }

    /// <summary>Projects one resolved band onto the wire shape the threshold editor reads.</summary>
    private static EffectiveThresholdView DescribeThreshold(
        EffectiveThreshold effective,
        MetricDefinition definition) => new(
        definition.ApiKey,
        definition.Unit,
        ThresholdNames.Phase(effective.Phase),
        ThresholdNames.Source(effective.Source),
        effective.Band.TargetMin,
        effective.Band.TargetMax,
        effective.Band.CriticalMin,
        effective.Band.CriticalMax,
        effective.Band.DwellWarnMinutes,
        effective.Band.DwellCritMinutes,
        effective.Band.RecoveryMargin);

    /// <summary>The wire shape of the effective set, resolved at one instant.</summary>
    private TerrariumThresholds DescribeThresholds(Terrarium terrarium, DateTimeOffset capturedAt) =>
        new(terrarium.Id, capturedAt, terrarium.TimeZoneId, ResolveAll(terrarium, capturedAt));

    /// <summary>
    /// Every metric that resolves to a band right now, ordered by metric key so the editor's rows, the wire response
    /// and the snapshot's JSON all keep one order.
    /// </summary>
    private List<EffectiveThresholdView> ResolveAll(Terrarium terrarium, DateTimeOffset capturedAt)
    {
        var entries = new List<EffectiveThresholdView>();

        foreach (var definition in MetricDictionary.All)
        {
            if (ResolveEffective(terrarium, definition.Code, capturedAt) is { } effective)
            {
                entries.Add(DescribeThreshold(effective, definition));
            }
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Metric, right.Metric));

        return entries;
    }

    /// <summary>One validated band, ready to be applied to the aggregate.</summary>
    private readonly record struct OverrideCandidate(
        MetricCode Metric,
        ThresholdPhase Phase,
        ThresholdBand Band,
        string? Note);

    /// <summary>
    /// Validates one entry of the write request. Returns the candidate band when the entry is usable, or null after
    /// appending every field-level violation it found, so one bad entry does not hide the problems in the next.
    /// </summary>
    private static OverrideCandidate? ParseOverride(
        ThresholdOverrideInput input,
        int index,
        List<IdentityViolation> violations)
    {
        var label = $"overrides[{index}]";
        var usable = true;

        if (!MetricDictionary.TryParseApiKey(input.Metric, out var metric))
        {
            violations.Add(new IdentityViolation(
                $"{label}.metric",
                "metric_invalid",
                $"'{input.Metric}' is not a metric key: {MetricKeys}."));
            usable = false;
        }

        if (!ThresholdNames.TryParsePhase(input.Phase, out var phase))
        {
            violations.Add(new IdentityViolation(
                $"{label}.phase",
                "threshold_phase_invalid",
                $"'{input.Phase}' is not a phase: any, day or night."));
            usable = false;
        }

        if (input.TargetMin is null)
        {
            violations.Add(new IdentityViolation(
                $"{label}.targetMin",
                "threshold_target_required",
                "targetMin is required."));
            usable = false;
        }

        if (input.TargetMax is null)
        {
            violations.Add(new IdentityViolation(
                $"{label}.targetMax",
                "threshold_target_required",
                "targetMax is required."));
            usable = false;
        }

        if (input.Note is { Length: > NoteMaxLength })
        {
            violations.Add(new IdentityViolation(
                $"{label}.note",
                "threshold_note_too_long",
                $"A note may be at most {NoteMaxLength} characters."));
            usable = false;
        }

        if (!usable)
        {
            return null;
        }

        var band = new ThresholdBand
        {
            Metric = metric,
            Phase = phase,
            TargetMin = input.TargetMin!.Value,
            TargetMax = input.TargetMax!.Value,
            CriticalMin = input.CriticalMin,
            CriticalMax = input.CriticalMax,
            DwellWarnMinutes = input.DwellWarnMinutes ?? DefaultDwellWarnMinutes,
            DwellCritMinutes = input.DwellCritMinutes ?? DefaultDwellCritMinutes,
            RecoveryMargin = input.RecoveryMargin ?? DefaultRecoveryMargin,
            Enabled = input.Enabled ?? true,
        };

        // The same rules the database CHECK constraints enforce, reported field by field so an editor can mark the
        // input that is wrong instead of showing one opaque refusal.
        var problems = band.Validate();

        foreach (var violation in problems)
        {
            violations.Add(new IdentityViolation($"{label}.{violation.Field}", violation.Code, violation.Message));
        }

        return problems.Count == 0 ? new OverrideCandidate(metric, phase, band, input.Note) : null;
    }

    /// <summary>
    /// Applies one validated band to the aggregate, returning true when it changed anything. A row already holding
    /// exactly these values is left untouched, so a redundant save is not recorded as a change.
    /// </summary>
    /// <remarks>
    /// A new row goes through <see cref="ITerrariumStore.AddThresholdOverride"/> rather than onto the collection
    /// directly, because the adapter has to mark it an insert — an entity discovered in a tracked navigation with a
    /// client-assigned key is a candidate for an update, not an insert — and the adapter's own fix-up is what puts it
    /// on the collection the snapshot is resolved from.
    /// </remarks>
    private bool ApplyOverride(
        Terrarium terrarium,
        OverrideCandidate candidate,
        Guid ownerUserId,
        DateTimeOffset now)
    {
        var band = candidate.Band;
        var existing = terrarium.ThresholdOverrides.FirstOrDefault(
            row => row.Metric == candidate.Metric && row.Phase == candidate.Phase);

        if (existing is null)
        {
            store.AddThresholdOverride(new ThresholdOverride
            {
                TerrariumId = terrarium.Id,
                Metric = candidate.Metric,
                Phase = candidate.Phase,
                TargetMin = band.TargetMin,
                TargetMax = band.TargetMax,
                CriticalMin = band.CriticalMin,
                CriticalMax = band.CriticalMax,
                DwellWarnMinutes = band.DwellWarnMinutes,
                DwellCritMinutes = band.DwellCritMinutes,
                RecoveryMargin = band.RecoveryMargin,
                Enabled = band.Enabled,
                Note = candidate.Note,
                CreatedByUserId = ownerUserId,
                CreatedAt = now,
            });

            return true;
        }

        if (existing.TargetMin == band.TargetMin
            && existing.TargetMax == band.TargetMax
            && existing.CriticalMin == band.CriticalMin
            && existing.CriticalMax == band.CriticalMax
            && existing.DwellWarnMinutes == band.DwellWarnMinutes
            && existing.DwellCritMinutes == band.DwellCritMinutes
            && existing.RecoveryMargin == band.RecoveryMargin
            && existing.Enabled == band.Enabled
            && existing.Note == candidate.Note)
        {
            return false;
        }

        existing.TargetMin = band.TargetMin;
        existing.TargetMax = band.TargetMax;
        existing.CriticalMin = band.CriticalMin;
        existing.CriticalMax = band.CriticalMax;
        existing.DwellWarnMinutes = band.DwellWarnMinutes;
        existing.DwellCritMinutes = band.DwellCritMinutes;
        existing.RecoveryMargin = band.RecoveryMargin;
        existing.Enabled = band.Enabled;
        existing.Note = candidate.Note;

        return true;
    }

    /// <summary>
    /// Writes the snapshot of the set now in force and commits, in one unit of work with the override change that
    /// caused it (UC-03 A4). The set is resolved <i>after</i> the change is applied, so the row records the band the
    /// next sample will be judged against rather than the band that just stopped being used — which is the whole
    /// point of keeping the snapshot at all.
    /// </summary>
    private async Task SaveWithSnapshotAsync(
        Terrarium terrarium,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken)
    {
        var json = SerializeEffectiveSet(ResolveAll(terrarium, capturedAt));

        store.AddThresholdSnapshot(new ThresholdSnapshot
        {
            TerrariumId = terrarium.Id,
            CapturedAtUtc = capturedAt,
            EffectiveThresholdsJson = json,
            ThresholdVersionHash = HashOf(json),
        });

        await store.SaveChangesAsync(cancellationToken);
    }

    private static string SerializeEffectiveSet(IReadOnlyList<EffectiveThresholdView> entries) =>
        JsonSerializer.Serialize(entries, SnapshotJsonOptions);

    /// <summary>SHA-256 of the snapshot content as lower-case hex — the 64 characters the column holds.</summary>
    private static string HashOf(string json) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));

    /// <summary>The metric keys a client may send, for a message that tells it what to send instead.</summary>
    private static string MetricKeys =>
        string.Join(", ", MetricDictionary.All.Select(definition => definition.ApiKey));

    /// <summary>
    /// Classifies one reading. Instantaneous only: a value outside the band is reported as
    /// <see cref="ReadingStatus.OutOfRange"/> / <see cref="ReadingStatus.Critical"/> the moment it is read, while the
    /// dwell time that turns an excursion into an alert stays with the evaluator (M3).
    /// </summary>
    private static string StatusFor(
        MetricReading reading,
        TelemetrySample sample,
        ThresholdBand? band,
        Device? device)
    {
        // Quality first. A faulted or physically impossible value is unusable, and colouring it "out of range"
        // would send the keeper to adjust a lamp instead of a probe (BR-11.1).
        if (!QualityRules.IsEvaluable(sample.QualityFlags)
            || !MetricDictionary.IsPlausible(reading.Metric, reading.Value))
        {
            return ReadingStatus.Unavailable;
        }

        if (device is { Status: DeviceStatus.Maintenance })
        {
            return ReadingStatus.Maintenance;
        }

        if (band is null)
        {
            // Nothing configured judges this metric, so there is no band to be outside of.
            return ReadingStatus.InRange;
        }

        return band.IsCritical(reading.Value) ? ReadingStatus.Critical
            : band.IsOutOfBand(reading.Value) ? ReadingStatus.OutOfRange
            : ReadingStatus.InRange;
    }

    /// <summary>
    /// The band in force for a metric at an instant, resolved by the shared rule: a per-terrarium override wins
    /// over the profile band, and inside a layer the phase decides (BR-10.3, BR-11.2).
    /// </summary>
    private ThresholdBand? EffectiveBand(Terrarium terrarium, MetricCode metric, DateTimeOffset atUtc) =>
        ResolveEffective(terrarium, metric, atUtc)?.Band;

    /// <summary>
    /// The same rule with the provenance attached, which is what the <c>effectiveThresholds</c> endpoint reports and
    /// what makes the band a card shows and the band the editor lists the same band (BR-10.3).
    /// </summary>
    private EffectiveThreshold? ResolveEffective(Terrarium terrarium, MetricCode metric, DateTimeOffset atUtc)
    {
        var profile = terrarium.SpeciesProfile;
        var local = clock.InZone(atUtc, terrarium.TimeZoneId);

        // A profile-less terrarium has no photoperiod, so the schedule defaults to "always day" rather than to a
        // window nobody configured. That is a real state since 2026-10-25 — an enclosure may be created before its
        // species is chosen — and the price is stated: with no profile bands and no overrides, the resolver finds
        // nothing to judge against, so no threshold alert can open until a profile is assigned.
        return ThresholdResolver.Resolve(
            metric,
            terrarium.ThresholdOverrides,
            profile?.Thresholds ?? [],
            TimeOnly.FromDateTime(local.DateTime),
            profile?.LightsOnLocalTime ?? TimeOnly.MinValue,
            profile?.PhotoperiodHours ?? 24m);
    }

    private DeviceSummary DescribeDevice(Device device) => new(
        device.PublicId,
        device.DeviceName,
        DeriveStatus(device),
        string.IsNullOrEmpty(device.FirmwareVersion) ? null : device.FirmwareVersion,
        device.LastSeenAt,
        device.SamplingIntervalSec,
        device.SignalStrengthDbm,
        device.BatteryPct,
        device.UptimeSeconds);

    /// <summary>
    /// The online/offline state the dashboards show. The stored status is authoritative for the states a device
    /// cannot leave on its own (revoked, provisioning, maintenance); otherwise silence is the signal, so a node that
    /// stops publishing reads offline after three missed intervals (FR-07 / BR-07.2). This is the *read* half of the
    /// rule the silence watchdog writes: both go through <see cref="DeviceSilencePolicy"/>, so the badge a client
    /// renders cannot disagree with the alert the watchdog opened (§02-design/03 §4.3).
    /// </summary>
    private string DeriveStatus(Device device)
    {
        if (device.Status is DeviceStatus.Revoked or DeviceStatus.Provisioning or DeviceStatus.Maintenance)
        {
            return DeviceStatusNames.Of(device.Status);
        }

        if (device.LastSeenAt is not { } lastSeenAt)
        {
            return DeviceStatusNames.Of(DeviceStatus.Offline);
        }

        return DeviceSilencePolicy.IsSilent(
            clock.UtcNow - lastSeenAt,
            device.SamplingIntervalSec,
            settings.SilentAfterIntervals)
            ? DeviceStatusNames.Of(DeviceStatus.Offline)
            : DeviceStatusNames.Of(DeviceStatus.Online);
    }

    /// <summary>
    /// Shapes one terrarium for the wire. <paramref name="includeOwnerUserId"/> is set for the administrator, whose
    /// list can hold rows belonging to accounts the caller did not name — a list of those without the owner beside
    /// them is not usable (TASKS 2026-10-14, item 5).
    /// </summary>
    /// <param name="terrarium">Row to shape.</param>
    /// <param name="activity">Newest sample and open-alert count per terrarium.</param>
    /// <param name="includeOwnerUserId">True for the administrator's answers.</param>
    /// <param name="ownerEmail">
    /// The owning account's email, resolved only on the cross-account path; null for an ordinary keeper, whose rows
    /// are all their own.
    /// </param>
    private TerrariumSummary Summarise(
        Terrarium terrarium,
        IReadOnlyDictionary<Guid, TerrariumActivity> activity,
        bool includeOwnerUserId = false,
        string? ownerEmail = null)
    {
        var stats = activity.TryGetValue(terrarium.Id, out var found) ? found : TerrariumActivity.None;

        return new TerrariumSummary(
            terrarium.Id,
            terrarium.Name,
            terrarium.SpeciesProfileId,
            terrarium.SpeciesProfile?.Name,
            terrarium.Location,
            terrarium.Description,
            terrarium.TimeZoneId,
            terrarium.CreatedAt,
            terrarium.UpdatedAt,
            terrarium.Device is null ? null : DescribeDevice(terrarium.Device),
            stats.LatestSampleAt,
            stats.OpenAlertCount)
        {
            OwnerUserId = includeOwnerUserId ? terrarium.UserId : null,
            OwnerEmail = includeOwnerUserId ? ownerEmail : null,
        };
    }

    /// <summary>No owner lookup at all — the ordinary keeper's answers, where every row is theirs.</summary>
    private static readonly IReadOnlyDictionary<Guid, string> EmptyOwners = new Dictionary<Guid, string>();

    /// <summary>The owning account's email, or null for an enclosure nobody owns yet.</summary>
    private static string? OwnerEmailOf(
        Terrarium terrarium,
        IReadOnlyDictionary<Guid, string> owners) =>
        terrarium.UserId is { } ownerId ? owners.GetValueOrDefault(ownerId) : null;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Finds the terrarium a request is about, and says what to answer when the caller may not have it.
    /// </summary>
    /// <remarks>
    /// One scoped query answers the ordinary cases, and a second, unscoped one is consulted only when the first
    /// finds nothing — so the happy path costs exactly what it did before. The second query is what lets the three
    /// refusals be told apart (TASKS 2026-10-14, item 3): absent is <c>404</c>, deleted is <c>410</c>, and another
    /// account's is <c>403</c> for anyone but the administrator (item 5). BR-02.2 chose to answer foreign and
    /// missing identically to stop ids being probed; this reverses that for terrariums, and the decision log records
    /// the reason and the fact that the other resources still answer <c>404</c>.
    /// </remarks>
    /// <param name="terrariumId">Terrarium the request names.</param>
    /// <param name="callerUserId">Authenticated caller.</param>
    /// <param name="mayReachAnyAccount">True when the caller holds the cross-account permission for this operation, so may reach any account's row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The terrarium to serve, or the refusal to answer with — never both, never neither.</returns>
    private async Task<(Terrarium? Terrarium, TerrariumOutcome? Refusal)> LocateAsync(
        Guid terrariumId,
        Guid callerUserId,
        bool mayReachAnyAccount,
        CancellationToken cancellationToken)
    {
        var owned = await store.FindOwnedAsync(terrariumId, callerUserId, cancellationToken);

        if (owned is not null)
        {
            return (owned, null);
        }

        var any = await store.FindAnyAsync(terrariumId, cancellationToken);

        if (any is null)
        {
            return (null, TerrariumOutcome.NotFound());
        }

        if (any.Status == TerrariumStatus.Deleted)
        {
            // A deleted enclosure is not "not yours" and not "never existed": its history is still there, and the
            // caller is told which of the three happened. 410 rather than 404 so a client can tell a typo'd id
            // from an enclosure that was removed.
            return (null, TerrariumOutcome.Gone());
        }

        return mayReachAnyAccount ? (any, null) : (null, TerrariumOutcome.Forbidden());
    }

    /// <summary>
    /// The terrarium state an audit row keeps: the fields a change can move, in the wire's camelCase, so a before
    /// and after pair reads like the responses they were captured beside. Never the whole entity, and never a
    /// child collection — the row records a change, it is not a second copy of the data (§02-design/02 §3.18).
    /// </summary>
    private static string DescribeTerrarium(Terrarium terrarium) => JsonSerializer.Serialize(
        new
        {
            userId = terrarium.UserId,
            name = terrarium.Name,
            speciesProfileId = terrarium.SpeciesProfileId,
            location = terrarium.Location,
            description = terrarium.Description,
            timeZoneId = terrarium.TimeZoneId,
            status = terrarium.Status.ToString(),
            deviceId = terrarium.Device?.PublicId,
        },
        SnapshotJsonOptions);
}
