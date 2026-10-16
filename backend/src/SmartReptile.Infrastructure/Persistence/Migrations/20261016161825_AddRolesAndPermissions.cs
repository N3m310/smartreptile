using System;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartReptile.Domain.Identity;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndPermissions : Migration
    {
        /// <summary>The timestamp every seeded role or permission row carries.</summary>
        /// <remarks>
        /// A constant rather than <c>UtcNow</c>: a migration replayed on two machines has to write the same rows, and
        /// a seed row's creation time is not a fact about the deployment. <c>IdentitySeeder</c> stamps the wall clock
        /// on the rows it repairs, so nothing downstream reads this as "when the deployment started".
        /// </remarks>
        private static readonly DateTimeOffset SeedTimestamp = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The three tables first, because the account's new foreign key points at Role and RolePermission points
            // at both. The old int column is dropped at the very end: until the back-fill below has run, it is the
            // only record of which role each account holds.
            migrationBuilder.CreateTable(
                name: "Permission",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Group = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CrossesAccounts = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permission", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Role",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Role", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermission",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermission", x => new { x.RoleId, x.PermissionCode });
                    table.ForeignKey(
                        name: "FK_RolePermission_Permission_PermissionCode",
                        column: x => x.PermissionCode,
                        principalTable: "Permission",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermission_Role_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Role",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The matrix itself, then the accounts. A fresh deployment is complete before the application runs, so
            // the seeder's first pass finds nothing to do — and the rows come from the catalogue in code, so the two
            // cannot describe different sets of permissions.
            SeedMatrix(migrationBuilder);

            // Added nullable, back-filled, then tightened: an existing deployment has accounts, and a NOT NULL
            // column with a default would silently point every one of them at the same role.
            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                table: "User",
                type: "uniqueidentifier",
                nullable: true);

            // The old column was the four-member UserRole enum: Owner, Technician, Viewer, Admin. Anything else —
            // including the impossible value — becomes Owner, which is what registration hands out.
            migrationBuilder.Sql(
                """
                UPDATE [User] SET [RoleId] = CASE [Role]
                    WHEN 1 THEN 'a0e00000-0000-4000-8000-000000000002'
                    WHEN 2 THEN 'a0e00000-0000-4000-8000-000000000003'
                    WHEN 3 THEN 'a0e00000-0000-4000-8000-000000000004'
                    ELSE 'a0e00000-0000-4000-8000-000000000001'
                END;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoleId",
                table: "User",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_User_RoleId",
                table: "User",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Role_Code",
                table: "Role",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermission_PermissionCode",
                table: "RolePermission",
                column: "PermissionCode");

            migrationBuilder.AddForeignKey(
                name: "FK_User_Role_RoleId",
                table: "User",
                column: "RoleId",
                principalTable: "Role",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Last: every account now points at a role row, so the enum column has nothing left to say.
            migrationBuilder.DropColumn(
                name: "Role",
                table: "User");
        }

        /// <summary>
        /// Writes the permission catalogue, the four system roles and their default grants.
        /// </summary>
        /// <remarks>
        /// Read from <see cref="Permissions.All"/> and <see cref="SystemRoles.Seed"/> rather than typed out: the
        /// catalogue is sixteen codes and the grants are forty-odd rows, and a hand-copied list would be a second
        /// source of truth that drifts the first time a permission is added. <c>IdentitySeeder</c> reconciles both on
        /// every start-up, so a deployment that upgrades past this migration lands on the same rows either way; what
        /// this buys is a database that is complete and consistent before the application has ever run.
        /// </remarks>
        /// <param name="migrationBuilder">Builder the rows are written through.</param>
        private static void SeedMatrix(MigrationBuilder migrationBuilder)
        {
            var permissions = Permissions.All;
            var permissionRows = new object[permissions.Count, 6];
            for (var index = 0; index < permissions.Count; index++)
            {
                var permission = permissions[index];
                permissionRows[index, 0] = permission.Code;
                permissionRows[index, 1] = permission.Group;
                permissionRows[index, 2] = permission.Name;
                permissionRows[index, 3] = permission.Description;
                permissionRows[index, 4] = permission.CrossesAccounts;
                permissionRows[index, 5] = index;
            }

            migrationBuilder.InsertData(
                table: "Permission",
                columns: ["Code", "Group", "Name", "Description", "CrossesAccounts", "SortOrder"],
                values: permissionRows);

            var roles = SystemRoles.Seed;
            var roleRows = new object[roles.Count, 8];
            for (var index = 0; index < roles.Count; index++)
            {
                var role = roles[index];
                roleRows[index, 0] = role.Id;
                roleRows[index, 1] = role.Code;
                roleRows[index, 2] = role.Name;
                roleRows[index, 3] = role.Description;

                // Every seeded role is a system role: it cannot be deleted, only re-described and re-granted.
                roleRows[index, 4] = true;
                roleRows[index, 5] = role.Rank;
                roleRows[index, 6] = SeedTimestamp;
                roleRows[index, 7] = SeedTimestamp;
            }

            migrationBuilder.InsertData(
                table: "Role",
                columns: ["Id", "Code", "Name", "Description", "IsSystem", "Rank", "CreatedAt", "UpdatedAt"],
                values: roleRows);

            var grants = roles.SelectMany(
                role => role.Permissions.Select(code => (RoleId: role.Id, PermissionCode: code))).ToList();
            var grantRows = new object[grants.Count, 2];
            for (var index = 0; index < grants.Count; index++)
            {
                grantRows[index, 0] = grants[index].RoleId;
                grantRows[index, 1] = grants[index].PermissionCode;
            }

            migrationBuilder.InsertData(
                table: "RolePermission",
                columns: ["RoleId", "PermissionCode"],
                values: grantRows);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The enum column comes back first, while RoleId is still there to be read: the roles that existed
            // before this migration have a number, and any role added afterwards folds into Owner — the closest row
            // the old shape had, and the same answer the forward back-fill gives.
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "User",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE u SET u.[Role] = CASE r.[Code]
                    WHEN 'Technician' THEN 1
                    WHEN 'Viewer' THEN 2
                    WHEN 'Admin' THEN 3
                    ELSE 0
                END
                FROM [User] u INNER JOIN [Role] r ON r.[Id] = u.[RoleId];
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_User_Role_RoleId",
                table: "User");

            migrationBuilder.DropTable(
                name: "RolePermission");

            migrationBuilder.DropTable(
                name: "Permission");

            migrationBuilder.DropTable(
                name: "Role");

            migrationBuilder.DropIndex(
                name: "IX_User_RoleId",
                table: "User");

            migrationBuilder.DropColumn(
                name: "RoleId",
                table: "User");
        }
    }
}
