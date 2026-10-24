using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly23 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssociatedIndustry",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssociatedLocation",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssociatedOrganizationName",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscoveryReason",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedDiscoveryValue",
                table: "DiscoveredUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SchedulingReason",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvestigationContacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ProspectId = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ExactValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContactType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PageTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SurroundingText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    AttributionEvidence = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ValidationStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Confidence = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewStatus = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestigationContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestigationContacts_Prospects_ProspectId",
                        column: x => x.ProspectId,
                        principalTable: "Prospects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_InvestigationContacts_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationContacts_ProspectId_NormalizedValue",
                table: "InvestigationContacts",
                columns: new[] { "ProspectId", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationContacts_ResearchRunId",
                table: "InvestigationContacts",
                column: "ResearchRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationContacts");

            migrationBuilder.DropColumn(
                name: "AssociatedIndustry",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "AssociatedLocation",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "AssociatedOrganizationName",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "DiscoveryReason",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "ExpectedDiscoveryValue",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "SchedulingReason",
                table: "DiscoveredUrls");
        }
    }
}
