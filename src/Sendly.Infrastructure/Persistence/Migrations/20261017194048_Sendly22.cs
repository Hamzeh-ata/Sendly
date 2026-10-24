using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly22 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalLinksJson",
                table: "CrawledPages",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PagePurpose",
                table: "CrawledPages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalLinksJson",
                table: "CrawledPages");

            migrationBuilder.DropColumn(
                name: "PagePurpose",
                table: "CrawledPages");
        }
    }
}
