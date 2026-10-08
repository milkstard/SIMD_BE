using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUsersAndAppTeams : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonitoredApps",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MonitoredApps", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntraObjectId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AppTeamMembers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TeamRole = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
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

        migrationBuilder.CreateIndex(
            name: "IX_Users_EntraObjectId",
            table: "Users",
            column: "EntraObjectId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AppTeamMembers");

        migrationBuilder.DropTable(
            name: "MonitoredApps");

        migrationBuilder.DropTable(
            name: "Users");
    }
}
