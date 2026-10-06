using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RecoveryCodeHash",
                table: "User",
                type: "varbinary(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecoveryCodeIssuedAt",
                table: "User",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RecoveryCodeSalt",
                table: "User",
                type: "varbinary(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecoveryCodeHash",
                table: "User");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeIssuedAt",
                table: "User");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeSalt",
                table: "User");
        }
    }
}
