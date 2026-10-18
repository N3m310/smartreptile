using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThresholdSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThresholdSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EffectiveThresholdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ThresholdVersionHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThresholdSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ThresholdSnapshot_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThresholdSnapshot_TerrariumId_CapturedAtUtc",
                table: "ThresholdSnapshot",
                columns: new[] { "TerrariumId", "CapturedAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThresholdSnapshot");
        }
    }
}
