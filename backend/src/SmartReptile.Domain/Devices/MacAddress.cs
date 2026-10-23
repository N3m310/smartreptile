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

    /// <summary>
    /// Checks the value is not only spelled like a MAC but usable as a board's own address. An ESP32 reports its
    /// station address through <c>WiFi.macAddress()</c>: always 48 bits, always <b>unicast</b> (the I/G bit — the low
    /// bit of the first octet — is clear), never the broadcast address, and never all zeros. A value that breaks one
    /// of those rules belongs to no board, so accepting it can only mis-bind something (`mac_invalid`).
    /// </summary>
    /// <param name="value">Value as it arrived.</param>
    /// <param name="normalised">Canonical form when the answer is <see cref="MacAddressProblem.None"/>.</param>
    /// <returns>Why the value was refused, or <see cref="MacAddressProblem.None"/> when it was accepted.</returns>
    public static MacAddressProblem Inspect(string? value, out string normalised)
    {
        if (!TryNormalise(value, out normalised))
        {
            return MacAddressProblem.Malformed;
        }

        if (normalised == BroadcastAddress)
        {
            return MacAddressProblem.Broadcast;
        }

        if (normalised == UnsetAddress)
        {
            // What a chip reports before Wi-Fi starts; treating it as an identity would put every unstarted board in
            // one row.
            return MacAddressProblem.AllZero;
        }

        var firstOctet = Convert.ToByte(normalised[..2], 16);

        // Bit 0 of the first octet is the I/G bit: set means a group (multicast) address, which is delivered to many
        // stations and therefore identifies none.
        return (firstOctet & 0x01) != 0 ? MacAddressProblem.Multicast : MacAddressProblem.None;
    }

    /// <summary>True when the value can be a board's own address, not merely a well-formed MAC.</summary>
    /// <param name="value">Value as it arrived.</param>
    public static bool IsDeviceAddress(string? value) =>
        Inspect(value, out _) == MacAddressProblem.None;

    /// <summary>
    /// A refusal a person holding the board can act on. Wording lives here so the self-register, claim-by-MAC and
    /// create-with-a-device paths cannot drift apart.
    /// </summary>
    /// <param name="problem">Why the value was refused.</param>
    public static string Describe(MacAddressProblem problem) => problem switch
    {
        MacAddressProblem.Malformed =>
            "A MAC address must be twelve hex digits, optionally separated by ':' or '-' "
            + "(for example AA:BB:CC:DD:EE:FF).",
        MacAddressProblem.Broadcast =>
            "FF:FF:FF:FF:FF:FF is the broadcast address, not a board's. Use the address the ESP32 prints at boot "
            + "or the one on its case.",
        MacAddressProblem.AllZero =>
            "00:00:00:00:00:00 is what an ESP32 reports before Wi-Fi starts, not its identity. Power the board up, "
            + "let it join the network and read the address it prints.",
        MacAddressProblem.Multicast =>
            "That is a group address (the low bit of its first octet is set), so it names no single board. Use the "
            + "unicast MAC the ESP32 prints at boot.",
        _ => string.Empty,
    };

    /// <summary>The all-ones address: every station on the segment, so no station.</summary>
    private const string BroadcastAddress = "FF:FF:FF:FF:FF:FF";

    /// <summary>The address a chip reports before it has one.</summary>
    private const string UnsetAddress = "00:00:00:00:00:00";
}

/// <summary>Why a value cannot be a board's own MAC address.</summary>
public enum MacAddressProblem
{
    /// <summary>The value is a usable unicast address.</summary>
    None = 0,

    /// <summary>Not twelve hex digits once the separators are gone.</summary>
    Malformed,

    /// <summary><c>FF:FF:FF:FF:FF:FF</c> — every station, so no station.</summary>
    Broadcast,

    /// <summary><c>00:00:00:00:00:00</c> — what a chip reports before Wi-Fi is up.</summary>
    AllZero,

    /// <summary>A group address: the I/G bit of the first octet is set.</summary>
    Multicast,
}
