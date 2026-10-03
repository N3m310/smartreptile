using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Device credential material (§02-design/06 §4.1, ADR-006): a 256-bit secret that only the device and the claim
/// response ever see, stored as <c>SHA-256(secret ‖ salt)</c> and compared in constant time.
/// </summary>
public sealed class DeviceCredentials : IDeviceCredentials
{
    private const int SecretBytes = 32;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int PublicIdLength = 6;

    /// <summary>
    /// Public-id alphabet: lower-case, no <c>0/1/l/i/o</c>, because the id is read off a serial log or a fleet
    /// screen and retyped into a bug report.
    /// </summary>
    private const string PublicIdAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    /// <inheritdoc />
    public string NewSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <inheritdoc />
    public string NewPublicId()
    {
        var suffix = new char[PublicIdLength];

        for (var index = 0; index < suffix.Length; index++)
        {
            suffix[index] = PublicIdAlphabet[RandomNumberGenerator.GetInt32(PublicIdAlphabet.Length)];
        }

        return $"sr-{new string(suffix)}";
    }

    /// <inheritdoc />
    public DeviceSecretHash HashSecret(string secret)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);

        return new DeviceSecretHash(ComputeHash(secret, salt), salt);
    }

    /// <inheritdoc />
    public bool VerifySecret(string secret, byte[] hash, byte[] salt)
    {
        // A row with no usable digest must fail closed rather than match anything.
        if (hash.Length != HashBytes || salt.Length == 0)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(ComputeHash(secret, salt), hash);
    }

    private static byte[] ComputeHash(string secret, byte[] salt) =>
        SHA256.HashData([.. Encoding.UTF8.GetBytes(secret), .. salt]);
}
