using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCatalog : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppTeamMembers");

        migrationBuilder.DropPrimaryKey(
            name: "PK_MonitoredApps",
            table: "MonitoredApps");

        migrationBuilder.RenameTable(
            name: "MonitoredApps",
            newName: "Applications");

        migrationBuilder.AddColumn<string>(
            name: "NotificationPrefs",
            table: "Users",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "{\"channel\":\"Email\"}");

        migrationBuilder.AddColumn<string>(
            name: "Code",
            table: "Applications",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Environments",
            table: "Applications",
            type: "nvarchar(max)",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<Guid>(
            name: "EscalationUserId",
            table: "Applications",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Applications",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<Guid>(
            name: "OwningTeamId",
            table: "Applications",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

        migrationBuilder.AddColumn<byte[]>(
            name: "RowVersion",
            table: "Applications",
            type: "rowversion",
            rowVersion: true,
            nullable: false,
            defaultValue: new byte[0]);

        migrationBuilder.AddPrimaryKey(
            name: "PK_Applications",
            table: "Applications",
            column: "Id");

        migrationBuilder.CreateTable(
            name: "Teams",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                TeamsChannelUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Teams", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TeamMembers",
            columns: table => new
            {
                TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TeamMembers", x => new { x.TeamId, x.UserId });
                table.ForeignKey(
                    name: "FK_TeamMembers_Teams_TeamId",
                    column: x => x.TeamId,
                    principalTable: "Teams",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TeamMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Applications_Code",
            table: "Applications",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Applications_EscalationUserId",
            table: "Applications",
            column: "EscalationUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Applications_OwningTeamId",
            table: "Applications",
            column: "OwningTeamId");

        migrationBuilder.CreateIndex(
            name: "IX_TeamMembers_UserId",
            table: "TeamMembers",
            column: "UserId");

        migrationBuilder.AddForeignKey(
            name: "FK_Applications_Teams_OwningTeamId",
            table: "Applications",
            column: "OwningTeamId",
            principalTable: "Teams",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_Applications_Users_EscalationUserId",
            table: "Applications",
            column: "EscalationUserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Applications_Teams_OwningTeamId",
            table: "Applications");

        migrationBuilder.DropForeignKey(
            name: "FK_Applications_Users_EscalationUserId",
            table: "Applications");

        migrationBuilder.DropTable(
            name: "TeamMembers");

        migrationBuilder.DropTable(
            name: "Teams");

        migrationBuilder.DropPrimaryKey(
            name: "PK_Applications",
            table: "Applications");

        migrationBuilder.DropIndex(
            name: "IX_Applications_Code",
            table: "Applications");

        migrationBuilder.DropIndex(
            name: "IX_Applications_EscalationUserId",
            table: "Applications");

        migrationBuilder.DropIndex(
            name: "IX_Applications_OwningTeamId",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "NotificationPrefs",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "Code",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "Environments",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "EscalationUserId",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "OwningTeamId",
            table: "Applications");

        migrationBuilder.DropColumn(
            name: "RowVersion",
            table: "Applications");

        migrationBuilder.RenameTable(
            name: "Applications",
            newName: "MonitoredApps");

        migrationBuilder.AddPrimaryKey(
            name: "PK_MonitoredApps",
            table: "MonitoredApps",
            column: "Id");

        migrationBuilder.CreateTable(
            name: "AppTeamMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TeamRole = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AppTeamMembers", x => x.Id);
                table.ForeignKey(
                    name: "FK_AppTeamMembers_MonitoredApps_ApplicationId",
                    column: x => x.ApplicationId,
                    principalTable: "MonitoredApps",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AppTeamMembers_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AppTeamMembers_ApplicationId_UserId",
            table: "AppTeamMembers",
            columns: new[] { "ApplicationId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AppTeamMembers_UserId",
            table: "AppTeamMembers",
            column: "UserId");
    }
}
