using FluentAssertions;
using SmartReptile.Domain.Devices;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>The claim-code shape rules of §07-appendices/03 §2.1.</summary>
public class ClaimCodeTests
{
    private const string Alphabet = ClaimCode.DefaultAlphabet;
    private const int Length = ClaimCode.DefaultLength;

    [Theory]
    [InlineData("k7m2-qp4t", "K7M2QP4T")]
    [InlineData("  K7M2 QP4T  ", "K7M2QP4T")]
    [InlineData("K7M2QP4T", "K7M2QP4T")]
    public void Normalises_user_input_to_its_stored_form(string input, string expected) =>
        ClaimCode.Normalise(input).Should().Be(expected);

    [Fact]
    public void Normalising_nothing_yields_nothing() =>
        ClaimCode.Normalise(null).Should().BeEmpty();

    [Fact]
    public void The_alphabet_omits_the_symbols_people_misread()
    {
        // 0/O, 1/I/L are the transcription errors this alphabet exists to prevent. Removing them from the 36
        // alphanumerics is what leaves 31 symbols (the count §07-appendices/03 §2.1 pins).
        foreach (var ambiguous in new[] { "0", "O", "1", "I", "L" })
        {
            Alphabet.Should().NotContain(ambiguous);
        }

        Alphabet.Should().HaveLength(31);
    }

    [Theory]
    [InlineData("K7M2QP4T", true)]
    [InlineData("k7m2-qp4t", true)]
    [InlineData("K7M2QP4", false)]
    [InlineData("K7M2QP4TT", false)]
    [InlineData("K7M2QP41", false)]
    [InlineData("K7M2QP4O", false)]
    [InlineData("", false)]
    public void Accepts_only_codes_of_the_right_shape(string candidate, bool expected) =>
        ClaimCode.IsWellFormed(candidate, Alphabet, Length).Should().Be(expected);

    [Fact]
    public void Formats_a_code_in_two_groups_for_display() =>
        ClaimCode.Format("K7M2QP4T").Should().Be("K7M2-QP4T");

    [Fact]
    public void Leaves_a_code_it_cannot_group_alone() =>
        ClaimCode.Format("K7M2Q").Should().Be("K7M2Q");
}
