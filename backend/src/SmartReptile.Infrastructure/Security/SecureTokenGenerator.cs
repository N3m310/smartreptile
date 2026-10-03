using System.Security.Cryptography;
using System.Text;
using SmartReptile.Application.Abstractions;

namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// Generates the opaque secrets the system hands to a client once — refresh tokens now, device secrets when
/// onboarding lands (ADR-006) — and hashes them for storage. Both are 256-bit CSPRNG values; only the hash is
/// ever persisted, so a database dump does not yield a usable credential.
/// </summary>
public sealed class SecureTokenGenerator : ISecretGenerator
{
    private const int TokenBytes = 32;

    /// <inheritdoc />
    public string NewOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    /// <inheritdoc />
    public byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
