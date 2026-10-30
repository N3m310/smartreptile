using FluentAssertions;
using SmartReptile.Application.Devices;
using SmartReptile.Application.Readings;
using SmartReptile.Application.Terrariums;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Domain.Metrics;
using SmartReptile.Domain.Readings;
using SmartReptile.Domain.Species;
using SmartReptile.Domain.Terrariums;
using SmartReptile.Domain.Thresholds;
using SmartReptile.Tests.Unit.Application;
using SmartReptile.Tests.Unit.Devices;

namespace SmartReptile.Tests.Unit.Terrariums;

/// <summary>
/// Task 2.8 — the terrarium read surface: list, detail, create, <c>readings/latest</c>, bucketed
/// <c>readings</c> and <c>coverage</c>. Covers TC-I-10 (bucket selection and gap handling) at the service level
/// and the parts of TC-I-11 and TC-I-13 that live here: ownership hiding, staleness, band and phase classification.
/// </summary>
public class TerrariumServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTerrariumStore _store = new();
    private readonly FakeTimeZoneStore _zones = new();
    private readonly FakeProvisioningStore _provisioningStore = new();
    private readonly FakeDeviceCredentials _credentials = new();
    private readonly TestClock _clock = new() { UtcNow = Now };
    private readonly DeviceProvisioningService _provisioning;
    private readonly TerrariumService _service;
    private readonly Guid _owner = Guid.NewGuid();

    public TerrariumServiceTests()
    {
        // Create keys its new row on the caller's own subject, so the caller has to be an account this store knows.
        _store.AddUser(_owner);

        // The create path stages a board's binding through the claim use case rather than re-implementing it
        // (TASKS 2026-10-14, item 2), so the service under test needs a real provisioning service over fakes.
        _provisioning = new DeviceProvisioningService(
            _provisioningStore,
            new FakeClaimCodeGenerator(),
            _credentials,
            new FakeOnboardingThrottleStore(),
            new FakeDeviceSessionRegistry(),
            _clock,
            new ProvisioningSettings(ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength, 15, OnboardingLimits.Documented),
            new DeviceSilenceSettings(3));

        _service = new TerrariumService(
            _store,
            _zones,
            _provisioning,
            _clock,
            new TerrariumSettings("Asia/Ho_Chi_Minh", 3));
    }

    // ---- live-update membership (FR-08, roadmap task 2.9) -------------------------------------------

    [Fact]
    public async Task IsMemberAsync_answers_true_for_the_owner_only()
    {
        var profile = _store.AddProfile();
        var mine = _store.AddTerrarium(_owner, "Alpha", profile);
        var someoneElses = _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        (await _service.IsMemberAsync(mine.Id, _owner)).Should().BeTrue();

        // This is what keeps the realtime hub from becoming a cross-tenant leak: the group name is derived from
        // the terrarium id, so joining one is asking to receive its data.
        (await _service.IsMemberAsync(someoneElses.Id, _owner)).Should().BeFalse();
        (await _service.IsMemberAsync(Guid.NewGuid(), _owner)).Should().BeFalse();
    }

    [Fact]
    public async Task IsMemberAsync_refuses_a_deleted_terrarium()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.SoftDelete(terrarium, Now);

        (await _service.IsMemberAsync(terrarium.Id, _owner)).Should().BeFalse();
    }

    // ---- list and detail ----------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_only_the_callers_terrariums()
    {
        var profile = _store.AddProfile();
        var mine = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(mine, DeviceStatus.Online, Now);
        _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrariums.Should().ContainSingle().Which.Name.Should().Be("Alpha");
        outcome.Terrariums!.Single().OwnerUserId.Should().BeNull();
        outcome.Terrariums!.Single().OwnerEmail.Should().BeNull("a keeper needs no label for their own rows");
    }

    [Fact]
    public async Task List_returns_every_accounts_terrariums_to_an_administrator_with_their_owners()
    {
        var profile = _store.AddProfile();
        var owner = Guid.NewGuid();
        _store.AddUser(owner, "theirs@example.test");
        _store.AddTerrarium(_owner, "Mine", profile);
        _store.AddTerrarium(owner, "Theirs", profile);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None, mayReachAnyAccount: true);

        outcome.Terrariums!.Select(item => item.Name).Should().BeEquivalentTo("Mine", "Theirs");

        var theirs = outcome.Terrariums.Single(item => item.Name == "Theirs");
        theirs.OwnerUserId.Should().Be(owner);
        theirs.OwnerEmail.Should().Be("theirs@example.test", "an id alone does not say whose enclosure it is");
    }

    [Fact]
    public async Task List_narrows_to_one_species_profile_when_asked()
    {
        var desert = _store.AddProfile("Arid (desert)");
        var tropical = _store.AddProfile("Tropical");
        _store.AddTerrarium(_owner, "Alpha", desert);
        _store.AddTerrarium(_owner, "Beta", tropical);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None, speciesProfileId: desert.Id);

        outcome.Terrariums.Should().ContainSingle().Which.Name.Should().Be("Alpha");
    }

    [Fact]
    public async Task List_narrows_what_the_caller_may_see_and_never_widens_it()
    {
        var profile = _store.AddProfile();
        _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None, speciesProfileId: profile.Id);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrariums.Should().BeEmpty("the filter narrows a permitted set; it does not reach across accounts");
    }

    [Fact]
    public async Task List_filters_an_administrators_cross_account_list_too()
    {
        var arid = _store.AddProfile("Arid (desert)");
        var tropical = _store.AddProfile("Tropical");
        _store.AddTerrarium(_owner, "Mine", arid);
        _store.AddTerrarium(Guid.NewGuid(), "Theirs", tropical);

        var outcome = await _service.ListAsync(
            _owner,
            CancellationToken.None,
            mayReachAnyAccount: true,
            speciesProfileId: arid.Id);

        outcome.Terrariums.Should().ContainSingle().Which.Name.Should().Be("Mine");
    }

    [Fact]
    public async Task List_reports_the_device_state_activity_and_species_name()
    {
        var profile = _store.AddProfile("Leopard gecko (semi-desert)");
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);
        _store.AddSample(terrarium.Id, device.Id, Now.AddMinutes(-1), default, (MetricCode.TempC, 28m));
        _store.AddAlert(terrarium.Id, device.Id);

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);
        var summary = outcome.Terrariums.Should().ContainSingle().Subject;

        summary.SpeciesName.Should().Be("Leopard gecko (semi-desert)");
        summary.Device!.Status.Should().Be("online");
        summary.Device.SamplingIntervalSec.Should().Be(60);
        summary.LatestSampleAt.Should().Be(Now.AddMinutes(-1));
        summary.OpenAlertCount.Should().Be(1);
    }

    [Fact]
    public async Task List_reports_a_silent_device_as_offline()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // Three missed 60 s intervals is the documented threshold (FR-07 / BR-07.2).
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, lastSeenAt: Now.AddSeconds(-181));

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Terrariums!.Single().Device!.Status.Should().Be("offline");
    }

    [Fact]
    public async Task List_does_not_resurrect_a_revoked_device_as_offline()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Revoked, lastSeenAt: Now.AddDays(-30));

        var outcome = await _service.ListAsync(_owner, CancellationToken.None);

        outcome.Terrariums!.Single().Device!.Status.Should().Be("revoked");
    }

    [Fact]
    public async Task Get_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.GetAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();

        // Told apart from a missing id on purpose (TASKS 2026-10-14, item 3): a keeper who mistyped an id and a
        // keeper looking at somebody else's enclosure need different answers, and the row is not disclosed either way.
        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
    }

    [Fact]
    public async Task Get_of_a_missing_terrarium_is_not_found()
    {
        var outcome = await _service.GetAsync(Guid.NewGuid(), _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Get_of_a_deleted_terrarium_is_gone()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.SoftDelete(terrarium, Now);

        var outcome = await _service.GetAsync(terrarium.Id, _owner, CancellationToken.None);

        // 410 rather than 404: the readings, alerts and summaries of this terrarium are still stored, so claiming it
        // never existed would say the opposite of the truth (TASKS 2026-10-14, item 6).
        outcome.Problem!.Code.Should().Be("terrarium_deleted");
    }

    [Fact]
    public async Task Get_of_another_accounts_terrarium_is_served_to_an_administrator()
    {
        var profile = _store.AddProfile();
        var owner = Guid.NewGuid();
        _store.AddUser(owner, "owner@example.test");
        var theirs = _store.AddTerrarium(owner, "Theirs", profile);

        var outcome = await _service.GetAsync(theirs.Id, _owner, CancellationToken.None, mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.Name.Should().Be("Theirs");

        // Named, because an administrator's answer can describe an enclosure they did not ask for and cannot
        // otherwise attribute (TASKS 2026-10-14, item 5).
        outcome.Terrarium.OwnerUserId.Should().Be(owner);
        outcome.Terrarium.OwnerEmail.Should().Be("owner@example.test");
    }

    [Fact]
    public async Task Get_reports_no_owner_to_a_caller_who_is_not_an_administrator()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.GetAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Terrarium!.OwnerUserId.Should().BeNull("ownership is not a field an ordinary client acts on");
        outcome.Terrarium.OwnerEmail.Should().BeNull();
    }

    // ---- create -------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_refuses_a_blank_name_with_a_field_error()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("   ", profile.Id, null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("name");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_without_a_species_profile_leaves_the_enclosure_profileless()
    {
        // 2026-10-25: a keeper may set the enclosure up before deciding what lives in it. The row is valid — no
        // bands to evaluate against, which is a state the evaluation layer already handles.
        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Empty tub", null, null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.SpeciesProfileId.Should().BeNull();
        outcome.Terrarium.SpeciesName.Should().BeNull("the name is resolved from the profile, so it is null with it");
        _store.Terrariums.Should().ContainSingle().Which.SpeciesProfileId.Should().BeNull();
    }

    [Fact]
    public async Task Create_refuses_an_unknown_species_profile()
    {
        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", Guid.NewGuid(), null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors.Should().ContainSingle().Which.Code.Should().Be("species_profile_not_found");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_a_caller_that_is_not_an_account_of_this_database()
    {
        var profile = _store.AddProfile();
        var request = new CreateTerrariumRequest("Alpha", profile.Id, null, null, null);

        // What an access token minted against another database — or before this one was rebuilt — looks like from
        // here: signature and lifetime are fine, the subject names nobody. The insert used to answer a foreign-key
        // violation as `500 internal_error`; the caller is told to sign in again instead (BUG-11).
        var unknownAccount = await _service.CreateAsync(request, Guid.NewGuid(), CancellationToken.None);

        unknownAccount.Problem!.Code.Should().Be("unauthenticated");

        // A token with no usable subject claim reaches the service as Guid.Empty, and takes the same path.
        var noSubject = await _service.CreateAsync(request, Guid.Empty, CancellationToken.None);

        noSubject.Problem!.Code.Should().Be("unauthenticated");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_an_unknown_time_zone()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, "Mars/Olympus_Mons"),
            _owner,
            CancellationToken.None);

        // The id is validated against the catalogue the picker is filled from, not against a bare provider lookup
        // (TASKS 2026-10-14, item 11): a zone the clients cannot offer must not be storable, or the edit form would
        // have no row to show for it.
        var violation = outcome.Problem!.Errors.Should().ContainSingle().Subject;
        violation.Field.Should().Be("timeZoneId");
        violation.Code.Should().Be("terrarium_timezone_invalid");
        violation.Message.Should().Contain("GET /api/v1/time-zones");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_stores_the_catalogues_spelling_of_a_zone_sent_in_another_case()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, "asia/ho_chi_minh"),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
    }

    // ---- create with a board (TASKS 2026-10-14, item 2) ---------------------------------------------

    [Fact]
    public async Task Create_binds_the_board_it_names_and_returns_its_secret_once()
    {
        var profile = _store.AddProfile();
        var device = AddRegisteredDevice("A0:B1:C2:D3:E4:F5");

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, DeviceId: device.PublicId),
            _owner,
            CancellationToken.None,
            new AuditActor("198.51.100.9", "curl/8.5.0", "corr-create"));

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.DeviceSecret.Should().NotBeNullOrWhiteSpace("the caller has one chance to keep it");

        // Bound to the enclosure that was just created, owned by the caller, and holding exactly one credential.
        device.TerrariumId.Should().Be(outcome.Terrarium.Id);
        device.UserId.Should().Be(_owner);
        device.ProvisionedAt.Should().Be(Now);
        device.ClaimCode.Should().BeNull("the code a code-claim would consume is cleared by either path");
        device.Credentials.Should().ContainSingle();

        // One commit for both writes: the enclosure and the binding are one act, so a failure between them cannot
        // leave a half-made pair behind. The two fakes stand in for the one scoped context the two adapters share
        // in production, which is why the row is still pending here rather than rolled up — what matters is that the
        // binding did not commit on its own.
        _store.SaveCount.Should().Be(1);
        _provisioningStore.PendingAuditEntries.Should().ContainSingle(
            "the audit row was staged into the same unit of work rather than committed separately");
        _provisioningStore.CommittedAuditEntries.Should().BeEmpty();
        _provisioningStore.Devices.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_leaves_the_secret_absent_when_no_board_is_named()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null),
            _owner,
            CancellationToken.None);

        outcome.Terrarium!.DeviceSecret.Should().BeNull();
        _provisioningStore.Devices.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_binds_a_board_by_mac_address_too()
    {
        var profile = _store.AddProfile();

        // Registered the way a board reports itself — colon-separated — and named the way a keeper types what is
        // printed on the case.
        var device = AddRegisteredDevice("A0:B1:C2:D3:E4:F5");

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, MacAddress: "a0b1c2d3e4f5"),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        device.TerrariumId.Should().Be(outcome.Terrarium!.Id);
    }

    [Fact]
    public async Task Create_refuses_two_selectors_rather_than_guessing()
    {
        var profile = _store.AddProfile();
        var device = AddRegisteredDevice("A0:B1:C2:D3:E4:F5");

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest(
                "Alpha",
                profile.Id,
                null,
                null,
                null,
                DeviceId: device.PublicId,
                MacAddress: "A0:B1:C2:D3:E4:F5"),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("device_selector_ambiguous");
        _store.SaveCount.Should().Be(0, "nothing is written when the request cannot be acted on");
    }

    [Fact]
    public async Task Create_refuses_an_unknown_board_and_creates_nothing()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, DeviceId: "sr-nope00"),
            _owner,
            CancellationToken.None);

        var violation = outcome.Problem!.Errors.Should().ContainSingle().Subject;
        violation.Field.Should().Be("deviceId");
        violation.Code.Should().Be("device_not_found");

        // All or nothing: a bad board must not leave an empty enclosure behind, which is what a keeper would end up
        // with if the binding happened after the insert.
        _store.SaveCount.Should().Be(0);
        _store.Terrariums.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_refuses_a_board_that_is_already_bound()
    {
        var profile = _store.AddProfile();
        var device = AddRegisteredDevice("A0:B1:C2:D3:E4:F5");
        var other = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);
        device.TerrariumId = other.Id;

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, DeviceId: device.PublicId),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("device_already_bound");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_refuses_a_malformed_mac_address()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, MacAddress: "not-a-mac"),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("mac_invalid");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_records_which_selector_bound_the_board()
    {
        var profile = _store.AddProfile();
        var device = AddRegisteredDevice("A0:B1:C2:D3:E4:F5");
        var actor = new AuditActor("198.51.100.9", "curl/8.5.0", "corr-create");

        (await _service.CreateAsync(
            new CreateTerrariumRequest("Alpha", profile.Id, null, null, null, DeviceId: device.PublicId),
            _owner,
            CancellationToken.None,
            actor)).Succeeded.Should().BeTrue();

        // The trail has to say how the board was identified: the id path and the MAC path carry different trust.
        var entry = _provisioningStore.PendingAuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.DeviceClaimed);
        entry.UserId.Should().Be(_owner);
        entry.IpAddress.Should().Be("198.51.100.9");
        entry.AfterJson.Should().Contain("\"method\":\"device_id\"");
    }

    /// <summary>A board that registered with this server and has not claimed anything yet.</summary>
    private Device AddRegisteredDevice(string mac)
    {
        var device = new Device
        {
            PublicId = $"sr-{Guid.NewGuid().ToString("N")[..6]}",
            DeviceName = "New node",
            ChipId = Guid.NewGuid().ToString("N")[..12],
            MacAddress = mac,
            Status = DeviceStatus.Provisioning,
            ClaimCode = "K7M2QP4T",
            ClaimCodeExpiresAt = Now.AddMinutes(15),
        };

        _provisioningStore.AddDevice(device);
        return device;
    }

    [Fact]
    public async Task Create_persists_the_terrarium_and_falls_back_to_the_configured_time_zone()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("  Alpha  ", profile.Id, "  desk ", string.Empty, null),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        var created = outcome.Terrarium!;

        created.Name.Should().Be("Alpha", "names are trimmed");
        created.Location.Should().Be("desk");
        created.Description.Should().BeNull("an empty string is not a description");
        created.TimeZoneId.Should().Be("Asia/Ho_Chi_Minh");
        created.SpeciesName.Should().Be(profile.Name);
        created.LatestSampleAt.Should().BeNull();
        created.OpenAlertCount.Should().Be(0);

        _store.SaveCount.Should().Be(1);
        _store.Terrariums.Should().Contain(terrarium => terrarium.Id == created.Id && terrarium.UserId == _owner);
    }

    // ---- readings/latest ----------------------------------------------------------------------------

    [Fact]
    public async Task LatestReadings_reports_the_newest_value_per_metric_including_ones_the_newest_sample_lacks()
    {
        var (terrarium, device, profile) = Arrange();

        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now.AddMinutes(-5),
            default,
            (MetricCode.TempC, 28m),
            (MetricCode.LightLux, 500m));

        // Light has no reading in the newest sample — the sun went down — and must still keep its last value.
        _store.AddSample(terrarium.Id, device.Id, Now.AddMinutes(-1), default, (MetricCode.TempC, 29m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.LastSampleAt.Should().Be(Now.AddMinutes(-1));
        readings.Device!.DeviceId.Should().Be(device.PublicId);

        readings.Metrics.Should().HaveCount(2);
        readings.Metrics.Should().ContainSingle(metric => metric.Code == "tempC")
            .Which.Value.Should().Be(29m, "the newest sample wins");
        readings.Metrics.Should().ContainSingle(metric => metric.Code == "lightLux")
            .Which.CapturedAt.Should().Be(Now.AddMinutes(-5), "its own sample's timestamp travels with it");
    }

    [Fact]
    public async Task LatestReadings_orders_metrics_by_key_so_cards_keep_their_place()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            default,
            (MetricCode.TempC, 28m),
            (MetricCode.HumidityPct, 35m),
            (MetricCode.LightLux, 400m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Metrics.Select(metric => metric.Code)
            .Should().BeInAscendingOrder(StringComparer.Ordinal)
            .And.OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(28, ReadingStatus.InRange)]
    [InlineData(33, ReadingStatus.OutOfRange)]
    [InlineData(40, ReadingStatus.Critical)]
    public async Task LatestReadings_classifies_a_value_against_the_effective_band(
        double value,
        string expected)
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            default,
            (MetricCode.TempC, (decimal)value));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Status.Should().Be(expected);
        metric.Target.Should().Be(new MetricBand(26m, 32m));
    }

    [Fact]
    public async Task LatestReadings_prefers_a_terrarium_override_over_the_profile_band()
    {
        var (terrarium, device, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 31m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Target.Should().Be(new MetricBand(28m, 30m));
        metric.Status.Should().Be(
            ReadingStatus.OutOfRange,
            "31 °C is inside the profile band but outside this terrarium's override");
    }

    [Fact]
    public async Task LatestReadings_uses_the_night_band_outside_the_photoperiod()
    {
        var (terrarium, device, profile) = Arrange();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);

        // The fake clock converts to UTC, so 23:00 is outside the 07:00–19:00 window.
        var night = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);
        _store.AddSample(terrarium.Id, device.Id, night, default, (MetricCode.TempC, 26m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Target.Should().Be(new MetricBand(20m, 24m), "the night band applies after lights-off");
        metric.Status.Should().Be(ReadingStatus.OutOfRange);
    }

    [Fact]
    public async Task LatestReadings_marks_a_faulted_sample_unavailable()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(
            terrarium.Id,
            device.Id,
            Now,
            QualityFlags.SensorFault,
            (MetricCode.TempC, 28m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var metric = outcome.Readings!.Metrics.Single();

        metric.Status.Should().Be(ReadingStatus.Unavailable);
        metric.QualityFlags.Should().Be((int)QualityFlags.SensorFault);
    }

    [Fact]
    public async Task LatestReadings_marks_an_implausible_value_unavailable_however_plausible_the_flags_look()
    {
        var (terrarium, device, _) = Arrange();
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 85m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Metrics.Single().Status.Should().Be(ReadingStatus.Unavailable);
    }

    [Fact]
    public async Task LatestReadings_marks_a_maintenance_device_as_maintenance()
    {
        var (terrarium, device, _) = Arrange(deviceStatus: DeviceStatus.Maintenance);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 28m));

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.Metrics.Single().Status.Should().Be(ReadingStatus.Maintenance);
        readings.Device!.Status.Should().Be("maintenance");
    }

    [Fact]
    public async Task LatestReadings_is_empty_rather_than_null_timestamped_before_the_first_sample()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var readings = outcome.Readings!;

        readings.LastSampleAt.Should().BeNull();
        readings.Metrics.Should().BeEmpty("both clients parse capturedAt unconditionally, so a metric without a sample is absent");
    }

    [Fact]
    public async Task LatestReadings_has_no_device_when_none_is_claimed()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Readings!.Device.Should().BeNull();
        outcome.Readings.Metrics.Should().BeEmpty();
    }

    [Fact]
    public async Task LatestReadings_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.LatestReadingsAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
    }

    [Fact]
    public async Task LatestReadings_of_another_accounts_terrarium_is_served_to_an_administrator()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);
        _store.AddSample(terrarium.Id, device.Id, Now.AddMinutes(-1), default, (MetricCode.TempC, 28m));

        var outcome = await _service.LatestReadingsAsync(
            terrarium.Id,
            _owner,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        outcome.Readings!.Metrics.Should().ContainSingle().Which.Code.Should().Be("tempC");
    }

    // ---- effective thresholds (FR-10, BR-10.3, roadmap 3.1) ------------------------------------------

    [Fact]
    public async Task EffectiveThresholds_lists_every_configured_metric_with_the_layer_it_came_from()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Thresholds!.TerrariumId.Should().Be(terrarium.Id);
        outcome.Thresholds.CapturedAtUtc.Should().Be(Now);
        outcome.Thresholds.TimeZoneId.Should().Be("UTC");

        // Sorted by metric key, the same way the cards are, so the editor's rows do not shuffle between reloads.
        outcome.Thresholds.EffectiveThresholds.Select(entry => entry.Metric)
            .Should().Equal("humidityPct", "tempC");

        var temperature = outcome.Thresholds.EffectiveThresholds[1];
        temperature.Source.Should().Be("override");
        temperature.Phase.Should().Be("any");
        temperature.Unit.Should().Be("°C");
        temperature.TargetMin.Should().Be(28m);
        temperature.TargetMax.Should().Be(30m);

        var humidity = outcome.Thresholds.EffectiveThresholds[0];
        humidity.Source.Should().Be("profile");
        humidity.TargetMin.Should().Be(30m);
        humidity.TargetMax.Should().Be(40m);
    }

    [Fact]
    public async Task EffectiveThresholds_reports_the_phase_the_card_is_being_judged_in()
    {
        var (terrarium, _, _) = Arrange();
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);
        var temperature = outcome.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");

        temperature.Phase.Should().Be("night");
        temperature.TargetMin.Should().Be(20m);
        temperature.TargetMax.Should().Be(24m);
    }

    [Fact]
    public async Task EffectiveThresholds_omits_a_metric_that_has_no_band_for_this_phase()
    {
        var profile = _store.AddProfile();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);

        var outcome = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        // Absent rather than present with null bounds: an absent row is what the editor unions with the dictionary
        // to show as unconfigured, and the evaluator treats it as unjudged.
        outcome.Thresholds!.EffectiveThresholds.Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveThresholds_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.EffectiveThresholdsAsync(foreign.Id, _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
    }

    [Fact]
    public async Task EffectiveThresholds_of_a_missing_terrarium_is_not_found()
    {
        var outcome = await _service.EffectiveThresholdsAsync(Guid.NewGuid(), _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task EffectiveThresholds_agrees_with_the_band_the_card_is_classified_against()
    {
        var (terrarium, device, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);
        _store.AddSample(terrarium.Id, device.Id, Now, default, (MetricCode.TempC, 31m));

        var readings = await _service.LatestReadingsAsync(terrarium.Id, _owner, CancellationToken.None);
        var thresholds = await _service.EffectiveThresholdsAsync(terrarium.Id, _owner, CancellationToken.None);

        var card = readings.Readings!.Metrics.Single(metric => metric.Code == "tempC");
        var resolved = thresholds.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");

        // The whole point of sharing the resolver: an editor previewing this band is previewing the band the card
        // was coloured with, so the two cannot drift apart.
        card.Target.Should().Be(new MetricBand(resolved.TargetMin, resolved.TargetMax));
        card.Status.Should().Be(ReadingStatus.OutOfRange);
    }

    // ---- threshold write (FR-10, roadmap 3.1's write half) -------------------------------------------

    [Fact]
    public async Task UpsertThresholds_writes_the_override_and_a_snapshot_of_the_new_set()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();

        // The write answers with the set now in force, so an editor does not have to re-read to re-render.
        var temperature = outcome.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");
        temperature.Source.Should().Be("override");
        temperature.TargetMin.Should().Be(28m);
        temperature.TargetMax.Should().Be(30m);

        var stored = terrarium.ThresholdOverrides.Should().ContainSingle().Which;
        stored.Metric.Should().Be(MetricCode.TempC);
        stored.TargetMin.Should().Be(28m);
        stored.CreatedByUserId.Should().Be(_owner);

        // TC-U-26: the change records one snapshot of the set now in force, hashed so it can be linked to later.
        var snapshot = _store.ThresholdSnapshots.Should().ContainSingle().Which;
        snapshot.TerrariumId.Should().Be(terrarium.Id);
        snapshot.CapturedAtUtc.Should().Be(Now);
        snapshot.EffectiveThresholdsJson.Should().Contain("\"targetMin\":28");
        snapshot.EffectiveThresholdsJson.Should().Contain("\"source\":\"override\"");
        snapshot.ThresholdVersionHash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task UpsertThresholds_records_the_snapshot_with_the_new_band_and_not_the_old_one()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 24m, 26m);

        await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        // The snapshot is resolved after the change, so it holds the band the next sample will be judged against.
        _store.ThresholdSnapshots.Single().EffectiveThresholdsJson
            .Should().Contain("\"targetMin\":28").And.NotContain("\"targetMin\":24");
    }

    [Fact]
    public async Task UpsertThresholds_of_the_same_band_again_is_a_no_op()
    {
        var (terrarium, _, _) = Arrange();

        await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        // Re-saving an unchanged editor page is not history: nothing is written and no second snapshot is recorded.
        outcome.Succeeded.Should().BeTrue();
        _store.ThresholdSnapshots.Should().ContainSingle();
        _store.SaveCount.Should().Be(1);
        terrarium.ThresholdOverrides.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertThresholds_keeps_a_metric_the_body_did_not_mention()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 24m, 26m);

        await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.HumidityPct, 50m, 70m)]),
            _owner,
            CancellationToken.None);

        // The request is a patch over the override set, not a replacement.
        terrarium.ThresholdOverrides.Should().HaveCount(2);
        terrarium.ThresholdOverrides.Single(row => row.Metric == MetricCode.TempC).TargetMin.Should().Be(24m);
        terrarium.ThresholdOverrides.Single(row => row.Metric == MetricCode.HumidityPct).TargetMin.Should().Be(50m);
    }

    [Fact]
    public async Task UpsertThresholds_defaults_the_engine_parameters_it_was_not_given()
    {
        var (terrarium, _, _) = Arrange();

        await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        var stored = terrarium.ThresholdOverrides.Single();
        stored.DwellWarnMinutes.Should().Be(2);
        stored.DwellCritMinutes.Should().Be(2);
        stored.RecoveryMargin.Should().Be(0.5m);
        stored.Enabled.Should().BeTrue();
        stored.Phase.Should().Be(ThresholdPhase.Any);
    }

    [Fact]
    public async Task UpsertThresholds_rejects_an_inverted_band_and_writes_nothing()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 40m, 30m)]),
            _owner,
            CancellationToken.None);

        // TC-U-21: refusal carries the stable code and the field, and nothing is persisted.
        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors!.Select(violation => violation.Code).Should().Contain("threshold_ordering_invalid");
        terrarium.ThresholdOverrides.Should().BeEmpty();
        _store.ThresholdSnapshots.Should().BeEmpty();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_rejects_a_critical_band_that_does_not_enclose_the_target()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 26m, 32m, criticalMin: 28m, criticalMax: 34m)]),
            _owner,
            CancellationToken.None);

        // TC-U-22.
        outcome.Problem!.Errors!.Select(violation => violation.Code).Should().Contain("threshold_critical_invalid");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_rejects_a_dwell_and_a_recovery_margin_that_cannot_hold()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest(
            [
                Override(MetricCode.TempC, 26m, 32m, dwellWarn: 5, dwellCrit: 6) with { RecoveryMargin = 4m },
            ]),
            _owner,
            CancellationToken.None);

        // TC-U-23: both rules are reported field by field rather than as one opaque refusal.
        outcome.Problem!.Errors!.Select(violation => violation.Code).Should().BeEquivalentTo(
        [
            "threshold_dwell_invalid",
            "threshold_recovery_margin_invalid",
        ]);
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_reports_every_bad_entry_it_was_given()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest(
            [
                Override(MetricCode.TempC, 26m, 32m) with { Metric = "luminosity", Phase = "dusk" },
                Override(MetricCode.TempC, 26m, 32m) with { TargetMin = null, TargetMax = null },
            ]),
            _owner,
            CancellationToken.None);

        // One bad entry does not hide the next, and the fields are addressed by their position in the body.
        outcome.Problem!.Errors!.Select(violation => (violation.Field, violation.Code)).Should().BeEquivalentTo(
        [
            ($"overrides[0].metric", "metric_invalid"),
            ($"overrides[0].phase", "threshold_phase_invalid"),
            ($"overrides[1].targetMin", "threshold_target_required"),
            ($"overrides[1].targetMax", "threshold_target_required"),
        ]);
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_refuses_two_bands_for_one_metric_and_phase()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest(
            [
                Override(MetricCode.TempC, 26m, 32m),
                Override(MetricCode.TempC, 28m, 30m),
            ]),
            _owner,
            CancellationToken.None);

        // The table has one row per (terrarium, metric, phase); the second entry is the one refused.
        var duplicate = outcome.Problem!.Errors!.Single();
        duplicate.Code.Should().Be("threshold_duplicate");
        duplicate.Field.Should().Be("overrides[1].metric");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_records_a_change_even_when_the_phase_in_force_does_not_use_it()
    {
        var (terrarium, _, _) = Arrange();
        _clock.UtcNow = new DateTimeOffset(2026, 10, 3, 23, 0, 0, TimeSpan.Zero);

        var outcome = await _service.UpsertThresholdsAsync(
            terrarium.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m, phase: "day")]),
            _owner,
            CancellationToken.None);

        // A Day band is silent at 23:00, so the band in force is still the profile's night band…
        outcome.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC").Source.Should().Be("profile");

        // …but the edit is still a change to this terrarium's configuration, so it is recorded (UC-03 A4).
        _store.ThresholdSnapshots.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertThresholds_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.UpsertThresholdsAsync(
            foreign.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
        foreign.ThresholdOverrides.Should().BeEmpty();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpsertThresholds_of_another_accounts_terrarium_succeeds_for_an_administrator()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.UpsertThresholdsAsync(
            foreign.Id,
            new UpdateThresholdsRequest([Override(MetricCode.TempC, 28m, 30m)]),
            _owner,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        foreign.ThresholdOverrides.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteThresholdOverride_reverts_the_metric_to_the_profile_and_snapshots()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);

        var outcome = await _service.DeleteThresholdOverrideAsync(
            terrarium.Id,
            "tempC",
            _owner,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.ThresholdOverrides.Should().BeEmpty();

        // The band in force is the profile's day band again, and the revert is snapshotted like any other change.
        var temperature = outcome.Thresholds!.EffectiveThresholds.Single(entry => entry.Metric == "tempC");
        temperature.Source.Should().Be("profile");
        temperature.TargetMin.Should().Be(26m);
        _store.ThresholdSnapshots.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteThresholdOverride_removes_every_phase_of_the_metric()
    {
        var (terrarium, _, _) = Arrange();
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 28m, 30m);
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.TempC, 21m, 23m, ThresholdPhase.Night);
        FakeTerrariumStore.AddOverride(terrarium, MetricCode.HumidityPct, 50m, 70m);

        await _service.DeleteThresholdOverrideAsync(terrarium.Id, "tempC", _owner, CancellationToken.None);

        // "Revert this metric" is the whole metric, not the phase the caller happened to be looking at.
        terrarium.ThresholdOverrides.Should().ContainSingle().Which.Metric.Should().Be(MetricCode.HumidityPct);
    }

    [Fact]
    public async Task DeleteThresholdOverride_of_a_metric_without_an_override_is_a_no_op()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.DeleteThresholdOverrideAsync(
            terrarium.Id,
            "tempC",
            _owner,
            CancellationToken.None);

        // Idempotent: a retry that failed would leave the caller believing the override was still in force.
        outcome.Succeeded.Should().BeTrue();
        _store.SaveCount.Should().Be(0);
        _store.ThresholdSnapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteThresholdOverride_refuses_an_unknown_metric_key()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.DeleteThresholdOverrideAsync(
            terrarium.Id,
            "luminosity",
            _owner,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors!.Single().Code.Should().Be("metric_invalid");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteThresholdOverride_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);
        FakeTerrariumStore.AddOverride(foreign, MetricCode.TempC, 28m, 30m);

        var outcome = await _service.DeleteThresholdOverrideAsync(foreign.Id, "tempC", _owner, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
        foreign.ThresholdOverrides.Should().ContainSingle();
    }

    // ---- readings (range) ---------------------------------------------------------------------------

    [Theory]
    [InlineData(1, "raw")]
    [InlineData(24, "5min")]
    [InlineData(24 * 30, "hourly")]
    public async Task Readings_echoes_the_bucket_the_range_width_selects(int hours, string expectedBucket)
    {
        var (terrarium, _, _) = Arrange();
        var from = Now.AddHours(-hours);

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            Now,
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be(expectedBucket);
    }

    [Fact]
    public async Task Readings_emits_every_bucket_so_a_gap_is_a_null_point_and_not_a_straight_line()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // Wider than six hours, so the series is five-minute buckets (BR-09.1) and the holes inside it are explicit.
        var to = from.AddHours(6).AddMinutes(10);

        _store.AddSample(terrarium.Id, device.Id, from, default, (MetricCode.TempC, 28m));
        _store.AddSample(terrarium.Id, device.Id, from.AddMinutes(20), default, (MetricCode.TempC, 30m));

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            to,
            CancellationToken.None);

        var series = outcome.Series!;

        series.Bucket.Should().Be("5min");
        series.Points.Should().HaveCount(74);
        series.Points[0].Should().Be(new ReadingPoint(from, 28m, 28m, 28m, 1));
        series.Points[4].Should().Be(new ReadingPoint(from.AddMinutes(20), 30m, 30m, 30m, 1));
        series.Points[1].Should().Be(
            new ReadingPoint(from.AddMinutes(5), null, null, null, 0),
            "an interval with no data is null with count 0 (BR-09.5)");
    }

    [Fact]
    public async Task Readings_aggregates_min_max_avg_and_count_per_bucket()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // Three samples inside one five-minute bucket of a window wide enough to be bucketed at all.
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(10), default, (MetricCode.TempC, 20m));
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(60), default, (MetricCode.TempC, 26m));
        _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(120), default, (MetricCode.TempC, 22m));

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            from.AddHours(6).AddMinutes(5),
            CancellationToken.None);

        var point = outcome.Series!.Points[0];

        point.T.Should().Be(from);
        point.Min.Should().Be(20m);
        point.Max.Should().Be(26m);
        point.Avg.Should().Be(22.667m);
        point.Count.Should().Be(3);
    }

    [Fact]
    public async Task Readings_degrades_to_five_minute_buckets_when_raw_would_break_the_point_budget()
    {
        var (terrarium, device, _) = Arrange();
        var from = new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

        // 721 samples inside a raw-width window: a 10 s device at the fastest allowed interval.
        for (var index = 0; index <= RangeQueryRules.MaxBucketPoints; index++)
        {
            _store.AddSample(terrarium.Id, device.Id, from.AddSeconds(index * 10), default, (MetricCode.TempC, 28m));
        }

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            from.AddHours(2),
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be("5min");
        outcome.Series.Points.Should().HaveCountLessOrEqualTo(RangeQueryRules.MaxBucketPoints);
    }

    [Fact]
    public async Task Readings_never_exceeds_the_point_budget_even_when_the_window_is_not_bucket_aligned()
    {
        var (terrarium, _, _) = Arrange();

        // A range that starts 17 minutes past the hour: the first hourly bucket starts before the window, which is
        // exactly the case that would otherwise produce a 721st point (NFR-02, TC-I-10).
        var to = new DateTimeOffset(2026, 10, 3, 14, 17, 0, TimeSpan.Zero);
        var from = to.AddDays(-30);

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            from,
            to,
            CancellationToken.None);

        outcome.Series!.Bucket.Should().Be("hourly");
        outcome.Series.Points.Should().HaveCount(RangeQueryRules.MaxBucketPoints);
    }

    [Fact]
    public async Task Readings_refuses_a_range_longer_than_thirty_days()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            Now.AddDays(-31),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("range_too_large");
    }

    [Fact]
    public async Task Readings_refuses_a_reversed_range()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.ReadingsAsync(
            terrarium.Id,
            _owner,
            MetricCode.TempC,
            Now,
            Now.AddHours(-1),
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("invalid_range");
    }

    [Fact]
    public async Task Readings_of_another_accounts_terrarium_is_forbidden()
    {
        var profile = _store.AddProfile();
        var foreign = _store.AddTerrarium(Guid.NewGuid(), "Theirs", profile);

        var outcome = await _service.ReadingsAsync(
            foreign.Id,
            _owner,
            MetricCode.TempC,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
    }

    // ---- coverage -----------------------------------------------------------------------------------

    [Fact]
    public async Task Coverage_compares_received_samples_with_what_the_interval_implies()
    {
        var (terrarium, device, _) = Arrange();

        // 60 s interval over one hour is 60 expected samples; 45 arrive.
        for (var index = 0; index < 45; index++)
        {
            _store.AddSample(terrarium.Id, device.Id, Now.AddHours(-1).AddSeconds(index * 60), default, (MetricCode.TempC, 28m));
        }

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        var coverage = outcome.Coverage!;

        coverage.SamplingIntervalSec.Should().Be(60);
        coverage.ExpectedSamples.Should().Be(60);
        coverage.ReceivedSamples.Should().Be(45);
        coverage.CoveragePct.Should().Be(75m);
    }

    [Fact]
    public async Task Coverage_without_a_bound_device_is_a_conflict()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddHours(-1),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("device_not_bound");
    }

    [Fact]
    public async Task Coverage_accepts_a_window_wider_than_the_chart_limit_but_inside_retention()
    {
        var (terrarium, _, _) = Arrange();

        var outcome = await _service.CoverageAsync(
            terrarium.Id,
            _owner,
            Now.AddDays(-60),
            Now,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
    }

    /// <summary>A terrarium with a day/night temperature band pair, a bound online device and a species profile.</summary>
    private (Terrarium Terrarium, Device Device, SpeciesProfile Profile) Arrange(
        DeviceStatus deviceStatus = DeviceStatus.Online)
    {
        var profile = _store.AddProfile();
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 26m, 32m, ThresholdPhase.Day);
        FakeTerrariumStore.AddBand(profile, MetricCode.TempC, 20m, 24m, ThresholdPhase.Night);
        FakeTerrariumStore.AddBand(profile, MetricCode.HumidityPct, 30m, 40m);

        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, deviceStatus, Now);

        return (terrarium, device, profile);
    }

    /// <summary>One band for a write request, with the metric spelled the way a client sends it.</summary>
    private static ThresholdOverrideInput Override(
        MetricCode metric,
        decimal targetMin,
        decimal targetMax,
        string phase = "any",
        decimal? criticalMin = null,
        decimal? criticalMax = null,
        int? dwellWarn = null,
        int? dwellCrit = null,
        string? note = null) =>
        new(
            MetricDictionary.Get(metric).ApiKey,
            phase,
            targetMin,
            targetMax,
            criticalMin,
            criticalMax,
            dwellWarn,
            dwellCrit,
            null,
            note,
            null);

    // ---- update and delete (FR-03, roadmap 2.8's remaining half) -------------------------------------

    [Fact]
    public async Task Update_changes_the_field_it_was_given_and_leaves_the_rest_alone()
    {
        var profile = _store.AddProfile("Leopard gecko");
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.Location = "desk";
        terrarium.Description = "notes";
        var rowVersion = terrarium.RowVersion!;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Alpha renamed", null, null, null, null),
            _owner,
            rowVersion,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.Name.Should().Be("Alpha renamed");
        terrarium.Location.Should().Be("desk");
        terrarium.Description.Should().Be("notes");
        terrarium.UpdatedAt.Should().Be(Now);

        // The response carries a token: without one the client could not make a second edit.
        outcome.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_clears_a_free_text_field_when_it_is_sent_empty()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.Location = "desk";
        terrarium.Description = "notes";

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, string.Empty, string.Empty, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        // An omitted member means "leave it"; an empty string is the one way a JSON body can say "clear it".
        outcome.Succeeded.Should().BeTrue();
        terrarium.Location.Should().BeNull();
        terrarium.Description.Should().BeNull();
    }

    [Fact]
    public async Task Update_leaves_the_name_alone_when_it_is_omitted()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, "windowsill", null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Name.Should().Be("Alpha");
        terrarium.Location.Should().Be("windowsill");
    }

    [Fact]
    public async Task Update_refuses_a_stale_rowversion_and_writes_nothing()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var savesBefore = _store.SaveCount;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [9, 9, 9, 9, 9, 9, 9, 9],
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("precondition_failed");

        // The point of a concurrency token: the stale writer does not overwrite what it never read.
        terrarium.Name.Should().Be("Alpha");
        _store.SaveCount.Should().Be(savesBefore);
    }

    [Fact]
    public async Task Update_accepts_the_wildcard_rowversion()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // "If-Match: *" is existence-only, which the endpoint hands over as an empty token.
        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [],
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Update_reports_a_race_lost_at_commit_as_precondition_failed()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // The token matched when the row was read, and another writer moved it before the write landed — a second
        // window the read-time comparison cannot close, which is why the store reports it too.
        _store.RowVersionConflict = true;

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("precondition_failed");
    }

    [Fact]
    public async Task Update_reports_a_foreign_terrarium_as_forbidden()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(Guid.NewGuid(), "Someone else's", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Stolen", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        // Refused as "not yours" rather than "not found" (TASKS 2026-10-14, item 3) — and refused either way, so the
        // name is untouched.
        outcome.Problem!.Code.Should().Be("forbidden_terrarium");
        terrarium.Name.Should().Be("Someone else's");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Update_of_another_accounts_terrarium_is_allowed_for_an_administrator_and_audited()
    {
        var profile = _store.AddProfile();
        var owner = Guid.NewGuid();
        var terrarium = _store.AddTerrarium(owner, "Theirs", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed by support", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Name.Should().Be("Renamed by support");

        // Recorded with the administrator as the actor and the owner in the payload, which is the point of the row:
        // "who changed whose enclosure" has two answers and the trail has to carry both (BR-18.4).
        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.TerrariumUpdated);
        entry.EntityName.Should().Be(AuditEntity.Terrarium);
        entry.EntityId.Should().Be(terrarium.Id.ToString());
        entry.UserId.Should().Be(_owner);
        entry.BeforeJson.Should().Contain("Theirs");
        entry.AfterJson.Should().Contain("Renamed by support");
    }

    [Fact]
    public async Task Update_by_the_owner_writes_no_audit_row()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.OwnerUserId.Should().BeNull("an ordinary keeper's answer carries no owner block");
        _store.AuditEntries.Should().BeEmpty("the trail records the administrator's cross-account actions, not the "
                                           + "ordinary edit a keeper makes to their own terrarium");
    }

    [Fact]
    public async Task Create_by_an_administrator_leaves_the_enclosure_unowned()
    {
        var profile = _store.AddProfile();

        var outcome = await _service.CreateAsync(
            new CreateTerrariumRequest("Unassigned", profile.Id, null, null, null),
            _owner,
            CancellationToken.None,
            mayReachAnyAccount: true);

        // An administrator works from the whole deployment, so guessing an owner would put a keeper's name on an
        // enclosure that keeper never asked for. The owner is assigned afterwards (2026-10-23).
        outcome.Succeeded.Should().BeTrue();
        outcome.Terrarium!.OwnerUserId.Should().BeNull();
        _store.Terrariums.Should().ContainSingle().Which.UserId.Should().BeNull();
    }

    [Fact]
    public async Task Update_reassigns_the_species_profile_and_can_clear_it()
    {
        var profile = _store.AddProfile("Arid (desert)");
        var tropical = _store.AddProfile("Tropical (humid forest)");
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var changed = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, tropical.Id, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        changed.Succeeded.Should().BeTrue();
        changed.Terrarium!.SpeciesProfileId.Should().Be(tropical.Id);
        changed.Terrarium.SpeciesName.Should().Be("Tropical (humid forest)");

        // The all-zero GUID is this patch's "empty": null would mean "leave it", which is unexpressible as a clear.
        var cleared = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, Guid.Empty, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        cleared.Succeeded.Should().BeTrue();
        terrarium.SpeciesProfileId.Should().BeNull("an enclosure may be left without a species");
        cleared.Terrarium!.SpeciesName.Should().BeNull();
    }

    [Fact]
    public async Task Update_leaves_the_species_profile_alone_when_the_member_is_omitted()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.SpeciesProfileId.Should().Be(profile.Id);
    }

    [Fact]
    public async Task Update_refuses_an_unknown_species_profile()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, Guid.NewGuid(), null, null, null),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors!.Should().ContainSingle().Which.Code.Should().Be("species_profile_not_found");
        terrarium.SpeciesProfileId.Should().Be(profile.Id);
    }

    [Fact]
    public async Task Update_assigns_an_unowned_enclosure_to_an_account_and_audits_it()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        terrarium.UserId = null;
        var keeper = Guid.NewGuid();
        _store.AddUser(keeper, "keeper@example.test");

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, null, null, null, OwnerUserId: keeper),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        terrarium.UserId.Should().Be(keeper);

        // The answer names the owner, exactly as the detail read does: an assignment that answered without it would
        // leave the administrator's screen saying the enclosure belongs to nobody (2026-10-24).
        outcome.Terrarium!.OwnerUserId.Should().Be(keeper);
        outcome.Terrarium.OwnerEmail.Should().Be("keeper@example.test");

        // Its own verb: "whose enclosure is this" changing hands is exactly what a later reader asks about.
        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.TerrariumOwnerChanged);
        entry.UserId.Should().Be(_owner);
        entry.BeforeJson.Should().Contain("\"userId\":null");
        entry.AfterJson.Should().Contain(keeper.ToString());
    }

    [Fact]
    public async Task Update_moves_the_bound_board_with_the_enclosure()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);
        device.UserId = _owner;
        var keeper = Guid.NewGuid();
        _store.AddUser(keeper);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, null, null, null, OwnerUserId: keeper),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();

        // The board stays in the enclosure — the reading history and the DI-04 slot are untouched — but the new
        // keeper's fleet has to list it, or the enclosure would show a board nobody can configure.
        device.TerrariumId.Should().Be(terrarium.Id);
        device.UserId.Should().Be(keeper);
    }

    [Fact]
    public async Task Update_refuses_an_owner_change_from_a_keeper()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var someoneElse = Guid.NewGuid();
        _store.AddUser(someoneElse);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, null, null, null, OwnerUserId: someoneElse),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors!.Should().ContainSingle().Which.Code.Should().Be("owner_change_forbidden");
        terrarium.UserId.Should().Be(_owner);
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Update_refuses_an_owner_that_is_not_an_account()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(null, null, null, null, null, OwnerUserId: Guid.NewGuid()),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Problem!.Code.Should().Be("validation_failed");
        outcome.Problem.Errors!.Should().ContainSingle().Which.Code.Should().Be("account_not_found");
    }

    [Fact]
    public async Task Delete_of_another_accounts_terrarium_is_allowed_for_an_administrator_and_audited()
    {
        var profile = _store.AddProfile();
        var owner = Guid.NewGuid();
        var terrarium = _store.AddTerrarium(owner, "Theirs", profile);

        var outcome = await _service.DeleteAsync(
            terrarium.Id,
            _owner,
            allowUnboundDevice: false,
            CancellationToken.None,
            mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Status.Should().Be(TerrariumStatus.Deleted);

        var entry = _store.AuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.TerrariumDeleted);
        entry.UserId.Should().Be(_owner);
        entry.BeforeJson.Should().Contain("Active");
    }

    [Fact]
    public async Task Update_validates_every_field_it_was_given()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.UpdateAsync(
            terrarium.Id,
            new UpdateTerrariumRequest(
                string.Empty,
                Guid.NewGuid(),
                new string('x', 121),
                new string('y', 1001),
                "Middle-earth/Shire"),
            _owner,
            terrarium.RowVersion!,
            CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("validation_failed");

        outcome.Problem.Errors!.Select(violation => violation.Code).Should().BeEquivalentTo(
        [
            "terrarium_name_required",
            "species_profile_not_found",
            "terrarium_location_too_long",
            "terrarium_description_too_long",
            "terrarium_timezone_invalid",
        ]);
    }

    [Fact]
    public async Task Update_reports_a_missing_terrarium_as_not_found()
    {
        var outcome = await _service.UpdateAsync(
            Guid.NewGuid(),
            new UpdateTerrariumRequest("Renamed", null, null, null, null),
            _owner,
            [1, 2, 3, 4, 5, 6, 7, 8],
            CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Delete_refuses_a_bound_device_without_the_flag()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("conflict_device_bound");
        terrarium.Status.Should().Be(TerrariumStatus.Active, "a refused delete must leave the state alone");
        terrarium.DeletedAt.Should().BeNull();
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_with_the_flag_unbinds_the_device_and_soft_deletes()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);
        var device = FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Online, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: true, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Status.Should().Be(TerrariumStatus.Deleted);
        terrarium.DeletedAt.Should().Be(Now);
        _store.SaveCount.Should().Be(1);

        // The board is released rather than orphaned: it keeps its credentials and owner, but ingest refuses an
        // unbound device, so it stops writing into an enclosure nobody can see.
        device.TerrariumId.Should().BeNull();
        device.Status.Should().Be(DeviceStatus.Provisioning);
        device.ProvisionedAt.Should().BeNull();
    }

    [Fact]
    public async Task Delete_deletes_an_unbound_terrarium_without_the_flag()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
        terrarium.Status.Should().Be(TerrariumStatus.Deleted);
        terrarium.DeletedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Delete_does_not_ask_about_a_revoked_device()
    {
        var profile = _store.AddProfile();
        var terrarium = _store.AddTerrarium(_owner, "Alpha", profile);

        // A revoked device has already released the slot, so requiring the flag would make the owner delete twice
        // for no reason.
        FakeTerrariumStore.AddDevice(terrarium, DeviceStatus.Revoked, Now);

        var outcome = await _service.DeleteAsync(terrarium.Id, _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_reports_a_missing_terrarium_as_not_found()
    {
        var outcome = await _service.DeleteAsync(Guid.NewGuid(), _owner, allowUnboundDevice: false, CancellationToken.None);

        outcome.Problem!.Code.Should().Be("not_found");
    }
}
