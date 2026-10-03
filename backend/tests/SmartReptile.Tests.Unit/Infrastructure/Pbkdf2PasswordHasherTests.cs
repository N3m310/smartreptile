using System.Security.Cryptography;
using FluentAssertions;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;
using SmartReptile.Infrastructure.Security;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>
/// The real PBKDF2 implementation. These cases run in the database-free job on purpose: a hasher is pure
/// computation, and the cost of finding out that <c>Verify</c> fails open must not be a Testcontainers start-up.
/// </summary>
public class Pbkdf2PasswordHasherTests
{
    private const string Password = "local-demo-1";

    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hashes_at_the_documented_cost_with_a_fresh_salt()
    {
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        first.Iterations.Should().Be(PasswordPolicy.TargetIterations);
        first.Hash.Should().HaveCount(32);
        first.Salt.Should().HaveCount(16);

        // The same password must not produce the same row twice, or the table would reveal shared passwords.
        first.Salt.Should().NotEqual(second.Salt);
        first.Hash.Should().NotEqual(second.Hash);
    }

    [Fact]
    public void Verifies_the_right_password_and_refuses_any_other()
    {
        var stored = _hasher.Hash(Password);

        _hasher.Verify(Password, stored).Should().BeTrue();
        _hasher.Verify(Password + "!", stored).Should().BeFalse();
        _hasher.Verify(string.Empty, stored).Should().BeFalse();
    }

    [Fact]
    public void Verifies_a_hash_written_by_an_older_release_at_a_lower_cost()
    {
        // TC-U-32: a row hashed at a lower iteration count still verifies, so raising the cost never locks
        // anyone out — the login that proves the password upgrades the row instead.
        var salt = RandomNumberGenerator.GetBytes(16);
        var legacy = new PasswordHash(
            Rfc2898DeriveBytes.Pbkdf2(Password, salt, 100_000, HashAlgorithmName.SHA256, 32),
            salt,
            100_000);

        _hasher.Verify(Password, legacy).Should().BeTrue();
        _hasher.NeedsRehash(legacy.Iterations).Should().BeTrue();
        _hasher.NeedsRehash(PasswordPolicy.TargetIterations).Should().BeFalse();
    }

    [Fact]
    public void Fails_closed_on_a_row_with_no_usable_hash()
    {
        // An empty hash must never compare equal to anything — that is the difference between "wrong password"
        // and "any password", and it is the failure mode worth a test.
        _hasher.Verify(Password, new PasswordHash([], [], 0)).Should().BeFalse();
        _hasher.Verify(Password, new PasswordHash([], new byte[16], 210_000)).Should().BeFalse();
    }
}
