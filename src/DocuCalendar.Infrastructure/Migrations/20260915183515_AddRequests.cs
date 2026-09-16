using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresConfirmation",
                schema: "calendar",
                table: "Calendars",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecidedAt",
                schema: "calendar",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecidedByName",
                schema: "calendar",
                table: "Appointments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresConfirmation",
                schema: "calendar",
                table: "Calendars");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DecidedByName",
                schema: "calendar",
                table: "Appointments");
        }
    }
}
