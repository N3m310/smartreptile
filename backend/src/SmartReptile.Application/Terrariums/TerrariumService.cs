using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Readings;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
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
/// <b>Ownership is a query parameter.</b> A foreign terrarium is <see cref="TerrariumOutcome.NotFound"/>, never
/// <c>forbidden</c>, so ids cannot be probed (BR-02.2).
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
public sealed class TerrariumService(ITerrariumStore store, IClock clock, TerrariumSettings settings)
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

    private static readonly Dictionary<MetricCode, MetricDefinition> Definitions =
        MetricDictionary.All.ToDictionary(definition => definition.Code);

    /// <summary>The caller's terrariums, each with its device state and activity summary.</summary>
    public async Task<TerrariumOutcome> ListAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var terrariums = await store.ListOwnedAsync(ownerUserId, cancellationToken);
        var activity = await store.SummariseActivityAsync([.. terrariums.Select(t => t.Id)], cancellationToken);

        return TerrariumOutcome.Listed([.. terrariums.Select(terrarium => Summarise(terrarium, activity))]);
    }

    /// <summary>One terrarium, or <c>not_found</c> when it is missing, deleted or someone else's.</summary>
    public async Task<TerrariumOutcome> GetAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

        var activity = await store.SummariseActivityAsync([terrariumId], cancellationToken);

        return TerrariumOutcome.Found(Summarise(terrarium, activity), terrarium.RowVersion);
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
    public async Task<bool> IsMemberAsync(
        Guid terrariumId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await store.FindOwnedAsync(terrariumId, userId, cancellationToken) is not null;

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
    public async Task<TerrariumOutcome> EffectiveThresholdsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

        var capturedAt = clock.UtcNow;
        var entries = new List<EffectiveThresholdView>();

        foreach (var definition in MetricDictionary.All)
        {
            if (ResolveEffective(terrarium, definition.Code, capturedAt) is { } effective)
            {
                entries.Add(DescribeThreshold(effective, definition));
            }
        }

        // Ordered by metric key so the editor's rows keep their places between reloads, the same way the latest
        // readings are ordered.
        entries.Sort((left, right) => string.CompareOrdinal(left.Metric, right.Metric));

        return TerrariumOutcome.Resolved(new TerrariumThresholds(
            terrariumId,
            capturedAt,
            terrarium.TimeZoneId,
            entries));
    }

    /// <summary>
    /// Creates a terrarium. Exists in this milestone because every other route needs one to point at: a device
    /// cannot be claimed without a terrarium to bind it to, and a read surface with no way to create its subject
    /// would only ever answer with an empty list.
    /// </summary>
    public async Task<TerrariumOutcome> CreateAsync(
        CreateTerrariumRequest request,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
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
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            violations.Add(new IdentityViolation(
                "timeZoneId",
                "terrarium_timezone_invalid",
                $"'{timeZoneId}' is not a known time zone."));
        }

        if (violations.Count > 0)
        {
            return TerrariumOutcome.Invalid(violations);
        }

        if (!await store.SpeciesProfileExistsAsync(request.SpeciesProfileId, cancellationToken))
        {
            // A 400, not a 404: the profile is part of the body the caller sent, so this is a field the client got
            // wrong rather than a resource someone else owns.
            return TerrariumOutcome.Invalid(
                [new IdentityViolation("speciesProfileId", "species_profile_not_found", "No such species profile.")]);
        }

        var now = clock.UtcNow;
        var terrarium = new Terrarium
        {
            UserId = ownerUserId,
            Name = name,
            SpeciesProfileId = request.SpeciesProfileId,
            Location = location,
            Description = description,
            TimeZoneId = timeZoneId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        store.AddTerrarium(terrarium);
        await store.SaveChangesAsync(cancellationToken);

        // Re-read so the response carries the profile name; a client that just created a terrarium should not need
        // a second call to render it.
        var created = await store.FindOwnedAsync(terrarium.Id, ownerUserId, cancellationToken) ?? terrarium;

        return TerrariumOutcome.Found(Summarise(created, new Dictionary<Guid, TerrariumActivity>()), created.RowVersion);
    }

    /// <summary>
    /// Updates the mutable fields of a terrarium (FR-03). Concurrency is the caller's responsibility: the request
    /// must carry the <c>rowversion</c> it read, and a row that has moved on since is refused as
    /// <c>precondition_failed</c> rather than overwritten.
    /// </summary>
    /// <param name="terrariumId">Terrarium to update.</param>
    /// <param name="request">The fields to change; omitted members are left alone.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium.</param>
    /// <param name="expectedRowVersion">
    /// The concurrency token the caller holds. An empty array means <c>If-Match: *</c> — existence is enough and
    /// the token is not compared.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TerrariumOutcome> UpdateAsync(
        Guid terrariumId,
        UpdateTerrariumRequest request,
        Guid ownerUserId,
        byte[] expectedRowVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(expectedRowVersion);

        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

        // Checked before validating the body: a stale client is told to reload, not handed a validation report
        // about a version of the terrarium it is not looking at.
        if (expectedRowVersion.Length > 0
            && (terrarium.RowVersion is null
                || !expectedRowVersion.AsSpan().SequenceEqual(terrarium.RowVersion)))
        {
            return TerrariumOutcome.PreconditionFailed();
        }

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
        if (request.TimeZoneId is not null
            && (timeZoneId is null || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _)))
        {
            violations.Add(new IdentityViolation(
                "timeZoneId",
                "terrarium_timezone_invalid",
                $"'{request.TimeZoneId}' is not a known time zone."));
        }

        if (request.SpeciesProfileId is { } profileId
            && !await store.SpeciesProfileExistsAsync(profileId, cancellationToken))
        {
            violations.Add(new IdentityViolation(
                "speciesProfileId",
                "species_profile_not_found",
                "No such species profile."));
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
            terrarium.SpeciesProfileId = newProfileId;
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

        terrarium.UpdatedAt = clock.UtcNow;

        if (!await store.TrySaveChangesAsync(cancellationToken))
        {
            return TerrariumOutcome.PreconditionFailed();
        }

        // Re-read for the profile name, and to pick up the rowversion the update generated so the response's ETag
        // is the one the next request must send.
        var updated = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken) ?? terrarium;
        var activity = await store.SummariseActivityAsync([terrariumId], cancellationToken);

        return TerrariumOutcome.Found(Summarise(updated, activity), updated.RowVersion);
    }

    /// <summary>
    /// Soft-deletes a terrarium (FR-03). Its readings, alerts and summaries survive for audit; only the terrarium
    /// disappears from reads, because a query filter on the entity keeps it out of every query.
    /// </summary>
    /// <param name="terrariumId">Terrarium to remove.</param>
    /// <param name="ownerUserId">Authenticated caller, who must own the terrarium.</param>
    /// <param name="allowUnboundDevice">
    /// Required when a live device is bound. The name counts the device, not the terrarium: it is the caller's
    /// explicit permission to detach a board that is still reporting, which would otherwise keep publishing into
    /// an enclosure nobody can see.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TerrariumOutcome> DeleteAsync(
        Guid terrariumId,
        Guid ownerUserId,
        bool allowUnboundDevice,
        CancellationToken cancellationToken)
    {
        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

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

        terrarium.DeletedAt = clock.UtcNow;

        await store.SaveChangesAsync(cancellationToken);

        return TerrariumOutcome.Deleted();
    }

    /// <summary>
    /// The newest reading per metric, with the band each is judged against and the device state behind them.
    /// </summary>
    public async Task<TerrariumOutcome> LatestReadingsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

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
    public async Task<TerrariumOutcome> ReadingsAsync(
        Guid terrariumId,
        Guid ownerUserId,
        MetricCode metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
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

        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
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
    public async Task<TerrariumOutcome> CoverageAsync(
        Guid terrariumId,
        Guid ownerUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
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

        var terrarium = await store.FindOwnedAsync(terrariumId, ownerUserId, cancellationToken);
        if (terrarium is null)
        {
            return TerrariumOutcome.NotFound();
        }

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
        // window nobody configured. Unreachable in practice — SpeciesProfileId is a required foreign key — but
        // stated instead of left to whatever the default happened to be.
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

    private TerrariumSummary Summarise(
        Terrarium terrarium,
        IReadOnlyDictionary<Guid, TerrariumActivity> activity)
    {
        var stats = activity.TryGetValue(terrarium.Id, out var found) ? found : TerrariumActivity.None;

        return new TerrariumSummary(
            terrarium.Id,
            terrarium.Name,
            terrarium.SpeciesProfileId,
            terrarium.SpeciesProfile?.Name ?? string.Empty,
            terrarium.Location,
            terrarium.Description,
            terrarium.TimeZoneId,
            terrarium.CreatedAt,
            terrarium.UpdatedAt,
            terrarium.Device is null ? null : DescribeDevice(terrarium.Device),
            stats.LatestSampleAt,
            stats.OpenAlertCount);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
