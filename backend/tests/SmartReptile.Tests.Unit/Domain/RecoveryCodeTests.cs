using FluentAssertions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>
/// BR-01.5 — the shape rules of a backup recovery code. Pure, so these assert the rules rather than the
/// generator: the alphabet is the part worth pinning down, because it is what makes the code safe to copy by
/// hand, and it is also what stops a mistyped symbol from being silently accepted as a different code.
/// </summary>
public class RecoveryCodeTests
{
    [Theory]
    [InlineData(" abcde-fghjk ", "ABCDEFGHJK")]
    [InlineData("ABCDE FGHJK", "ABCDEFGHJK")]
    [InlineData("dem2d-em2de-m2dem-2dem2", "DEM2DEM2DEM2DEM2DEM2")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void Normalise_reduces_input_to_its_stored_form(string? input, string expected) =>
        RecoveryCode.Normalise(input).Should().Be(expected);

    [Fact]
    public void IsWellFormed_accepts_a_code_of_the_documented_shape()
    {
        var code = new string([.. RecoveryCode.DefaultAlphabet.Take(RecoveryCode.DefaultLength)]);

        RecoveryCode.IsWellFormed(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("TOOSHORT")]
    [InlineData("DEM2DEM2DEM2DEM2DEM0")]      // right length, but 0 is not in the alphabet
    [InlineData("IIIIIIIIIIIIIIIIIIII")]
    [InlineData("LLLLLLLLLLLLLLLLLLLL")]
    [InlineData("00000000000000000000")]
    [InlineData("")]
    [InlineData(null)]
    public void IsWellFormed_refuses_anything_else(string? candidate) =>
        RecoveryCode.IsWellFormed(candidate).Should().BeFalse();

    [Fact]
    public void The_alphabet_excludes_the_symbols_people_misread_when_copying_by_hand()
    {
        foreach (var excluded in "01OIL")
        {
            RecoveryCode.DefaultAlphabet.Should().NotContain(excluded.ToString());
        }

        // 31 symbols is what the entropy claim in the type's documentation rests on, and a repeated symbol would
        // shrink it silently.
        RecoveryCode.DefaultAlphabet.Should().HaveLength(31);
        RecoveryCode.DefaultAlphabet.Distinct().Should().HaveCount(RecoveryCode.DefaultAlphabet.Length);
    }

    [Fact]
    public void Format_groups_the_code_for_display()
    {
        RecoveryCode.Format("DEM2DEM2DEM2DEM2DEM2").Should().Be("DEM2D-EM2DE-M2DEM-2DEM2");

        // A code whose length is not a multiple of the group size is left alone rather than mis-grouped.
        RecoveryCode.Format("ABCDEFGHJ").Should().Be("ABCDEFGHJ");
    }
}
