using FluentAssertions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>BR-01.2 password rules (§02-design/06 §2).</summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Reptile2026")]
    [InlineData("a1bcdefghij")]
    [InlineData("Linh's-gecko-01")]
    public void Accepts_a_password_that_meets_every_rule(string password) =>
        PasswordPolicy.Validate(password).Should().BeEmpty();

    [Fact]
    public void Rejects_a_missing_password_with_a_required_code()
    {
        var violations = PasswordPolicy.Validate(null);

        violations.Should().ContainSingle()
            .Which.Code.Should().Be("password_required");
    }

    [Fact]
    public void Rejects_a_password_shorter_than_the_minimum()
    {
        var violations = PasswordPolicy.Validate("Gecko1");

        violations.Should().ContainSingle()
            .Which.Code.Should().Be("password_too_short");
    }

    [Fact]
    public void Rejects_a_password_longer_than_the_maximum()
    {
        var violations = PasswordPolicy.Validate(new string('a', PasswordPolicy.MaxLength) + "1");

        violations.Should().ContainSingle()
            .Which.Code.Should().Be("password_too_long");
    }

    [Fact]
    public void Requires_a_letter_and_a_digit_separately()
    {
        var digitsOnly = PasswordPolicy.Validate("1234567890");
        var lettersOnly = PasswordPolicy.Validate("abcdefghij");

        digitsOnly.Should().Contain(violation => violation.Code == "password_letter_required");
        lettersOnly.Should().Contain(violation => violation.Code == "password_digit_required");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD")]
    [InlineData("1234567890")]
    [InlineData("matkhau123")]
    public void Rejects_a_common_password_regardless_of_case(string password)
    {
        PasswordPolicy.IsCommon(password).Should().BeTrue();
        PasswordPolicy.Validate(password).Should().Contain(violation => violation.Code == "password_too_common");
    }

    [Fact]
    public void Reports_every_violation_at_once_so_the_form_can_show_all_of_them()
    {
        var violations = PasswordPolicy.Validate("abc");

        violations.Select(violation => violation.Code)
            .Should().BeEquivalentTo(["password_too_short", "password_digit_required"]);
    }

    [Fact]
    public void Tags_every_violation_with_the_password_field() =>
        PasswordPolicy.Validate("abc").Should().OnlyContain(violation => violation.Field == "password");
}
