using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LittleSister.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Cameras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MinFreeGb",
                table: "SystemSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 10.0);

            migrationBuilder.CreateTable(
                name: "Cameras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MainStreamUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SubStreamUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ProtectedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    OnvifAddress = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Manufacturer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecordingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    QuotaGb = table.Column<double>(type: "REAL", nullable: false),
                    MaxRetentionDays = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cameras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Segments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CameraId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    IsProtected = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Segments_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Segments_CameraId_StartUtc",
                table: "Segments",
                columns: new[] { "CameraId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Segments_StartUtc",
                table: "Segments",
                column: "StartUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Segments");

            migrationBuilder.DropTable(
                name: "Cameras");

            migrationBuilder.DropColumn(
                name: "MinFreeGb",
                table: "SystemSettings");
        }
    }
}
