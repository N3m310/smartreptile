namespace SmartReptile.Domain.Devices;

/// <summary>
/// A MAC address in the one spelling the API compares and stores (BR-04.1's low-trust pairing path,
/// TASKS 2026-10-14, item 8).
/// </summary>
/// <remarks>
/// <b>Why a type rather than a string comparison.</b> A MAC printed on a board is typed by a human, so it arrives as
/// <c>AA:BB:CC:DD:EE:FF</c>, <c>aa-bb-cc-dd-ee-ff</c>, <c>AABB.CCDD.EEFF</c> or bare hex, in either case. The
/// device's own report always uses the colon form, so the two sides only match if one of them is normalised — and
/// doing that in one place is what keeps a claim-by-MAC from failing over a difference that carries no meaning.
/// <para>
/// The canonical spelling is upper-case, colon-separated, and identical to the form the self-registration validator
/// already accepts.
/// </para>
/// </remarks>
public static class MacAddress
{
    /// <summary>Separators a human or a vendor may use between the octets.</summary>
    private static readonly char[] Separators = [':', '-', '.', ' '];

    /// <summary>Six octets, each two hex digits.</summary>
    private const int Octets = 6;

    /// <summary>Twelve hex digits once the separators are gone.</summary>
    private const int HexDigits = Octets * 2;

    /// <summary>
    /// Normalises a caller-supplied MAC, or answers <c>false</c> when it is not one.
    /// </summary>
    /// <param name="value">Value as it arrived, possibly separated, spaced or in a different case.</param>
    /// <param name="normalised">Canonical form, <c>AA:BB:CC:DD:EE:FF</c>, when the value was usable.</param>
    public static bool TryNormalise(string? value, out string normalised)
    {
        normalised = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = value.Where(c => !Separators.Contains(c)).ToArray();

        // Exactly twelve characters, every one of them a hex digit. A truncated value is refused rather than
        // accepted: a shorter address could match a different board instead of none.
        if (digits.Length != HexDigits || !digits.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        normalised = string.Join(':', Enumerable.Range(0, Octets).Select(octet =>
            new string([char.ToUpperInvariant(digits[octet * 2]), char.ToUpperInvariant(digits[(octet * 2) + 1])])));

        return true;
    }

    /// <summary>True when the value is a MAC in any of the accepted spellings.</summary>
    /// <param name="value">Value to check.</param>
    public static bool IsWellFormed(string? value) => TryNormalise(value, out _);
}
