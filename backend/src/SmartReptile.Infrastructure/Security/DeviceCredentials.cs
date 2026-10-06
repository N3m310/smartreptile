using System.Security.Cryptography;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Device credential material (§02-design/06 §4.1, ADR-006): a 256-bit secret that only the device and the claim
/// response ever see, stored as <c>SHA-256(secret ‖ salt)</c> and compared in constant time.
/// </summary>
/// <remarks>
/// The hashing itself is <see cref="Sha256SecretHasher"/>, composed rather than injected so this class is still
/// constructible without a container and so <c>SHA-256(secret ‖ salt)</c> has exactly one implementation in the
/// codebase — the recovery-code and password-reset paths use the same one.
/// </remarks>
public sealed class DeviceCredentials : IDeviceCredentials
{
    private const int SecretBytes = 32;
    private const int PublicIdLength = 6;

    private static readonly Sha256SecretHasher Hasher = new();

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
        var hashed = Hasher.Hash(secret);

        return new DeviceSecretHash(hashed.Hash, hashed.Salt);
    }

    /// <inheritdoc />
    public bool VerifySecret(string secret, byte[] hash, byte[] salt) => Hasher.Verify(secret, hash, salt);
}
