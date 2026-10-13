namespace SmartReptile.Domain.Auditing;

/// <summary>
/// The entity names an audit row's <c>EntityName</c> may carry. Closed for the same reason
/// <see cref="AuditAction"/> is: the column is filtered and grouped on, and <c>GET /audit</c> takes it as a filter,
/// so the vocabulary has to be one list rather than three string literals in three factories.
/// </summary>
public static class AuditEntity
{
    /// <summary>A sensor node (BR-18.4's claim, rotation and revocation).</summary>
    public const string Device = "Device";

    /// <summary>An alert (FR-12's acknowledgement and resolution).</summary>
    public const string Alert = "Alert";

    /// <summary>A metric silence window (FR-13).</summary>
    public const string MetricSilence = "MetricSilence";

    /// <summary>A species profile (FR-10's create, edit, duplicate and delete).</summary>
    public const string SpeciesProfile = "SpeciesProfile";

    /// <summary>Every name this build writes, for validation and for a filter's error message.</summary>
    public static IReadOnlyList<string> All { get; } = [Device, Alert, MetricSilence, SpeciesProfile];

    /// <summary>
    /// Resolves a filter value to the name as it is stored. Case-insensitive, because the value travels in a query
    /// string a human may have typed; false means the caller named something this build never writes.
    /// </summary>
    /// <param name="value">Value as it arrived.</param>
    /// <param name="entity">Canonical name, when the value resolves.</param>
    public static bool TryParse(string? value, out string entity)
    {
        entity = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in All)
        {
            if (string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                entity = candidate;

                return true;
            }
        }

        return false;
    }
}
