using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MotionRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreRollSeconds",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "PostRollSeconds",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "RecordingMode",
                table: "Cameras",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Continuous");

            // Bisher nur an/aus: aus wird "Off", an bleibt Daueraufnahme
            migrationBuilder.Sql("UPDATE Cameras SET RecordingMode = CASE WHEN RecordingEnabled THEN 'Continuous' ELSE 'Off' END");

            migrationBuilder.DropColumn(
                name: "RecordingEnabled",
                table: "Cameras");

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_CameraId_StartUtc",
                table: "Events",
                columns: new[] { "CameraId", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.AddColumn<bool>(
                name: "RecordingEnabled",
                table: "Cameras",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE Cameras SET RecordingEnabled = CASE WHEN RecordingMode = 'Off' THEN 0 ELSE 1 END");

            migrationBuilder.DropColumn(
                name: "PreRollSeconds",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "PostRollSeconds",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "RecordingMode",
                table: "Cameras");
        }
    }
}
