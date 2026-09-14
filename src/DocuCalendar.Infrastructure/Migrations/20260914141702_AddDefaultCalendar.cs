using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                schema: "calendar",
                table: "Calendars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Every person who already has calendars gets a star on their oldest active one —
            // the same choice the code makes for a person's first calendar — so nobody is left
            // with a diary the assistant cannot pick.
            migrationBuilder.Sql("""
                UPDATE calendar."Calendars" SET "IsDefault" = true
                WHERE "Id" IN (
                    SELECT DISTINCT ON ("TenantId", "OwnerUserId") "Id"
                    FROM calendar."Calendars"
                    WHERE "Active" = true
                    ORDER BY "TenantId", "OwnerUserId", "CreatedAt"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                schema: "calendar",
                table: "Calendars");
        }
    }
}
