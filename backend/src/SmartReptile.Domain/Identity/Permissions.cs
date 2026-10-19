namespace SmartReptile.Domain.Identity;

/// <summary>One thing the system can be asked to do, as the catalogue defines it.</summary>
/// <param name="Code">Stable dotted code, e.g. <c>terrarium.write</c>.</param>
/// <param name="Group">Display group, so a matrix of seventeen rows is readable.</param>
/// <param name="Name">Short human label.</param>
/// <param name="Description">What granting it actually allows.</param>
/// <param name="CrossesAccounts">
/// True for the two permissions that reach data outside the caller's own account. It is what makes "Admin = toàn bộ
/// dữ liệu" a property of the permission set rather than a special case in a service, and it is why the dashboard
/// can say which role sees what.
/// </param>
public sealed record PermissionDefinition(
    string Code,
    string Group,
    string Name,
    string Description,
    bool CrossesAccounts = false);

/// <summary>
/// The permission catalogue (FR-18, §02-design/06 §3). Every authorisation decision in the API is one of these
/// codes: a role is nothing more than a named set of them, and <c>RolePermission</c> is where the two meet.
/// </summary>
/// <remarks>
/// <b>Why the catalogue is code and the mapping is data.</b> The set of things the software can do changes only
/// when the software changes, so a permission row is written by a migration or a seeder and never by a user; the
/// <em>assignment</em> of permissions to roles is a deployment's policy decision, so it lives in
/// <c>RolePermission</c> where an administrator can edit it and an audit row can record who did. A permission that
/// does not exist in this file cannot be granted by any amount of data.
/// <para>
/// <b>Own-data versus any-data is expressed as separate permissions, not as a scope column.</b> "Admin sees
/// everything, a keeper sees their own" is the product's rule, and a scope flag on the role would make it one bit
/// with no room for the case the product actually has — <c>species.read</c> is cross-account for every role, while
/// <c>terrarium.read</c> is not for any of them below Admin. Two codes per reach say exactly which reads widen.
/// </para>
/// </remarks>
public static class Permissions
{
    // ---- The caller's own enclosure and boards ------------------------------------------------------

    /// <summary>See one's own enclosures.</summary>
    public const string TerrariumRead = "terrarium.read";

    /// <summary>Create, edit and delete one's own enclosures, and their threshold overrides.</summary>
    public const string TerrariumWrite = "terrarium.write";

    /// <summary>See one's own boards.</summary>
    public const string DeviceRead = "device.read";

    /// <summary>Rename or remove one's own boards.</summary>
    public const string DeviceWrite = "device.write";

    /// <summary>Claim, re-bind, rotate the secret of and revoke one's own boards (FR-04, FR-05).</summary>
    public const string DeviceProvision = "device.provision";

    // ---- Alerts -------------------------------------------------------------------------------------

    /// <summary>See the alerts of one's own enclosures.</summary>
    public const string AlertRead = "alert.read";

    /// <summary>Acknowledge and resolve alerts (FR-12); §02-design/06 §3's Technician row.</summary>
    public const string AlertWrite = "alert.write";

    /// <summary>Open and cancel silence windows (FR-13).</summary>
    public const string SilenceWrite = "silence.write";

    // ---- The keeper's own account -------------------------------------------------------------------

    /// <summary>Read one's own notification inbox.</summary>
    public const string NotificationRead = "notification.read";

    /// <summary>Read the audit trail of one's own account and enclosures.</summary>
    public const string AuditRead = "audit.read";

    // ---- Shared catalogues --------------------------------------------------------------------------

    /// <summary>Read the species-profile catalogue. Every signed-in account holds it, because every enclosure names a profile.</summary>
    public const string SpeciesRead = "species.read";

    /// <summary>Author the species-profile catalogue (FR-10 A2 — the built-in bands every enclosure is judged against).</summary>
    public const string SpeciesWrite = "species.write";

    // ---- Accounts -----------------------------------------------------------------------------------

    /// <summary>See accounts other than one's own — the administrator's account list.</summary>
    public const string UserRead = "user.read";

    /// <summary>Create accounts, edit them, reset a password, disable, re-enable, change a role or delete them.</summary>
    public const string UserManage = "user.manage";

    // ---- The two that reach across accounts ---------------------------------------------------------

    /// <summary>See every account's enclosures, boards and alerts — what fills the administrator's dashboard.</summary>
    public const string DataReadAny = "data.read.any";

    /// <summary>Edit and delete any account's enclosure, on top of reading it.</summary>
    public const string DataWriteAny = "data.write.any";

    /// <summary>Every permission, in catalogue order.</summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(TerrariumRead, "Enclosure", "View enclosures", "Read the caller's own enclosures, their bands and their readings."),
        new(TerrariumWrite, "Enclosure", "Edit enclosures", "Create, edit and delete the caller's own enclosures, including per-enclosure threshold overrides."),
        new(DeviceRead, "Enclosure", "View boards", "Read the caller's own boards and their health."),
        new(DeviceWrite, "Enclosure", "Edit boards", "Rename or remove a board the caller owns."),
        new(DeviceProvision, "Enclosure", "Provision boards", "Claim a board, pair one by MAC address, rotate its secret or revoke it."),
        new(AlertRead, "Alerts", "View alerts", "Read the alerts of the caller's own enclosures, of both families."),
        new(AlertWrite, "Alerts", "Handle alerts", "Acknowledge and resolve alerts."),
        new(SilenceWrite, "Alerts", "Silence a metric", "Open a time-boxed silence window and cancel one early."),
        new(NotificationRead, "Alerts", "Read the inbox", "Read the caller's own notification inbox."),
        new(AuditRead, "Account", "Read the audit trail", "Read the audit rows of the caller's own account and enclosures."),
        new(SpeciesRead, "Catalogue", "View species", "Read the shared species-profile catalogue."),
        new(SpeciesWrite, "Catalogue", "Edit species", "Author the shared species-profile catalogue — the built-in bands every enclosure is judged against."),
        new(UserRead, "Accounts", "View accounts", "See accounts other than the caller's own."),
        new(UserManage, "Accounts", "Manage accounts", "Create an account, edit it, reset its password, disable, re-enable, change its role or delete it."),
        new(DataReadAny, "Whole system", "View all data", "See every account's enclosures, boards, alerts and counts — the administrator's overview.", CrossesAccounts: true),
        new(DataWriteAny, "Whole system", "Edit all data", "Edit and delete any account's enclosure.", CrossesAccounts: true),
    ];

    /// <summary>Every code, for validation and for the "does this code exist" question.</summary>
    public static IReadOnlySet<string> Codes { get; } =
        All.Select(definition => definition.Code).ToHashSet(StringComparer.Ordinal);

    /// <summary>True when <paramref name="code"/> is a permission this build defines.</summary>
    /// <param name="code">Candidate code.</param>
    public static bool Exists(string? code) => code is not null && Codes.Contains(code);

    /// <summary>The definition of one code, or null when the code is not in the catalogue.</summary>
    /// <param name="code">Candidate code.</param>
    public static PermissionDefinition? Find(string code) =>
        All.FirstOrDefault(definition => definition.Code == code);

    /// <summary>The display group a code belongs to, or an empty string when the code is unknown.</summary>
    /// <param name="code">Candidate code.</param>
    public static string GroupOf(string code) => Find(code)?.Group ?? string.Empty;

    /// <summary>
    /// Where a code sits in catalogue order. Used to keep a role's permission list stable across reads, so the
    /// console's matrix does not reorder itself between two identical requests.
    /// </summary>
    /// <param name="code">Candidate code.</param>
    public static int SortOrderOf(string code) =>
        Order.TryGetValue(code, out var index) ? index : int.MaxValue;

    private static readonly Dictionary<string, int> Order =
        All.Select((definition, index) => (definition.Code, index))
            .ToDictionary(entry => entry.Code, entry => entry.index, StringComparer.Ordinal);

    /// <summary>The codes that reach past the caller's own account: precisely the ones marked
    /// <see cref="PermissionDefinition.CrossesAccounts"/>. The dashboard's "which data does this role map to" is
    /// answered by intersecting a role's set with this one.
    /// </summary>
    public static IReadOnlySet<string> CrossAccountCodes { get; } =
        All.Where(definition => definition.CrossesAccounts)
            .Select(definition => definition.Code)
            .ToHashSet(StringComparer.Ordinal);
}
