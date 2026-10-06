namespace SmartReptile.Application.Ingest;

/// <summary>
/// The wire form of a device credential: <c>Device {deviceId}.{secret}</c>, the scheme §07-appendices/03 §1 gives
/// the HTTPS fallback (rule V-05).
/// </summary>
/// <remarks>
/// A pure function in the application layer rather than a private helper in the endpoint, for two reasons. It is a
/// rule about what a device credential <i>is</i>, which is FR-05's business rather than ASP.NET's, and it is the
/// kind of parsing that gets an edge case wrong silently — a header that parses to an empty secret would reach
/// <c>DeviceAuthenticator</c> and be refused with the same answer as a wrong one, so the mistake would never
/// surface. Here it can be tested without a request.
/// <para>
/// It deliberately does not decide whether the credential is <i>valid</i>: that needs the stored hash and belongs
/// to <see cref="DeviceAuthenticator"/>. This only answers "did the caller present the two parts at all".
/// </para>
/// </remarks>
public static class DeviceCredentialHeader
{
    /// <summary>Scheme name, compared case-insensitively because HTTP schemes are.</summary>
    public const string Scheme = "Device";

    /// <summary>
    /// Splits a header value into its two parts. False for a missing value, a different scheme (a keeper's
    /// <c>Bearer</c> token is not a device credential) or a value missing either part. Never throws.
    /// </summary>
    /// <param name="headerValue">The raw <c>Authorization</c> header, or null.</param>
    /// <param name="devicePublicId">The device id, when the value parsed.</param>
    /// <param name="presentedSecret">The secret, when the value parsed.</param>
    public static bool TryParse(string? headerValue, out string devicePublicId, out string presentedSecret)
    {
        devicePublicId = string.Empty;
        presentedSecret = string.Empty;

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var value = headerValue.TrimStart();

        if (!value.StartsWith($"{Scheme} ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var credential = value[(Scheme.Length + 1)..].Trim();

        // Split on the *first* dot. A device id never contains one, and splitting on the last would quietly accept
        // an id with a dot in it by treating part of the id as the start of the secret.
        var separator = credential.IndexOf('.');

        // Both halves must be non-empty: `sr-x.` and `.secret` are a missing credential, not a credential that
        // happens to be wrong, and a zero-length secret must never reach the hash comparison.
        if (separator <= 0 || separator == credential.Length - 1)
        {
            return false;
        }

        devicePublicId = credential[..separator];
        presentedSecret = credential[(separator + 1)..];

        return true;
    }
}
