using FluentAssertions;
using SmartReptile.Application.Devices;
using SmartReptile.Domain.Auditing;
using SmartReptile.Domain.Devices;
using SmartReptile.Tests.Unit.Application;

namespace SmartReptile.Tests.Unit.Devices;

/// <summary>
/// Task 2.2 — the onboarding flow of UC-01: TC-I-05 (self-register → claim → credentials) and the parts of
/// TC-I-13 that live in this service (revoked device refused, expired and consumed codes indistinguishable,
/// foreign terrarium hidden). The MQTT half of TC-I-05 belongs to task 2.3.
/// </summary>
public class DeviceProvisioningServiceTests
{
    private const string Ip = "198.51.100.4";
    private const string ChipId = "A0B1C2D3E4F5";
    private const string Mac = "A0:B1:C2:D3:E4:F5";

    private readonly TestClock _clock = new();
    private readonly FakeProvisioningStore _store = new();
    private readonly FakeDeviceCredentials _credentials = new();
    private readonly FakeOnboardingThrottleStore _throttle = new();
    private readonly FakeDeviceSessionRegistry _sessions = new();
    private readonly DeviceProvisioningService _service;
    private readonly Guid _owner = Guid.NewGuid();
    private readonly AuditActor _actor = new("203.0.113.7", "curl/8.5.0", "corr-m2-audit");

    public DeviceProvisioningServiceTests() =>
        _service = new DeviceProvisioningService(
            _store,
            new FakeClaimCodeGenerator(),
            _credentials,
            _throttle,
            _sessions,
            _clock,
            new ProvisioningSettings(ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength, 15, OnboardingLimits.Documented),
            new DeviceSilenceSettings(3));

    // ---- self-register ------------------------------------------------------------------------------

    [Fact]
    public async Task SelfRegister_creates_a_provisioning_device_holding_a_claim_code()
    {
        var outcome = await SelfRegisterAsync();

        outcome.Succeeded.Should().BeTrue();

        var result = outcome.Registration!;
        result.AlreadyRegistered.Should().BeFalse();
        result.DeviceId.Should().StartWith("sr-");
        ClaimCode.IsWellFormed(result.ClaimCode, ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength)
            .Should().BeTrue();
        result.ExpiresAtUtc.Should().Be(_clock.UtcNow.AddMinutes(15));

        var device = _store.Devices.Should().ContainSingle().Subject;
        device.Status.Should().Be(DeviceStatus.Provisioning);
        device.TerrariumId.Should().BeNull();
        device.ChipId.Should().Be(ChipId);
        device.Credentials.Should().BeEmpty("a secret is issued at claim time, not at registration");
    }

    [Fact]
    public async Task SelfRegister_reissues_a_code_for_a_board_that_never_claimed()
    {
        var first = (await SelfRegisterAsync()).Registration!;

        // Past the per-address window, which is what the documented 1-per-5-minutes limit means in practice.
        _clock.Advance(TimeSpan.FromMinutes(6));

        var second = (await SelfRegisterAsync()).Registration!;

        second.AlreadyRegistered.Should().BeTrue();
        second.DeviceId.Should().Be(first.DeviceId);
        second.ClaimCode.Should().NotBe(first.ClaimCode, "the previous code may already have expired");
        _store.Devices.Should().ContainSingle("a reboot must not create a second device");
    }

    [Fact]
    public async Task SelfRegister_refuses_a_board_that_is_already_claimed()
    {
        var terrarium = _store.AddTerrarium(_owner);
        var registered = (await SelfRegisterAsync()).Registration!;
        (await _service.ClaimAsync(new ClaimRequest(registered.ClaimCode, terrarium.Id), _owner))
            .Succeeded.Should().BeTrue();

        _clock.Advance(TimeSpan.FromMinutes(6));
        var again = await SelfRegisterAsync();

        again.Problem!.Code.Should().Be("device_already_registered");
        again.Problem.DeviceId.Should().Be(registered.DeviceId);
        again.Registration.Should().BeNull("a claimed board is not offered a code");
    }

    [Fact]
    public async Task SelfRegister_is_throttled_after_the_first_attempt_from_one_address()
    {
        (await SelfRegisterAsync()).Succeeded.Should().BeTrue();

        var throttled = await SelfRegisterAsync();

        throttled.Problem!.Code.Should().Be("rate_limited");
        _store.Devices.Should().ContainSingle("a refused attempt must not write a row");
    }

    [Fact]
    public async Task SelfRegister_refuses_a_malformed_payload_with_field_errors()
    {
        var outcome = await _service.SelfRegisterAsync(
            new SelfRegisterRequest("bad chip!", "not-a-mac", null),
            Ip);

        outcome.Problem!.Code.Should().Be("registration_invalid");
        outcome.Problem.Errors!.Select(violation => violation.Code)
            .Should().Contain(["chip_id_invalid", "mac_invalid"]);
        _store.Devices.Should().BeEmpty();
    }

    [Theory]
    [InlineData("FF:FF:FF:FF:FF:FF", "broadcast")]
    [InlineData("00:00:00:00:00:00", "before Wi-Fi starts")]
    [InlineData("33:33:00:00:00:01", "group address")]
    public async Task SelfRegister_refuses_an_address_no_board_can_own(string mac, string because)
    {
        // What the board reports must be its own unicast station address; a group, broadcast or unset one would put
        // a row in the fleet that no ESP32 can ever match (2026-10-23).
        var outcome = await _service.SelfRegisterAsync(new SelfRegisterRequest(ChipId, mac, null), Ip);

        outcome.Problem!.Code.Should().Be("registration_invalid");
        outcome.Problem.Errors!.Single().Field.Should().Be("macAddress");
        outcome.Problem.Errors!.Single().Code.Should().Be("mac_invalid");
        outcome.Problem.Errors!.Single().Message.Should().Contain(because);
        _store.Devices.Should().BeEmpty();
    }

    [Fact]
    public async Task SelfRegister_stores_the_canonical_spelling_of_the_mac()
    {
        // A board that reports lower-case hex is the same board as one that reports upper case, so the row is stored
        // in the one form the claim-by-MAC lookup searches for.
        (await _service.SelfRegisterAsync(
            new SelfRegisterRequest(ChipId, "a0-b1-c2-d3-e4-f5", null),
            Ip)).Succeeded.Should().BeTrue();

        _store.Devices.Single().MacAddress.Should().Be("A0:B1:C2:D3:E4:F5");
    }

    // ---- claim --------------------------------------------------------------------------------------

    [Fact]
    public async Task Claim_binds_the_device_and_returns_the_secret_exactly_once()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();

        var outcome = await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner);

        outcome.Succeeded.Should().BeTrue();

        var claim = outcome.Claim!;
        claim.DeviceId.Should().Be(deviceId);
        claim.TerrariumId.Should().Be(terrariumId);
        claim.Secret.Should().NotBeNullOrEmpty();
        claim.BoundAtUtc.Should().Be(_clock.UtcNow);

        var device = _store.Devices.Single();
        device.TerrariumId.Should().Be(terrariumId);
        device.UserId.Should().Be(_owner);
        device.ClaimCode.Should().BeNull("the code is single use");
        device.ClaimCodeExpiresAt.Should().BeNull();

        // Only a digest and a salt reach the database.
        device.Credentials.Should().ContainSingle();
        device.Credentials.Single().SecretHash.Should().HaveCount(32);
        device.Credentials.Single().Salt.Should().HaveCount(16);
    }

    [Fact]
    public async Task Claim_names_the_board_when_the_request_does()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();

        // The claim dialog collects the name, so the fleet never shows "New node" for a board the keeper has just
        // named (2026-10-23).
        var outcome = await _service.ClaimAsync(
            new ClaimRequest(code, terrariumId, "  Gecko box node  "),
            _owner);

        outcome.Succeeded.Should().BeTrue();
        _store.Devices.Single(device => device.PublicId == deviceId).DeviceName.Should().Be("Gecko box node");
    }

    [Fact]
    public async Task Claim_falls_back_to_the_default_label_for_a_blank_name()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();

        var outcome = await _service.ClaimAsync(new ClaimRequest(code, terrariumId, "   "), _owner);

        outcome.Succeeded.Should().BeTrue();
        _store.Devices.Single(device => device.PublicId == deviceId).DeviceName.Should().Be("New node");
    }

    [Fact]
    public async Task Claim_refuses_a_name_longer_than_the_column()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();
        var savesBeforeClaim = _store.SaveCount;

        var outcome = await _service.ClaimAsync(
            new ClaimRequest(code, terrariumId, new string('a', DeviceLimits.NameMaxLength + 1)),
            _owner);

        // Refused before anything is written, so the board is not claimed with a label nobody can see in full.
        outcome.Problem!.Code.Should().Be("claim_invalid");
        outcome.Problem.Errors!.Single().Code.Should().Be("device_name_too_long");
        _store.Devices.Single(device => device.PublicId == deviceId).TerrariumId.Should().BeNull();
        _store.SaveCount.Should().Be(savesBeforeClaim, "the claim was refused, so it wrote nothing");
    }

    [Fact]
    public async Task ClaimByMac_names_the_board_when_the_request_does()
    {
        (await SelfRegisterAsync()).Succeeded.Should().BeTrue();
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(
            new ClaimByMacRequest(Mac, terrarium.Id, "Kitchen gecko"),
            _owner);

        outcome.Succeeded.Should().BeTrue();
        _store.Devices.Single().DeviceName.Should().Be("Kitchen gecko");
    }

    [Fact]
    public async Task Claim_accepts_the_code_as_the_keeper_would_type_it()
    {
        var (code, _, terrariumId) = await ArrangeAsync();

        var typed = ClaimCode.Format(code).ToLowerInvariant();

        var outcome = await _service.ClaimAsync(new ClaimRequest(typed, terrariumId), _owner);

        outcome.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Claim_rejects_an_expired_code()
    {
        var (code, _, terrariumId) = await ArrangeAsync();

        _clock.Advance(TimeSpan.FromMinutes(16));

        var outcome = await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner);

        outcome.Problem!.Code.Should().Be("claim_code_invalid");
    }

    [Fact]
    public async Task Claim_reports_a_consumed_code_exactly_like_an_unknown_one()
    {
        var (code, _, terrariumId) = await ArrangeAsync();
        (await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner)).Succeeded.Should().BeTrue();

        var consumed = await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner);
        var unknown = await _service.ClaimAsync(new ClaimRequest("K7M2QP4T", terrariumId), _owner);

        // BR-04.3: the two must be indistinguishable, message included.
        consumed.Problem!.Code.Should().Be("claim_code_invalid");
        unknown.Problem!.Code.Should().Be("claim_code_invalid");
        consumed.Problem.Message.Should().Be(unknown.Problem.Message);
        _store.Devices.Single().Credentials.Should().ContainSingle("a replay must not mint a second secret");
    }

    [Fact]
    public async Task Claim_hides_a_terrarium_the_caller_does_not_own()
    {
        var (code, _, _) = await ArrangeAsync();
        var someoneElses = _store.AddTerrarium(Guid.NewGuid());

        var outcome = await _service.ClaimAsync(new ClaimRequest(code, someoneElses.Id), _owner);

        // A foreign id and a missing id are the same answer (BR-02.2), so ids cannot be probed.
        outcome.Problem!.Code.Should().Be("not_found");
        _store.Devices.Single().TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task Claim_refuses_a_terrarium_that_already_has_a_device()
    {
        var terrarium = _store.AddTerrarium(_owner);
        var first = (await SelfRegisterAsync()).Registration!;
        (await _service.ClaimAsync(new ClaimRequest(first.ClaimCode, terrarium.Id), _owner))
            .Succeeded.Should().BeTrue();

        _clock.Advance(TimeSpan.FromMinutes(6));
        var second = (await SelfRegisterAsync("BBBBBBBBBBBB", "AA:BB:CC:DD:EE:02")).Registration!;

        var outcome = await _service.ClaimAsync(new ClaimRequest(second.ClaimCode, terrarium.Id), _owner);

        outcome.Problem!.Code.Should().Be("terrarium_already_bound");
    }

    [Fact]
    public async Task Claim_reports_a_race_the_database_won_as_the_same_conflict()
    {
        var terrarium = _store.AddTerrarium(_owner);
        var registration = (await SelfRegisterAsync()).Registration!;

        // The pre-check passed because the other writer had not committed yet; the filtered unique index DI-04
        // then refused the insert. That must read as the documented 409 rather than escaping as a 500 (TC-I-05).
        _store.ClaimRaceLost = true;

        var outcome = await _service.ClaimAsync(new ClaimRequest(registration.ClaimCode, terrarium.Id), _owner);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("terrarium_already_bound");
    }

    // ---- bind / move ---------------------------------------------------------------------------------

    [Fact]
    public async Task Move_binds_an_unbound_board_to_the_callers_terrarium()
    {
        var board = AddBoard("sr-free", DeviceStatus.Provisioning, terrariumId: null);
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.MoveAsync(board.PublicId, terrarium.Id, _owner, actor: _actor);

        outcome.Succeeded.Should().BeTrue();

        outcome.Move!.Device.TerrariumId.Should().Be(terrarium.Id);
        outcome.Move.Device.TerrariumName.Should().Be(terrarium.Name);
        outcome.Move.PreviousTerrariumId.Should().BeNull();
        outcome.Move.DisplacedDeviceId.Should().BeNull();

        var entry = _store.CommittedAuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.DeviceMoved);
        entry.EntityId.Should().Be(board.PublicId);
        entry.UserId.Should().Be(_owner);
        entry.IpAddress.Should().Be(_actor.IpAddress);
        entry.AfterJson.Should().Contain(terrarium.Id.ToString());
    }

    [Fact]
    public async Task Move_takes_the_board_out_of_the_terrarium_it_held()
    {
        var (deviceId, _, first) = await ClaimedAsync();
        var second = _store.AddTerrarium(_owner);

        var outcome = await _service.MoveAsync(deviceId, second.Id, _owner);

        outcome.Succeeded.Should().BeTrue();
        outcome.Move!.PreviousTerrariumId.Should().Be(first);

        var board = _store.Devices.Single(device => device.PublicId == deviceId);
        board.TerrariumId.Should().Be(second.Id);
        board.ProvisionedAt.Should().NotBeNull("a move does not unclaim the board");
    }

    [Fact]
    public async Task Move_displaces_the_board_already_bound_to_the_target()
    {
        var (firstId, _, target) = await ClaimedAsync();
        _store.CommittedAuditEntries.Clear();
        var newcomer = AddBoard("sr-newcomer", DeviceStatus.Online, terrariumId: null);

        var outcome = await _service.MoveAsync(newcomer.PublicId, target, _owner);

        outcome.Succeeded.Should().BeTrue();
        outcome.Move!.DisplacedDeviceId.Should().Be(firstId);

        var displaced = _store.Devices.Single(device => device.PublicId == firstId);
        displaced.TerrariumId.Should().BeNull("one enclosure holds one board");
        displaced.Status.Should().Be(DeviceStatus.Provisioning);
        displaced.ProvisionedAt.Should().BeNull();

        _store.Devices.Single(device => device.PublicId == newcomer.PublicId).TerrariumId.Should().Be(target);

        _store.CommittedAuditEntries.Select(entry => entry.Action)
            .Should().Equal(AuditAction.DeviceUnbound, AuditAction.DeviceMoved);
    }

    [Fact]
    public async Task Move_hides_a_terrarium_the_caller_does_not_own()
    {
        var board = AddBoard("sr-mine", DeviceStatus.Online, terrariumId: null);
        var someoneElses = _store.AddTerrarium(Guid.NewGuid());

        var outcome = await _service.MoveAsync(board.PublicId, someoneElses.Id, _owner);

        // A foreign enclosure and a missing one are the same answer (BR-02.2).
        outcome.Problem!.Code.Should().Be("not_found");
        board.TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task Move_hides_a_board_owned_by_someone_else()
    {
        var theirs = AddBoard("sr-theirs", DeviceStatus.Online, terrariumId: null, ownerUserId: Guid.NewGuid());
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.MoveAsync(theirs.PublicId, terrarium.Id, _owner);

        outcome.Problem!.Code.Should().Be("not_found");
        theirs.TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task Move_refuses_a_revoked_board()
    {
        var board = AddBoard("sr-dead", DeviceStatus.Revoked, terrariumId: null);
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.MoveAsync(board.PublicId, terrarium.Id, _owner);

        outcome.Problem!.Code.Should().Be("device_revoked");
        board.TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task Move_refuses_a_board_that_was_never_claimed()
    {
        var board = new Device
        {
            PublicId = "sr-unclaimed",
            DeviceName = "Unclaimed",
            ChipId = "ABCDEF123456",
            MacAddress = "AA:BB:CC:DD:EE:02",
            Status = DeviceStatus.Provisioning,
        };
        _store.AddDevice(board);
        var terrarium = _store.AddTerrarium(_owner);

        // Only an administrator reaches an unclaimed board at all, and even then there is no account to bind it for.
        var outcome = await _service.MoveAsync(board.PublicId, terrarium.Id, _owner, mayReachAnyAccount: true);

        outcome.Problem!.Code.Should().Be("device_not_claimed");
        board.TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task Move_is_a_no_op_when_the_board_is_already_there()
    {
        var (deviceId, _, terrariumId) = await ClaimedAsync();
        _store.CommittedAuditEntries.Clear();

        var outcome = await _service.MoveAsync(deviceId, terrariumId, _owner);

        outcome.Succeeded.Should().BeTrue();
        outcome.Move!.PreviousTerrariumId.Should().Be(terrariumId);
        outcome.Move.DisplacedDeviceId.Should().BeNull();
        _store.CommittedAuditEntries.Should().BeEmpty("nothing changed, so there is nothing to audit");
    }

    [Fact]
    public async Task Move_requires_a_terrarium()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.MoveAsync(deviceId, null, _owner);

        outcome.Problem!.Code.Should().Be("move_invalid");
        outcome.Problem.Errors!.Should().ContainSingle().Which.Field.Should().Be("terrariumId");
    }

    [Fact]
    public async Task Move_reports_a_race_the_database_won_as_the_same_conflict()
    {
        var board = AddBoard("sr-race", DeviceStatus.Online, terrariumId: null);
        var terrarium = _store.AddTerrarium(_owner);
        _store.RebindRaceLost = true;

        var outcome = await _service.MoveAsync(board.PublicId, terrarium.Id, _owner);

        outcome.Succeeded.Should().BeFalse();
        outcome.Problem!.Code.Should().Be("terrarium_already_bound");
    }

    [Fact]
    public async Task Move_lets_an_administrator_pair_another_accounts_board_with_another_accounts_terrarium()
    {
        var keeper = Guid.NewGuid();
        var board = AddBoard("sr-their-board", DeviceStatus.Online, terrariumId: null, ownerUserId: keeper);
        var terrarium = _store.AddTerrarium(Guid.NewGuid());

        var outcome = await _service.MoveAsync(board.PublicId, terrarium.Id, _owner, mayReachAnyAccount: true);

        outcome.Succeeded.Should().BeTrue();
        board.TerrariumId.Should().Be(terrarium.Id);
        board.UserId.Should().Be(keeper, "a move does not transfer ownership");
    }

    // ---- rotate and revoke --------------------------------------------------------------------------

    [Fact]
    public async Task Rotate_keeps_the_previous_secret_alive_through_the_grace_window_only()
    {
        var (deviceId, secret, _) = await ClaimedAsync();

        var outcome = await _service.RotateSecretAsync(deviceId, _owner);

        outcome.Succeeded.Should().BeTrue();
        var rotation = outcome.Rotation!;
        rotation.Secret.Should().NotBe(secret);
        rotation.PreviousUsableUntilUtc.Should().Be(_clock.UtcNow.AddMinutes(10));

        // Both work during the window, which is what lets a device reconnect and persist the new secret.
        (await _service.VerifyCredentialAsync(deviceId, secret)).IsValid.Should().BeTrue();
        (await _service.VerifyCredentialAsync(deviceId, rotation.Secret)).IsValid.Should().BeTrue();

        _clock.Advance(TimeSpan.FromMinutes(11));

        (await _service.VerifyCredentialAsync(deviceId, secret)).IsValid.Should().BeFalse();
        (await _service.VerifyCredentialAsync(deviceId, rotation.Secret)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Rotate_hides_a_device_owned_by_someone_else()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.RotateSecretAsync(deviceId, Guid.NewGuid());

        outcome.Problem!.Code.Should().Be("not_found");
        _store.Devices.Single().Credentials.Should().ContainSingle("no second credential was issued");
    }

    [Fact]
    public async Task Revoke_invalidates_every_credential_and_is_idempotent()
    {
        var (deviceId, secret, _) = await ClaimedAsync();

        (await _service.RevokeAsync(deviceId, _owner)).Succeeded.Should().BeTrue();

        var device = _store.Devices.Single();
        device.Status.Should().Be(DeviceStatus.Revoked);
        device.RevokedAt.Should().Be(_clock.UtcNow);
        device.Credentials.Should().OnlyContain(credential => credential.RevokedAt != null);
        (await _service.VerifyCredentialAsync(deviceId, secret)).IsValid.Should().BeFalse();

        (await _service.RevokeAsync(deviceId, _owner)).Succeeded.Should().BeTrue("revoking twice is not an error");
    }

    [Fact]
    public async Task Revoke_disconnects_the_live_mqtt_session()
    {
        // BR-05.4: the credential stops the next connect, the kick stops the one that is already open.
        var (deviceId, _, _) = await ClaimedAsync();
        _sessions.Connect(deviceId, "client-1", "client-2");

        await _service.RevokeAsync(deviceId, _owner);

        _sessions.Kicks.Should().ContainSingle().Which.Should().Be((deviceId, "device_revoked"));
        _sessions.IsConnected(deviceId).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_still_succeeds_when_nothing_is_connected()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        (await _service.RevokeAsync(deviceId, _owner)).Succeeded.Should().BeTrue();

        _sessions.Kicks.Should().ContainSingle("the ask is recorded even when there is nothing to close");
    }

    [Fact]
    public async Task Rotate_does_not_disconnect_the_device_that_is_still_using_its_old_secret()
    {
        // BR-05.5 exists so a device can reconnect and persist the new secret; dropping it would defeat that.
        var (deviceId, _, _) = await ClaimedAsync();
        _sessions.Connect(deviceId, "client-1");

        await _service.RotateSecretAsync(deviceId, _owner);

        _sessions.Kicks.Should().BeEmpty();
        _sessions.IsConnected(deviceId).Should().BeTrue();
    }

    // ---- credential verification --------------------------------------------------------------------

    [Fact]
    public async Task Verify_reports_the_binding_so_the_pipeline_can_route_a_sample()
    {
        var (deviceId, secret, terrariumId) = await ClaimedAsync();

        var verification = await _service.VerifyCredentialAsync(deviceId, secret);

        verification.IsValid.Should().BeTrue();
        verification.DeviceId.Should().Be(_store.Devices.Single().Id);
        verification.TerrariumId.Should().Be(terrariumId);
    }

    [Fact]
    public async Task Verify_refuses_a_wrong_secret_an_unknown_device_and_a_missing_secret()
    {
        var (deviceId, _) = (await ClaimedAsync()).ToTuple();

        (await _service.VerifyCredentialAsync(deviceId, "not-the-secret")).IsValid.Should().BeFalse();
        (await _service.VerifyCredentialAsync("sr-999999", "whatever")).IsValid.Should().BeFalse();
        (await _service.VerifyCredentialAsync(deviceId, null)).IsValid.Should().BeFalse();
        (await _service.VerifyCredentialAsync(null, "whatever")).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_refuses_every_credential_of_a_revoked_device()
    {
        var (deviceId, secret, _) = await ClaimedAsync();

        await _service.RevokeAsync(deviceId, _owner);

        var verification = await _service.VerifyCredentialAsync(deviceId, secret);

        verification.Should().Be(DeviceCredentialVerification.Rejected);
    }

    // ---- audit trail (FR-18, BR-18.4) ---------------------------------------------------------------

    [Fact]
    public async Task Claim_records_who_claimed_the_device_and_from_where()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();

        var outcome = await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner, actor: _actor);

        var entry = _store.CommittedAuditEntries.Should().ContainSingle().Subject;

        entry.Action.Should().Be(AuditAction.DeviceClaimed);
        entry.EntityName.Should().Be("Device");
        entry.EntityId.Should().Be(deviceId, "the row is readable without joining to the device table");
        entry.DeviceId.Should().Be(_store.Devices.Single().Id);
        entry.UserId.Should().Be(_owner);
        entry.OccurredAt.Should().Be(_clock.UtcNow);
        entry.IpAddress.Should().Be(_actor.IpAddress);
        entry.UserAgent.Should().Be(_actor.UserAgent);
        entry.CorrelationId.Should().Be(_actor.CorrelationId);
        entry.BeforeJson.Should().BeNull("the device existed but carried no binding to report");
        entry.AfterJson.Should().Contain(terrariumId.ToString()).And.Contain(_owner.ToString());

        // The trail must not become a second place the credential lives.
        entry.AfterJson.Should().NotContain(outcome.Claim!.Secret);
    }

    [Fact]
    public async Task Claim_without_a_request_behind_it_still_records_the_change()
    {
        var (code, _, terrariumId) = await ArrangeAsync();

        await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner);

        var entry = _store.CommittedAuditEntries.Should().ContainSingle().Subject;

        entry.IpAddress.Should().Be("unknown");
        entry.UserAgent.Should().BeNull();
        entry.CorrelationId.Should().BeNull();
    }

    [Fact]
    public async Task Rotate_records_that_it_happened_without_recording_the_secret()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.RotateSecretAsync(deviceId, _owner, actor: _actor);

        var entry = _store.CommittedAuditEntries.Last();

        entry.Action.Should().Be(AuditAction.DeviceSecretRotated);
        entry.EntityId.Should().Be(deviceId);
        entry.AfterJson.Should().Contain("previousUsableUntil").And.NotContain(outcome.Rotation!.Secret);
    }

    [Fact]
    public async Task Revoke_records_the_revocation_once_and_a_repeat_records_nothing()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        await _service.RevokeAsync(deviceId, _owner, actor: _actor);
        await _service.RevokeAsync(deviceId, _owner, actor: _actor);

        var entry = _store.CommittedAuditEntries
            .Where(candidate => candidate.Action == AuditAction.DeviceRevoked)
            .Should().ContainSingle("a repeat revoke changes nothing, so there is no change to record")
            .Subject;

        entry.AfterJson.Should().Contain("\"credentialsRevoked\":1");
    }

    [Fact]
    public async Task A_refused_claim_records_nothing()
    {
        var (code, _, _) = await ArrangeAsync();
        var foreignTerrarium = _store.AddTerrarium(Guid.NewGuid());

        var outcome = await _service.ClaimAsync(
            new ClaimRequest(code, foreignTerrarium.Id),
            _owner,
            actor: _actor);

        outcome.Succeeded.Should().BeFalse();
        _store.CommittedAuditEntries.Should().BeEmpty("nothing changed, so there is nothing to audit");
    }

    // ---- claim by MAC (TASKS 2026-10-14, item 8) ----------------------------------------------------

    [Fact]
    public async Task ClaimByMac_binds_a_registered_board_and_issues_its_secret_once()
    {
        var registered = (await SelfRegisterAsync()).Registration!;
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(Mac, terrarium.Id), _owner);

        outcome.Succeeded.Should().BeTrue();
        var claim = outcome.Claim!;
        claim.DeviceId.Should().Be(registered.DeviceId);
        claim.Secret.Should().NotBeNullOrWhiteSpace();
        claim.TerrariumId.Should().Be(terrarium.Id);

        var device = _store.Devices.Single();
        device.TerrariumId.Should().Be(terrarium.Id);
        device.ClaimCode.Should().BeNull("the code a code-claim would have consumed is cleared by either path");
        device.Credentials.Should().ContainSingle("the secret is issued at bind time on both paths");
    }

    [Theory]
    [InlineData("a0:b1:c2:d3:e4:f5")]
    [InlineData("A0-B1-C2-D3-E4-F5")]
    [InlineData("a0b1c2d3e4f5")]
    [InlineData("A0B1.C2D3.E4F5")]
    [InlineData("  A0:B1:C2:D3:E4:F5  ")]
    public async Task ClaimByMac_accepts_the_spellings_a_human_types(string typed)
    {
        var registered = (await SelfRegisterAsync()).Registration!;
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(typed, terrarium.Id), _owner);

        outcome.Succeeded.Should().BeTrue();
        outcome.Claim!.DeviceId.Should().Be(registered.DeviceId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A0:B1:C2:D3:E4")]
    [InlineData("A0:B1:C2:D3:E4:F5:06")]
    [InlineData("ZZ:B1:C2:D3:E4:F5")]
    public async Task ClaimByMac_refuses_an_address_that_is_not_twelve_hex_digits(string? typed)
    {
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(typed, terrarium.Id), _owner);

        outcome.Problem!.Code.Should().Be("registration_invalid");
        outcome.Problem.Errors.Should().ContainSingle().Which.Field.Should().Be("macAddress");
        outcome.Problem.Errors!.Single().Code.Should().Be("mac_invalid");
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task ClaimByMac_provisions_and_binds_an_unregistered_address()
    {
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(
            new ClaimByMacRequest("AA:BB:CC:DD:EE:99", terrarium.Id),
            _owner);

        outcome.Succeeded.Should().BeTrue();
        outcome.Claim.Should().NotBeNull();
        outcome.Claim!.TerrariumId.Should().Be(terrarium.Id);
    }

    [Theory]
    [InlineData("FF:FF:FF:FF:FF:FF", "broadcast")]
    [InlineData("00:00:00:00:00:00", "before Wi-Fi starts")]
    [InlineData("01:00:5E:00:00:01", "group address")]
    public async Task ClaimByMac_refuses_an_address_no_board_can_own(string typed, string because)
    {
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(typed, terrarium.Id), _owner);

        // An ESP32 reports a unicast station address, so a group, broadcast or unset one names no board and
        // accepting it could only bind the wrong thing (2026-10-23).
        outcome.Problem!.Code.Should().Be("registration_invalid");
        outcome.Problem.Errors!.Single().Code.Should().Be("mac_invalid");
        outcome.Problem.Errors!.Single().Message.Should().Contain(because);
        _store.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task ClaimByMac_refuses_a_board_that_is_already_bound()
    {
        var first = (await SelfRegisterAsync()).Registration!;
        var terrarium = _store.AddTerrarium(_owner);
        (await _service.ClaimAsync(new ClaimRequest(first.ClaimCode, terrarium.Id), _owner))
            .Succeeded.Should().BeTrue();

        var bound = await _service.ClaimByMacAsync(new ClaimByMacRequest(Mac, _store.AddTerrarium(_owner).Id), _owner);

        bound.Problem!.Code.Should().Be("device_already_bound");
    }

    [Fact]
    public async Task ClaimByMac_refuses_a_revoked_board()
    {
        var (deviceId, _, _) = await ClaimedAsync();
        (await _service.RevokeAsync(deviceId, _owner)).Succeeded.Should().BeTrue();
        var terrarium = _store.AddTerrarium(_owner);

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(Mac, terrarium.Id), _owner);

        outcome.Problem!.Code.Should().Be("device_already_bound");
    }

    [Fact]
    public async Task ClaimByMac_still_hides_a_terrarium_the_caller_does_not_own()
    {
        (await SelfRegisterAsync()).Succeeded.Should().BeTrue();
        var someoneElses = _store.AddTerrarium(Guid.NewGuid());

        var outcome = await _service.ClaimByMacAsync(new ClaimByMacRequest(Mac, someoneElses.Id), _owner);

        outcome.Problem!.Code.Should().Be("not_found");
        _store.Devices.Single().TerrariumId.Should().BeNull();
    }

    [Fact]
    public async Task ClaimByMac_records_the_path_it_used_on_the_audit_row()
    {
        (await SelfRegisterAsync()).Succeeded.Should().BeTrue();
        var terrarium = _store.AddTerrarium(_owner);

        (await _service.ClaimByMacAsync(new ClaimByMacRequest(Mac, terrarium.Id), _owner, actor: _actor))
            .Succeeded.Should().BeTrue();

        // Both paths write `device.claimed`; the row says which one, because they carry different trust and a trail
        // that could not tell them apart could not answer "how did this board get bound?" (item 8).
        var entry = _store.CommittedAuditEntries.Should().ContainSingle().Subject;
        entry.Action.Should().Be(AuditAction.DeviceClaimed);
        entry.AfterJson.Should().Contain("\"method\":\"mac_address\"");
    }

    [Fact]
    public async Task Claim_records_the_code_path_so_the_two_are_tellable_apart()
    {
        var (code, _, terrariumId) = await ArrangeAsync();

        (await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner, actor: _actor))
            .Succeeded.Should().BeTrue();

        _store.CommittedAuditEntries.Should().ContainSingle()
            .Which.AfterJson.Should().Contain("\"method\":\"claim_code\"");
    }

    // ---- fleet reads and edits (TASKS 2026-10-14, item 9b) ------------------------------------------

    [Fact]
    public async Task List_returns_the_callers_boards_with_their_enclosure_and_derived_badge()
    {
        var (deviceId, _, terrariumId) = await ClaimedAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        _store.Devices.Single().LastSeenAt = _clock.UtcNow;

        var someoneElse = new Device
        {
            PublicId = "sr-other1",
            DeviceName = "Theirs",
            ChipId = "FFFFFFFFFFFF",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            UserId = Guid.NewGuid(),
        };
        _store.AddDevice(someoneElse);

        var outcome = await _service.ListAsync(_owner);

        outcome.Succeeded.Should().BeTrue();
        var board = outcome.Devices.Should().ContainSingle().Subject;
        board.DeviceId.Should().Be(deviceId);
        board.TerrariumId.Should().Be(terrariumId);
        board.Status.Should().Be("provisioning", "a claimed board stays in provisioning until it reports");
        board.Runtime.Should().Be("provisioning");
        board.TerrariumName.Should().BeNull("the store's fixture terrarium carries no name navigation");
    }

    [Fact]
    public async Task List_reads_a_silent_board_as_offline_and_a_revoked_one_as_revoked()
    {
        var terrarium = _store.AddTerrarium(_owner, "Alpha");
        var silent = BoundDevice("sr-silent", terrarium.Id, DeviceStatus.Online);
        var revoked = BoundDevice("sr-revokd", terrarium.Id, DeviceStatus.Revoked);

        // Silence is the signal for a board that could still report; a revoked one is a state it cannot leave.
        silent.LastSeenAt = _clock.UtcNow.AddSeconds(-181);
        revoked.LastSeenAt = _clock.UtcNow;

        var outcome = await _service.ListAsync(_owner);

        outcome.Devices!.Single(board => board.DeviceId == "sr-silent").Runtime.Should().Be("offline");
        outcome.Devices.Single(board => board.DeviceId == "sr-revokd").Runtime.Should().Be("revoked");
    }

    [Fact]
    public async Task Get_hides_a_board_belonging_to_another_account()
    {
        var theirs = new Device
        {
            PublicId = "sr-theirs",
            DeviceName = "Theirs",
            ChipId = "FFFFFFFFFFFF",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            UserId = Guid.NewGuid(),
        };
        _store.AddDevice(theirs);

        var outcome = await _service.GetAsync("sr-theirs", _owner);

        // Same answer as an unknown id (BR-02.2), so the route cannot be used to enumerate boards.
        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Get_does_not_offer_a_board_that_only_registered()
    {
        var registered = (await SelfRegisterAsync()).Registration!;

        var outcome = await _service.GetAsync(registered.DeviceId, _owner);

        // An unclaimed board has no owner, so it belongs to nobody's fleet yet.
        outcome.Problem!.Code.Should().Be("not_found");
    }

    [Fact]
    public async Task Update_renames_a_board_and_changes_its_intervals()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.UpdateAsync(
            deviceId,
            new UpdateDeviceRequest("Shed node", 30, 120),
            _owner);

        outcome.Succeeded.Should().BeTrue();

        var board = outcome.Device!;
        board.DeviceName.Should().Be("Shed node");
        board.SamplingIntervalSec.Should().Be(30);
        board.PublishIntervalSec.Should().Be(120);

        var stored = _store.Devices.Single(device => device.PublicId == deviceId);
        stored.DeviceName.Should().Be("Shed node");
        stored.SamplingIntervalSec.Should().Be(30);
    }

    [Fact]
    public async Task Update_is_a_patch_that_leaves_omitted_fields_alone()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        await _service.UpdateAsync(deviceId, new UpdateDeviceRequest("Shed node", 30, 120), _owner);
        var outcome = await _service.UpdateAsync(deviceId, new UpdateDeviceRequest("Shed node 2", null, null), _owner);

        var board = outcome.Device!;
        board.DeviceName.Should().Be("Shed node 2");
        board.SamplingIntervalSec.Should().Be(30, "a field the body did not mention keeps its value");
        board.PublishIntervalSec.Should().Be(120);
    }

    [Fact]
    public async Task Update_restores_the_default_label_when_the_name_is_emptied()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.UpdateAsync(deviceId, new UpdateDeviceRequest(string.Empty, null, null), _owner);

        outcome.Device!.DeviceName.Should().Be("New node");
    }

    [Theory]
    [InlineData(9)]
    [InlineData(301)]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task Update_refuses_an_interval_outside_the_bounds(int interval)
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.UpdateAsync(
            deviceId,
            new UpdateDeviceRequest(null, interval, null),
            _owner);

        outcome.Problem!.Code.Should().Be("device_invalid");

        var violation = outcome.Problem.Errors.Should().ContainSingle().Subject;
        violation.Field.Should().Be("samplingIntervalSec");
        violation.Code.Should().Be("interval_out_of_range");
        _store.Devices.Single(device => device.PublicId == deviceId).SamplingIntervalSec.Should().Be(60);
    }

    [Fact]
    public async Task Update_refuses_a_name_longer_than_the_column()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        var outcome = await _service.UpdateAsync(
            deviceId,
            new UpdateDeviceRequest(new string('x', DeviceLimits.NameMaxLength + 1), null, null),
            _owner);

        outcome.Problem!.Errors.Should().ContainSingle().Which.Code.Should().Be("device_name_too_long");
    }

    [Fact]
    public async Task Update_of_another_accounts_board_is_not_found()
    {
        var theirs = new Device
        {
            PublicId = "sr-theirs",
            DeviceName = "Theirs",
            ChipId = "FFFFFFFFFFFF",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            UserId = Guid.NewGuid(),
        };
        _store.AddDevice(theirs);

        var outcome = await _service.UpdateAsync("sr-theirs", new UpdateDeviceRequest("Mine now", null, null), _owner);

        outcome.Problem!.Code.Should().Be("not_found");
        theirs.DeviceName.Should().Be("Theirs");
    }

    [Fact]
    public async Task Delete_revokes_the_board_detaches_it_and_records_the_detachment()
    {
        var (deviceId, _, terrariumId) = await ClaimedAsync();
        var device = _store.Devices.Single(candidate => candidate.PublicId == deviceId);

        // Revoke keeps the binding, so delete is what releases the enclosure — and it no longer revokes, because
        // "remove" means unbind since 2026-10-25.
        await _service.RevokeAsync(deviceId, _owner, actor: _actor);
        var outcome = await _service.DeleteAsync(deviceId, _owner, actor: _actor);

        outcome.Succeeded.Should().BeTrue();
        outcome.Device!.TerrariumId.Should().BeNull("the DI-04 slot is free again for a replacement");
        outcome.Device.ProvisionedAt.Should().BeNull();

        // A revoked board stays revoked: detaching must not resurrect a credential that was killed on purpose.
        device.Status.Should().Be(DeviceStatus.Revoked);
        outcome.Device.Status.Should().Be("revoked");
        _sessions.Kicks.Should().ContainSingle().Which.DeviceId.Should().Be(deviceId);

        var entry = _store.CommittedAuditEntries.Last();
        entry.Action.Should().Be(AuditAction.DeviceUnbound);

        // The row survives — the readings, health reports and audit rows that name it do too.
        _store.Devices.Should().ContainSingle(candidate => candidate.PublicId == deviceId);
        terrariumId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Delete_only_detaches_the_board_and_keeps_it_in_the_fleet()
    {
        var (deviceId, _, _) = await ClaimedAsync();
        var device = _store.Devices.Single(candidate => candidate.PublicId == deviceId);
        var claimsBefore = device.Credentials.Count;

        var outcome = await _service.DeleteAsync(deviceId, _owner, actor: _actor);

        outcome.Succeeded.Should().BeTrue();
        outcome.Device!.TerrariumId.Should().BeNull("the DI-04 slot is free again for a replacement");
        outcome.Device.Status.Should().Be("provisioning", "an unbound board is waiting for an enclosure");
        outcome.Device.ProvisionedAt.Should().BeNull();

        // The credentials are untouched: "remove" is about the enclosure, and a board that kept its secret can be
        // bound again without being claimed twice (2026-10-25).
        device.Credentials.Should().HaveCount(claimsBefore);
        device.Credentials.Should().OnlyContain(credential => credential.RevokedAt == null);
        _sessions.Kicks.Should().BeEmpty("nothing was revoked, so there is no session to close");

        var entry = _store.CommittedAuditEntries.Last();
        entry.Action.Should().Be(AuditAction.DeviceUnbound);
        entry.AfterJson.Should().Contain("\"terrariumId\":null");

        // And it is still the account's board.
        var fleet = await _service.ListAsync(_owner);
        fleet.Devices.Should().ContainSingle(item => item.DeviceId == deviceId);
    }

    [Fact]
    public async Task Delete_is_idempotent_for_a_board_that_is_already_unbound()
    {
        var (deviceId, _, _) = await ClaimedAsync();

        await _service.DeleteAsync(deviceId, _owner);
        var second = await _service.DeleteAsync(deviceId, _owner);

        second.Succeeded.Should().BeTrue();
        second.Device!.TerrariumId.Should().BeNull();
        second.Device.Status.Should().Be("provisioning");
    }

    [Fact]
    public async Task Purge_erases_a_board_that_nothing_references()
    {
        var (deviceId, _, _) = await ClaimedAsync();
        var device = _store.Devices.Single(candidate => candidate.PublicId == deviceId);

        var outcome = await _service.DeleteAsync(deviceId, _owner, actor: _actor, purge: true);

        outcome.Succeeded.Should().BeTrue();
        _store.RemovedDevices.Should().Contain(device, "the row goes, not just its credentials");
        _store.Devices.Should().NotContain(candidate => candidate.PublicId == deviceId);

        // The revocation is still recorded: the audit row outlives the board it names.
        _store.CommittedAuditEntries.Last().Action.Should().Be(AuditAction.DeviceRevoked);
    }

    [Fact]
    public async Task Purge_is_refused_while_the_board_has_history_and_changes_nothing()
    {
        var (deviceId, _, _) = await ClaimedAsync();
        var device = _store.Devices.Single(candidate => candidate.PublicId == deviceId);
        _store.DeviceHasHistory = true;
        var savesBefore = _store.SaveCount;

        var outcome = await _service.DeleteAsync(deviceId, _owner, actor: _actor, purge: true);

        outcome.Problem!.Code.Should().Be("device_has_history");
        _store.RemovedDevices.Should().BeEmpty();
        device.Status.Should().NotBe(DeviceStatus.Revoked, "a refused purge leaves the board exactly as it was");
        device.TerrariumId.Should().NotBeNull("the board is still bound");
        _store.SaveCount.Should().Be(savesBefore, "a refusal writes nothing");
    }

    [Fact]
    public async Task Delete_of_another_accounts_board_is_not_found()
    {
        var theirs = new Device
        {
            PublicId = "sr-theirs",
            DeviceName = "Theirs",
            ChipId = "FFFFFFFFFFFF",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            UserId = Guid.NewGuid(),
        };
        _store.AddDevice(theirs);

        var outcome = await _service.DeleteAsync("sr-theirs", _owner);

        outcome.Problem!.Code.Should().Be("not_found");
        theirs.Status.Should().Be(DeviceStatus.Provisioning);
    }

    [Fact]
    public async Task Delete_still_detaches_a_board_that_was_already_revoked_while_bound()
    {
        var (deviceId, _, _) = await ClaimedAsync();
        var device = _store.Devices.Single(candidate => candidate.PublicId == deviceId);
        device.Status = DeviceStatus.Revoked;
        device.RevokedAt = _clock.UtcNow;

        var outcome = await _service.DeleteAsync(deviceId, _owner);

        // Revoke leaves the binding alone, so delete has to be able to finish the job — otherwise a board revoked
        // while bound could never release its enclosure's slot.
        outcome.Device!.TerrariumId.Should().BeNull();
        device.TerrariumId.Should().BeNull();
    }

    /// <summary>A board already bound to a terrarium and owned by the caller.</summary>
    private Device BoundDevice(string publicId, Guid terrariumId, DeviceStatus status)
    {
        var device = new Device
        {
            PublicId = publicId,
            DeviceName = publicId,
            ChipId = Guid.NewGuid().ToString("N")[..12],
            MacAddress = "AA:BB:CC:DD:EE:01",
            Status = status,
            TerrariumId = terrariumId,
            UserId = _owner,
        };

        _store.AddDevice(device);
        return device;
    }

    /// <summary>An already-owned board, unbound unless a terrarium is named.</summary>
    private Device AddBoard(string publicId, DeviceStatus status, Guid? terrariumId, Guid? ownerUserId = null)
    {
        var device = new Device
        {
            PublicId = publicId,
            DeviceName = publicId,
            ChipId = Guid.NewGuid().ToString("N")[..12],
            MacAddress = "AA:BB:CC:DD:EE:01",
            Status = status,
            TerrariumId = terrariumId,
            UserId = ownerUserId ?? _owner,
            ProvisionedAt = status == DeviceStatus.Provisioning ? null : _clock.UtcNow,
        };

        _store.AddDevice(device);
        return device;
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private Task<DeviceOutcome> SelfRegisterAsync(
        string chipId = ChipId,
        string mac = Mac,
        string? firmware = "1.0.0") =>
        _service.SelfRegisterAsync(new SelfRegisterRequest(chipId, mac, firmware), Ip);

    private async Task<(string Code, string DeviceId, Guid TerrariumId)> ArrangeAsync()
    {
        var registered = (await SelfRegisterAsync()).Registration!;
        var terrarium = _store.AddTerrarium(_owner);

        return (registered.ClaimCode, registered.DeviceId, terrarium.Id);
    }

    private async Task<(string DeviceId, string Secret, Guid TerrariumId)> ClaimedAsync()
    {
        var (code, deviceId, terrariumId) = await ArrangeAsync();
        var outcome = await _service.ClaimAsync(new ClaimRequest(code, terrariumId), _owner);

        outcome.Succeeded.Should().BeTrue();

        return (deviceId, outcome.Claim!.Secret, terrariumId);
    }
}

/// <summary>Small adapter so a three-value helper can be destructured down to two in one place.</summary>
internal static class ValueTupleExtensions
{
    public static (string DeviceId, string Secret) ToTuple(this (string DeviceId, string Secret, Guid TerrariumId) value) =>
        (value.DeviceId, value.Secret);
}
