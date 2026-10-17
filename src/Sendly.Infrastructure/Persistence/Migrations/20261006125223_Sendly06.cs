using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly06 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_SmtpSettings_SmtpSettingsId",
                table: "Campaigns");

            migrationBuilder.CreateTable(
                name: "EmailAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<int>(type: "INTEGER", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EmailAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: true),
                    SecurityMode = table.Column<int>(type: "INTEGER", nullable: true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    PasswordProtected = table.Column<string>(type: "TEXT", nullable: true),
                    SenderEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    SenderDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReplyToEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    AccessTokenProtected = table.Column<string>(type: "TEXT", nullable: true),
                    RefreshTokenProtected = table.Column<string>(type: "TEXT", nullable: true),
                    TokenExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MicrosoftUserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MicrosoftTenantId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAccounts", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO EmailAccounts (
                    Id, Provider, DisplayName, EmailAddress, IsDefault, IsActive,
                    Host, Port, SecurityMode, Username, PasswordProtected,
                    SenderEmail, SenderDisplayName, ReplyToEmail,
                    AccessTokenProtected, RefreshTokenProtected, TokenExpiresAtUtc,
                    MicrosoftUserId, MicrosoftTenantId, CreatedAtUtc, UpdatedAtUtc
                )
                SELECT
                    Id,
                    0,
                    CASE WHEN TRIM(Name) <> '' THEN Name ELSE SenderEmail END,
                    SenderEmail,
                    IsDefault,
                    1,
                    Host,
                    Port,
                    SecurityMode,
                    Username,
                    PasswordProtected,
                    SenderEmail,
                    SenderDisplayName,
                    ReplyToEmail,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    UpdatedAtUtc,
                    UpdatedAtUtc
                FROM SmtpSettings;
                """);

            migrationBuilder.DropTable(
                name: "SmtpSettings");

            migrationBuilder.RenameColumn(
                name: "SmtpSettingsId",
                table: "Campaigns",
                newName: "EmailAccountId");

            migrationBuilder.RenameColumn(
                name: "SmtpProfileName",
                table: "Campaigns",
                newName: "SendingAccountName");

            migrationBuilder.RenameIndex(
                name: "IX_Campaigns_SmtpSettingsId",
                table: "Campaigns",
                newName: "IX_Campaigns_EmailAccountId");

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "TemplateRecipientHistories",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendingAccountEmail",
                table: "TemplateRecipientHistories",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SendingAccountId",
                table: "TemplateRecipientHistories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "Campaigns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SendingAccountEmail",
                table: "Campaigns",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE Campaigns
                SET Provider = 0,
                    SendingAccountEmail = (
                        SELECT EmailAddress FROM EmailAccounts WHERE EmailAccounts.Id = Campaigns.EmailAccountId
                    )
                WHERE EmailAccountId IS NOT NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_EmailAccounts_EmailAccountId",
                table: "Campaigns",
                column: "EmailAccountId",
                principalTable: "EmailAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_EmailAccounts_EmailAccountId",
                table: "Campaigns");

            migrationBuilder.CreateTable(
                name: "SmtpSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PasswordProtected = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplyToEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    SecurityMode = table.Column<int>(type: "INTEGER", nullable: false),
                    SenderDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SenderEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmtpSettings", x => x.Id);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO SmtpSettings (
                    Id, Name, IsDefault, Host, Port, SecurityMode, Username, PasswordProtected,
                    SenderEmail, SenderDisplayName, ReplyToEmail, UpdatedAtUtc
                )
                SELECT
                    Id,
                    DisplayName,
                    IsDefault,
                    COALESCE(Host, ''),
                    COALESCE(Port, 587),
                    COALESCE(SecurityMode, 1),
                    COALESCE(Username, ''),
                    COALESCE(PasswordProtected, ''),
                    COALESCE(SenderEmail, EmailAddress),
                    COALESCE(SenderDisplayName, DisplayName),
                    ReplyToEmail,
                    UpdatedAtUtc
                FROM EmailAccounts
                WHERE Provider = 0;
                """);

            migrationBuilder.DropTable(
                name: "EmailAccounts");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "TemplateRecipientHistories");

            migrationBuilder.DropColumn(
                name: "SendingAccountEmail",
                table: "TemplateRecipientHistories");

            migrationBuilder.DropColumn(
                name: "SendingAccountId",
                table: "TemplateRecipientHistories");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SendingAccountEmail",
                table: "Campaigns");

            migrationBuilder.RenameColumn(
                name: "SendingAccountName",
                table: "Campaigns",
                newName: "SmtpProfileName");

            migrationBuilder.RenameColumn(
                name: "EmailAccountId",
                table: "Campaigns",
                newName: "SmtpSettingsId");

            migrationBuilder.RenameIndex(
                name: "IX_Campaigns_EmailAccountId",
                table: "Campaigns",
                newName: "IX_Campaigns_SmtpSettingsId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_SmtpSettings_SmtpSettingsId",
                table: "Campaigns",
                column: "SmtpSettingsId",
                principalTable: "SmtpSettings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
