using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTemplateLanguage",
                table: "EmailTemplates",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppTemplateName",
                table: "EmailTemplates",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutreachSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachSequences_OutreachProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "OutreachProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutreachSequenceSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DelayHoursAfterPrevious = table.Column<int>(type: "INTEGER", nullable: false),
                    WhatsAppTemplateName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    WhatsAppTemplateLanguage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachSequenceSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachSequenceSteps_EmailTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "EmailTemplates",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OutreachSequenceSteps_OutreachSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "OutreachSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProspectSequenceEnrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProspectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStepOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    NextRunAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    EnrolledAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProspectSequenceEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProspectSequenceEnrollments_OutreachSequences_SequenceId",
                        column: x => x.SequenceId,
                        principalTable: "OutreachSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProspectSequenceEnrollments_Prospects_ProspectId",
                        column: x => x.ProspectId,
                        principalTable: "Prospects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachSequences_ProjectId",
                table: "OutreachSequences",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachSequenceSteps_SequenceId",
                table: "OutreachSequenceSteps",
                column: "SequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachSequenceSteps_TemplateId",
                table: "OutreachSequenceSteps",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProspectSequenceEnrollments_ProspectId_SequenceId",
                table: "ProspectSequenceEnrollments",
                columns: new[] { "ProspectId", "SequenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProspectSequenceEnrollments_SequenceId",
                table: "ProspectSequenceEnrollments",
                column: "SequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProspectSequenceEnrollments_Status_NextRunAtUtc",
                table: "ProspectSequenceEnrollments",
                columns: new[] { "Status", "NextRunAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachSequenceSteps");

            migrationBuilder.DropTable(
                name: "ProspectSequenceEnrollments");

            migrationBuilder.DropTable(
                name: "OutreachSequences");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateLanguage",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppTemplateName",
                table: "EmailTemplates");
        }
    }
}
