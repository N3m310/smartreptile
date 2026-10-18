namespace SmartReptile.Domain.Identity;

/// <summary>
/// A named set of permissions (FR-18). The four the product ships are <b>system</b> roles: they exist from the
/// first start-up, their codes are referenced by the seed, and they cannot be deleted.
/// </summary>
/// <remarks>
/// <b>Why a table and not an enum.</b> The role was an <c>int</c> column on <c>User</c> with four members, which
/// made the mapping of role to permission a fact of the build. The permission matrix is a deployment's policy
/// decision — a team that wants its technicians to acknowledge alerts in *their* enclosure's stead, or a demo that
/// wants a read-only auditor role, should not need a release — so the mapping moved into rows an administrator can
/// edit and an audit entry can record.
/// <para>
/// <b>What stayed in code, deliberately.</b> The permission <em>catalogue</em> (<see cref="Permissions"/>) and the
/// four system roles' <em>identities</em> are constants. A permission the software does not implement cannot be
/// granted by inserting a row, and a role's id is known before the row is written, which is what lets the seeder
/// be an upsert and registration assign <c>Owner</c> without a lookup that could fail on an empty database.
/// </para>
/// </remarks>
public class Role
{
    /// <summary>Column width of <see cref="Code"/>.</summary>
    public const int CodeMaxLength = 40;

    /// <summary>Column width of <see cref="Name"/>.</summary>
    public const int NameMaxLength = 80;

    /// <summary>Column width of <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 300;

    /// <summary>Surrogate key, fixed for the four system roles (see <see cref="SystemRoles"/>).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Stable code, unique and case-insensitive, e.g. <c>Owner</c>. It is what a client and the seed refer to; the
    /// display name is free text an administrator may change.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What the role is for, shown next to it in the matrix.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// True for the four roles the product ships. A system role's permissions may still be edited — that is the
    /// point of the matrix — but it cannot be deleted or re-coded, because the seed and the documentation name it.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Display order, lowest first, and deliberately not a security rank: the four system roles are shown
    /// Administrator, Owner, Technician, Viewer because that is the order a reader expects, and a custom role takes
    /// a number an administrator chooses. Nothing authorises on this value.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Optimistic concurrency token, so two administrators editing one role's permission set cannot silently
    /// overwrite each other — the same rule the terrarium's own <c>rowVersion</c> follows (UC-03 A3).
    /// </summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>Creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The permissions this role grants. Empty is legal and means "can sign in and do nothing".</summary>
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();

    /// <summary>Accounts holding this role.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();
}

/// <summary>
/// One permission in the catalogue, as a row so the dashboard can list what exists without a second source of
/// truth. Written by the seeder from <see cref="Permissions.All"/> and never edited by a user.
/// </summary>
public class Permission
{
    /// <summary>Column width of <see cref="Code"/>.</summary>
    public const int CodeMaxLength = 60;

    /// <summary>Column width of <see cref="Group"/>.</summary>
    public const int GroupMaxLength = 40;

    /// <summary>Column width of <see cref="Name"/>.</summary>
    public const int NameMaxLength = 80;

    /// <summary>Column width of <see cref="Description"/>.</summary>
    public const int DescriptionMaxLength = 300;

    /// <summary>The code itself, primary key — the catalogue's only identity.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Display group.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Short human label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What granting it allows.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True when the permission reaches data outside the caller's own account.</summary>
    public bool CrossesAccounts { get; set; }

    /// <summary>Display order inside the group.</summary>
    public int SortOrder { get; set; }

    /// <summary>Roles granting it.</summary>
    public ICollection<RolePermission> Roles { get; set; } = new List<RolePermission>();
}

/// <summary>
/// The join: this role grants this permission. A row's existence is the whole statement, which is why the table has
/// no timestamp and no soft-delete flag — removing a grant is deleting the row, and the audit entry is what records
/// who removed it.
/// </summary>
public class RolePermission
{
    /// <summary>Role granting.</summary>
    public Guid RoleId { get; set; }

    /// <summary>Role navigation.</summary>
    public Role? Role { get; set; }

    /// <summary>Permission granted. Validated against <see cref="Permissions.Codes"/> before it is written.</summary>
    public string PermissionCode { get; set; } = string.Empty;

    /// <summary>Permission navigation.</summary>
    public Permission? Permission { get; set; }
}

/// <summary>
/// The four roles the product ships, with ids fixed in code.
/// </summary>
/// <remarks>
/// <b>Why literal ids rather than a lookup.</b> Registration assigns <c>Owner</c>, the seed creates the
/// administrator from configuration, and the tests need a role to hand an account — all three would otherwise need
/// a query whose answer can be absent (an unseeded database, a deployment with
/// <c>Startup:SeedReferenceDataOnStartup=false</c>). A deterministic id makes those three writes a constant, and the
/// seeder's job becomes "make these four rows exist and describe them", not "find out what the ids are".
/// <para>
/// The values are arbitrary but must never change: a different literal is a different role, and every account
/// pointing at the old one would be orphaned. They are written here once, in a comment, so a reader can see they
/// are deliberate rather than generated.
/// </para>
/// </remarks>
public static class SystemRoles
{
    /// <summary>The keeper who owns enclosures.</summary>
    public static readonly Guid OwnerId = Guid.Parse("a0e00000-0000-4000-8000-000000000001");

    /// <summary>A helper who may acknowledge and resolve alerts but not reshape the enclosure.</summary>
    public static readonly Guid TechnicianId = Guid.Parse("a0e00000-0000-4000-8000-000000000002");

    /// <summary>A read-only account.</summary>
    public static readonly Guid ViewerId = Guid.Parse("a0e00000-0000-4000-8000-000000000003");

    /// <summary>The system administrator: every permission, the account and catalogue writes, and every account's data.</summary>
    public static readonly Guid AdminId = Guid.Parse("a0e00000-0000-4000-8000-000000000004");

    /// <summary>Code of the keeper role.</summary>
    public const string Owner = "Owner";

    /// <summary>Code of the helper role.</summary>
    public const string Technician = "Technician";

    /// <summary>Code of the read-only role.</summary>
    public const string Viewer = "Viewer";

    /// <summary>Code of the administrator role.</summary>
    public const string Admin = "Admin";

    /// <summary>The four, in display order, with the permissions each is seeded with.</summary>
    /// <remarks>
    /// The sets are read as a ladder with one deliberate exception. <b>Viewer → Technician</b> adds the two alert
    /// writes, which is exactly §02-design/06 §3's Technician row. <b>Technician → Owner</b> adds the enclosure and
    /// board writes plus the audit read. <b>Owner → Admin</b> adds the two account permissions, the catalogue write
    /// and — the exception — the two cross-account ones, which is what makes "Admin = toàn bộ dữ liệu" a fact about
    /// this set rather than a branch in a service. Nothing here is a superset by accident: each line is the
    /// permission a reader would expect, and <c>web/src</c> gates its screens on these codes, not on role names.
    /// </remarks>
    public static IReadOnlyList<(Guid Id, string Code, string Name, string Description, int Rank, string[] Permissions)> Seed { get; } =
    [
        (
            AdminId,
            Admin,
            "Administrator",
            "Toàn hệ thống: quản lý tài khoản, duyệt loài, và xem/sửa dữ liệu của mọi tài khoản.",
            0,
            [
                Permissions.TerrariumRead, Permissions.TerrariumWrite,
                Permissions.DeviceRead, Permissions.DeviceWrite, Permissions.DeviceProvision,
                Permissions.AlertRead, Permissions.AlertWrite, Permissions.SilenceWrite,
                Permissions.NotificationRead, Permissions.AuditRead,
                Permissions.SpeciesRead, Permissions.SpeciesWrite,
                Permissions.UserRead, Permissions.UserManage,
                Permissions.DataReadAny, Permissions.DataWriteAny,
            ]),
        (
            OwnerId,
            Owner,
            "Owner",
            "Chủ chuồng: toàn quyền trên chuồng và thiết bị của mình.",
            1,
            [
                Permissions.TerrariumRead, Permissions.TerrariumWrite,
                Permissions.DeviceRead, Permissions.DeviceWrite, Permissions.DeviceProvision,
                Permissions.AlertRead, Permissions.AlertWrite, Permissions.SilenceWrite,
                Permissions.NotificationRead, Permissions.AuditRead,
                Permissions.SpeciesRead,
            ]),
        (
            TechnicianId,
            Technician,
            "Technician",
            "Kỹ thuật viên: xem chuồng của mình và xử lý cảnh báo.",
            2,
            [
                Permissions.TerrariumRead,
                Permissions.DeviceRead,
                Permissions.AlertRead, Permissions.AlertWrite, Permissions.SilenceWrite,
                Permissions.NotificationRead,
                Permissions.SpeciesRead,
            ]),
        (
            ViewerId,
            Viewer,
            "Viewer",
            "Chỉ xem: đọc dữ liệu của mình, không thay đổi gì.",
            3,
            [
                Permissions.TerrariumRead,
                Permissions.DeviceRead,
                Permissions.AlertRead,
                Permissions.NotificationRead,
                Permissions.SpeciesRead,
            ]),
    ];

    /// <summary>True when <paramref name="roleId"/> is one of the four system roles.</summary>
    /// <param name="roleId">Candidate id.</param>
    public static bool IsSystem(Guid roleId) => Seed.Any(entry => entry.Id == roleId);

    /// <summary>The code of a system role by id, or null when it is not one.</summary>
    /// <param name="roleId">Candidate id.</param>
    public static string? CodeOf(Guid roleId) =>
        Seed.FirstOrDefault(entry => entry.Id == roleId).Code;
}
