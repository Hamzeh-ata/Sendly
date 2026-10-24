using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly18 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscoverySources",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedBaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedDomain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    AdapterKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CountriesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IndustriesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    AutomatedAccessPermitted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentDiscoverySourceId = table.Column<string>(type: "TEXT", nullable: true),
                    LastAttemptedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastSucceededAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    NextEligibleRetryAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastFreshnessAssessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidatesDiscovered = table.Column<int>(type: "INTEGER", nullable: false),
                    UniqueCompaniesDiscovered = table.Column<int>(type: "INTEGER", nullable: false),
                    QualifiedProspectsYielded = table.Column<int>(type: "INTEGER", nullable: false),
                    RelevanceScore = table.Column<double>(type: "REAL", nullable: false),
                    FreshnessScore = table.Column<double>(type: "REAL", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ProvenanceNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ConfigurationJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    IsSystemManaged = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoverySources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoverySources_DiscoverySources_ParentDiscoverySourceId",
                        column: x => x.ParentDiscoverySourceId,
                        principalTable: "DiscoverySources",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySources_NormalizedBaseUrl",
                table: "DiscoverySources",
                column: "NormalizedBaseUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySources_ParentDiscoverySourceId",
                table: "DiscoverySources",
                column: "ParentDiscoverySourceId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoverySources_Status",
                table: "DiscoverySources",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscoverySources");
        }
    }
}
