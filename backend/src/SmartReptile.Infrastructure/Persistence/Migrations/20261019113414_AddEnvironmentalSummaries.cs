using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironmentalSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyEnvironmentalSummary",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CoveragePct = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    TempMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    TempMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    TempAvg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    HumidityMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    HumidityMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    HumidityAvg = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    LuxAvg = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: true),
                    LightHours = table.Column<decimal>(type: "decimal(8,3)", precision: 8, scale: 3, nullable: false),
                    UvMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    OutOfRangeMinutesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TempExposureDegCHours = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    HumidityDryExposurePctHours = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    HumidityWetExposurePctHours = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    LightDeficitHours = table.Column<decimal>(type: "decimal(8,3)", precision: 8, scale: 3, nullable: false),
                    AlertCount = table.Column<int>(type: "int", nullable: false),
                    CriticalAlertCount = table.Column<int>(type: "int", nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    IsLowConfidence = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyEnvironmentalSummary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyEnvironmentalSummary_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelemetryHourlyRollup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    HourStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    AvgValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: true),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    OutOfRangeMinutes = table.Column<int>(type: "int", nullable: false),
                    ExposureValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ThresholdSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ComputedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelemetryHourlyRollup", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelemetryHourlyRollup_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TelemetryHourlyRollup_ThresholdSnapshot_ThresholdSnapshotId",
                        column: x => x.ThresholdSnapshotId,
                        principalTable: "ThresholdSnapshot",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyEnvironmentalSummary_TerrariumId_LocalDate",
                table: "DailyEnvironmentalSummary",
                columns: new[] { "TerrariumId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHourlyRollup_TerrariumId_Metric_HourStartUtc",
                table: "TelemetryHourlyRollup",
                columns: new[] { "TerrariumId", "Metric", "HourStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHourlyRollup_ThresholdSnapshotId",
                table: "TelemetryHourlyRollup",
                column: "ThresholdSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyEnvironmentalSummary");

            migrationBuilder.DropTable(
                name: "TelemetryHourlyRollup");
        }
    }
}
