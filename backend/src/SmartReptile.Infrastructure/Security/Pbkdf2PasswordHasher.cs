using System.Security.Cryptography;
using SmartReptile.Application.Abstractions;
using SmartReptile.Domain.Identity;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing (§02-design/06 §2). Iterations are stored with the hash rather than
/// assumed, so the cost can be raised later and the existing rows upgraded on the next successful login
/// (TC-U-32).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <inheritdoc />
    public PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordPolicy.TargetIterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return new PasswordHash(hash, salt, PasswordPolicy.TargetIterations);
    }

    /// <inheritdoc />
    public bool Verify(string password, PasswordHash stored)
    {
        // A row with no usable hash must fail closed rather than compare against an empty value.
        if (stored.Hash.Length == 0 || stored.Salt.Length == 0 || stored.Iterations <= 0)
        {
            return false;
        }

        var candidate = Rfc2898DeriveBytes.Pbkdf2(
            password,
            stored.Salt,
            stored.Iterations,
            HashAlgorithmName.SHA256,
            stored.Hash.Length);

        return CryptographicOperations.FixedTimeEquals(candidate, stored.Hash);
    }

    /// <inheritdoc />
    public bool NeedsRehash(int iterations) => iterations < PasswordPolicy.TargetIterations;
}
