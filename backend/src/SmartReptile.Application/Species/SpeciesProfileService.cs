using System.Globalization;
using System.Text.Json;
using SmartReptile.Application.Abstractions;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Identity;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Thresholds;

namespace SmartReptile.Application.Species;

/// <summary>
/// The species-profile catalogue (FR-10, `07-appendices/03` §4.3 — roadmap 3.9). It answers the one question the
/// picker in <c>POST /terrariums</c> could not: which profiles exist, and what are their bands.
/// </summary>
/// <remarks>
/// Five decisions worth defending:
/// <list type="number">
/// <item>
/// <b>A built-in is `403`, a foreign profile is `404`.</b> Both are refusals, but only the first is about a row the
/// caller can see: telling the client <c>builtin_immutable</c> is what lets it offer "duplicate it instead", while a
/// foreign id has to stay indistinguishable from a made-up one (BR-02.2).
/// </item>
/// <item>
/// <b>The band set is replaced, never merged.</b> <c>bands</c> on a patch is the complete set after the change.
/// Merging per field would let a caller edit a band into a state that neither the stored row nor the request
/// described, and a band is validated as a whole — bounding it in halves cannot be validated.
/// </item>
/// <item>
/// <b>Band validation is <see cref="ThresholdBand.Validate"/>, the same call the override write path makes.</b> The
/// rules (ordering, the critical band enclosing the target, dwell, the recovery margin, the metric's plausible
/// range) live in the domain and are enforced again by CHECK constraints, so this service is a third reader of one
/// rule rather than a second copy of it.
/// </item>
/// <item>
/// <b>Deleting is refused while a terrarium is assigned, and soft-deleted terrariums count.</b> Their rows still
/// hold the foreign key, so ignoring them would turn a documented `409` into a constraint violation (DI-10).
/// </item>
/// <item>
/// <b>Every write is audited with its before and after state</b> (BR-10.4). The audit row is staged on the same
/// unit of work as the change, so a profile cannot move without its entry.
/// </item>
/// </list>
/// </remarks>
public sealed class SpeciesProfileService(ISpeciesProfileStore store, IClock clock)
{
    private const int NameMaxLength = 80;
    private const int ScientificNameMaxLength = 120;
    private const int NotesMaxLength = 1000;
    private const int SourceRefMaxLength = 300;
    private const int DefaultDwellWarnMinutes = 5;
    private const int DefaultDwellCritMinutes = 2;
    private const decimal DefaultRecoveryMargin = 0.5m;
    private const decimal DefaultPhotoperiodHours = 12m;
    private const decimal DefaultLightThresholdLux = 1000m;
    private const decimal DefaultMinLightHoursPerDay = 8m;

    private static readonly TimeOnly DefaultLightsOn = new(7, 0);

    /// <summary>
    /// Audit rows carry the view rather than the entity, in the wire's camelCase, so a stored entry reads like the
    /// response it was captured from and needs no second projection to be shown.
    /// </summary>
    private static readonly JsonSerializerOptions AuditJson = JsonSerializerOptions.Web;

    private static string MetricKeys => string.Join(", ", MetricDictionary.All.Select(definition => definition.ApiKey));

    private static string ZoneNames => string.Join(", ", Enum.GetNames<ClimateZone>());

    /// <summary>Built-ins plus the caller's own, each with its bands (BR-10.1, FR-10 A2).</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profiles = await store.ListVisibleAsync(userId, cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Listed([.. profiles.Select(Describe)]);
    }

    /// <summary>Creates a custom profile with its bands.</summary>
    /// <param name="request">Profile fields and bands as the caller sent them.</param>
    /// <param name="userId">Signed-in account, which becomes the owner.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> CreateAsync(
        CreateSpeciesProfileRequest request,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await store.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.Unauthenticated();
        }

        var violations = new List<IdentityViolation>();

        // Every field is checked even after one is refused, so a caller fixing a form learns about all of its
        // problems from one response instead of one per attempt.
        var named = TryName(request.Name, violations, out var name);

        var candidate = ParseFields(
            name,
            request.ScientificName,
            request.ClimateZone,
            request.PhotoperiodHours,
            request.LightsOnLocalTime,
            request.LightThresholdLux,
            request.MinLightHoursPerDay,
            request.Notes,
            violations);

        var bands = ParseBands(request.Bands, violations);

        if (!named || candidate is null || bands is null)
        {
            return SpeciesProfileOutcome.Invalid(violations);
        }

        if (await store.NameTakenAsync(userId, candidate.Name, cancellationToken).ConfigureAwait(false))
        {
            return NameTaken(candidate.Name);
        }

        var now = clock.UtcNow;
        var profile = new SpeciesProfile
        {
            Name = candidate.Name,
            ScientificName = candidate.ScientificName ?? string.Empty,
            ClimateZone = candidate.ClimateZone,
            IsBuiltIn = false,
            CreatedByUserId = userId,
            PhotoperiodHours = candidate.PhotoperiodHours,
            LightsOnLocalTime = candidate.LightsOnLocalTime,
            LightThresholdLux = candidate.LightThresholdLux,
            MinLightHoursPerDay = candidate.MinLightHoursPerDay,
            Notes = candidate.Notes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var band in bands)
        {
            profile.Thresholds.Add(band);
        }

        store.Add(profile);
        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileCreated,
            profile.Id,
            userId,
            actor,
            now,
            afterJson: JsonSerializer.Serialize(Describe(profile), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(profile));
    }

    /// <summary>
    /// Copies a profile the caller may see — a built-in, or one of their own — into a custom profile they own. This
    /// is the only way to tune a seeded profile, because the seeded ones are immutable (FR-10 A2).
    /// </summary>
    /// <param name="profileId">Profile to copy.</param>
    /// <param name="request">Name for the copy, or null to derive one.</param>
    /// <param name="userId">Signed-in account, which becomes the owner.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> DuplicateAsync(
        Guid profileId,
        DuplicateSpeciesProfileRequest request,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        if (!await store.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.Unauthenticated();
        }

        var source = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        // Someone else's profile is not "forbidden", it is invisible: the same answer a made-up id gets.
        if (source is null || (source.CreatedByUserId is { } owner && owner != userId))
        {
            return SpeciesProfileOutcome.NotFound();
        }

        var violations = new List<IdentityViolation>();

        // The derived name is the source's with a suffix rather than nothing: a copy that arrived unnamed would be
        // indistinguishable from the original in the picker, which is the one thing the keeper is choosing between.
        var named = TryName(request.Name ?? $"{source.Name} (copy)", violations, out var name);

        if (!named)
        {
            return SpeciesProfileOutcome.Invalid(violations);
        }

        if (await store.NameTakenAsync(userId, name, cancellationToken).ConfigureAwait(false))
        {
            return NameTaken(name);
        }

        var now = clock.UtcNow;
        var copy = new SpeciesProfile
        {
            Name = name,
            ScientificName = source.ScientificName,
            ClimateZone = source.ClimateZone,
            IsBuiltIn = false,
            CreatedByUserId = userId,
            PhotoperiodHours = source.PhotoperiodHours,
            LightsOnLocalTime = source.LightsOnLocalTime,
            LightThresholdLux = source.LightThresholdLux,
            MinLightHoursPerDay = source.MinLightHoursPerDay,
            Notes = source.Notes,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The bands come across with their provenance. A copy whose numbers lost the citation they were taken from
        // would be a second, quieter way of shipping an unsourced range.
        foreach (var band in source.Thresholds)
        {
            copy.Thresholds.Add(new Threshold
            {
                Metric = band.Metric,
                Phase = band.Phase,
                TargetMin = band.TargetMin,
                TargetMax = band.TargetMax,
                CriticalMin = band.CriticalMin,
                CriticalMax = band.CriticalMax,
                DwellWarnMinutes = band.DwellWarnMinutes,
                DwellCritMinutes = band.DwellCritMinutes,
                RecoveryMargin = band.RecoveryMargin,
                Enabled = band.Enabled,
                SourceRef = band.SourceRef,
                SourceUrl = band.SourceUrl,
            });
        }

        store.Add(copy);
        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileDuplicated,
            copy.Id,
            userId,
            actor,
            now,
            beforeJson: JsonSerializer.Serialize(new { sourceProfileId = source.Id, sourceBuiltIn = source.IsBuiltIn }, AuditJson),
            afterJson: JsonSerializer.Serialize(Describe(copy), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(copy));
    }

    /// <summary>Edits one of the caller's own profiles: fields, the band set, or both.</summary>
    /// <param name="profileId">Profile to change.</param>
    /// <param name="request">Fields to change; omitted members are left alone.</param>
    /// <param name="userId">Signed-in account, which must own the profile.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> UpdateAsync(
        Guid profileId,
        UpdateSpeciesProfileRequest request,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await store.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.Unauthenticated();
        }

        var profile = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return SpeciesProfileOutcome.NotFound();
        }

        if (profile.IsBuiltIn)
        {
            return SpeciesProfileOutcome.BuiltInImmutable();
        }

        if (profile.CreatedByUserId != userId)
        {
            return SpeciesProfileOutcome.NotFound();
        }

        var violations = new List<IdentityViolation>();
        var named = TryName(request.Name ?? profile.Name, violations, out var name);

        // Everything the request omits falls back to what the row already holds, so patch and create validate one
        // set of resolved values rather than two sets of raw ones.
        var candidate = ParseFields(
            name,
            request.ScientificName ?? profile.ScientificName,
            request.ClimateZone ?? profile.ClimateZone.ToString(),
            request.PhotoperiodHours ?? profile.PhotoperiodHours,
            request.LightsOnLocalTime ?? profile.LightsOnLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            request.LightThresholdLux ?? profile.LightThresholdLux,
            request.MinLightHoursPerDay ?? profile.MinLightHoursPerDay,
            request.Notes ?? profile.Notes,
            violations);

        // Null bands means "not sent" and leaves the set alone; an empty list is a caller asking for a profile with
        // no bands, which is refused rather than quietly ignored.
        List<Threshold>? replacement = null;

        if (request.Bands is not null)
        {
            replacement = ParseBands(request.Bands, violations);
        }

        if (!named || candidate is null || (request.Bands is not null && replacement is null))
        {
            return SpeciesProfileOutcome.Invalid(violations);
        }

        // Only a name that actually moves can collide: the row holds its own name, so checking the unchanged one
        // would refuse every edit of every other field.
        if (!string.Equals(candidate.Name, profile.Name, StringComparison.Ordinal)
            && await store.NameTakenAsync(userId, candidate.Name, cancellationToken).ConfigureAwait(false))
        {
            return NameTaken(candidate.Name);
        }

        var before = Describe(profile);
        var now = clock.UtcNow;

        profile.Name = candidate.Name;
        profile.ScientificName = candidate.ScientificName ?? string.Empty;
        profile.ClimateZone = candidate.ClimateZone;
        profile.PhotoperiodHours = candidate.PhotoperiodHours;
        profile.LightsOnLocalTime = candidate.LightsOnLocalTime;
        profile.LightThresholdLux = candidate.LightThresholdLux;
        profile.MinLightHoursPerDay = candidate.MinLightHoursPerDay;
        profile.Notes = candidate.Notes;
        profile.UpdatedAt = now;

        if (replacement is { } incoming)
        {
            store.ReplaceBands(profile, incoming);
        }

        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileUpdated,
            profile.Id,
            userId,
            actor,
            now,
            beforeJson: JsonSerializer.Serialize(before, AuditJson),
            afterJson: JsonSerializer.Serialize(Describe(profile), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(profile));
    }

    /// <summary>Deletes one of the caller's own profiles, unless a terrarium is assigned to it.</summary>
    /// <param name="profileId">Profile to delete.</param>
    /// <param name="userId">Signed-in account, which must own the profile.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> DeleteAsync(
        Guid profileId,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken)
    {
        if (!await store.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.Unauthenticated();
        }

        var profile = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return SpeciesProfileOutcome.NotFound();
        }

        // Checked before ownership on purpose: a built-in has no owner, so the comparison below would report it as
        // someone else's and the client would lose the one answer that tells it to duplicate instead (FR-10 A2).
        if (profile.IsBuiltIn)
        {
            return SpeciesProfileOutcome.BuiltInImmutable();
        }

        if (profile.CreatedByUserId != userId)
        {
            return SpeciesProfileOutcome.NotFound();
        }

        if (await store.IsAssignedAsync(profileId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.InUse();
        }

        var now = clock.UtcNow;
        var before = Describe(profile);

        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileDeleted,
            profile.Id,
            userId,
            actor,
            now,
            beforeJson: JsonSerializer.Serialize(before, AuditJson)));

        store.Remove(profile);

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Deleted();
    }

    private static SpeciesProfileOutcome NameTaken(string name) =>
        SpeciesProfileOutcome.Failed(new SpeciesProfileProblem(
            "profile_name_taken",
            "You already have a profile with that name.",
            [new IdentityViolation("name", "profile_name_taken", $"'{name}' is already the name of one of your profiles.")]));

    /// <summary>Trims and bounds a name, appending the violation when it is unusable.</summary>
    private static bool TryName(string? raw, List<IdentityViolation> violations, out string name)
    {
        name = raw?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            violations.Add(new IdentityViolation("name", "profile_name_required", "A name is required."));

            return false;
        }

        if (name.Length > NameMaxLength)
        {
            violations.Add(new IdentityViolation(
                "name",
                "profile_name_too_long",
                $"A name may be at most {NameMaxLength} characters."));

            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates the profile's own fields — the ones that are not bands — and returns them parsed, or null after
    /// appending every violation so one bad field does not hide the next. Values are the resolved ones: create and
    /// patch differ only in what they fall back to.
    /// </summary>
    private static ProfileCandidate? ParseFields(
        string name,
        string? scientificName,
        string? climateZone,
        decimal? photoperiodHours,
        string? lightsOnLocalTime,
        decimal? lightThresholdLux,
        decimal? minLightHoursPerDay,
        string? notes,
        List<IdentityViolation> violations)
    {
        var usable = true;

        if (!Enum.TryParse<ClimateZone>(climateZone?.Trim(), ignoreCase: true, out var zone))
        {
            violations.Add(new IdentityViolation(
                "climateZone",
                "climate_zone_invalid",
                $"'{climateZone}' is not a climate zone: {ZoneNames}."));
            usable = false;
        }

        var trimmedScientificName = Trimmed(scientificName);
        if (trimmedScientificName is { Length: > ScientificNameMaxLength })
        {
            violations.Add(new IdentityViolation(
                "scientificName",
                "scientific_name_too_long",
                $"A scientific name may be at most {ScientificNameMaxLength} characters."));
            usable = false;
        }

        var trimmedNotes = Trimmed(notes);
        if (trimmedNotes is { Length: > NotesMaxLength })
        {
            violations.Add(new IdentityViolation(
                "notes",
                "profile_notes_too_long",
                $"Notes may be at most {NotesMaxLength} characters."));
            usable = false;
        }

        var photoperiod = photoperiodHours ?? DefaultPhotoperiodHours;
        if (photoperiod <= 0m || photoperiod > 24m)
        {
            violations.Add(new IdentityViolation(
                "photoperiodHours",
                "photoperiod_invalid",
                "A photoperiod must be longer than 0 and at most 24 hours."));
            usable = false;
        }

        // Absent means the documented default on create and the stored value on a patch, so only a value that was
        // actually sent can be refused here.
        var lightsOnText = Trimmed(lightsOnLocalTime);
        TimeOnly lightsOn;

        if (lightsOnText is null)
        {
            lightsOn = DefaultLightsOn;
        }
        else if (!TimeOnly.TryParse(lightsOnText, CultureInfo.InvariantCulture, out lightsOn))
        {
            violations.Add(new IdentityViolation(
                "lightsOnLocalTime",
                "lights_on_invalid",
                $"'{lightsOnLocalTime}' is not a time of day. Use HH:mm, for example 07:00."));
            usable = false;
        }

        var lightThreshold = lightThresholdLux ?? DefaultLightThresholdLux;
        if (lightThreshold < 0m)
        {
            violations.Add(new IdentityViolation(
                "lightThresholdLux",
                "light_threshold_invalid",
                "A light threshold cannot be negative."));
            usable = false;
        }

        var minLightHours = minLightHoursPerDay ?? DefaultMinLightHoursPerDay;
        if (minLightHours < 0m || minLightHours > 24m)
        {
            violations.Add(new IdentityViolation(
                "minLightHoursPerDay",
                "min_light_hours_invalid",
                "Minimum light hours must be between 0 and 24."));
            usable = false;
        }

        return usable
            ? new ProfileCandidate(name, trimmedScientificName, zone, photoperiod, lightsOn, lightThreshold, minLightHours, trimmedNotes)
            : null;
    }

    /// <summary>
    /// Validates the band set: at least one band, no metric and phase twice (the table's unique index), and every
    /// band against <see cref="ThresholdBand.Validate"/>. Field names are addressed by position the way the override
    /// write path addresses its entries, so an editor can mark the row that is wrong.
    /// </summary>
    private static List<Threshold>? ParseBands(
        IReadOnlyList<SpeciesProfileBandInput>? inputs,
        List<IdentityViolation> violations)
    {
        if (inputs is null || inputs.Count == 0)
        {
            violations.Add(new IdentityViolation(
                "bands",
                "profile_bands_required",
                "A profile needs at least one band; a profile without bands cannot judge anything."));

            return null;
        }

        var usable = true;
        var seen = new HashSet<(MetricCode Metric, ThresholdPhase Phase)>();
        var bands = new List<Threshold>(inputs.Count);

        for (var index = 0; index < inputs.Count; index++)
        {
            var band = ParseBand(inputs[index], index, violations);

            if (band is null)
            {
                usable = false;

                continue;
            }

            if (!seen.Add((band.Metric, band.Phase)))
            {
                violations.Add(new IdentityViolation(
                    $"bands[{index}].metric",
                    "threshold_duplicate",
                    "A metric and phase may appear only once: a band is a single row per metric and phase."));
                usable = false;

                continue;
            }

            bands.Add(band);
        }

        return usable ? bands : null;
    }

    /// <summary>Validates one band. Null when it is unusable, with the violations appended.</summary>
    private static Threshold? ParseBand(
        SpeciesProfileBandInput input,
        int index,
        List<IdentityViolation> violations)
    {
        var label = $"bands[{index}]";
        var usable = true;

        if (!MetricDictionary.TryParseApiKey(input.Metric, out var metric))
        {
            violations.Add(new IdentityViolation(
                $"{label}.metric",
                "metric_invalid",
                $"'{input.Metric}' is not a metric key: {MetricKeys}."));
            usable = false;
        }

        if (!Terrariums.ThresholdNames.TryParsePhase(input.Phase, out var phase))
        {
            violations.Add(new IdentityViolation(
                $"{label}.phase",
                "threshold_phase_invalid",
                $"'{input.Phase}' is not a phase: any, day or night."));
            usable = false;
        }

        if (input.TargetMin is null)
        {
            violations.Add(new IdentityViolation($"{label}.targetMin", "threshold_target_required", "targetMin is required."));
            usable = false;
        }

        if (input.TargetMax is null)
        {
            violations.Add(new IdentityViolation($"{label}.targetMax", "threshold_target_required", "targetMax is required."));
            usable = false;
        }

        var sourceRef = Trimmed(input.SourceRef);
        if (sourceRef is { Length: > SourceRefMaxLength })
        {
            violations.Add(new IdentityViolation(
                $"{label}.sourceRef",
                "source_ref_too_long",
                $"A source reference may be at most {SourceRefMaxLength} characters."));
            usable = false;
        }

        if (!usable)
        {
            return null;
        }

        var band = new Threshold
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
            SourceRef = sourceRef,
        };

        // The same rules the CHECK constraints enforce, reported field by field (BR-10.2).
        var problems = band.ToBand().Validate();

        foreach (var violation in problems)
        {
            violations.Add(new IdentityViolation($"{label}.{violation.Field}", violation.Code, violation.Message));
        }

        return problems.Count == 0 ? band : null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The wire shape of one profile, bands included, ordered so two reads of one row agree.</summary>
    private static SpeciesProfileView Describe(SpeciesProfile profile) =>
        new(
            profile.Id,
            profile.Name,
            profile.ScientificName,
            profile.ClimateZone.ToString(),
            profile.IsBuiltIn,
            profile.PhotoperiodHours,
            profile.LightsOnLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            profile.LightThresholdLux,
            profile.MinLightHoursPerDay,
            profile.Notes,
            [
                .. profile.Thresholds
                    .OrderBy(band => band.Metric)
                    .ThenBy(band => band.Phase)
                    .Select(DescribeBand),
            ]);

    private static SpeciesProfileBandView DescribeBand(Threshold band)
    {
        var definition = MetricDictionary.Get(band.Metric);

        return new SpeciesProfileBandView(
            definition.ApiKey,
            definition.Unit,
            Terrariums.ThresholdNames.Phase(band.Phase),
            band.TargetMin,
            band.TargetMax,
            band.CriticalMin,
            band.CriticalMax,
            band.DwellWarnMinutes,
            band.DwellCritMinutes,
            band.RecoveryMargin,
            band.Enabled,
            band.SourceRef,
            band.SourceUrl);
    }

    /// <summary>One profile's fields once the request and the stored row have been resolved into a single set.</summary>
    private sealed record ProfileCandidate(
        string Name,
        string? ScientificName,
        ClimateZone ClimateZone,
        decimal PhotoperiodHours,
        TimeOnly LightsOnLocalTime,
        decimal LightThresholdLux,
        decimal MinLightHoursPerDay,
        string? Notes);
}
