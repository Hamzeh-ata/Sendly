using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly21 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PagePurpose",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PriorityScore",
                table: "DiscoveredUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasonCode",
                table: "DiscoveredUrls",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PagePurpose",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "PriorityScore",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "RejectionReasonCode",
                table: "DiscoveredUrls");
        }
    }
}
