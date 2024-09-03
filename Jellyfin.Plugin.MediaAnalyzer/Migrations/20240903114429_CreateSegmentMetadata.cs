using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Plugin.MediaAnalyzer.Migrations
{
    /// <inheritdoc />
    public partial class CreateSegmentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlacklistSegment");

            migrationBuilder.CreateTable(
                name: "SegmentMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesName = table.Column<string>(type: "TEXT", nullable: false),
                    SegmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    PreventAnalyzing = table.Column<bool>(type: "INTEGER", nullable: false),
                    AnalyzerType = table.Column<int>(type: "INTEGER", nullable: true),
                    AnalyzerNote = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SegmentMetadata", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SegmentMetadata_ItemId",
                table: "SegmentMetadata",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SegmentMetadata");

            migrationBuilder.CreateTable(
                name: "BlacklistSegment",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistSegment", x => new { x.ItemId, x.Type });
                });
        }
    }
}
