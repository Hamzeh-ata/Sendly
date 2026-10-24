using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly17 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CrawlErrors",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PagesCrawled",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ResearchQueryId",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResearchQueryVersionId",
                table: "ResearchRuns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UrlsDiscovered",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DiscoveredUrls",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedDomain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ParentDiscoveredUrlId = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DiscoveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscoveredUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscoveredUrls_DiscoveredUrls_ParentDiscoveredUrlId",
                        column: x => x.ParentDiscoveredUrlId,
                        principalTable: "DiscoveredUrls",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiscoveredUrls_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QualificationCriterionResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    ProspectId = table.Column<string>(type: "TEXT", nullable: false),
                    CriterionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CriterionLabel = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IsMandatory = table.Column<bool>(type: "INTEGER", nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScoreContribution = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    EvidenceIdsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualificationCriterionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualificationCriterionResults_Prospects_ProspectId",
                        column: x => x.ProspectId,
                        principalTable: "Prospects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QualificationCriterionResults_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchQueries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ActiveVersionId = table.Column<string>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchQueries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchQueries_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchRunEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DataJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchRunEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchRunEvents_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrawledPages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    DiscoveredUrlId = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedDomain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    HttpStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ContentLengthBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CanonicalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    FinalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RobotsAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TextExcerpt = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CrawlDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    CrawledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawledPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrawledPages_DiscoveredUrls_DiscoveredUrlId",
                        column: x => x.DiscoveredUrlId,
                        principalTable: "DiscoveredUrls",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CrawledPages_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchQueryVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchQueryId = table.Column<string>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PlainLanguageBrief = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    TargetIndustry = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    BusinessCategoriesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    TargetCountry = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RegionsJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CitiesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompanyTypesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompanySizesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RequiredCriteriaJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ExclusionCriteriaJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RequiredContactInfoJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RequiredEvidenceJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DiscoveryStrategiesJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MaxPagesPerDomain = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxCrawlDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConcurrentRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestsPerDomainPerMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxUrlsPerRun = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireOfficialWebsite = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequirePublicBusinessEmail = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableDirectoryDiscovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableSitemapDiscovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableLinkExpansion = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnablePriorResearchUrls = table.Column<bool>(type: "INTEGER", nullable: false),
                    StructuredDefinitionJson = table.Column<string>(type: "TEXT", maxLength: 16000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchQueryVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchQueryVersions_ResearchQueries_ResearchQueryId",
                        column: x => x.ResearchQueryId,
                        principalTable: "ResearchQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchSeedUrls",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchQueryId = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedDomain = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Label = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchSeedUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchSeedUrls_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResearchSeedUrls_ResearchQueries_ResearchQueryId",
                        column: x => x.ResearchQueryId,
                        principalTable: "ResearchQueries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<string>(type: "TEXT", nullable: false),
                    ProspectId = table.Column<string>(type: "TEXT", nullable: true),
                    CrawledPageId = table.Column<string>(type: "TEXT", nullable: true),
                    FactType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FactValue = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ConfidenceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FirstDiscoveredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastVerifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchEvidenceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchEvidenceItems_CrawledPages_CrawledPageId",
                        column: x => x.CrawledPageId,
                        principalTable: "CrawledPages",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResearchEvidenceItems_Prospects_ProspectId",
                        column: x => x.ProspectId,
                        principalTable: "Prospects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ResearchEvidenceItems_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_ResearchQueryId",
                table: "ResearchRuns",
                column: "ResearchQueryId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_ResearchQueryVersionId",
                table: "ResearchRuns",
                column: "ResearchQueryVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledPages_ContentHash",
                table: "CrawledPages",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledPages_DiscoveredUrlId",
                table: "CrawledPages",
                column: "DiscoveredUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledPages_NormalizedDomain",
                table: "CrawledPages",
                column: "NormalizedDomain");

            migrationBuilder.CreateIndex(
                name: "IX_CrawledPages_ResearchRunId_NormalizedUrl",
                table: "CrawledPages",
                columns: new[] { "ResearchRunId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredUrls_NormalizedDomain",
                table: "DiscoveredUrls",
                column: "NormalizedDomain");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredUrls_ParentDiscoveredUrlId",
                table: "DiscoveredUrls",
                column: "ParentDiscoveredUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredUrls_ResearchRunId_NormalizedUrl",
                table: "DiscoveredUrls",
                columns: new[] { "ResearchRunId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredUrls_ResearchRunId_Status",
                table: "DiscoveredUrls",
                columns: new[] { "ResearchRunId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_QualificationCriterionResults_ProspectId_CriterionKey",
                table: "QualificationCriterionResults",
                columns: new[] { "ProspectId", "CriterionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualificationCriterionResults_ResearchRunId",
                table: "QualificationCriterionResults",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchEvidenceItems_CrawledPageId",
                table: "ResearchEvidenceItems",
                column: "CrawledPageId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchEvidenceItems_ProspectId_FactType",
                table: "ResearchEvidenceItems",
                columns: new[] { "ProspectId", "FactType" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchEvidenceItems_ResearchRunId",
                table: "ResearchEvidenceItems",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchQueries_ProjectId_Name",
                table: "ResearchQueries",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchQueryVersions_ResearchQueryId_VersionNumber",
                table: "ResearchQueryVersions",
                columns: new[] { "ResearchQueryId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRunEvents_ResearchRunId_CreatedAtUtc",
                table: "ResearchRunEvents",
                columns: new[] { "ResearchRunId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSeedUrls_ProjectId_NormalizedUrl",
                table: "ResearchSeedUrls",
                columns: new[] { "ProjectId", "NormalizedUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSeedUrls_ResearchQueryId_NormalizedUrl",
                table: "ResearchSeedUrls",
                columns: new[] { "ResearchQueryId", "NormalizedUrl" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchRuns_ResearchQueries_ResearchQueryId",
                table: "ResearchRuns",
                column: "ResearchQueryId",
                principalTable: "ResearchQueries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ResearchRuns_ResearchQueryVersions_ResearchQueryVersionId",
                table: "ResearchRuns",
                column: "ResearchQueryVersionId",
                principalTable: "ResearchQueryVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResearchRuns_ResearchQueries_ResearchQueryId",
                table: "ResearchRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ResearchRuns_ResearchQueryVersions_ResearchQueryVersionId",
                table: "ResearchRuns");

            migrationBuilder.DropTable(
                name: "QualificationCriterionResults");

            migrationBuilder.DropTable(
                name: "ResearchEvidenceItems");

            migrationBuilder.DropTable(
                name: "ResearchQueryVersions");

            migrationBuilder.DropTable(
                name: "ResearchRunEvents");

            migrationBuilder.DropTable(
                name: "ResearchSeedUrls");

            migrationBuilder.DropTable(
                name: "CrawledPages");

            migrationBuilder.DropTable(
                name: "ResearchQueries");

            migrationBuilder.DropTable(
                name: "DiscoveredUrls");

            migrationBuilder.DropIndex(
                name: "IX_ResearchRuns_ResearchQueryId",
                table: "ResearchRuns");

            migrationBuilder.DropIndex(
                name: "IX_ResearchRuns_ResearchQueryVersionId",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CrawlErrors",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "PagesCrawled",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchQueryId",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchQueryVersionId",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "UrlsDiscovered",
                table: "ResearchRuns");
        }
    }
}
