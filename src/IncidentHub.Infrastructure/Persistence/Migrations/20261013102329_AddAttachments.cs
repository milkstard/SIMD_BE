using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IncidentHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAttachments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Attachments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FileName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                BlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ScanStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                ScanAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Attachments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Attachments_Comments_CommentId",
                    column: x => x.CommentId,
                    principalTable: "Comments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Attachments_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Attachments_Users_UploadedById",
                    column: x => x.UploadedById,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_CommentId",
            table: "Attachments",
            column: "CommentId");

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_IncidentId",
            table: "Attachments",
            column: "IncidentId");

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_Pending",
            table: "Attachments",
            columns: new[] { "ScanAttempts", "CreatedAt" },
            filter: "[ScanStatus] = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "IX_Attachments_Unlinked",
            table: "Attachments",
            columns: new[] { "UploadedById", "CreatedAt" },
            filter: "[IncidentId] IS NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Attachments");
    }
}
