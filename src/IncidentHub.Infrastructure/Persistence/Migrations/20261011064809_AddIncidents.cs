using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIncidents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "dbo");

        migrationBuilder.CreateSequence<int>(
            name: "IncidentNumbers",
            schema: "dbo",
            startValue: 1001L);

        migrationBuilder.CreateTable(
            name: "Incidents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Number = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR dbo.IncidentNumbers"),
                ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Environment = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                StepsToReproduce = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ReporterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssigneeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AssignedTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Fingerprint = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                AckDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResolveDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                IsSlaBreached = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                AcknowledgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                CloseReason = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                DuplicateOfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Incidents", x => x.Id);
                table.ForeignKey(
                    name: "FK_Incidents_Applications_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "Applications",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Incidents_Incidents_DuplicateOfId",
                    column: x => x.DuplicateOfId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Incidents_Teams_AssignedTeamId",
                    column: x => x.AssignedTeamId,
                    principalTable: "Teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Incidents_Users_AssigneeId",
                    column: x => x.AssigneeId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Incidents_Users_ReporterId",
                    column: x => x.ReporterId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IncidentHistory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Field = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                NewValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IncidentHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_IncidentHistory_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_IncidentHistory_Users_ActorId",
                    column: x => x.ActorId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IncidentHistory_ActorId",
            table: "IncidentHistory",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_IncidentHistory_Incident_CreatedAt",
            table: "IncidentHistory",
            columns: new[] { "IncidentId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_Application_CreatedAt",
            table: "Incidents",
            columns: new[] { "ApplicationId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_AssignedTeamId",
            table: "Incidents",
            column: "AssignedTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_AssigneeId",
            table: "Incidents",
            column: "AssigneeId");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_DuplicateOfId",
            table: "Incidents",
            column: "DuplicateOfId");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_Fingerprint_Open",
            table: "Incidents",
            column: "Fingerprint",
            unique: true,
            filter: "[Status] <> 'Closed' AND [Fingerprint] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_Number",
            table: "Incidents",
            column: "Number",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_ReporterId",
            table: "Incidents",
            column: "ReporterId");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_Status_Severity",
            table: "Incidents",
            columns: new[] { "Status", "Severity" })
            .Annotation("SqlServer:Include", new[] { "ApplicationId", "AssigneeId" });

        // Full-text search on Title + Description (similar-incident hints, BE-06). Needs SQL Server with Full-Text Search
        // installed (docker/mssql-fts). These statements cannot run inside a user transaction.
        migrationBuilder.Sql(
            "IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ftIncidentHub') " +
            "CREATE FULLTEXT CATALOG ftIncidentHub;",
            suppressTransaction: true);

        migrationBuilder.Sql(
            "CREATE FULLTEXT INDEX ON dbo.Incidents (Title, Description) " +
            "KEY INDEX PK_Incidents ON ftIncidentHub WITH CHANGE_TRACKING AUTO;",
            suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Incidents')) " +
            "DROP FULLTEXT INDEX ON dbo.Incidents;",
            suppressTransaction: true);

        migrationBuilder.Sql(
            "IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ftIncidentHub') " +
            "DROP FULLTEXT CATALOG ftIncidentHub;",
            suppressTransaction: true);

        migrationBuilder.DropTable(
            name: "IncidentHistory");

        migrationBuilder.DropTable(
            name: "Incidents");

        migrationBuilder.DropSequence(
            name: "IncidentNumbers",
            schema: "dbo");
    }
}
