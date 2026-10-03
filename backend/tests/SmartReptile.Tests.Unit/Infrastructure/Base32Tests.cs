using System.Text;
using FluentAssertions;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>RFC 4648 base32, unpadded — the encoding that makes a device secret typeable by hand.</summary>
public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Encodes_the_reference_vectors(string input, string expected) =>
        Base32.Encode(Encoding.ASCII.GetBytes(input)).Should().Be(expected);

    [Fact]
    public void Encodes_a_256_bit_secret_as_52_characters()
    {
        // 32 bytes is what §02-design/06 §4.1 specifies, and 52 characters is what that comes out as.
        var encoded = Base32.Encode(new byte[32]);

        encoded.Should().HaveLength(52);
        encoded.Should().MatchRegex("^[A-Z2-7]+$");
    }

    [Fact]
    public void Pads_the_final_group_with_zero_bits_rather_than_dropping_them()
    {
        // A one-byte input has three spare bits; they must encode as 'A' (zero), not be omitted.
        Base32.Encode([0xFF]).Should().Be("74");
        Base32.Encode([0x00]).Should().Be("AA");
    }
}
