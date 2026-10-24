namespace Sendly.Application.DTOs;

public class UpsertResearchQueryDto
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? PlainLanguageBrief { get; set; }

    public string? TargetIndustry { get; set; }

    public List<string> BusinessCategories { get; set; } = [];

    public string? TargetCountry { get; set; }

    public List<string> Regions { get; set; } = [];

    public List<string> Cities { get; set; } = [];

    public List<string> CompanyTypes { get; set; } = [];

    public List<string> CompanySizes { get; set; } = [];

    public List<string> RequiredCriteria { get; set; } = [];

    public List<string> ExclusionCriteria { get; set; } = [];

    public List<string> RequiredContactInfo { get; set; } = [];

    public List<string> RequiredEvidence { get; set; } = [];

    public List<string> DiscoveryStrategies { get; set; } = [];

    public int MaxPagesPerDomain { get; set; } = 5;

    public int MaxCrawlDepth { get; set; } = 4;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaxConcurrentRequests { get; set; } = 2;

    public int RequestsPerDomainPerMinute { get; set; } = 10;

    public int MaxUrlsPerRun { get; set; } = 200;

    public bool RequireOfficialWebsite { get; set; } = true;

    public bool RequirePublicBusinessEmail { get; set; } = true;

    public bool EnableDirectoryDiscovery { get; set; } = true;

    public bool EnableSitemapDiscovery { get; set; } = true;

    public bool EnableLinkExpansion { get; set; } = true;

    public bool EnablePriorResearchUrls { get; set; } = true;

    public List<UpsertResearchSeedUrlDto> SeedUrls { get; set; } = [];
}

public class UpsertResearchSeedUrlDto
{
    public string Url { get; set; } = string.Empty;

    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;
}

public class ResearchSeedUrlDto
{
    public Guid Id { get; set; }

    public string Url { get; set; } = string.Empty;

    public string NormalizedUrl { get; set; } = string.Empty;

    public string? NormalizedDomain { get; set; }

    public string? Label { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ResearchQueryVersionDto
{
    public Guid Id { get; set; }

    public int VersionNumber { get; set; }

    public string? PlainLanguageBrief { get; set; }

    public string? TargetIndustry { get; set; }

    public List<string> BusinessCategories { get; set; } = [];

    public string? TargetCountry { get; set; }

    public List<string> Regions { get; set; } = [];

    public List<string> Cities { get; set; } = [];

    public List<string> CompanyTypes { get; set; } = [];

    public List<string> CompanySizes { get; set; } = [];

    public List<string> RequiredCriteria { get; set; } = [];

    public List<string> ExclusionCriteria { get; set; } = [];

    public List<string> RequiredContactInfo { get; set; } = [];

    public List<string> RequiredEvidence { get; set; } = [];

    public List<string> DiscoveryStrategies { get; set; } = [];

    public int MaxPagesPerDomain { get; set; }

    public int MaxCrawlDepth { get; set; }

    public int RequestTimeoutSeconds { get; set; }

    public int MaxConcurrentRequests { get; set; }

    public int RequestsPerDomainPerMinute { get; set; }

    public int MaxUrlsPerRun { get; set; }

    public bool RequireOfficialWebsite { get; set; }

    public bool RequirePublicBusinessEmail { get; set; }

    public bool EnableDirectoryDiscovery { get; set; }

    public bool EnableSitemapDiscovery { get; set; }

    public bool EnableLinkExpansion { get; set; }

    public bool EnablePriorResearchUrls { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ResearchQuerySummaryDto
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? ActiveVersionId { get; set; }

    public int VersionCount { get; set; }

    public int ActiveSeedUrlCount { get; set; }

    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public class ResearchQueryDetailDto : ResearchQuerySummaryDto
{
    public ResearchQueryVersionDto? ActiveVersion { get; set; }

    public List<ResearchQueryVersionDto> Versions { get; set; } = [];

    public List<ResearchSeedUrlDto> SeedUrls { get; set; } = [];
}

public class DuplicateResearchQueryDto
{
    public string Name { get; set; } = string.Empty;
}

public class ResearchEvidenceDto
{
    public Guid Id { get; set; }

    public string FactType { get; set; } = string.Empty;

    public string? FactValue { get; set; }

    public string ConfidenceKind { get; set; } = string.Empty;

    public string? SourceUrl { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset FirstDiscoveredAtUtc { get; set; }

    public DateTimeOffset LastVerifiedAtUtc { get; set; }
}

public class QualificationCriterionResultDto
{
    public Guid Id { get; set; }

    public string CriterionKey { get; set; } = string.Empty;

    public string? CriterionLabel { get; set; }

    public bool IsMandatory { get; set; }

    public bool Passed { get; set; }

    public int ScoreContribution { get; set; }

    public string? Reason { get; set; }

    public List<Guid> EvidenceIds { get; set; } = [];

    public DateTimeOffset EvaluatedAtUtc { get; set; }
}

public class DiscoveredUrlDto
{
    public Guid Id { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? NormalizedDomain { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string? SourceName { get; set; }

    public string? DiscoveryReason { get; set; }

    public Guid? ParentDiscoveredUrlId { get; set; }

    public string? ParentUrl { get; set; }

    public string Status { get; set; } = string.Empty;

    public string LiveVerificationStatus { get; set; } = string.Empty;

    public Guid? DiscoverySourceId { get; set; }

    public int Depth { get; set; }

    public int RetryCount { get; set; }

    public string? RejectionReasonCode { get; set; }

    public string? PagePurpose { get; set; }

    public int PriorityScore { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset DiscoveredAtUtc { get; set; }

    public DateTimeOffset? ProcessedAtUtc { get; set; }

    public bool ProvenanceComplete { get; set; }
}

public class CrawledPageDto
{
    public Guid Id { get; set; }

    public Guid ResearchRunId { get; set; }

    public Guid? DiscoveredUrlId { get; set; }

    public string Url { get; set; } = string.Empty;

    public string NormalizedUrl { get; set; } = string.Empty;

    public string? NormalizedDomain { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? ContentType { get; set; }

    public long? ContentLengthBytes { get; set; }

    public string? ContentHash { get; set; }

    public string? CanonicalUrl { get; set; }

    public string? FinalUrl { get; set; }

    public string? Title { get; set; }

    public bool RobotsAllowed { get; set; }

    public bool BlockedBySsrf { get; set; }

    public bool WasRedirected { get; set; }

    public int RedirectCount { get; set; }

    public int? DurationMs { get; set; }

    public string? Error { get; set; }

    public int CrawlDepth { get; set; }

    public string? TextExcerpt { get; set; }

    public bool TextExcerptAvailable { get; set; }

    public DateTimeOffset CrawledAtUtc { get; set; }
}

public class ResearchRunEventDto
{
    public Guid Id { get; set; }

    public Guid ResearchRunId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string Severity { get; set; } = string.Empty;

    public string Stage { get; set; } = string.Empty;

    public string? Message { get; set; }

    public string? DataJson { get; set; }

    public int? DurationMs { get; set; }

    public Guid? RelatedEntityId { get; set; }

    public string? RelatedEntityKind { get; set; }

    public string? Domain { get; set; }

    public string? Url { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? Provider { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ResearchOverviewDto
{
    public int ActiveRuns { get; set; }

    public int QueuedRuns { get; set; }

    public int CompletedRuns { get; set; }

    public int FailedRuns { get; set; }

    public int CancelledRuns { get; set; }

    public int PagesFetched { get; set; }

    public int UrlsDiscovered { get; set; }

    public int DomainsDiscovered { get; set; }

    public int CompaniesFound { get; set; }

    public int ContactsFound { get; set; }

    public int QualifiedProspects { get; set; }

    public int CrawlErrors { get; set; }

    public int EventErrors { get; set; }

    public int EventWarnings { get; set; }

    public List<ResearchRunSummaryDto> RecentRuns { get; set; } = [];

    public List<ResearchRunSummaryDto> ActiveRunDetails { get; set; } = [];

    public List<ResearchErrorGroupDto> RecentCriticalErrors { get; set; } = [];

    public List<DiscoverySourceSummaryDto> TopSources { get; set; } = [];

    public List<DiscoverySourceSummaryDto> UnhealthySources { get; set; } = [];
}

public class ResearchErrorGroupDto
{
    public string Category { get; set; } = string.Empty;

    public string SampleMessage { get; set; } = string.Empty;

    public int Count { get; set; }

    public DateTimeOffset FirstOccurredAtUtc { get; set; }

    public DateTimeOffset LastOccurredAtUtc { get; set; }

    public Guid? ResearchRunId { get; set; }

    public string? Domain { get; set; }

    public string? Url { get; set; }

    public string? Stage { get; set; }

    public string? SuggestedRemediation { get; set; }

    public bool RetryPossible { get; set; }
}
