using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class StartResearchRequestDto
{
    public int RequestedProspectCount { get; set; } = 50;

    public bool IncludeEmail { get; set; } = true;

    public bool IncludeWhatsApp { get; set; }

    public int? MinQualificationScore { get; set; }
}

public class ResearchRunSummaryDto
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string? ProjectName { get; set; }

    public ResearchRunStatus Status { get; set; }

    public int RequestedProspectCount { get; set; }

    public bool IncludeEmail { get; set; }

    public bool IncludeWhatsApp { get; set; }

    public int MinQualificationScore { get; set; }

    public int ProgressPercent { get; set; }

    public int CompaniesFound { get; set; }

    public int ContactsFound { get; set; }

    public int ValidEmails { get; set; }

    public int ValidWhatsApp { get; set; }

    public int DuplicatesFound { get; set; }

    public int RejectedCount { get; set; }

    public int QualifiedCount { get; set; }

    public int SearchQueriesExecuted { get; set; }

    public int SearchResultsReceived { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int CandidatesAfterDeduplication { get; set; }

    public int CandidatesValidated { get; set; }

    public int ProspectsSaved { get; set; }

    public int ProspectsSkippedAsDuplicates { get; set; }

    public bool ResearchLimitsReached { get; set; }

    public string? ResearchLimitsReachedReason { get; set; }

    public Dictionary<string, int> RejectionReasons { get; set; } = new();

    public ResearchSearchProviderDiagnosticsDto? SearchProviderDiagnostics { get; set; }

    public string? Error { get; set; }

    public string? Warning { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public class ResearchSearchProviderDiagnosticsDto
{
    public string Provider { get; set; } = string.Empty;

    public int QueriesExecuted { get; set; }

    public int RequestsSuccessful { get; set; }

    public int RequestsFailed { get; set; }

    public int ResultsReceived { get; set; }

    public int Http401 { get; set; }

    public int Http429 { get; set; }

    public int Http5xx { get; set; }

    public int Timeouts { get; set; }
}

public class ResearchRunDetailDto : ResearchRunSummaryDto
{
    public List<ResearchRunProspectItemDto> Prospects { get; set; } = [];
}

public class ResearchRunProspectItemDto
{
    public Guid Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? City { get; set; }

    public string? Country { get; set; }

    public string? Email { get; set; }

    public string? WhatsApp { get; set; }

    public string? SourceUrl { get; set; }

    public int? QualificationScore { get; set; }

    public ProspectStatus Status { get; set; }

    public bool IsQualified { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
