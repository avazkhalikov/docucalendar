using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "calendar");

            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VisitorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VisitorPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Topic = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "manual"),
                    SourceRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "confirmed"),
                    CancelledByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusyBlocks",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "manual"),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusyBlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calendars",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 20),
                    MaxMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    BufferMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MinLeadMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 60),
                    HorizonDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    WeeklyAvailabilityJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{\"mon\":[[\"09:00\",\"13:00\"],[\"14:00\",\"18:00\"]],\"tue\":[[\"09:00\",\"13:00\"],[\"14:00\",\"18:00\"]],\"wed\":[[\"09:00\",\"13:00\"],[\"14:00\",\"18:00\"]],\"thu\":[[\"09:00\",\"13:00\"],[\"14:00\",\"18:00\"]],\"fri\":[[\"09:00\",\"13:00\"],[\"14:00\",\"18:00\"]],\"sat\":[],\"sun\":[]}"),
                    Active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContextDefaults",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantContextId = table.Column<Guid>(type: "uuid", nullable: true),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContextDefaults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnownContexts",
                schema: "calendar",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    Domain = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownContexts", x => new { x.TenantId, x.TenantContextId });
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "calendar",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: "Asia/Tashkent"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CalendarId_StartsAt",
                schema: "calendar",
                table: "Appointments",
                columns: new[] { "CalendarId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TenantId_StartsAt",
                schema: "calendar",
                table: "Appointments",
                columns: new[] { "TenantId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_VisitorPhone",
                schema: "calendar",
                table: "Appointments",
                column: "VisitorPhone");

            migrationBuilder.CreateIndex(
                name: "IX_BusyBlocks_CalendarId_Source_ExternalId",
                schema: "calendar",
                table: "BusyBlocks",
                columns: new[] { "CalendarId", "Source", "ExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusyBlocks_CalendarId_StartsAt",
                schema: "calendar",
                table: "BusyBlocks",
                columns: new[] { "CalendarId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_OwnerUserId",
                schema: "calendar",
                table: "Calendars",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_TenantId_Active",
                schema: "calendar",
                table: "Calendars",
                columns: new[] { "TenantId", "Active" });

            migrationBuilder.CreateIndex(
                name: "IX_ContextDefaults_TenantId_TenantContextId",
                schema: "calendar",
                table: "ContextDefaults",
                columns: new[] { "TenantId", "TenantContextId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "BusyBlocks",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "Calendars",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "ContextDefaults",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "KnownContexts",
                schema: "calendar");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "calendar");
        }
    }
}
