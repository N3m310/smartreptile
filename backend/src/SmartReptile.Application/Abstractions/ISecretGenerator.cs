namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Cryptographically strong random material and the one-way hash used to store it. Refresh tokens and (later)
/// device secrets both need "generate 256 bits, store only a hash" — ADR-006 for devices, BR-01.3 for sessions.
/// </summary>
public interface ISecretGenerator
{
    /// <summary>A fresh 256-bit token, URL-safe encoded, returned to the client once and never stored.</summary>
    string NewOpaqueToken();

    /// <summary>Hash of a token, for storage. The plaintext must not be recoverable from it.</summary>
    byte[] Sha256(string value);
}
