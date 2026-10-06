namespace SmartReptile.Application.Abstractions;

/// <summary>A stored password hash and the parameters it was produced with (TC-U-32).</summary>
/// <param name="Hash">PBKDF2 output.</param>
/// <param name="Salt">Per-user salt.</param>
/// <param name="Iterations">Iteration count, stored so the cost can be raised later.</param>
public sealed record PasswordHash(byte[] Hash, byte[] Salt, int Iterations);

/// <summary>
/// Password hashing port. Implemented in Infrastructure over PBKDF2-HMAC-SHA256 (§02-design/06 §2) so the
/// domain and the use cases never touch a crypto primitive directly, and so tests can substitute a cheap fake
/// instead of paying 210 000 iterations per case.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password with a fresh random salt at the current target cost.</summary>
    PasswordHash Hash(string password);

    /// <summary>Verifies a password against a stored hash in constant time.</summary>
    bool Verify(string password, PasswordHash stored);

    /// <summary>True when a stored hash was produced with a lower cost than the current target.</summary>
    bool NeedsRehash(int iterations);
}
