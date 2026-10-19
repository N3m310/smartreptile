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
}
