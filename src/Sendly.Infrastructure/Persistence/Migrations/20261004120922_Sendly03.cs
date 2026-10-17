using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly03 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateName",
                table: "Campaigns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "CampaignAttachments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    HtmlBody = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailTemplates_EmailSignatures_SignatureId",
                        column: x => x.SignatureId,
                        principalTable: "EmailSignatures",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_TemplateId",
                table: "Campaigns",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAttachments_TemplateId",
                table: "CampaignAttachments",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_SignatureId",
                table: "EmailTemplates",
                column: "SignatureId");

            migrationBuilder.AddForeignKey(
                name: "FK_CampaignAttachments_EmailTemplates_TemplateId",
                table: "CampaignAttachments",
                column: "TemplateId",
                principalTable: "EmailTemplates",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_EmailTemplates_TemplateId",
                table: "Campaigns",
                column: "TemplateId",
                principalTable: "EmailTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CampaignAttachments_EmailTemplates_TemplateId",
                table: "CampaignAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_EmailTemplates_TemplateId",
                table: "Campaigns");

            migrationBuilder.DropTable(
                name: "EmailTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_TemplateId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_CampaignAttachments_TemplateId",
                table: "CampaignAttachments");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "TemplateName",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "CampaignAttachments");
        }
    }
}
