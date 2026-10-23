using FluentAssertions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// The MAC spelling rules of the claim-by-MAC path (TASKS 2026-10-14, item 8). Pure, so the accepted and refused
/// shapes are asserted without a store or a request.
/// </summary>
public class MacAddressTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa:bb:cc:dd:ee:ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AA-BB-CC-DD-EE-FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("AABBCCDDEEFF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabb.ccdd.eeff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("  AA:BB:CC:DD:EE:FF  ", "AA:BB:CC:DD:EE:FF")]
    [InlineData("A0:B1:C2:D3:E4:F5", "A0:B1:C2:D3:E4:F5")]
    public void Accepted_spellings_normalise_to_one_form(string typed, string expected)
    {
        MacAddress.TryNormalise(typed, out var normalised).Should().BeTrue();
        normalised.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB:CC:DD:EE:FF:00")]
    [InlineData("GG:BB:CC:DD:EE:FF")]
    [InlineData("AA:BB:CC:DD:EE:F")]
    [InlineData("A0:B1:C2:D3:E4:F5:06:07")]
    [InlineData("not-a-mac-address")]
    public void Anything_that_is_not_twelve_hex_digits_is_refused(string? typed)
    {
        MacAddress.TryNormalise(typed, out var normalised).Should().BeFalse();

        // Nothing usable comes out, so a caller cannot accidentally bind a board with a half-read address.
        normalised.Should().BeEmpty();
    }

    [Fact]
    public void Two_spellings_of_one_address_normalise_identically()
    {
        // This is the whole point of the type: the board reports the colon form and the keeper types whatever is
        // printed on the case, and the lookup only matches if both sides end up the same string.
        MacAddress.TryNormalise("a0-b1-c2-d3-e4-f5", out var typed).Should().BeTrue();
        MacAddress.TryNormalise("A0:B1:C2:D3:E4:F5", out var reported).Should().BeTrue();

        typed.Should().Be(reported);
        MacAddress.IsWellFormed(typed).Should().BeTrue();
    }

    [Theory]
    [InlineData("A0:B1:C2:D3:E4:F5")]
    [InlineData("a0-b1-c2-d3-e4-f5")]
    [InlineData("a0b1.c2d3.e4f5")]
    [InlineData("B0:CB:D8:CE:01:E0")]
    public void A_uniquely_assigned_unicast_address_is_a_board_address(string typed)
    {
        MacAddress.Inspect(typed, out var normalised).Should().Be(MacAddressProblem.None);
        MacAddress.IsDeviceAddress(typed).Should().BeTrue();
        normalised.Should().Be(normalised.ToUpperInvariant());
    }

    [Theory]
    [InlineData("FF:FF:FF:FF:FF:FF", MacAddressProblem.Broadcast)]
    [InlineData("ff-ff-ff-ff-ff-ff", MacAddressProblem.Broadcast)]
    [InlineData("00:00:00:00:00:00", MacAddressProblem.AllZero)]
    [InlineData("01:00:5E:00:00:01", MacAddressProblem.Multicast)]
    [InlineData("33:33:00:00:00:01", MacAddressProblem.Multicast)]
    [InlineData("AA:BB:CC:DD:EE", MacAddressProblem.Malformed)]
    [InlineData("", MacAddressProblem.Malformed)]
    public void An_address_no_single_board_can_own_is_refused_with_its_reason(
        string typed,
        MacAddressProblem expected)
    {
        // An ESP32 reports its station address through `WiFi.macAddress()`: 48 bits, unicast, never the broadcast
        // address and never all zeros. Anything else would match nothing — or, worse, something wrong.
        MacAddress.Inspect(typed, out _).Should().Be(expected);
        MacAddress.IsDeviceAddress(typed).Should().BeFalse();
        MacAddress.Describe(expected).Should().NotBeNullOrWhiteSpace();
    }
}
