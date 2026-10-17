using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly07 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmailAccountId",
                table: "EmailTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_EmailAccountId",
                table: "EmailTemplates",
                column: "EmailAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmailTemplates_EmailAccounts_EmailAccountId",
                table: "EmailTemplates",
                column: "EmailAccountId",
                principalTable: "EmailAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmailTemplates_EmailAccounts_EmailAccountId",
                table: "EmailTemplates");

            migrationBuilder.DropIndex(
                name: "IX_EmailTemplates_EmailAccountId",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "EmailAccountId",
                table: "EmailTemplates");
        }
    }
}
