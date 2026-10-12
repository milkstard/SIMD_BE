using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOutboxMessages : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                Error = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Unprocessed",
            table: "OutboxMessages",
            column: "ProcessedAt",
            filter: "[ProcessedAt] IS NULL")
            .Annotation("SqlServer:Include", new[] { "OccurredAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OutboxMessages");
    }
}
