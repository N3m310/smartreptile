namespace SmartReptile.Domain.Devices;

/// <summary>
/// The short onboarding code the device shows on its OLED and the keeper types into the app (§02-design/06 §5).
/// </summary>
/// <remarks>
/// The alphabet deliberately omits <c>0 O 1 I L</c>: the code is read off a small screen and transcribed by hand,
/// and those five are the symbols people get wrong. What is left of the 36 alphanumerics is 31 symbols, so the
/// space is 31⁸ ≈ 8.5 × 10¹¹ (≈ 39.6 bits) — with a 15-minute window and a single use that is out of reach of
/// guessing, and the alphabet is still wide enough that a 500-draw sample does not collide.
/// <para>
/// The rules here are also what make BR-04.3 possible: an unknown, an expired and an already-consumed code all
/// fail the same check and must be reported with the same problem code, so this type never distinguishes them.
/// </para>
/// </remarks>
public static class ClaimCode
{
    /// <summary>Default alphabet — 31 symbols, excluding <c>0 O 1 I L</c> (§07-appendices/03 §2.1).</summary>
    public const string DefaultAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Default code length.</summary>
    public const int DefaultLength = 8;

    /// <summary>
    /// Reduces user input to its canonical stored form: trims, removes the display separator and any internal
    /// whitespace, and upper-cases. <c>" k7m2-qp4t "</c> and <c>"K7M2QP4T"</c> are the same code.
    /// </summary>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c == '-')
            {
                continue;
            }

            buffer[length++] = char.ToUpperInvariant(c);
        }

        return new string(buffer, 0, length);
    }

    /// <summary>
    /// True when the value could be a code of this shape. Says nothing about whether it was ever issued — that is
    /// the lookup's job, and keeping the two apart is what keeps the failure modes indistinguishable.
    /// </summary>
    public static bool IsWellFormed(string? value, string alphabet, int length)
    {
        var candidate = Normalise(value);

        if (candidate.Length != length || string.IsNullOrEmpty(alphabet))
        {
            return false;
        }

        foreach (var c in candidate)
        {
            if (!alphabet.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Groups the code for display: <c>K7M2QP4T</c> → <c>K7M2-QP4T</c>.</summary>
    public static string Format(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length % 2 != 0)
        {
            return code;
        }

        var half = code.Length / 2;

        return string.Concat(code.AsSpan(0, half), "-", code.AsSpan(half));
    }
}
