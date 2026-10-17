using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly05 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "SmtpSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SmtpSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpProfileName",
                table: "Campaigns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SmtpSettingsId",
                table: "Campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE SmtpSettings
                SET Name = CASE
                    WHEN TRIM(SenderDisplayName) <> '' THEN SenderDisplayName
                    WHEN TRIM(SenderEmail) <> '' THEN SenderEmail
                    ELSE 'Default'
                END,
                IsDefault = 1
                WHERE Name = '' OR Name IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_SmtpSettingsId",
                table: "Campaigns",
                column: "SmtpSettingsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_SmtpSettings_SmtpSettingsId",
                table: "Campaigns",
                column: "SmtpSettingsId",
                principalTable: "SmtpSettings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_SmtpSettings_SmtpSettingsId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_SmtpSettingsId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "SmtpSettings");

            migrationBuilder.DropColumn(
                name: "SmtpProfileName",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SmtpSettingsId",
                table: "Campaigns");
        }
    }
}
