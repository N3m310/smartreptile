using FluentAssertions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Tests.Unit.Domain;

/// <summary>Username and email rules used at registration.</summary>
public class AccountIdentifierPolicyTests
{
    [Theory]
    [InlineData("Linh", "linh")]
    [InlineData("  keeper.one  ", "keeper.one")]
    [InlineData("TECH-02", "tech-02")]
    public void Normalises_a_username_to_its_stored_form(string input, string expected) =>
        AccountIdentifierPolicy.NormaliseUsername(input).Should().Be(expected);

    [Theory]
    [InlineData("Linh@Example.COM", "linh@example.com")]
    public void Normalises_an_email_to_its_stored_form(string input, string expected) =>
        AccountIdentifierPolicy.NormaliseEmail(input).Should().Be(expected);

    [Theory]
    [InlineData("linh")]
    [InlineData("keeper.one")]
    [InlineData("tech_02")]
    public void Accepts_a_well_formed_username(string username) =>
        AccountIdentifierPolicy.ValidateUsername(username).Should().BeEmpty();

    [Fact]
    public void Rejects_a_username_that_is_too_short_or_does_not_start_with_a_letter()
    {
        AccountIdentifierPolicy.ValidateUsername("ab")
            .Should().Contain(violation => violation.Code == "username_too_short");

        AccountIdentifierPolicy.ValidateUsername("1keeper")
            .Should().Contain(violation => violation.Code == "username_invalid");
    }

    [Fact]
    public void Rejects_a_username_containing_a_forbidden_character() =>
        AccountIdentifierPolicy.ValidateUsername("keeper one")
            .Should().Contain(violation => violation.Code == "username_invalid");

    [Fact]
    public void Rejects_a_username_longer_than_the_column_allows() =>
        AccountIdentifierPolicy.ValidateUsername(new string('a', AccountIdentifierPolicy.UsernameMaxLength + 1))
            .Should().Contain(violation => violation.Code == "username_too_long");

    [Theory]
    [InlineData("keeper@example.com")]
    [InlineData("first.last@sub.example.org")]
    public void Accepts_a_structurally_valid_email(string email) =>
        AccountIdentifierPolicy.ValidateEmail(email).Should().BeEmpty();

    [Theory]
    [InlineData("keeper")]
    [InlineData("keeper@example")]
    [InlineData("keeper@@example.com")]
    [InlineData("keeper@.com")]
    [InlineData("keeper@example.")]
    [InlineData("keep er@example.com")]
    public void Rejects_a_malformed_email(string email) =>
        AccountIdentifierPolicy.ValidateEmail(email)
            .Should().Contain(violation => violation.Code == "email_invalid");

    [Fact]
    public void Rejects_a_missing_identifier_with_its_own_code()
    {
        AccountIdentifierPolicy.ValidateUsername(null)
            .Should().ContainSingle().Which.Code.Should().Be("username_required");

        AccountIdentifierPolicy.ValidateEmail("  ")
            .Should().ContainSingle().Which.Code.Should().Be("email_required");
    }
}
