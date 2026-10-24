using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sendly19 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateHits",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCheckedAtUtc",
                table: "DiscoverySources",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LiveVerifiedCompanies",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PagesAccessible",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PagesParsed",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PublicContactsExtracted",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RelevantCompaniesDiscovered",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "TotalProcessingMs",
                table: "DiscoverySources",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DiscoverySourceId",
                table: "DiscoveredUrls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LiveVerificationStatus",
                table: "DiscoveredUrls",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LiveVerifiedAtUtc",
                table: "DiscoveredUrls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscoveredUrls_DiscoverySourceId",
                table: "DiscoveredUrls",
                column: "DiscoverySourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiscoveredUrls_DiscoverySources_DiscoverySourceId",
                table: "DiscoveredUrls",
                column: "DiscoverySourceId",
                principalTable: "DiscoverySources",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiscoveredUrls_DiscoverySources_DiscoverySourceId",
                table: "DiscoveredUrls");

            migrationBuilder.DropIndex(
                name: "IX_DiscoveredUrls_DiscoverySourceId",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "DuplicateHits",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "LastCheckedAtUtc",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "LiveVerifiedCompanies",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "PagesAccessible",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "PagesParsed",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "PublicContactsExtracted",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "RelevantCompaniesDiscovered",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "TotalProcessingMs",
                table: "DiscoverySources");

            migrationBuilder.DropColumn(
                name: "DiscoverySourceId",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "LiveVerificationStatus",
                table: "DiscoveredUrls");

            migrationBuilder.DropColumn(
                name: "LiveVerifiedAtUtc",
                table: "DiscoveredUrls");
        }
    }
}
