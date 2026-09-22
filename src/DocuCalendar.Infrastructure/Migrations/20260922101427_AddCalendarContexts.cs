using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarContexts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarContexts",
                schema: "calendar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CalendarId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantContextId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarContexts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarContexts_CalendarId_TenantContextId",
                schema: "calendar",
                table: "CalendarContexts",
                columns: new[] { "CalendarId", "TenantContextId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarContexts_TenantId_TenantContextId",
                schema: "calendar",
                table: "CalendarContexts",
                columns: new[] { "TenantId", "TenantContextId" });

            // Carry the old routing across, so nobody's assistant stops booking on deploy.
            //
            // A per-context default becomes one link. The account-wide fallback ("Everywhere
            // else") applied to every knowledge base without a row of its own, so it becomes a
            // link to each of those: the same calendars keep answering the same lines, and the
            // owner can now add colleagues alongside them instead of replacing them.
            migrationBuilder.Sql("""
                INSERT INTO calendar."CalendarContexts" ("Id", "TenantId", "CalendarId", "TenantContextId", "CreatedAt")
                SELECT gen_random_uuid(), d."TenantId", d."CalendarId", d."TenantContextId", now()
                FROM calendar."ContextDefaults" d
                JOIN calendar."Calendars" c ON c."Id" = d."CalendarId" AND c."Active"
                WHERE d."TenantContextId" IS NOT NULL
                ON CONFLICT DO NOTHING;

                INSERT INTO calendar."CalendarContexts" ("Id", "TenantId", "CalendarId", "TenantContextId", "CreatedAt")
                SELECT gen_random_uuid(), f."TenantId", f."CalendarId", k."TenantContextId", now()
                FROM calendar."ContextDefaults" f
                JOIN calendar."Calendars" c ON c."Id" = f."CalendarId" AND c."Active"
                JOIN calendar."KnownContexts" k ON k."TenantId" = f."TenantId"
                WHERE f."TenantContextId" IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM calendar."ContextDefaults" own
                      WHERE own."TenantId" = f."TenantId" AND own."TenantContextId" = k."TenantContextId")
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarContexts",
                schema: "calendar");
        }
    }
}
