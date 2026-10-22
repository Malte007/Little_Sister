using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VideoMotionDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bestehende Kameras erkennen Bewegung künftig im Bild; ONVIF bleibt wählbar
            migrationBuilder.AddColumn<string>(
                name: "MotionDetection",
                table: "Cameras",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Video");

            migrationBuilder.AddColumn<int>(
                name: "MotionSensitivity",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MotionDetection",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "MotionSensitivity",
                table: "Cameras");
        }
    }
}
