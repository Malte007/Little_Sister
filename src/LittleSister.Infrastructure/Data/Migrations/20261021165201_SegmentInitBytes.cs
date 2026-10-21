using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SegmentInitBytes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InitBytes",
                table: "Segments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitBytes",
                table: "Segments");
        }
    }
}
