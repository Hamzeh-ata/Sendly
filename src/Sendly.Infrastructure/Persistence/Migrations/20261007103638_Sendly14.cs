using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalEmailsJson",
                table: "Prospects",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalPhonesJson",
                table: "Prospects",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCity",
                table: "Prospects",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhone",
                table: "Prospects",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedWebsite",
                table: "Prospects",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrlsJson",
                table: "Prospects",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Prospects"
                SET "NormalizedEmail" = NULL
                WHERE "Id" IN (
                    SELECT p."Id"
                    FROM "Prospects" AS p
                    INNER JOIN (
                        SELECT "ProjectId", "NormalizedEmail", MIN("Id") AS "KeepId"
                        FROM "Prospects"
                        WHERE "NormalizedEmail" IS NOT NULL AND TRIM("NormalizedEmail") != ''
                        GROUP BY "ProjectId", "NormalizedEmail"
                        HAVING COUNT(*) > 1
                    ) AS d
                        ON p."ProjectId" = d."ProjectId"
                       AND p."NormalizedEmail" = d."NormalizedEmail"
                    WHERE p."Id" != d."KeepId"
                );
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Prospects"
                SET "NormalizedWhatsApp" = NULL
                WHERE "Id" IN (
                    SELECT p."Id"
                    FROM "Prospects" AS p
                    INNER JOIN (
                        SELECT "ProjectId", "NormalizedWhatsApp", MIN("Id") AS "KeepId"
                        FROM "Prospects"
                        WHERE "NormalizedWhatsApp" IS NOT NULL AND TRIM("NormalizedWhatsApp") != ''
                        GROUP BY "ProjectId", "NormalizedWhatsApp"
                        HAVING COUNT(*) > 1
                    ) AS d
                        ON p."ProjectId" = d."ProjectId"
                       AND p."NormalizedWhatsApp" = d."NormalizedWhatsApp"
                    WHERE p."Id" != d."KeepId"
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Prospects_NormalizedWebsite"
                ON "Prospects" ("NormalizedWebsite");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Prospects_ProjectId_NormalizedEmail"
                ON "Prospects" ("ProjectId", "NormalizedEmail");
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Prospects_ProjectId_NormalizedPhone"
                ON "Prospects" ("ProjectId", "NormalizedPhone");
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Prospects_ProjectId_NormalizedWebsite"
                ON "Prospects" ("ProjectId", "NormalizedWebsite");
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Prospects_ProjectId_NormalizedWhatsApp"
                ON "Prospects" ("ProjectId", "NormalizedWhatsApp");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Prospects_NormalizedWebsite";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Prospects_ProjectId_NormalizedEmail";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Prospects_ProjectId_NormalizedPhone";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Prospects_ProjectId_NormalizedWebsite";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Prospects_ProjectId_NormalizedWhatsApp";""");

            migrationBuilder.DropColumn(name: "AdditionalEmailsJson", table: "Prospects");
            migrationBuilder.DropColumn(name: "AdditionalPhonesJson", table: "Prospects");
            migrationBuilder.DropColumn(name: "NormalizedCity", table: "Prospects");
            migrationBuilder.DropColumn(name: "NormalizedPhone", table: "Prospects");
            migrationBuilder.DropColumn(name: "NormalizedWebsite", table: "Prospects");
            migrationBuilder.DropColumn(name: "SourceUrlsJson", table: "Prospects");
        }
    }
}
