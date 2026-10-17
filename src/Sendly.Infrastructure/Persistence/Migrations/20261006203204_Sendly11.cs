using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedVariantKey",
                table: "ProspectSequenceEnrollments",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrafficPercent",
                table: "OutreachSequenceSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<string>(
                name: "VariantKey",
                table: "OutreachSequenceSteps",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppButtonIndex",
                table: "EmailTemplates",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppButtonParameter",
                table: "EmailTemplates",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppButtonSubType",
                table: "EmailTemplates",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppHeaderParameter",
                table: "EmailTemplates",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackgroundJobWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueueName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PayloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    AvailableAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobWorkItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobWorkItems_QueueName_CompletedAtUtc_AvailableAtUtc",
                table: "BackgroundJobWorkItems",
                columns: new[] { "QueueName", "CompletedAtUtc", "AvailableAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobWorkItems_QueueName_PayloadId",
                table: "BackgroundJobWorkItems",
                columns: new[] { "QueueName", "PayloadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundJobWorkItems");

            migrationBuilder.DropColumn(
                name: "AssignedVariantKey",
                table: "ProspectSequenceEnrollments");

            migrationBuilder.DropColumn(
                name: "TrafficPercent",
                table: "OutreachSequenceSteps");

            migrationBuilder.DropColumn(
                name: "VariantKey",
                table: "OutreachSequenceSteps");

            migrationBuilder.DropColumn(
                name: "WhatsAppButtonIndex",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppButtonParameter",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppButtonSubType",
                table: "EmailTemplates");

            migrationBuilder.DropColumn(
                name: "WhatsAppHeaderParameter",
                table: "EmailTemplates");
        }
    }
}
