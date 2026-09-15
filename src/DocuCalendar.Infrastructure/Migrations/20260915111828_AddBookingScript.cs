using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingScript : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BookingScriptJson",
                schema: "calendar",
                table: "Calendars",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnswersJson",
                schema: "calendar",
                table: "Appointments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceName",
                schema: "calendar",
                table: "Appointments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingScriptJson",
                schema: "calendar",
                table: "Calendars");

            migrationBuilder.DropColumn(
                name: "AnswersJson",
                schema: "calendar",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "ServiceName",
                schema: "calendar",
                table: "Appointments");
        }
    }
}
