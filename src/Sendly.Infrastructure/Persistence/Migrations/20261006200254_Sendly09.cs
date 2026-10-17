using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly09 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonalizationJson",
                table: "Prospects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "CampaignRecipients",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "CampaignRecipients",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonalizationJson",
                table: "CampaignRecipients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProspectId",
                table: "CampaignRecipients",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonalizationJson",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "PersonalizationJson",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "ProspectId",
                table: "CampaignRecipients");
        }
    }
}
