using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sendly.Infrastructure.Persistence;

#nullable disable

namespace Sendly.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(SendlyDbContext))]
    [Migration("20261007154800_Sendly15")]
    public class Sendly15 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CandidatesAfterDeduplication",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CandidatesDiscovered",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CandidatesValidated",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProspectsSaved",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProspectsSkippedAsDuplicates",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ResearchLimitsReached",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResearchLimitsReachedReason",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReasonsJson",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchQueriesExecuted",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SearchProviderDiagnosticsJson",
                table: "ResearchRuns",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchResultsReceived",
                table: "ResearchRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CandidatesAfterDeduplication",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CandidatesDiscovered",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "CandidatesValidated",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ProspectsSaved",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ProspectsSkippedAsDuplicates",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchLimitsReached",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "ResearchLimitsReachedReason",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "RejectionReasonsJson",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "SearchQueriesExecuted",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "SearchProviderDiagnosticsJson",
                table: "ResearchRuns");

            migrationBuilder.DropColumn(
                name: "SearchResultsReceived",
                table: "ResearchRuns");
        }
    }
}
