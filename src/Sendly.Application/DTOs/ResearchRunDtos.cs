using System.Collections.Generic;
using Sendly.Domain.Enums;

namespace Sendly.Application.DTOs;

public class StartResearchRequestDto
{
    public Guid? ResearchQueryId { get; set; }

    public Guid? ResearchQueryVersionId { get; set; }

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

    public Guid? ResearchQueryId { get; set; }

    public Guid? ResearchQueryVersionId { get; set; }

    public string? ResearchQueryName { get; set; }

    public int UrlsDiscovered { get; set; }

    public int PagesCrawled { get; set; }

    public int CrawlErrors { get; set; }

    public ResearchRunStatus Status { get; set; }

    public ResearchMode Mode { get; set; }

    public int MapRegionsCompleted { get; set; }

    public int MapRegionsTotal { get; set; }

    public int MapPlacesDiscovered { get; set; }

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

    public string? AutomaticDiscoverySummary { get; set; }

    public int AutomaticDiscoveryDuplicatesRemoved { get; set; }

    public int AutomaticDiscoveryRejectedUnsafe { get; set; }

    public string? DiscoveryPlanSummary { get; set; }

    public int UniqueDomainsDiscovered { get; set; }

    public int RecoveryAttempts { get; set; }

    public string? DiscoveryStopReason { get; set; }

    public List<string> RecoveryStrategies { get; set; } = [];

    public int SourcesReused { get; set; }

    public int SourcesAdded { get; set; }

    public int SourcesSkipped { get; set; }

    public int SourcesUnavailable { get; set; }

    public int UrlsAccessible { get; set; }

    public int UrlsParsed { get; set; }

    public int UrlsUnavailable { get; set; }

    public int UrlsRobotsBlocked { get; set; }

    public int CompaniesIdentified { get; set; }

    public int PublicContactsExtracted { get; set; }

    public int LiveVerifiedCompanies { get; set; }

    public int ProspectsQualified { get; set; }

    public int UrlsRejectedFiltered { get; set; }

    public int UrlsAdmitted { get; set; }

    public int UrlsReusedFromCache { get; set; }

    public int UrlsSkippedKnownProspect { get; set; }

    public int UsefulPendingUrls { get; set; }

    public int UsefulUrlsLeftPendingAtStop { get; set; }

    public int NewlyDiscoveredProspects { get; set; }

    public int PreviouslyKnownProspects { get; set; }

    public int PreviouslyContactedProspects { get; set; }

    public int NewlyOutreachEligibleProspects { get; set; }

    public int IdentityVerifiedBusinesses { get; set; }

    public int EmailReadyProspects { get; set; }

    public int ContactableProspects { get; set; }

    public int PagesFetched { get; set; }

    public int PagesQueued { get; set; }

    public int PagesSkipped { get; set; }

    public int FetchBudgetConsumed { get; set; }

    public int FetchBudgetLimit { get; set; }

    public int RequestedFetchBudget { get; set; }

    public int ConfiguredBatchSize { get; set; }

    public int BatchesStarted { get; set; }

    public int BatchesCompleted { get; set; }

    public int FetchesAttempted { get; set; }

    public int FetchesSucceeded { get; set; }

    public int FetchesFailed { get; set; }

    public int FetchesSkipped { get; set; }

    public int BudgetReserved { get; set; }

    public int BudgetRemaining { get; set; }

    public int EligiblePendingUrls { get; set; }

    public int DeferredUrls { get; set; }

    public bool MoreEligibleWorkRemained { get; set; }

    public string? LastBudgetStopReason { get; set; }

    public bool SearchLimitsExhausted { get; set; }

    public string? SearchLimitsExhaustedReason { get; set; }

    public bool LimitsExhausted { get; set; }

    public string? LimitsExhaustedReason { get; set; }

    public Dictionary<string, long> StageDurationsMs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> UrlRejectionReasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> FetchFailureReasons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<DiscoveryStageDto> DiscoveryStages { get; set; } = [];

    public List<DiscoveryProviderStatDto> DiscoveryProviders { get; set; } = [];

    public List<PendingUrlDiagnosticDto> TopPendingUrls { get; set; } = [];

    public List<PendingUrlDiagnosticDto> DeferredHighScoreUrls { get; set; } = [];

    public string? CurrentInvestigationActivity { get; set; }

    public List<string> UnderCoveredIndustries { get; set; } = [];

    public string? NextBestInvestigationStrategy { get; set; }

    public bool OllamaChecked { get; set; }

    public bool OllamaReachable { get; set; }

    public bool OllamaModelAvailable { get; set; }

    public string? OllamaModel { get; set; }

    public int OllamaQueriesGenerated { get; set; }

    public string? SearchProviderHealth { get; set; }

    public string? SearchProviderHealthSummary { get; set; }

    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }
}

public class PendingUrlDiagnosticDto
{
    public string Url { get; set; } = string.Empty;

    public int Priority { get; set; }

    public string PagePurpose { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public class DiscoveryStageDto
{
    public int StageNumber { get; set; }

    public string StageName { get; set; } = string.Empty;

    public List<string> ProvidersAttempted { get; set; } = [];

    public int CandidatesAdded { get; set; }

    public bool RecoveryTriggered { get; set; }

    public string? Notes { get; set; }
}

public class DiscoveryProviderStatDto
{
    public string ProviderName { get; set; } = string.Empty;

    public bool Attempted { get; set; }

    public bool Succeeded { get; set; }

    public bool ProducedUsefulCandidates { get; set; }

    public int CandidatesReturned { get; set; }

    public int StageNumber { get; set; }

    public string? Error { get; set; }

    public string? ZeroYieldReason { get; set; }
}

public class DiscoverySourceSummaryDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? NormalizedDomain { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? CountriesJson { get; set; }

    public string? IndustriesJson { get; set; }

    public double RelevanceScore { get; set; }

    public double FreshnessScore { get; set; }

    public int AttemptCount { get; set; }

    public int SuccessCount { get; set; }

    public int FailureCount { get; set; }

    public int CandidatesDiscovered { get; set; }

    public int UniqueCompaniesDiscovered { get; set; }

    public int RelevantCompaniesDiscovered { get; set; }

    public int QualifiedProspectsYielded { get; set; }

    public int PagesAccessible { get; set; }

    public int PagesParsed { get; set; }

    public int LiveVerifiedCompanies { get; set; }

    public int PublicContactsExtracted { get; set; }

    public int DuplicateHits { get; set; }

    public long TotalProcessingMs { get; set; }

    public double? AverageFetchMs { get; set; }

    public double? FailureRate { get; set; }

    public double? DuplicateRate { get; set; }

    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    public DateTimeOffset? LastSucceededAtUtc { get; set; }

    public DateTimeOffset? LastCheckedAtUtc { get; set; }

    public string? LastError { get; set; }

    public string? ProvenanceNotes { get; set; }
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

public class ProspectJunkPreviewDto
{
    public int Count { get; set; }

    public string Criteria { get; set; } = string.Empty;

    public bool Reversible { get; set; } = true;

    public List<ProspectJunkItemDto> Items { get; set; } = [];
}

public class ProspectJunkItemDto
{
    public Guid Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string? Website { get; set; }

    public Guid? ResearchRunId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

public class ProspectJunkQuarantineRequestDto
{
    public List<Guid> ProspectIds { get; set; } = [];
}
