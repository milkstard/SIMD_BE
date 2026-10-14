using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOutboxNextAttemptAt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Unprocessed",
            table: "OutboxMessages");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAt",
            table: "OutboxMessages",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Unprocessed",
            table: "OutboxMessages",
            column: "ProcessedAt",
            filter: "[ProcessedAt] IS NULL")
            .Annotation("SqlServer:Include", new[] { "OccurredAt", "NextAttemptAt", "Attempts" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Unprocessed",
            table: "OutboxMessages");

        migrationBuilder.DropColumn(
            name: "NextAttemptAt",
            table: "OutboxMessages");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Unprocessed",
            table: "OutboxMessages",
            column: "ProcessedAt",
            filter: "[ProcessedAt] IS NULL")
            .Annotation("SqlServer:Include", new[] { "OccurredAt" });
    }
}
