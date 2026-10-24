using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly25 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MapCoverageSummaryJson",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MapPlacesDiscovered",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MapPlanJson",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 64000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MapRegionsCompleted",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MapRegionsTotal",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CategoryTagsJson",
                table: "Prospects",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Prospects",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Prospects",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "Prospects",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsmElementId",
                table: "Prospects",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsmElementType",
                table: "Prospects",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceObjectKey",
                table: "Prospects",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StreetAddress",
                table: "Prospects",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MapCoverageRegions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    ParentRegionId = table.Column<string>(type: "TEXT", nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    RegionLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    South = table.Column<double>(type: "REAL", nullable: false),
                    West = table.Column<double>(type: "REAL", nullable: false),
                    North = table.Column<double>(type: "REAL", nullable: false),
                    East = table.Column<double>(type: "REAL", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    KeywordFamilyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceCategoriesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordsFound = table.Column<int>(type: "INTEGER", nullable: false),
                    UniqueRecordsFound = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CoverageNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapCoverageRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapCoverageRegions_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prospects_ProjectId_SourceObjectKey",
                table: "Prospects",
                columns: new[] { "ProjectId", "SourceObjectKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MapCoverageRegions_ProjectId_CountryCode",
                table: "MapCoverageRegions",
                columns: new[] { "ProjectId", "CountryCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MapCoverageRegions_ResearchRunId_SourceName_KeywordFamilyKey",
                table: "MapCoverageRegions",
                columns: new[] { "ResearchRunId", "SourceName", "KeywordFamilyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_MapCoverageRegions_ResearchRunId_Status",
                table: "MapCoverageRegions",
                columns: new[] { "ResearchRunId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapCoverageRegions");

            migrationBuilder.DropIndex(
                name: "IX_Prospects_ProjectId_SourceObjectKey",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "MapCoverageSummaryJson",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "MapPlacesDiscovered",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "MapPlanJson",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "MapRegionsCompleted",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "MapRegionsTotal",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CategoryTagsJson",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "OsmElementId",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "OsmElementType",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "SourceObjectKey",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "StreetAddress",
                table: "Prospects");
        }
    }
}
