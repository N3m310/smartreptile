namespace SmartReptile.Domain.Alerts;

/// <summary>
/// The two alert families the product keeps apart (TASKS 2026-10-14, item 1).
/// </summary>
/// <remarks>
/// <b>Why the split exists.</b> An excursion has two possible subjects. One is the <em>enclosure</em>: its humidity
/// drifted, its lamp did not come on, or the board measuring it stopped answering. The other is the <em>animal</em>
/// the enclosure holds, and what it is judged against is not the enclosure's condition but its species' bands — a
/// leopard gecko's temperature may be wrong in a terrarium whose humidity and light are both perfectly fine, and the
/// alert on that screen is a different alert with a different owner.
/// <para>
/// The two are separate value sets rather than a flag on one list, because a client renders them on separate screens
/// and a reader of the alerts screen has to know which of the two questions a row is answering. Persisting the
/// family (rather than deriving it from the metric on every read) is what makes a device-level alert — silence,
/// sensor fault, clock skew — say plainly that it belongs to the enclosure, with no metric to derive it from.
/// </para>
/// </remarks>
public enum AlertFamily
{
    /// <summary>About the enclosure and the board in it: humidity, light, silence, sensor faults.</summary>
    Enclosure = 0,

    /// <summary>About the animal: temperature and UV, judged against its species profile's bands.</summary>
    Species = 1,
}

/// <summary>
/// The families as the API and the clients name them.
/// </summary>
public static class AlertFamilies
{
    /// <summary>All families, in the order a client lists them.</summary>
    public static IReadOnlyList<AlertFamily> All { get; } = [AlertFamily.Enclosure, AlertFamily.Species];

    /// <summary>
    /// The stable key a client sends in <c>?family=</c> and reads back on every alert. Lower case, because it names a
    /// family rather than a stored enumeration member, exactly as the metric keys do.
    /// </summary>
    /// <param name="family">Family to name.</param>
    public static string ApiKey(AlertFamily family) => family switch
    {
        AlertFamily.Enclosure => "enclosure",
        AlertFamily.Species => "species",
        _ => throw new Common.DomainValidationException("unknown_family", $"Alert family {family} has no key."),
    };

    /// <summary>
    /// Resolves a key from <c>?family=</c>. False means the caller asked for a family this build does not have,
    /// which the endpoint reports as a bad request rather than silently listing both.
    /// </summary>
    /// <param name="apiKey">Candidate key.</param>
    /// <param name="family">The resolved family.</param>
    public static bool TryParseApiKey(string? apiKey, out AlertFamily family)
    {
        family = default;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(ApiKey(candidate), apiKey, StringComparison.OrdinalIgnoreCase))
            {
                family = candidate;
                return true;
            }
        }

        return false;
    }
}
