using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbedHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmbedHosts",
                schema: "calendar",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbedHosts", x => new { x.TenantId, x.Host });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmbedHosts",
                schema: "calendar");
        }
    }
}
