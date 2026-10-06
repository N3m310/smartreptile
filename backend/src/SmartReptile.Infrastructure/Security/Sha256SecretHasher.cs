using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// The single implementation of <c>SHA-256(secret ‖ salt)</c> plus a constant-time compare. Device credentials,
/// recovery codes and (phase 2) password-reset codes all store their secret this way; one implementation is what
/// stops one of those paths from quietly becoming an unsalted hash of a guessable value.
/// </summary>
public sealed class Sha256SecretHasher : ISecretHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <inheritdoc />
    public SecretHash Hash(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        return new SecretHash(Compute(secret, salt), salt);
    }

    /// <inheritdoc />
    public bool Verify(string secret, byte[] hash, byte[] salt)
    {
        // A row with no usable digest must fail closed rather than match anything.
        if (hash.Length != HashBytes || salt.Length == 0)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Compute(secret, salt), hash);
    }

    private static byte[] Compute(string secret, byte[] salt) =>
        SHA256.HashData([.. Encoding.UTF8.GetBytes(secret), .. salt]);
}
