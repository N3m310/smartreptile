using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropTelegramChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChannelTelegramEnabled",
                table: "User");

            migrationBuilder.DropColumn(
                name: "TelegramChatId",
                table: "User");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChannelTelegramEnabled",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TelegramChatId",
                table: "User",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }
    }
}
