namespace SmartReptile.Infrastructure.Security;

/// <summary>
/// RFC 4648 base32 without padding.
/// </summary>
/// <remarks>
/// Device secrets are base32 rather than base64 because they are meant to be readable aloud and typed by hand
/// into the captive portal when the pairing endpoint is unavailable (option B in §02-design/06 §5): base64's
/// mixed case plus <c>+</c> and <c>/</c> is a transcription hazard, base32's A–Z 2–7 is not.
/// 32 bytes therefore come out as 52 characters.
/// </remarks>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes as unpadded base32.</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        // Five bits per character, rounded up.
        var result = new char[(data.Length * 8 + 4) / 5];
        var buffer = 0;
        var bitsInBuffer = 0;
        var written = 0;

        foreach (var value in data)
        {
            // bitsInBuffer stays below 5 after each drain, so buffer never exceeds 20 bits.
            buffer = (buffer << 8) | value;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                result[written++] = Alphabet[(buffer >> bitsInBuffer) & 0x1F];
            }
        }

        // Trailing bits are padded with zeroes, which is what makes the encoding deterministic.
        if (bitsInBuffer > 0)
        {
            result[written++] = Alphabet[(buffer << (5 - bitsInBuffer)) & 0x1F];
        }

        return new string(result, 0, written);
    }
}
