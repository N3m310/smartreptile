using System.Text;

namespace SmartReptile.Domain.Identity;

/// <summary>
/// The backup code a keeper stores when they create an account and presents if they forget their password
/// (§02-design/06 §2).
/// </summary>
/// <remarks>
/// The same 31-symbol alphabet as the device claim code, for the same reason — it is copied by hand — but longer,
/// because this one is long-lived rather than valid for fifteen minutes: 20 symbols over 31 values is
/// 31²⁰ ≈ 10²⁹·⁸ (≈ 99 bits), so it is not reachable by guessing even before the lockout, and the lockout exists
/// anyway.
/// <para>
/// This type holds the <em>shape</em> rules only. Whether a code was ever issued is the database's answer, and
/// keeping the two apart is what lets an unknown identifier and a wrong code fail identically (BR-02.2).
/// </para>
/// </remarks>
public static class RecoveryCode
{
    /// <summary>Alphabet — 31 symbols, excluding the five that are misread when copied by hand.</summary>
    public const string DefaultAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Length of a generated code.</summary>
    public const int DefaultLength = 20;

    /// <summary>
    /// Reduces user input to its canonical stored form: trims, drops the display separators and any internal
    /// whitespace, and upper-cases. <c>" abcde-fghjk " </c> and <c>"ABCDEFGHJK"</c> are the same code.
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
    /// True when the value has the shape of a code. Says nothing about whether it was ever issued: that is the
    /// lookup's job, and keeping the two apart is what keeps the failure modes indistinguishable.
    /// </summary>
    public static bool IsWellFormed(
        string? value,
        string alphabet = DefaultAlphabet,
        int length = DefaultLength)
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

    /// <summary>Groups the code for display: <c>ABCDEFGHJKMNPQRSTUVW</c> → <c>ABCDE-FGHJK-MNPQR-STUVW</c>.</summary>
    public static string Format(string code, int groupSize = 5)
    {
        if (string.IsNullOrEmpty(code) || groupSize <= 0 || code.Length % groupSize != 0)
        {
            return code;
        }

        var builder = new StringBuilder(code.Length + (code.Length / groupSize));

        for (var index = 0; index < code.Length; index++)
        {
            if (index > 0 && index % groupSize == 0)
            {
                builder.Append('-');
            }

            builder.Append(code[index]);
        }

        return builder.ToString();
    }
}
