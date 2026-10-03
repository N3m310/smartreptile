namespace SmartReptile.Application.Abstractions;

/// <summary>A stored device secret: the digest and the salt it was produced with (ADR-006).</summary>
/// <param name="Hash"><c>SHA-256(secret ‖ salt)</c>.</param>
/// <param name="Salt">Per-credential 16-byte salt.</param>
public sealed record DeviceSecretHash(byte[] Hash, byte[] Salt);

/// <summary>
/// Device credential material: the secret itself, the device's public id, and the one-way hash the database
/// keeps. Grouped in one port because the three are used together and must agree on the encoding
/// (§02-design/06 §4.1).
/// </summary>
public interface IDeviceCredentials
{
    /// <summary>A fresh 256-bit secret, base32-encoded (52 characters) so it can be typed in the fallback flow.</summary>
    string NewSecret();

    /// <summary>A fresh device public id of the form <c>sr-3f9a2c</c>, used in MQTT topics.</summary>
    string NewPublicId();

    /// <summary>Salts and hashes a secret for storage. The plaintext exists only in the claim response.</summary>
    DeviceSecretHash HashSecret(string secret);

    /// <summary>Constant-time verification of a presented secret.</summary>
    bool VerifySecret(string secret, byte[] hash, byte[] salt);
}
