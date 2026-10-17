using System.Text.Json;
using Sendly.Application.DTOs;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services;

public static class ResearchRunMapper
{
    public static ResearchRunSummaryDto MapSummary(ResearchRun run, string? projectName)
    {
        return new ResearchRunSummaryDto
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            ProjectName = projectName,
            Status = run.Status,
            RequestedProspectCount = run.RequestedProspectCount,
            IncludeEmail = run.IncludeEmail,
            IncludeWhatsApp = run.IncludeWhatsApp,
            MinQualificationScore = run.MinQualificationScore,
            ProgressPercent = run.ProgressPercent,
            CompaniesFound = run.CompaniesFound,
            ContactsFound = run.ContactsFound,
            ValidEmails = run.ValidEmails,
            ValidWhatsApp = run.ValidWhatsApp,
            DuplicatesFound = run.DuplicatesFound,
            RejectedCount = run.RejectedCount,
            QualifiedCount = run.QualifiedCount,
            SearchQueriesExecuted = run.SearchQueriesExecuted,
            SearchResultsReceived = run.SearchResultsReceived,
            CandidatesDiscovered = run.CandidatesDiscovered,
            CandidatesAfterDeduplication = run.CandidatesAfterDeduplication,
            CandidatesValidated = run.CandidatesValidated,
            ProspectsSaved = run.ProspectsSaved,
            ProspectsSkippedAsDuplicates = run.ProspectsSkippedAsDuplicates,
            ResearchLimitsReached = run.ResearchLimitsReached,
            ResearchLimitsReachedReason = run.ResearchLimitsReachedReason,
            RejectionReasons = DeserializeRejectionReasons(run.RejectionReasonsJson),
            SearchProviderDiagnostics = DeserializeDiagnostics(run.SearchProviderDiagnosticsJson),
            Error = run.Error,
            Warning = run.Warning,
            CreatedAtUtc = run.CreatedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc
        };
    }

    public static ResearchRunDetailDto MapDetail(
        ResearchRun run,
        string? projectName,
        IEnumerable<Prospect>? prospects = null)
    {
        var summary = MapSummary(run, projectName);
        return new ResearchRunDetailDto
        {
            Id = summary.Id,
            ProjectId = summary.ProjectId,
            ProjectName = summary.ProjectName,
            Status = summary.Status,
            RequestedProspectCount = summary.RequestedProspectCount,
            IncludeEmail = summary.IncludeEmail,
            IncludeWhatsApp = summary.IncludeWhatsApp,
            MinQualificationScore = summary.MinQualificationScore,
            ProgressPercent = summary.ProgressPercent,
            CompaniesFound = summary.CompaniesFound,
            ContactsFound = summary.ContactsFound,
            ValidEmails = summary.ValidEmails,
            ValidWhatsApp = summary.ValidWhatsApp,
            DuplicatesFound = summary.DuplicatesFound,
            RejectedCount = summary.RejectedCount,
            QualifiedCount = summary.QualifiedCount,
            SearchQueriesExecuted = summary.SearchQueriesExecuted,
            SearchResultsReceived = summary.SearchResultsReceived,
            CandidatesDiscovered = summary.CandidatesDiscovered,
            CandidatesAfterDeduplication = summary.CandidatesAfterDeduplication,
            CandidatesValidated = summary.CandidatesValidated,
            ProspectsSaved = summary.ProspectsSaved,
            ProspectsSkippedAsDuplicates = summary.ProspectsSkippedAsDuplicates,
            ResearchLimitsReached = summary.ResearchLimitsReached,
            ResearchLimitsReachedReason = summary.ResearchLimitsReachedReason,
            RejectionReasons = summary.RejectionReasons,
            SearchProviderDiagnostics = summary.SearchProviderDiagnostics,
            Error = summary.Error,
            Warning = summary.Warning,
            CreatedAtUtc = summary.CreatedAtUtc,
            StartedAtUtc = summary.StartedAtUtc,
            CompletedAtUtc = summary.CompletedAtUtc,
            Prospects = MapProspectItems(prospects)
        };
    }

    public static List<ResearchRunProspectItemDto> MapProspectItems(IEnumerable<Prospect>? prospects)
    {
        if (prospects is null)
        {
            return [];
        }

        return prospects
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new ResearchRunProspectItemDto
            {
                Id = x.Id,
                CompanyName = x.CompanyName,
                Website = x.Website,
                City = x.City,
                Country = x.Country,
                Email = x.Email,
                WhatsApp = x.WhatsApp,
                SourceUrl = x.SourceUrl,
                QualificationScore = x.QualificationScore,
                Status = x.Status,
                IsQualified = x.IsQualified,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToList();
    }

    private static Dictionary<string, int> DeserializeRejectionReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, int>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                   ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }

    private static ResearchSearchProviderDiagnosticsDto? DeserializeDiagnostics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ResearchSearchProviderDiagnosticsDto
            {
                Provider = root.TryGetProperty("provider", out var provider)
                    ? provider.GetString() ?? string.Empty
                    : string.Empty,
                QueriesExecuted = ReadInt(root, "queriesExecuted"),
                RequestsSuccessful = ReadInt(root, "requestsSuccessful"),
                RequestsFailed = ReadInt(root, "requestsFailed"),
                ResultsReceived = ReadInt(root, "resultsReceived"),
                Http401 = ReadInt(root, "http401"),
                Http429 = ReadInt(root, "http429"),
                Http5xx = ReadInt(root, "http5xx"),
                Timeouts = ReadInt(root, "timeouts")
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return 0;
        }

        return prop.ValueKind == JsonValueKind.Number ? prop.GetInt32() : 0;
    }
}
