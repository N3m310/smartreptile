using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTerrariumRowVersionAndDeviceEvaluationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Terrarium",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: true),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceEvent_Device_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Device",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeviceEvent_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationState",
                columns: table => new
                {
                    TerrariumId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<int>(type: "int", nullable: false),
                    Violation = table.Column<int>(type: "int", nullable: false),
                    FirstOutOfBandAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CriticalSinceAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    ConsecutiveRecoveryTicks = table.Column<int>(type: "int", nullable: false),
                    OpenAlertId = table.Column<long>(type: "bigint", nullable: true),
                    LastEvaluatedSampleId = table.Column<long>(type: "bigint", nullable: false),
                    LastNotificationAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationState", x => new { x.TerrariumId, x.Metric, x.Phase });
                    table.ForeignKey(
                        name: "FK_EvaluationState_Terrarium_TerrariumId",
                        column: x => x.TerrariumId,
                        principalTable: "Terrarium",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvent_DeviceId_RecordedAt",
                table: "DeviceEvent",
                columns: new[] { "DeviceId", "RecordedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvent_TerrariumId",
                table: "DeviceEvent",
                column: "TerrariumId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceEvent");

            migrationBuilder.DropTable(
                name: "EvaluationState");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Terrarium");
        }
    }
}
