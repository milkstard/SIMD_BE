using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSlaPolicies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SlaPolicies",
            columns: table => new
            {
                Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                AckMinutes = table.Column<int>(type: "int", nullable: false),
                ResolveMinutes = table.Column<int>(type: "int", nullable: false),
                EscalateAtPercent = table.Column<int>(type: "int", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SlaPolicies", x => x.Severity);
            });

        // Same values as the built-in Sla:Defaults, so incident due times do not change when the table replaces the config.
        migrationBuilder.InsertData(
            table: "SlaPolicies",
            columns: new[] { "Severity", "AckMinutes", "ResolveMinutes", "EscalateAtPercent" },
            values: new object[,]
            {
                { "Critical", 15, 240, 80 },
                { "High", 60, 480, 80 },
                { "Medium", 240, 4320, 80 },
                { "Low", 480, 7200, 80 },
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "SlaPolicies");
    }
}
