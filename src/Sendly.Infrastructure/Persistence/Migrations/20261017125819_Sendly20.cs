using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Domain",
                table: "ResearchRunEvents",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "ResearchRunEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpStatusCode",
                table: "ResearchRunEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "ResearchRunEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityId",
                table: "ResearchRunEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityKind",
                table: "ResearchRunEvents",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "ResearchRunEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Stage",
                table: "ResearchRunEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "ResearchRunEvents",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BlockedBySsrf",
                table: "CrawledPages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "CrawledPages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RedirectCount",
                table: "CrawledPages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WasRedirected",
                table: "CrawledPages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRunEvents_ResearchRunId_Severity",
                table: "ResearchRunEvents",
                columns: new[] { "ResearchRunId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_ResearchRunEvents_ResearchRunId_Stage",
                table: "ResearchRunEvents",
                columns: new[] { "ResearchRunId", "Stage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ResearchRunEvents_ResearchRunId_Severity",
                table: "ResearchRunEvents");

            migrationBuilder.DropIndex(
                name: "IX_ResearchRunEvents_ResearchRunId_Stage",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "Domain",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "HttpStatusCode",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "RelatedEntityId",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "RelatedEntityKind",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "Stage",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "ResearchRunEvents");

            migrationBuilder.DropColumn(
                name: "BlockedBySsrf",
                table: "CrawledPages");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "CrawledPages");

            migrationBuilder.DropColumn(
                name: "RedirectCount",
                table: "CrawledPages");

            migrationBuilder.DropColumn(
                name: "WasRedirected",
                table: "CrawledPages");
        }
    }
}
