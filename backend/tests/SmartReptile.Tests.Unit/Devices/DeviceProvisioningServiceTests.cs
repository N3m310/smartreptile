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
            new ProvisioningSettings(ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength, 15, OnboardingLimits.Documented));

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
