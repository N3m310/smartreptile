using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartReptile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added with the enclosure default, because that is what a metric-less alert is: a silent board or a
            // failed probe is a statement about the enclosure, and those rows have no metric to read a family from.
            migrationBuilder.AddColumn<int>(
                name: "Family",
                table: "Alert",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing rows are back-filled from the metric they are about, which is the rule the domain's
            // MetricDictionary states: 1 TempC, 4 UvIndex and 5 SurfaceTempC belong to the species family (the
            // animal's thermal and UV bands), while 2 HumidityPct, 3 LightLux, 6 BatteryPct, 7 RssiDbm and every
            // metric-less row belong to the enclosure's. Written as literals rather than read from the dictionary so
            // a later edit to the dictionary cannot change what this migration did.
            migrationBuilder.Sql(
                """
                UPDATE [Alert] SET [Family] = CASE [Metric]
                    WHEN 1 THEN 1
                    WHEN 4 THEN 1
                    WHEN 5 THEN 1
                    ELSE 0
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Family",
                table: "Alert");
        }
    }
}
