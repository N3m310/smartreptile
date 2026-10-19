using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationEventTimeWatermark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEvaluatedAtUtc",
                table: "EvaluationState",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastEvaluatedSequence",
                table: "EvaluationState",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE state
                SET LastEvaluatedAtUtc = sample.RecordedAt,
                    LastEvaluatedSequence = sample.Sequence
                FROM EvaluationState AS state
                INNER JOIN TelemetrySample AS sample
                    ON sample.Id = state.LastEvaluatedSampleId;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastEvaluatedAtUtc",
                table: "EvaluationState");

            migrationBuilder.DropColumn(
                name: "LastEvaluatedSequence",
                table: "EvaluationState");
        }
    }
}
