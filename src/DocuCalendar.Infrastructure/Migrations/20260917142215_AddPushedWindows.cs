using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPushedWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PushedAt",
                schema: "calendar",
                table: "BusyBlocks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PushedEventId",
                schema: "calendar",
                table: "BusyBlocks",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PushedProvider",
                schema: "calendar",
                table: "BusyBlocks",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PushedAt",
                schema: "calendar",
                table: "BusyBlocks");

            migrationBuilder.DropColumn(
                name: "PushedEventId",
                schema: "calendar",
                table: "BusyBlocks");

            migrationBuilder.DropColumn(
                name: "PushedProvider",
                schema: "calendar",
                table: "BusyBlocks");
        }
    }
}
