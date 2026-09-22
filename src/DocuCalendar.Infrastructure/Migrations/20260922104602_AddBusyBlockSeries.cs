using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuCalendar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusyBlockSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeriesId",
                schema: "calendar",
                table: "BusyBlocks",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeriesId",
                schema: "calendar",
                table: "BusyBlocks");
        }
    }
}
