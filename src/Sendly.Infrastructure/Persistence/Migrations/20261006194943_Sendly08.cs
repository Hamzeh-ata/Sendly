using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly08 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutreachContactHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactValue = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    ProspectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContactedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachContactHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinQualificationScore = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateHandling = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableEmail = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnableWhatsApp = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultRequestedProspectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutreachProjectCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachProjectCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachProjectCountries_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutreachProjectIndustries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachProjectIndustries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachProjectIndustries_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutreachProjectKeywords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsExcluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachProjectKeywords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachProjectKeywords_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutreachProjectTargetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachProjectTargetRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachProjectTargetRoles_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchIndustry = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MatchCompanySize = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MatchNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectTemplates_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectTemplates_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResearchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedProspectCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IncludeEmail = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeWhatsApp = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinQualificationScore = table.Column<int>(type: "INTEGER", nullable: false),
                    ProgressPercent = table.Column<int>(type: "INTEGER", nullable: false),
                    CompaniesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    ContactsFound = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidEmails = table.Column<int>(type: "INTEGER", nullable: false),
                    ValidWhatsApp = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicatesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    QualifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Warning = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResearchRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResearchRuns_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prospects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResearchRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedCompanyName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Website = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Industry = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CompanyDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CompanySize = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ContactName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ContactRole = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    WhatsApp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    NormalizedWhatsApp = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LinkedInUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ResearchNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    QualificationSummary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    QualificationScore = table.Column<int>(type: "INTEGER", nullable: true),
                    EmailConfidenceScore = table.Column<int>(type: "INTEGER", nullable: true),
                    WhatsAppConfidenceScore = table.Column<int>(type: "INTEGER", nullable: true),
                    IsQualified = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DuplicateKind = table.Column<int>(type: "INTEGER", nullable: false),
                    RecommendedTemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecommendedTemplateReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RecommendedTemplateConfidence = table.Column<int>(type: "INTEGER", nullable: true),
                    OutreachCampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prospects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prospects_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prospects_ResearchRuns_ResearchRunId",
                        column: x => x.ResearchRunId,
                        principalTable: "ResearchRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachContactHistories_NormalizedValue_Channel",
                table: "OutreachContactHistories",
                columns: new[] { "NormalizedValue", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachProjectCountries_ProjectId",
                table: "OutreachProjectCountries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachProjectIndustries_ProjectId",
                table: "OutreachProjectIndustries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachProjectKeywords_ProjectId",
                table: "OutreachProjectKeywords",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachProjectTargetRoles_ProjectId",
                table: "OutreachProjectTargetRoles",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplates_ProjectId",
                table: "ProjectTemplates",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectTemplates_TemplateId",
                table: "ProjectTemplates",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Prospects_NormalizedEmail",
                table: "Prospects",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Prospects_NormalizedWhatsApp",
                table: "Prospects",
                column: "NormalizedWhatsApp");

            migrationBuilder.CreateIndex(
                name: "IX_Prospects_ProjectId_NormalizedCompanyName",
                table: "Prospects",
                columns: new[] { "ProjectId", "NormalizedCompanyName" });

            migrationBuilder.CreateIndex(
                name: "IX_Prospects_ResearchRunId",
                table: "Prospects",
                column: "ResearchRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRuns_ProjectId",
                table: "ResearchRuns",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachContactHistories");

            migrationBuilder.DropTable(
                name: "OutreachProjectCountries");

            migrationBuilder.DropTable(
                name: "OutreachProjectIndustries");

            migrationBuilder.DropTable(
                name: "OutreachProjectKeywords");

            migrationBuilder.DropTable(
                name: "OutreachProjectTargetRoles");

            migrationBuilder.DropTable(
                name: "ProjectTemplates");

            migrationBuilder.DropTable(
                name: "Prospects");

            migrationBuilder.DropTable(
                name: "ResearchRuns");

            migrationBuilder.DropTable(
                name: "OutreachProjects");
        }
    }
}
