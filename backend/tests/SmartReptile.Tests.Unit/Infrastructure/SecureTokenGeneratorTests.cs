using FluentAssertions;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>The 256-bit opaque tokens and the hash that is all the database ever sees.</summary>
public class SecureTokenGeneratorTests
{
    private readonly SecureTokenGenerator _generator = new();

    [Fact]
    public void Issues_a_url_safe_256_bit_token()
    {
        var token = _generator.NewOpaqueToken();

        // 32 bytes, base64url-encoded and unpadded.
        token.Should().HaveLength(43);
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Never_issues_the_same_token_twice()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => _generator.NewOpaqueToken()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Hashes_to_a_stable_32_byte_value_that_distinguishes_its_input()
    {
        var hash = _generator.Sha256("refresh-token");

        hash.Should().HaveCount(32);
        _generator.Sha256("refresh-token").Should().Equal(hash);
        _generator.Sha256("refresh-token-2").Should().NotEqual(hash);
    }
}
