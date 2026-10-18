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

    /// <summary>
    /// A monitored enclosure (FR-03). Written only when an administrator acts on an account's enclosure, which is
    /// the one terrarium change the trail was asked to record (TASKS 2026-10-14, item 5).
    /// </summary>
    public const string Terrarium = "Terrarium";

    /// <summary>
    /// An account (FR-01, FR-18): created, edited, disabled, re-enabled, deleted, or having its role changed.
    /// Distinct from the entities a row may also name, because "what happened to this account" is a question the
    /// trail has to answer on its own.
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// A role (FR-18): created, having its permission set edited, or deleted. The matrix is who-can-do-what, so an
    /// edit to it belongs in the trail for the same reason a role change on an account does.
    /// </summary>
    public const string Role = "Role";

    /// <summary>Every name this build writes, for validation and for a filter's error message.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Device, Alert, MetricSilence, SpeciesProfile, Terrarium, User, Role];

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
