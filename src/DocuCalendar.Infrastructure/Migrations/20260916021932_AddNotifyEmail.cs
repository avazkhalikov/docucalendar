using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifyEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotifyEmail",
                schema: "calendar",
                table: "Appointments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyEmail",
                schema: "calendar",
                table: "Appointments");
        }
    }
}
