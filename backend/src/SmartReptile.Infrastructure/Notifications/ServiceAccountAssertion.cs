using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartReptile.Infrastructure.Notifications;

/// <summary>
/// The signed assertion a service account exchanges for an access token (RFC 7523), the one piece of crypto the
/// push channel needs.
/// </summary>
/// <remarks>
/// Kept in its own type, and pure, so the one thing that can be wrong in a way nobody notices — a signature that
/// does not verify — is unit-testable without a network or a credential file: the test signs with a throwaway key
/// and verifies with its public half.
/// </remarks>
public static class ServiceAccountAssertion
{
    /// <summary>How long an assertion claims to be valid. Google honours at most an hour.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    /// <summary>Builds the assertion.</summary>
    /// <param name="clientEmail">Service-account address (<c>client_email</c> in the JSON).</param>
    /// <param name="privateKeyPem">PEM private key (<c>private_key</c> in the JSON).</param>
    /// <param name="audience">Token endpoint.</param>
    /// <param name="scope">Scope being requested.</param>
    /// <param name="nowUtc">Instant the assertion is issued at; defaults to now.</param>
    public static string Create(
        string clientEmail,
        string privateKeyPem,
        string audience,
        string scope,
        DateTimeOffset? nowUtc = null)
    {
        var issuedAt = nowUtc ?? DateTimeOffset.UtcNow;

        var header = Base64Url(Encode(new { alg = "RS256", typ = "JWT" }));
        var claims = Base64Url(Encode(new
        {
            iss = clientEmail,
            scope,
            aud = audience,
            iat = issuedAt.ToUnixTimeSeconds(),
            exp = (issuedAt + Lifetime).ToUnixTimeSeconds(),
        }));

        var signingInput = $"{header}.{claims}";

        using var key = RSA.Create();
        key.ImportFromPem(privateKeyPem);

        var signature = key.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    private static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
