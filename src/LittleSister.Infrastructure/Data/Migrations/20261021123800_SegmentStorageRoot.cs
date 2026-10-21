using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SegmentStorageRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageRoot",
                table: "Segments",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            // Bestehende Segmente lagen bisher immer im aktuellen Aufnahmeordner
            migrationBuilder.Sql("UPDATE Segments SET StorageRoot = (SELECT RecordingsPath FROM SystemSettings WHERE Id = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageRoot",
                table: "Segments");
        }
    }
}
