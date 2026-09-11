using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKeyProtected",
                schema: "calendar",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "calendar",
                table: "BusyBlocks",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalEventId",
                schema: "calendar",
                table: "Appointments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                schema: "calendar",
                table: "Appointments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalSyncedAt",
                schema: "calendar",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalConnections",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountEmail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RefreshTokenProtected = table.Column<string>(type: "text", nullable: false),
                    AccessTokenProtected = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "connected"),
                    LastSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastPulled = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastPushed = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ConnectedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CalendarId_ExternalEventId",
                schema: "calendar",
                table: "Appointments",
                columns: new[] { "CalendarId", "ExternalEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_CalendarId",
                schema: "calendar",
                table: "ExternalConnections",
                column: "CalendarId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConnections_TenantId_Status",
                schema: "calendar",
                table: "ExternalConnections",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalConnections",
                schema: "calendar");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CalendarId_ExternalEventId",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ApiKeyProtected",
                schema: "calendar",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ExternalEventId",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ExternalSyncedAt",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                schema: "calendar",
                table: "BusyBlocks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);
        }
    }
}
