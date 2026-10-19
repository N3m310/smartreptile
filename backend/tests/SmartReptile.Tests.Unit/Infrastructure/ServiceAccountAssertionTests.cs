using System.Security.Cryptography;
using FluentAssertions;
using SmartReptile.Infrastructure.Notifications;

namespace SmartReptile.Tests.Unit.Infrastructure;

/// <summary>
/// The service-account assertion the push channel exchanges for a token (roadmap task 3.5). One test, because the
/// thing that can be wrong silently is whether the signature verifies — and that is checkable with a throwaway key.
/// </summary>
public class ServiceAccountAssertionTests
{
    [Fact]
    public void The_assertion_is_a_three_part_jwt_whose_signature_verifies_with_the_public_key()
    {
        using var key = RSA.Create(2048);
        var pem = key.ExportPkcs8PrivateKeyPem();
        var issuedAt = new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

        var assertion = ServiceAccountAssertion.Create(
            "pusher@project.iam.gserviceaccount.com",
            pem,
            "https://oauth2.googleapis.com/token",
            "https://www.googleapis.com/auth/firebase.messaging",
            issuedAt);

        var parts = assertion.Split('.');

        parts.Should().HaveCount(3);

        var payload = System.Text.Json.JsonDocument.Parse(Decode(parts[1])).RootElement;

        payload.GetProperty("iss").GetString().Should().Be("pusher@project.iam.gserviceaccount.com");
        payload.GetProperty("aud").GetString().Should().Be("https://oauth2.googleapis.com/token");
        payload.GetProperty("scope").GetString().Should().Be("https://www.googleapis.com/auth/firebase.messaging");
        payload.GetProperty("iat").GetInt64().Should().Be(issuedAt.ToUnixTimeSeconds());
        payload.GetProperty("exp").GetInt64().Should()
            .Be((issuedAt + ServiceAccountAssertion.Lifetime).ToUnixTimeSeconds());

        var verified = key.VerifyData(
            System.Text.Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"),
            Decode(parts[2]),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        verified.Should().BeTrue("the assertion is only useful if the token endpoint can verify it");

        var header = System.Text.Json.JsonDocument.Parse(Decode(parts[0])).RootElement;
        header.GetProperty("alg").GetString().Should().Be("RS256");
        header.GetProperty("typ").GetString().Should().Be("JWT");
    }

    private static byte[] Decode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');

        return Convert.FromBase64String(padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '='));
    }
}
