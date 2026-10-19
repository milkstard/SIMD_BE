using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIncidentSlaWarn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SlaWarnAt",
            table: "Incidents",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "SlaWarnedAt",
            table: "Incidents",
            type: "datetimeoffset",
            nullable: true);

        // Open incidents get the warn time of the current policy, counted back from their due time (the start time is not stored).
        migrationBuilder.Sql(
            """
            UPDATE i
            SET SlaWarnAt = DATEADD(second, -CAST(p.ResolveMinutes * 60.0 * (100 - p.EscalateAtPercent) / 100 AS int), i.ResolveDueAt)
            FROM dbo.Incidents AS i
            INNER JOIN dbo.SlaPolicies AS p ON p.Severity = i.Severity
            WHERE i.ResolveDueAt IS NOT NULL AND i.Status NOT IN ('Resolved', 'Closed');
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_SlaCandidates",
            table: "Incidents",
            column: "ResolveDueAt",
            filter: "[IsSlaBreached] = 0 AND [Status] <> 'Resolved' AND [Status] <> 'Closed'")
            .Annotation("SqlServer:Include", new[] { "SlaWarnAt", "SlaWarnedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Incidents_SlaCandidates",
            table: "Incidents");

        migrationBuilder.DropColumn(
            name: "SlaWarnAt",
            table: "Incidents");

        migrationBuilder.DropColumn(
            name: "SlaWarnedAt",
            table: "Incidents");
    }
}
