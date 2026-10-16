using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerrariumStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Terrarium_UserId",
                table: "Terrarium");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Terrarium",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Back-fill from the column this migration replaces: a row with a delete timestamp was soft-deleted
            // under the old rule, and the new state column — which the query filter now reads — has to agree, or
            // every already-deleted terrarium would come back to life on the next read (DI-10). The default 0 the
            // AddColumn above declares means "active", so only the deleted rows need writing.
            migrationBuilder.Sql("UPDATE [Terrarium] SET [Status] = 1 WHERE [DeletedAt] IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Terrarium_UserId_Status",
                table: "Terrarium",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Terrarium_UserId_Status",
                table: "Terrarium");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Terrarium");

            migrationBuilder.CreateIndex(
                name: "IX_Terrarium_UserId",
                table: "Terrarium",
                column: "UserId");
        }
    }
}
