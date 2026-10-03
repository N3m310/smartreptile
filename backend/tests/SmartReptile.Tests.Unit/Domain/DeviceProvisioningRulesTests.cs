using FluentAssertions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The "may this claim code be consumed?" rule (BR-04.1, BR-04.3). Every path that would produce a different
/// message for an attacker is asserted here, because the caller must not be able to tell them apart.
/// </summary>
public class DeviceProvisioningRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static Device Provisioning(string? code = "K7M2QP4T", DateTimeOffset? expiry = null) => new()
    {
        Status = DeviceStatus.Provisioning,
        ClaimCode = code,
        ClaimCodeExpiresAt = expiry ?? Now.AddMinutes(15),
    };

    [Fact]
    public void A_fresh_unclaimed_device_has_a_usable_code() =>
        DeviceProvisioningRules.ClaimCodeIsUsable(Provisioning(), Now).Should().BeTrue();

    [Fact]
    public void An_expired_code_is_not_usable() =>
        DeviceProvisioningRules.ClaimCodeIsUsable(Provisioning(expiry: Now.AddMinutes(-1)), Now).Should().BeFalse();

    [Fact]
    public void A_consumed_code_is_not_usable() =>
        DeviceProvisioningRules.ClaimCodeIsUsable(Provisioning(code: null), Now).Should().BeFalse();

    [Fact]
    public void A_code_with_no_expiry_stamp_is_not_usable()
    {
        // A row that carries a code but never got an expiry stamped on it must fail closed, not read as usable.
        var unstamped = Provisioning();
        unstamped.ClaimCodeExpiresAt = null;

        DeviceProvisioningRules.ClaimCodeIsUsable(unstamped, Now).Should().BeFalse();
    }

    [Fact]
    public void A_code_on_an_already_claimed_device_is_not_usable()
    {
        var claimed = Provisioning();
        claimed.TerrariumId = Guid.NewGuid();

        DeviceProvisioningRules.ClaimCodeIsUsable(claimed, Now).Should().BeFalse();
    }

    [Fact]
    public void A_code_on_a_revoked_device_is_not_usable()
    {
        var revoked = Provisioning();
        revoked.Status = DeviceStatus.Revoked;

        DeviceProvisioningRules.ClaimCodeIsUsable(revoked, Now).Should().BeFalse();
    }

    [Fact]
    public void IsClaimed_follows_the_terrarium_binding()
    {
        var device = Provisioning();

        DeviceProvisioningRules.IsClaimed(device).Should().BeFalse();

        device.TerrariumId = Guid.NewGuid();

        DeviceProvisioningRules.IsClaimed(device).Should().BeTrue();
    }

    [Fact]
    public void Claim_code_expiry_uses_the_configured_ttl() =>
        DeviceProvisioningRules.ClaimCodeExpiry(Now, 15).Should().Be(Now.AddMinutes(15));

    [Fact]
    public void The_documented_windows_are_pinned()
    {
        DeviceProvisioningRules.ClaimCodeTtlMinutes.Should().Be(15, "BR-04.1");
        DeviceProvisioningRules.RotationGraceMinutes.Should().Be(10, "BR-05.5");
    }
}
