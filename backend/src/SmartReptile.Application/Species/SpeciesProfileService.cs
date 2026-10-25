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
/// <item>
/// <b>A picture is a file plus three columns, not a blob.</b> The bytes go to <see cref="IFileStore"/> and the row
/// keeps the key, the sniffed media type and when it was set — so listing the catalogue never drags image bytes
/// through a query, and a profile without a picture costs nothing. What is stored is the type the bytes turned out
/// to be, never the type the caller declared: a claim is not a reason for a browser to treat a file as an image.
/// </item>
/// </list>
/// </remarks>
public sealed class SpeciesProfileService(ISpeciesProfileStore store, IFileStore files, IClock clock)
{
    private const int NameMaxLength = 80;
    private const int ScientificNameMaxLength = 120;
    private const int NotesMaxLength = 1000;

    /// <summary>
    /// Largest picture accepted, 2 MB. Big enough for a phone photograph of a terrarium's inhabitant and small
    /// enough that the whole thing can be held in memory to sniff it — which is what lets the bytes be checked
    /// before anything is written. It is deliberately not configurable: nothing in the product needs a larger one,
    /// and a limit a deployment can raise is a limit nobody measured.
    /// </summary>
    private const int ImageMaxBytes = 2 * 1024 * 1024;
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

    /// <summary>
    /// The picture types the catalogue accepts, and the extension each is stored under. Three raster formats that
    /// every browser and the Flutter client render without a plugin; no SVG, which is a document that can carry
    /// script, and no HEIC, which half the clients cannot decode anyway.
    /// </summary>
    private static readonly (string ContentType, string Extension)[] ImageTypes =
    [
        ("image/png", ".png"),
        ("image/jpeg", ".jpg"),
        ("image/webp", ".webp"),
    ];

    /// <summary>The accepted types as a sentence, so a refusal and the documentation cannot drift apart.</summary>
    private static readonly string AcceptedImageTypes = string.Join(", ", ImageTypes.Select(type => type.ContentType));

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffSignature = [0x52, 0x49, 0x46, 0x46];
    private static readonly byte[] WebpSignature = [0x57, 0x45, 0x42, 0x50];

    private static string MetricKeys => string.Join(", ", MetricDictionary.All.Select(definition => definition.ApiKey));

    private static string ZoneNames => string.Join(", ", Enum.GetNames<ClimateZone>());

    /// <summary>Built-ins plus the caller's own, each with its bands (BR-10.1, FR-10 A2).</summary>
    /// <param name="userId">Signed-in account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">
    /// True for an administrator, who may edit the built-ins and other accounts' profiles (TASKS 2026-10-19). It only
    /// widens what <c>canEdit</c> reports; which profiles are listed is unchanged.
    /// </param>
    public async Task<SpeciesProfileOutcome> ListAsync(
        Guid userId,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        var profiles = await store.ListVisibleAsync(userId, cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Listed([.. profiles.Select(profile => Describe(profile, userId, mayEditAny))]);
    }

    /// <summary>One profile the caller may see: a built-in, or one of their own (BR-10.1, BR-02.2).</summary>
    /// <param name="profileId">Profile identity.</param>
    /// <param name="userId">Signed-in account, which decides what it may see.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">True for an administrator, who may also edit it; it only widens <c>canEdit</c>.</param>
    public async Task<SpeciesProfileOutcome> GetAsync(
        Guid profileId,
        Guid userId,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        var profile = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        var isCreatedByAdmin = profile?.CreatedByUserId is { } author
            && await store.IsAdminUserAsync(author, cancellationToken).ConfigureAwait(false);

        // A foreign custom profile answers exactly like a missing one (BR-02.2) — unless it was created by an
        // administrator as part of the shared catalogue.
        if (profile is null || (!profile.IsBuiltIn && profile.CreatedByUserId != userId && !isCreatedByAdmin))
        {
            return SpeciesProfileOutcome.NotFound();
        }

        return SpeciesProfileOutcome.Found(Describe(profile, userId, mayEditAny));
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

        if (await store.NameTakenAsync(userId, candidate.Name, null, cancellationToken).ConfigureAwait(false))
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
            afterJson: JsonSerializer.Serialize(Describe(profile, userId), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(profile, userId));
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

        if (await store.NameTakenAsync(userId, name, null, cancellationToken).ConfigureAwait(false))
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
            afterJson: JsonSerializer.Serialize(Describe(copy, userId), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(copy, userId));
    }

    /// <summary>Edits a profile: its fields, the band set, or both.</summary>
    /// <param name="profileId">Profile to change.</param>
    /// <param name="request">Fields to change; omitted members are left alone.</param>
    /// <param name="userId">Signed-in account, which must own the profile unless it is an administrator.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">
    /// True for an administrator: a built-in and another account's profile are then editable instead of answering
    /// <c>403 builtin_immutable</c> / <c>404 not_found</c>, and the change is audited with before and after like any
    /// other (TASKS 2026-10-19). Narrowing the permission, not the validation: every band rule still applies.
    /// </param>
    public async Task<SpeciesProfileOutcome> UpdateAsync(
        Guid profileId,
        UpdateSpeciesProfileRequest request,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (profile, refusal) = await OwnedForWriteAsync(profileId, userId, mayEditAny, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return refusal!;
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
        // would refuse every edit of every other field. The namespace is the profile's owner — an administrator
        // renaming another account's profile must not collide with their own catalogue's names.
        if (!string.Equals(candidate.Name, profile.Name, StringComparison.Ordinal)
            && await store.NameTakenAsync(profile.CreatedByUserId, candidate.Name, profile.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return NameTaken(candidate.Name);
        }

        var before = Describe(profile, userId);
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
            afterJson: JsonSerializer.Serialize(Describe(profile, userId), AuditJson)));

        if (!await store.TrySaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.ProfileConflict();
        }

        return SpeciesProfileOutcome.Found(Describe(profile, userId, mayEditAny));
    }

    /// <summary>Deletes a profile, unless a terrarium is assigned to it.</summary>
    /// <param name="profileId">Profile to delete.</param>
    /// <param name="userId">Signed-in account, which must own the profile unless it is an administrator.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">
    /// True for an administrator: a built-in and another account's profile become deletable. The <c>409</c> for a
    /// profile still in use stands for everyone, because a terrarium pointing at a deleted profile is a broken
    /// terrarium whoever asked.
    /// </param>
    public async Task<SpeciesProfileOutcome> DeleteAsync(
        Guid profileId,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        var (profile, refusal) = await OwnedForWriteAsync(profileId, userId, mayEditAny, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return refusal!;
        }

        if (await store.IsAssignedAsync(profileId, cancellationToken).ConfigureAwait(false))
        {
            return SpeciesProfileOutcome.InUse();
        }

        var now = clock.UtcNow;
        var before = Describe(profile, userId);

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

    /// <summary>
    /// Stores a picture for one of the caller's own profiles, replacing whatever was there (FR-10).
    /// </summary>
    /// <remarks>
    /// The order of operations is the part worth reading. The bytes are validated and written to the store
    /// <i>before</i> anything is committed, so the row and its picture can never disagree in the direction that
    /// matters — a profile pointing at a file that was never written. The reverse is possible and harmless: a
    /// commit that fails after the file was written leaves a file nothing references. The picture the change
    /// replaces is deleted only after the commit, for the same reason.
    /// <para>
    /// The upload is read into memory and sniffed rather than trusted, because the declared content type is the one
    /// thing about a file a caller controls completely: what the profile records is what the bytes turned out to be.
    /// </para>
    /// </remarks>
    /// <param name="profileId">Profile to set the picture on.</param>
    /// <param name="userId">Signed-in account, which must own the profile unless it is an administrator.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="upload">The bytes and the caller's claim about their type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">True for an administrator, who may set a picture on any profile, built-in included.</param>
    public async Task<SpeciesProfileOutcome> SetImageAsync(
        Guid profileId,
        Guid userId,
        AuditActor actor,
        ProfileImageUpload upload,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var (profile, refusal) = await OwnedForWriteAsync(profileId, userId, mayEditAny, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return refusal!;
        }

        // `application/octet-stream` counts as no claim at all: it is what a client sends when it does not know its
        // own file's type, and refusing it would refuse a correct picture from a command-line upload for saying
        // nothing. A *specific* type that is not a picture is a different matter and is named in the refusal.
        if (upload.DeclaredContentType is { Length: > 0 } declared
            && !string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !ImageTypes.Any(type => string.Equals(type.ContentType, declared, StringComparison.OrdinalIgnoreCase)))
        {
            return ImageTypeUnsupported(declared);
        }

        await using var buffered = await BufferAsync(upload.Content, cancellationToken).ConfigureAwait(false);

        if (buffered is null)
        {
            return ImageTooLarge();
        }

        if (buffered.Length == 0)
        {
            return ImageRequired();
        }

        if (Sniff(buffered.GetBuffer().AsSpan(0, (int)buffered.Length)) is not { } image)
        {
            return ImageInvalid();
        }

        buffered.Position = 0;

        var key = await files.SaveAsync(buffered, image.Extension, cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        var before = Describe(profile, userId);
        var replaced = profile.ImageStorageKey;

        profile.ImageStorageKey = key;
        profile.ImageContentType = image.ContentType;
        profile.ImageUpdatedAt = now;
        profile.UpdatedAt = now;

        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileUpdated,
            profile.Id,
            userId,
            actor,
            now,
            beforeJson: JsonSerializer.Serialize(before, AuditJson),
            afterJson: JsonSerializer.Serialize(Describe(profile, userId), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await DiscardAsync(replaced, key, cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(profile, userId, mayEditAny));
    }

    /// <summary>
    /// Removes a profile's picture. A profile that has none is not an error — the caller asked for a state, and it
    /// already holds — and it writes no audit row, because a verb claiming a change that did not happen is the one
    /// row a trail must not carry.
    /// </summary>
    /// <param name="profileId">Profile to clear.</param>
    /// <param name="userId">Signed-in account, which must own the profile unless it is an administrator.</param>
    /// <param name="actor">Request provenance for the audit row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="mayEditAny">True for an administrator, who may clear the picture of any profile.</param>
    public async Task<SpeciesProfileOutcome> RemoveImageAsync(
        Guid profileId,
        Guid userId,
        AuditActor actor,
        CancellationToken cancellationToken,
        bool mayEditAny = false)
    {
        var (profile, refusal) = await OwnedForWriteAsync(profileId, userId, mayEditAny, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return refusal!;
        }

        if (profile.ImageStorageKey is not { } removed)
        {
            return SpeciesProfileOutcome.Found(Describe(profile, userId, mayEditAny));
        }

        var now = clock.UtcNow;
        var before = Describe(profile, userId);

        profile.ImageStorageKey = null;
        profile.ImageContentType = null;
        profile.ImageUpdatedAt = now;
        profile.UpdatedAt = now;

        store.AddAuditEntry(AuditLog.ForSpeciesProfile(
            AuditAction.ProfileUpdated,
            profile.Id,
            userId,
            actor,
            now,
            beforeJson: JsonSerializer.Serialize(before, AuditJson),
            afterJson: JsonSerializer.Serialize(Describe(profile, userId), AuditJson)));

        await store.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await files.DeleteAsync(removed, cancellationToken).ConfigureAwait(false);

        return SpeciesProfileOutcome.Found(Describe(profile, userId, mayEditAny));
    }

    /// <summary>
    /// The picture itself, for any profile the caller may see: a built-in or their own (BR-10.1, BR-02.2).
    /// </summary>
    /// <remarks>
    /// The route takes a bearer token like every other read, so a client cannot point an <c>img</c> tag at it — the
    /// web client fetches the bytes and hands the browser a blob URL instead. That is the cost of not publishing
    /// pictures to anybody who guesses an id, and it is the cheaper half of the trade: the alternative was a second
    /// place a token is accepted from a query string, which <c>/hubs</c> holds alone for a reason the WebSocket
    /// handshake forced.
    /// </remarks>
    /// <param name="profileId">Profile whose picture is wanted.</param>
    /// <param name="userId">Signed-in account, which decides what it may see.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SpeciesProfileOutcome> OpenImageAsync(
        Guid profileId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var profile = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        var isCreatedByAdmin = profile?.CreatedByUserId is { } author
            && await store.IsAdminUserAsync(author, cancellationToken).ConfigureAwait(false);

        // A foreign custom profile answers exactly like a missing one (BR-02.2) — unless it was created by an
        // administrator as part of the shared catalogue.
        if (profile is null || (!profile.IsBuiltIn && profile.CreatedByUserId != userId && !isCreatedByAdmin))
        {
            return SpeciesProfileOutcome.NotFound();
        }

        if (profile.ImageStorageKey is not { } key)
        {
            return SpeciesProfileOutcome.ImageNotSet();
        }

        var content = await files.OpenAsync(key, cancellationToken).ConfigureAwait(false);

        // A row naming a file the store cannot open answers as "no picture" rather than as a server fault: the
        // client's next step is the same either way, and a 500 would take a whole catalogue render down over one
        // file that went missing. The row is left exactly as it is, so the inconsistency stays visible in the data.
        if (content is null)
        {
            return SpeciesProfileOutcome.ImageNotSet();
        }

        return SpeciesProfileOutcome.WithImage(new SpeciesProfileImage(
            content,
            profile.ImageContentType ?? "application/octet-stream",
            profile.ImageUpdatedAt ?? profile.UpdatedAt));
    }

    /// <summary>
    /// The profile a write needs: it exists, it is the caller's, and it is not a built-in. One place holds that
    /// order, because the three refusals are ordered deliberately — a built-in has no owner, so checking ownership
    /// first would report it as someone else's and cost the client the one answer that tells it to duplicate
    /// instead (FR-10 A2).
    /// </summary>
    /// <remarks>
    /// An administrator skips the last two checks (TASKS 2026-10-19): the built-ins and other accounts' profiles are
    /// theirs to edit, and the decision is "admin toàn quyền, không bắt nhân bản". What is not skipped is the
    /// existence check or anything downstream — band validation, the in-use rule and the audit trail are unchanged.
    /// </remarks>
    private async Task<(SpeciesProfile? Profile, SpeciesProfileOutcome? Refusal)> OwnedForWriteAsync(
        Guid profileId,
        Guid userId,
        bool mayEditAny,
        CancellationToken cancellationToken)
    {
        if (!await store.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
        {
            return (null, SpeciesProfileOutcome.Unauthenticated());
        }

        var profile = await store.FindAsync(profileId, cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return (null, SpeciesProfileOutcome.NotFound());
        }

        if (mayEditAny)
        {
            return (profile, null);
        }

        if (profile.IsBuiltIn)
        {
            return (null, SpeciesProfileOutcome.BuiltInImmutable());
        }

        return profile.CreatedByUserId == userId
            ? (profile, null)
            : (null, SpeciesProfileOutcome.NotFound());
    }

    /// <summary>
    /// Reads an upload into memory, stopping at the limit rather than after it: a caller who sends a gigabyte pays
    /// for one bounded buffer and one refusal. Null means the limit was passed.
    /// </summary>
    private static async Task<MemoryStream?> BufferAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;

        while ((read = await content.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > ImageMaxBytes)
            {
                await buffer.DisposeAsync().ConfigureAwait(false);

                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        buffer.Position = 0;

        return buffer;
    }

    /// <summary>
    /// Identifies an image from its bytes, not from what the caller said about them: PNG's signature, JPEG's
    /// start-of-image, and RIFF/WEBP. Anything else is not an accepted picture, which is the whole of the check —
    /// decoding the file to be certain would mean a dependency and an image-parsing loop on an upload path, and
    /// what is stored is served back as the sniffed type, so a mislabelled file cannot become an active document.
    /// </summary>
    private static (string ContentType, string Extension)? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(PngSignature))
        {
            return ImageTypes[0];
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ImageTypes[1];
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual(RiffSignature) && bytes.Slice(8, 4).SequenceEqual(WebpSignature))
        {
            return ImageTypes[2];
        }

        return null;
    }

    /// <summary>Deletes the file a committed change replaced, once nothing refers to it any more.</summary>
    private async Task DiscardAsync(string? replaced, string key, CancellationToken cancellationToken)
    {
        if (replaced is null || replaced == key)
        {
            return;
        }

        await files.DeleteAsync(replaced, cancellationToken).ConfigureAwait(false);
    }

    private static SpeciesProfileOutcome ImageRequired() =>
        SpeciesProfileOutcome.Failed(new SpeciesProfileProblem(
            "image_required",
            "A picture is required: send one file part named 'file'.",
            [new IdentityViolation("file", "image_required", "Choose a picture.")]));

    private static SpeciesProfileOutcome ImageTooLarge() =>
        SpeciesProfileOutcome.Failed(new SpeciesProfileProblem(
            "image_too_large",
            $"The picture is larger than {ImageMaxBytes / (1024 * 1024)} MB.",
            [new IdentityViolation("file", "image_too_large", "Choose a smaller picture.")]));

    private static SpeciesProfileOutcome ImageTypeUnsupported(string declared) =>
        SpeciesProfileOutcome.Failed(new SpeciesProfileProblem(
            "image_type_unsupported",
            $"'{declared}' is not a picture type the catalogue accepts: {AcceptedImageTypes}.",
            [new IdentityViolation("file", "image_type_unsupported", "Choose a PNG, JPEG or WebP picture.")]));

    private static SpeciesProfileOutcome ImageInvalid() =>
        SpeciesProfileOutcome.Failed(new SpeciesProfileProblem(
            "image_invalid",
            $"The file is not one of the accepted picture types ({AcceptedImageTypes}).",
            [new IdentityViolation("file", "image_invalid", "That file is not a readable picture.")]));

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

    /// <summary>The wire shape of one profile for one caller, bands included, ordered so two reads of one row agree.</summary>
    /// <remarks>
    /// <c>canEdit</c> is resolved here rather than left to the client: the rule is "not a built-in, and mine", or
    /// "any of them" for an administrator — and a client re-deriving it from <c>isBuiltIn</c> would have to guess
    /// both halves.
    /// </remarks>
    private static SpeciesProfileView Describe(SpeciesProfile profile, Guid viewerId, bool mayEditAny = false) =>
        new(
            profile.Id,
            profile.Name,
            profile.ScientificName,
            profile.ClimateZone.ToString(),
            profile.IsBuiltIn,
            mayEditAny || (!profile.IsBuiltIn && profile.CreatedByUserId == viewerId),
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
            ],
            // The key is not published: it names a file on a server, and what a client needs is the route that
            // serves it. A profile with no picture says so with null, which is what the form reads as "nothing to
            // show" rather than as a failure.
            profile.ImageStorageKey is null ? null : $"/api/v1/species-profiles/{profile.Id}/image",
            profile.ImageUpdatedAt);

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
