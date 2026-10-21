using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMetricCatalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Metric",
                columns: table => new
                {
                    Code = table.Column<int>(type: "int", nullable: false),
                    ApiKey = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    PayloadKey = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Precision = table.Column<int>(type: "int", nullable: false),
                    PlausibleMin = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    PlausibleMax = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    IsCore = table.Column<bool>(type: "bit", nullable: false),
                    Family = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Metric", x => x.Code);
                });

            // The seven rows the foreign keys below need. Written here rather than by the seeder because the
            // constraints are added against a database that already holds readings: an empty catalogue would make
            // every one of those rows a violation. The seeder then only reconciles labels.
            migrationBuilder.InsertData(
                table: "Metric",
                columns: new[]
                {
                    "Code", "ApiKey", "PayloadKey", "DisplayName", "Unit", "Precision",
                    "PlausibleMin", "PlausibleMax", "IsCore", "Family", "SortOrder",
                },
                values: new object[,]
                {
                    { 1, "tempC", "tf", "Air temperature", "°C", 2, -10m, 60m, true, 1, 1 },
                    { 2, "humidityPct", "rh", "Relative humidity", "%RH", 2, 0m, 100m, true, 0, 2 },
                    { 3, "lightLux", "lux", "Illuminance", "lx", 1, 0m, 200_000m, true, 0, 3 },
                    { 4, "uvIndex", "uvi", "UV index", "UVI", 2, 0m, 15m, true, 1, 4 },
                    { 5, "surfaceTempC", "st", "Surface temperature", "°C", 2, -10m, 80m, false, 1, 5 },
                    { 6, "batteryPct", "bat", "Battery", "%", 1, 0m, 100m, false, 0, 6 },
                    { 7, "rssiDbm", "rssi", "Wi-Fi signal", "dBm", 0, -120m, 0m, false, 0, 7 },
                });

            migrationBuilder.CreateIndex(
                name: "IX_ThresholdOverride_Metric",
                table: "ThresholdOverride",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_Threshold_Metric",
                table: "Threshold",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_TelemetryHourlyRollup_Metric",
                table: "TelemetryHourlyRollup",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_MetricSilence_Metric",
                table: "MetricSilence",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationState_Metric",
                table: "EvaluationState",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvent_Metric",
                table: "DeviceEvent",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_Alert_Metric",
                table: "Alert",
                column: "Metric");

            migrationBuilder.CreateIndex(
                name: "IX_Metric_ApiKey",
                table: "Metric",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Metric_SortOrder",
                table: "Metric",
                column: "SortOrder");

            migrationBuilder.AddForeignKey(
                name: "FK_Alert_Metric_Metric",
                table: "Alert",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceEvent_Metric_Metric",
                table: "DeviceEvent",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EvaluationState_Metric_Metric",
                table: "EvaluationState",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MetricReading_Metric_Metric",
                table: "MetricReading",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MetricSilence_Metric_Metric",
                table: "MetricSilence",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TelemetryHourlyRollup_Metric_Metric",
                table: "TelemetryHourlyRollup",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Threshold_Metric_Metric",
                table: "Threshold",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ThresholdOverride_Metric_Metric",
                table: "ThresholdOverride",
                column: "Metric",
                principalTable: "Metric",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Alert_Metric_Metric",
                table: "Alert");

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceEvent_Metric_Metric",
                table: "DeviceEvent");

            migrationBuilder.DropForeignKey(
                name: "FK_EvaluationState_Metric_Metric",
                table: "EvaluationState");

            migrationBuilder.DropForeignKey(
                name: "FK_MetricReading_Metric_Metric",
                table: "MetricReading");

            migrationBuilder.DropForeignKey(
                name: "FK_MetricSilence_Metric_Metric",
                table: "MetricSilence");

            migrationBuilder.DropForeignKey(
                name: "FK_TelemetryHourlyRollup_Metric_Metric",
                table: "TelemetryHourlyRollup");

            migrationBuilder.DropForeignKey(
                name: "FK_Threshold_Metric_Metric",
                table: "Threshold");

            migrationBuilder.DropForeignKey(
                name: "FK_ThresholdOverride_Metric_Metric",
                table: "ThresholdOverride");

            migrationBuilder.DropTable(
                name: "Metric");

            migrationBuilder.DropIndex(
                name: "IX_ThresholdOverride_Metric",
                table: "ThresholdOverride");

            migrationBuilder.DropIndex(
                name: "IX_Threshold_Metric",
                table: "Threshold");

            migrationBuilder.DropIndex(
                name: "IX_TelemetryHourlyRollup_Metric",
                table: "TelemetryHourlyRollup");

            migrationBuilder.DropIndex(
                name: "IX_MetricSilence_Metric",
                table: "MetricSilence");

            migrationBuilder.DropIndex(
                name: "IX_EvaluationState_Metric",
                table: "EvaluationState");

            migrationBuilder.DropIndex(
                name: "IX_DeviceEvent_Metric",
                table: "DeviceEvent");

            migrationBuilder.DropIndex(
                name: "IX_Alert_Metric",
                table: "Alert");
        }
    }
}
