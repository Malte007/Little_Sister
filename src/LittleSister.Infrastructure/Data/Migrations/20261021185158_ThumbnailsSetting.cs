using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ThumbnailsSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ThumbnailsEnabled",
                table: "SystemSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // Bestehende Installationen: Vorschaubilder an
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailsEnabled",
                table: "SystemSettings");
        }
    }
}
