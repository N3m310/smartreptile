using FluentAssertions;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>Device secret material: shape, uniqueness, and a hash that cannot be reversed into the secret.</summary>
public class DeviceCredentialsTests
{
    private readonly DeviceCredentials _credentials = new();

    [Fact]
    public void Issues_a_256_bit_secret_in_the_typeable_alphabet()
    {
        var secret = _credentials.NewSecret();

        secret.Should().HaveLength(52);
        secret.Should().MatchRegex("^[A-Z2-7]+$");
    }

    [Fact]
    public void Never_issues_the_same_secret_twice()
    {
        var secrets = Enumerable.Range(0, 50).Select(_ => _credentials.NewSecret()).ToList();

        secrets.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Issues_a_readable_public_id()
    {
        var publicId = _credentials.NewPublicId();

        publicId.Should().MatchRegex("^sr-[abcdefghjkmnpqrstuvwxyz23456789]{6}$");
        publicId.Should().NotContain("0").And.NotContain("1").And.NotContain("l").And.NotContain("o");
    }

    [Fact]
    public void Never_issues_the_same_public_id_twice()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => _credentials.NewPublicId()).ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Stores_a_salted_digest_rather_than_the_secret()
    {
        var secret = _credentials.NewSecret();

        var first = _credentials.HashSecret(secret);
        var second = _credentials.HashSecret(secret);

        first.Hash.Should().HaveCount(32);
        first.Salt.Should().HaveCount(16);

        // The same secret must not produce the same row twice, or the table would reveal shared secrets.
        first.Salt.Should().NotEqual(second.Salt);
        first.Hash.Should().NotEqual(second.Hash);
    }

    [Fact]
    public void Verifies_the_right_secret_and_refuses_any_other()
    {
        var secret = _credentials.NewSecret();
        var stored = _credentials.HashSecret(secret);

        _credentials.VerifySecret(secret, stored.Hash, stored.Salt).Should().BeTrue();
        _credentials.VerifySecret(secret + "X", stored.Hash, stored.Salt).Should().BeFalse();
        _credentials.VerifySecret(string.Empty, stored.Hash, stored.Salt).Should().BeFalse();
    }

    [Fact]
    public void Fails_closed_on_a_row_with_no_usable_digest()
    {
        _credentials.VerifySecret("anything", [], []).Should().BeFalse();
        _credentials.VerifySecret("anything", new byte[16], new byte[16]).Should().BeFalse();
        _credentials.VerifySecret("anything", new byte[32], []).Should().BeFalse();
    }
}
