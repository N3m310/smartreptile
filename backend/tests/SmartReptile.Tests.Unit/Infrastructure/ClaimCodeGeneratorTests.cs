using FluentAssertions;
using Microsoft.Extensions.Options;
using SmartReptile.Domain.Devices;
using SmartReptile.Infrastructure.Options;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>The configured claim-code generator.</summary>
public class ClaimCodeGeneratorTests
{
    private static ClaimCodeGenerator Generator(string alphabet = ClaimCode.DefaultAlphabet, int length = 8) =>
        new(Options.Create(new ProvisioningOptions { ClaimCodeAlphabet = alphabet, ClaimCodeLength = length }));

    [Fact]
    public void Draws_codes_from_the_configured_alphabet()
    {
        var generator = Generator();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var code = generator.Generate();

            code.Should().HaveLength(ClaimCode.DefaultLength);
            code.Should().MatchRegex($"^[{ClaimCode.DefaultAlphabet}]+$");
        }
    }

    [Fact]
    public void Produces_codes_that_pass_the_domain_shape_check()
    {
        var generator = Generator();

        for (var attempt = 0; attempt < 200; attempt++)
        {
            ClaimCode.IsWellFormed(generator.Generate(), ClaimCode.DefaultAlphabet, ClaimCode.DefaultLength)
                .Should().BeTrue();
        }
    }

    [Fact]
    public void Rarely_repeats_itself()
    {
        var generator = Generator();

        var codes = Enumerable.Range(0, 500).Select(_ => generator.Generate()).ToList();

        // 32^8 ≈ 1.1e12, so 500 draws should be distinct; a repeat means the generator is not random.
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Honours_a_configured_alphabet_and_length()
    {
        var generator = Generator("AB", 6);

        var code = generator.Generate();

        code.Should().HaveLength(6);
        code.Should().MatchRegex("^[AB]+$");
    }

    [Fact]
    public void Falls_back_to_the_documented_defaults_when_configuration_is_blank()
    {
        var code = Generator(string.Empty, 0).Generate();

        code.Should().HaveLength(ClaimCode.DefaultLength);
        code.Should().MatchRegex($"^[{ClaimCode.DefaultAlphabet}]+$");
    }
}
