using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly26 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fax",
                table: "Prospects",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawOsmTagsJson",
                table: "Prospects",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fax",
                table: "Prospects");

            migrationBuilder.DropColumn(
                name: "RawOsmTagsJson",
                table: "Prospects");
        }
    }
}
