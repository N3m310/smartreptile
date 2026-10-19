using FluentAssertions;
using SmartReptile.Application.Species;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Metrics;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Species;

/// <summary>
/// FR-10's catalogue (roadmap 3.9): listing what a keeper may pick from, and the four writes over a profile the
/// caller owns. Covers the readings that are contract rather than implementation — a built-in is `403` while
/// someone else's profile is `404`, the band set is replaced rather than merged, and a profile in use is not
/// deletable — so the endpoint statuses those produce stay backed by something.
/// </summary>
public class SpeciesProfileServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private readonly FakeSpeciesProfileStore _store = new();
    private readonly FakeFileStore _files = new();
    private readonly TestClock _clock = new() { UtcNow = Now };
    private readonly SpeciesProfileService _service;
    private readonly Guid _owner = Guid.NewGuid();

    public SpeciesProfileServiceTests()
    {
        // The write paths key their rows on the caller's own subject, so the caller has to be an account here.
        _store.AddUser(_owner);
        _service = new SpeciesProfileService(_store, _files, _clock);
    }

    private static SpeciesProfileBandInput Band(
        string? metric = "tempC",
        string? phase = null,
        decimal? min = 26m,
        decimal? max = 32m,
        string? sourceRef = null) =>
        new(metric, phase, min, max, null, null, null, null, null, null, sourceRef);

    private static CreateSpeciesProfileRequest Create(
        string? name = "My species",
        string? climateZone = "Tropical",
        IReadOnlyList<SpeciesProfileBandInput>? bands = null) =>
        new(name, "Testus testus", climateZone, null, null, null, null, null, bands ?? [Band()]);

    // ---- list ---------------------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_the_built_ins_and_the_accounts_own_but_nobody_elses()
    {
        _store.AddBuiltIn("Arid (desert)", (MetricCode.TempC, 38m, 42m));
        _store.AddProfile(_owner, "Mine", (MetricCode.HumidityPct, 30m, 40m));
        _store.AddProfile(Guid.NewGuid(), "Someone else's", (MetricCode.TempC, 20m, 24m));

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profiles!.Select(profile => profile.Name).Should().Equal("Arid (desert)", "Mine");
        outcome.Profiles![0].IsBuiltIn.Should().BeTrue("a seeded profile is read-only in the UI");
    }

    // ---- create -------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_persists_a_custom_profile_with_its_bands_and_audits_it()
    {
        var outcome = await _service.CreateAsync(
            Create(bands: [Band("tempC", "Day", 26m, 32m), Band("humidityPct")]),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        var created = outcome.Profile!;

        created.Name.Should().Be("My species");
        created.ClimateZone.Should().Be("Tropical");
        created.IsBuiltIn.Should().BeFalse("only the seeder writes built-ins");
        created.LightsOnLocalTime.Should().Be("07:00", "the default photoperiod starts at 07:00");
        created.PhotoperiodHours.Should().Be(12m);
        created.Bands.Should().HaveCount(2);

        var day = created.Bands.Single(band => band.Phase == "day");
        day.Metric.Should().Be("tempC", "the wire carries the dictionary key, not the enum");
        day.Unit.Should().Be("°C", "the unit travels with the band so an editor needs no dictionary");
        day.DwellWarnMinutes.Should().Be(5);
        day.DwellCritMinutes.Should().Be(2);
        day.RecoveryMargin.Should().Be(0.5m);
        day.Enabled.Should().BeTrue();

        _store.SaveCount.Should().Be(1);
        _store.Profiles.Should().ContainSingle().Which.CreatedByUserId.Should().Be(_owner);
        _store.AuditEntries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.ProfileCreated);
    }

    [Fact]
    public async Task Create_refuses_an_inverted_band_with_a_field_error_naming_the_row()
    {
        var outcome = await _service.CreateAsync(
            Create(bands: [Band("humidityPct"), Band("tempC", min: 40m, max: 30m)]),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");

        // The row is addressed by position, like the override write path. An inverted band breaks more than the
        // ordering rule — the recovery margin cannot fit inside a negative width either — so the assertion is that
        // the ordering violation is there and that every error names the row that caused it.
        outcome.Problem.Errors.Should().Contain(violation =>
            violation.Field == "bands[1].targetMin" && violation.Code == "threshold_ordering_invalid");

        outcome.Problem.Errors!.Select(violation => violation.Field)
            .Should().OnlyContain(field => field.StartsWith("bands[1].", StringComparison.Ordinal));

        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_a_profile_with_no_bands()
    {
        var outcome = await _service.CreateAsync(
            Create(bands: []),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("profile_bands_required");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_the_same_metric_and_phase_twice()
    {
        var outcome = await _service.CreateAsync(
            Create(bands: [Band("tempC", "Day"), Band("tempC", "day", 20m, 24m)]),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("threshold_duplicate");
    }

    [Fact]
    public async Task Create_refuses_a_bad_metric_and_a_bad_phase_together()
    {
        var outcome = await _service.CreateAsync(
            Create(bands: [Band("notAMetric", "dusk")]),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors!.Select(violation => violation.Code)
            .Should().BeEquivalentTo(["metric_invalid", "threshold_phase_invalid"]);
    }

    [Fact]
    public async Task Create_refuses_an_unknown_climate_zone_and_a_missing_name()
    {
        var outcome = await _service.CreateAsync(
            Create(name: "  ", climateZone: "Antarctic"),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Errors!.Select(violation => violation.Code)
            .Should().BeEquivalentTo(["profile_name_required", "climate_zone_invalid"]);
    }

    [Fact]
    public async Task Create_refuses_a_name_the_account_already_uses()
    {
        await _service.CreateAsync(Create(name: "Duplicated"), _owner, AuditActor.Unknown, CancellationToken.None);

        var outcome = await _service.CreateAsync(
            Create(name: "duplicated"),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("profile_name_taken");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("name");
        _store.SaveCount.Should().Be(1, "the refused create wrote nothing");
    }

    [Fact]
    public async Task Create_refuses_a_caller_that_is_not_an_account_of_this_database()
    {
        var outcome = await _service.CreateAsync(
            Create(),
            Guid.NewGuid(),
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("unauthenticated");
        _store.SaveCount.Should().Be(0);
    }

    // ---- duplicate ----------------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_copies_a_built_in_into_the_callers_own_profile_with_its_provenance()
    {
        var builtIn = SeedBuiltIn();

        var outcome = await _service.DuplicateAsync(
            builtIn,
            new DuplicateSpeciesProfileRequest(null),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        var copy = outcome.Profile!;

        copy.Name.Should().Be("Arid (desert) (copy)", "an unnamed copy has to be tellable apart in the picker");
        copy.IsBuiltIn.Should().BeFalse();
        copy.Bands.Should().ContainSingle();
        copy.Bands[0].SourceRef.Should().Be("test citation", "a copy keeps the citation its numbers came from");
        copy.Id.Should().NotBe(builtIn);

        _store.Profiles.Should().HaveCount(2);
        _store.AuditEntries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.ProfileDuplicated);
    }

    [Fact]
    public async Task Duplicate_of_another_accounts_profile_is_not_found()
    {
        var foreign = _store.AddProfile(Guid.NewGuid(), "Theirs", (MetricCode.TempC, 20m, 24m));

        var outcome = await _service.DuplicateAsync(
            foreign.Id,
            new DuplicateSpeciesProfileRequest("Mine now"),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found", "a foreign profile is indistinguishable from a made-up id");
        _store.SaveCount.Should().Be(0);
    }

    // ---- patch --------------------------------------------------------------------------------------

    [Fact]
    public async Task Patch_edits_the_callers_profile_and_leaves_omitted_fields_alone()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m));

        var outcome = await _service.UpdateAsync(
            profile.Id,
            new UpdateSpeciesProfileRequest("Renamed", null, null, null, null, null, 10m, null, null),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile!.Name.Should().Be("Renamed");
        outcome.Profile.MinLightHoursPerDay.Should().Be(10m);
        outcome.Profile.ScientificName.Should().BeEmpty("the field was not sent and the row had none");
        outcome.Profile.Bands.Should().ContainSingle("bands were not sent, so the set stands");

        var audit = _store.AuditEntries.Should().ContainSingle().Subject;
        audit.Action.Should().Be(AuditAction.ProfileUpdated);
        audit.BeforeJson.Should().Contain("Mine", "BR-10.4 wants the state before the change");
        audit.AfterJson.Should().Contain("Renamed");
        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Patch_replaces_the_whole_band_set_when_bands_are_sent()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m), (MetricCode.HumidityPct, 30m, 40m));

        var outcome = await _service.UpdateAsync(
            profile.Id,
            new UpdateSpeciesProfileRequest(null, null, null, null, null, null, null, null, [Band("uvIndex", min: 0m, max: 3m)]),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile!.Bands.Should().ContainSingle()
            .Which.Metric.Should().Be("uvIndex", "a sent band set is the complete set, not a merge");
        _store.Profiles.Single().Thresholds.Should().ContainSingle();
    }

    [Fact]
    public async Task Patch_keeps_a_name_the_caller_did_not_move()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m));

        // The row already holds this name, so the collision check must not treat it as someone else's.
        var outcome = await _service.UpdateAsync(
            profile.Id,
            new UpdateSpeciesProfileRequest("Mine", null, null, null, null, null, null, null, null),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile!.Name.Should().Be("Mine");
    }

    [Fact]
    public async Task Patch_refuses_a_built_in_profile_and_another_accounts_profile_differently()
    {
        var builtIn = SeedBuiltIn();
        var foreign = _store.AddProfile(Guid.NewGuid(), "Theirs", (MetricCode.TempC, 20m, 24m));
        var request = new UpdateSpeciesProfileRequest("Changed", null, null, null, null, null, null, null, null);

        var immutable = await _service.UpdateAsync(builtIn, request, _owner, AuditActor.Unknown, CancellationToken.None);
        var hidden = await _service.UpdateAsync(foreign.Id, request, _owner, AuditActor.Unknown, CancellationToken.None);

        immutable.Problem!.Code.Should().Be("builtin_immutable", "the caller can see it, and the client can offer to copy it");
        hidden.Problem!.Code.Should().Be("not_found");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Patch_refuses_a_rename_onto_a_name_the_account_already_uses()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m));
        _store.AddProfile(_owner, "Taken", (MetricCode.TempC, 20m, 24m));

        var outcome = await _service.UpdateAsync(
            profile.Id,
            new UpdateSpeciesProfileRequest("Taken", null, null, null, null, null, null, null, null),
            _owner,
            AuditActor.Unknown,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("profile_name_taken");
        _store.SaveCount.Should().Be(0);
    }

    // ---- delete -------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_an_unused_profile_and_audits_it()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m));

        var outcome = await _service.DeleteAsync(profile.Id, _owner, AuditActor.Unknown, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Profile.Should().BeNull("the route answers 204");
        _store.Profiles.Should().BeEmpty();
        _store.AuditEntries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.ProfileDeleted);
        _store.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_refuses_a_profile_a_terrarium_is_assigned_to()
    {
        var profile = _store.AddProfile(_owner, "Mine", (MetricCode.TempC, 26m, 32m));
        _store.AssignToTerrarium(profile.Id);

        var outcome = await _service.DeleteAsync(profile.Id, _owner, AuditActor.Unknown, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("profile_in_use");
        _store.Profiles.Should().ContainSingle();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_refuses_a_built_in_even_though_nobody_owns_it()
    {
        var builtIn = SeedBuiltIn();

        var outcome = await _service.DeleteAsync(builtIn, _owner, AuditActor.Unknown, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("builtin_immutable");
        _store.Profiles.Should().ContainSingle();
    }

    private Guid SeedBuiltIn() =>
        // A built-in arrives from the seeder, not from a request, so the fixture writes the row directly.
        _store.AddBuiltIn("Arid (desert)", (MetricCode.TempC, 38m, 42m)).Id;
}
