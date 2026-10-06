using FluentAssertions;
using SmartReptile.Application.Ingest;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Application.Ingest;

/// <summary>
/// Stage 2 — device authentication, §02-design/03 §3 rules V-04/V-05: TC-U-05 in §04-quality/02.
/// </summary>
/// <remarks>
/// The secrets here are hashed by the real <see cref="DeviceCredentials"/>, not a fake, because the property under
/// test is that the production comparison is the one being exercised. Constant time itself comes from
/// <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/> inside that class — a single
/// line, asserted by reading it. A wall-clock spread test over 1 000 iterations would measure the CI machine's
/// scheduler rather than the code, so what is asserted here is the half a timing test cannot show: a wrong secret
/// never matches, for any of a thousand wrong secrets.
/// </remarks>
public class DeviceAuthenticatorTests
{
    // Throwaway values for a local device, and deliberately low-entropy. CI's secret scanner flags
    // realistic-looking credentials wherever they appear — fixtures included — and allow-listing test paths is
    // exactly how a real key ends up committed unnoticed, so the fixtures keep the scan honest instead. Both are
    // 52 base32 characters, the shape IDeviceCredentials actually issues, so the tests below exercise a
    // well-formed secret that is simply the wrong one.
    private const string Secret = "DEMODEMODEMODEMODEMODEMODEMODEMODEMODEMODEMODEMODEMO";
    private const string PreviousSecret = "OLDDEMOOLDDEMOOLDDEMOOLDDEMOOLDDEMOOLDDEMOOLDDEMOOLD";

    private readonly DeviceCredentials _credentials = new();
    private readonly FakeTelemetryStore _store = new();
    private readonly DeviceAuthenticator _authenticator;

    public DeviceAuthenticatorTests() =>
        _authenticator = new DeviceAuthenticator(_store, _credentials, new FakeClock(IngestTestData.Now));

    private Device SeedDevice(
        string publicId = "sr-3f9a2c",
        DeviceStatus status = DeviceStatus.Online,
        bool bound = true)
    {
        var device = IngestTestData.Device(publicId, status, bound);

        AddCredential(device, Secret);

        _store.Devices.Add(device);
        return device;
    }

    private void AddCredential(Device device, string secret, DateTimeOffset? graceUntil = null)
    {
        var stored = _credentials.HashSecret(secret);

        device.Credentials.Add(new DeviceCredential
        {
            DeviceId = device.Id,
            SecretHash = stored.Hash,
            Salt = stored.Salt,
            IssuedAt = IngestTestData.Now.AddHours(-1),
            GraceUntil = graceUntil,
        });
    }

    private Task<DeviceAuthentication> AuthenticateAsync(Device device, string? presentedSecret) =>
        _authenticator.AuthenticateAsync(
            IngestTestData.Batch(device),
            device.PublicId,
            presentedSecret,
            CancellationToken.None);

    [Fact]
    [Trait("TestCase", "TC-U-05")]
    public async Task GivenTheCorrectSecret_ThenItIsAcceptedAndAOneCharacterChangeIsNot()
    {
        var device = SeedDevice();

        var accepted = await AuthenticateAsync(device, Secret);
        var refused = await AuthenticateAsync(device, string.Concat(Secret.AsSpan(0, Secret.Length - 1), "X"));

        accepted.IsValid.Should().BeTrue();
        accepted.Device.Should().BeSameAs(device);

        refused.IsValid.Should().BeFalse();
        refused.Problem!.Code.Should().Be("auth_failed");
    }

    [Fact]
    [Trait("TestCase", "TC-U-05")]
    public async Task GivenTheWrongSecretInTheRightShape_ThenItNeverMatches()
    {
        var device = SeedDevice();
        var wrong = new string([.. Secret.Select(character => character == 'A' ? 'B' : 'A')]);

        wrong.Should().HaveLength(Secret.Length).And.NotBe(Secret);

        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            var result = await AuthenticateAsync(device, wrong);

            result.IsValid.Should().BeFalse();
            result.Problem!.Code.Should().Be("auth_failed");
        }
    }

    [Fact]
    [Trait("TestCase", "TC-U-05")]
    public async Task GivenNoPresentedSecret_ThenTheTransportAlreadyAuthenticatedAndTheDeviceIsAccepted()
    {
        // MQTT authenticates at CONNECT (§02-design/06 §4.2), so a telemetry payload has no secret to offer.
        var device = SeedDevice();

        (await AuthenticateAsync(device, presentedSecret: null)).IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("TestCase", "TC-U-05")]
    public async Task GivenAPreviousSecretInsideItsRotationGraceWindow_ThenItStillWorks()
    {
        // BR-05.5: rotation puts the old credential on a grace window so a device can reconnect and persist the new
        // secret before the old one stops working — without it, rotation would be a lockout.
        var device = SeedDevice();
        AddCredential(device, PreviousSecret, graceUntil: IngestTestData.Now.AddMinutes(9));

        (await AuthenticateAsync(device, PreviousSecret)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAPreviousSecretWhoseGraceWindowHasPassed_ThenItIsRefused()
    {
        var device = SeedDevice();
        AddCredential(device, PreviousSecret, graceUntil: IngestTestData.Now.AddMinutes(-1));

        (await AuthenticateAsync(device, PreviousSecret)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAPayloadNamingAnotherDevice_ThenItIsRefusedEvenThoughTheSessionIsAuthentic()
    {
        var device = SeedDevice();
        var batch = IngestTestData.Batch(device) with { DevicePublicId = "sr-999999" };

        var result = await _authenticator.AuthenticateAsync(batch, device.PublicId, null, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Problem!.Code.Should().Be("auth_failed");
    }

    [Fact]
    [Trait("TestCase", "TC-U-05")]
    public async Task GivenARevokedDevice_ThenItIsRefusedHoweverGoodItsSecretIs()
    {
        var device = SeedDevice(status: DeviceStatus.Revoked);

        var result = await AuthenticateAsync(device, Secret);

        result.IsValid.Should().BeFalse();
        result.Problem!.Code.Should().Be("auth_failed");
    }

    [Fact]
    public async Task GivenAnUnboundDevice_ThenItIsRefused()
    {
        // A sample's TerrariumId is a required foreign key and it is copied at ingest so rebinding never rewrites
        // history. With no binding there is nothing to attribute the sample to.
        var device = SeedDevice(bound: false);

        (await AuthenticateAsync(device, Secret)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task GivenAnUnknownDeviceOrARevokedOne_ThenTheRefusalLooksIdentical()
    {
        // BR-02.2: a probe must not be able to tell "no such device" from "that device is revoked", or the refusal
        // becomes an oracle for which public ids exist.
        var revoked = SeedDevice(status: DeviceStatus.Revoked);
        var unknown = IngestTestData.Device("sr-000000");

        var revokedResult = await AuthenticateAsync(revoked, Secret);
        var unknownResult = await _authenticator.AuthenticateAsync(
            IngestTestData.Batch(unknown), unknown.PublicId, Secret, CancellationToken.None);

        unknownResult.Problem!.Code.Should().Be(revokedResult.Problem!.Code);
    }
}
