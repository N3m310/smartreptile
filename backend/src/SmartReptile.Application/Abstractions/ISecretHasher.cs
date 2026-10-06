namespace SmartReptile.Application.Abstractions;

/// <summary>A stored secret: the digest and the salt it was produced with.</summary>
/// <param name="Hash"><c>SHA-256(secret ‖ salt)</c>.</param>
/// <param name="Salt">Per-secret 16-byte salt.</param>
public sealed record SecretHash(byte[] Hash, byte[] Salt);

/// <summary>
/// Salted one-way hashing for the high-entropy secrets this system hands out once — a device credential
/// (ADR-006), a recovery code, and a password-reset code.
/// </summary>
/// <remarks>
/// SHA-256 is the right primitive here <em>because</em> these are 128-bit-plus CSPRNG values: there is nothing to
/// brute-force, so a slow KDF would only spend CPU on every verification. Passwords are the opposite case and go
/// through <see cref="IPasswordHasher"/>. Which primitive a secret gets is decided by how it was created, not by
/// where it is stored — this sentence is the reason the two ports both exist.
/// </remarks>
public interface ISecretHasher
{
    /// <summary>Salts and hashes a secret for storage. The plaintext is never persisted.</summary>
    SecretHash Hash(string secret);

    /// <summary>
    /// Constant-time verification of a presented secret. A stored row with no usable digest fails closed rather
    /// than matching anything.
    /// </summary>
    bool Verify(string secret, byte[] hash, byte[] salt);
}
