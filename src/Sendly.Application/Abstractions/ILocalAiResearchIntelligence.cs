namespace Sendly.Application.Abstractions;

public static class ResearchActionTypes
{
    public const string ExactIndustryLocationSearch = "exact_industry_location_search";
    public const string AlternativeTerminologySearch = "alternative_terminology_search";
    public const string LocalLanguageSearch = "local_language_search";
    public const string DirectoryListingSearch = "directory_listing_search";
    public const string AssociationListingSearch = "association_listing_search";
    public const string OfficialRegistrySearch = "official_registry_search";
    public const string BusinessNameWebsiteSearch = "business_name_website_search";
    public const string MissingContactSearch = "missing_contact_search";
    public const string CoverageGapSearch = "coverage_gap_search";
    public const string InvestigateDiscoveredDomain = "investigate_discovered_domain";

    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ExactIndustryLocationSearch,
        AlternativeTerminologySearch,
        LocalLanguageSearch,
        DirectoryListingSearch,
        AssociationListingSearch,
        OfficialRegistrySearch,
        BusinessNameWebsiteSearch,
        MissingContactSearch,
        CoverageGapSearch,
        InvestigateDiscoveredDomain
    };
}

public class ResearchSourceCatalogEntry
{
    public string Name { get; set; } = string.Empty;

    public string SourceKind { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public bool SupportsIndustryFilter { get; set; }

    public bool SupportsCountryFilter { get; set; }

    public bool RequiresPaidApi { get; set; }

    public bool IsTemporarilyDisabled { get; set; }

    public string Health { get; set; } = "Unknown";

    public int UsefulResultYield { get; set; }

    public string AccessNotes { get; set; } = string.Empty;
}

public class ResearchActionProposal
{
    public string ActionType { get; set; } = string.Empty;

    public string? Query { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string? TargetIndustry { get; set; }

    public string? Geography { get; set; }

    public string? ExpectedResult { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string? PreferredSource { get; set; }

    public string? TargetDomainOrUrl { get; set; }

    public int Priority { get; set; } = 50;

    public List<string> AddressesRequirements { get; set; } = [];

    public bool IsAccepted { get; set; }

    public string? RejectionReason { get; set; }
}

public class ResearchPlanRequest
{
    public string ProjectName { get; set; } = string.Empty;

    public string? ProjectDescription { get; set; }

    public string? ResearchInstructions { get; set; }

    public ResearchSpecification Specification { get; set; } = new();

    public List<string> ExistingQueries { get; set; } = [];

    public List<string> AbandonedPatterns { get; set; } = [];

    public List<ResearchSourceCatalogEntry> AvailableSources { get; set; } = [];

    public string? OutcomeSummary { get; set; }

    public string? BudgetSummary { get; set; }

    public string? CoverageSummary { get; set; }

    public string? RejectionSummary { get; set; }

    public List<string> UnderCoveredIndustries { get; set; } = [];

    public List<string> UnderCoveredLocations { get; set; } = [];

    public int MaxActions { get; set; } = 8;

    public bool IsFollowUp { get; set; }
}

public class ResearchPlanResult
{
    public List<ResearchActionProposal> Actions { get; set; } = [];

    public List<string> AbandonedPatterns { get; set; } = [];

    public string StrategySummary { get; set; } = string.Empty;

    public bool IsValid { get; set; }

    public string? ErrorSummary { get; set; }
}

public class AiBatchSearchResultItem
{
    public int Index { get; set; }

    public string? Title { get; set; }

    public string? Snippet { get; set; }

    public string? Url { get; set; }

    public string? Query { get; set; }
}

public class AiBatchSearchClassificationRequest
{
    public List<AiBatchSearchResultItem> Results { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> Locations { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];
}

public class AiBatchSearchClassificationItem
{
    public int Index { get; set; }

    public string Decision { get; set; } = "Ambiguous";

    public string? BusinessType { get; set; }

    public string? CountryEvidence { get; set; }

    public string? IndustryEvidence { get; set; }

    public int Confidence { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<string> EvidenceQuotes { get; set; } = [];

    public bool NeedsVerification { get; set; } = true;

    public int CrawlPriority { get; set; } = 3;

    public bool IsValid { get; set; }
}

public class AiBatchSearchClassificationResult
{
    public List<AiBatchSearchClassificationItem> Items { get; set; } = [];

    public bool IsValid { get; set; }
}

public class AiEvidenceExtractionRequest
{
    public string? CompanyName { get; set; }

    public string? Url { get; set; }

    public string? PageTitle { get; set; }

    public string? PageText { get; set; }

    public List<string> AllowedEmails { get; set; } = [];

    public List<string> AllowedPhones { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];
}

public class AiEvidenceExtractionResult
{
    public string? BusinessName { get; set; }

    public string? OfficialWebsite { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? Subindustry { get; set; }

    public string? Description { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public List<string> EvidenceUrls { get; set; } = [];

    public List<string> EvidenceQuotes { get; set; } = [];

    public List<string> MissingFields { get; set; } = [];

    public List<string> Uncertainties { get; set; } = [];

    public bool IsRealBusiness { get; set; }

    public bool IsOfficialWebsite { get; set; }

    public bool MatchesIndustry { get; set; }

    public bool MatchesCountry { get; set; }

    public int Confidence { get; set; }

    public string Reason { get; set; } = string.Empty;

    public bool IsValid { get; set; }
}

public class ResearchIntelligenceDecisionRecord
{
    public string ActionType { get; set; } = string.Empty;

    public string? Query { get; set; }

    public string Reason { get; set; } = string.Empty;

    public List<string> AddressesRequirements { get; set; } = [];

    public string? EvidenceSummary { get; set; }

    public string? Outcome { get; set; }

    public bool WasUseful { get; set; }

    public string? NextPlanChange { get; set; }

    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public class ResearchQualityMetrics
{
    public double QualifiedBusinessesPer100SearchRequests { get; set; }

    public double VerifiedBusinessesPer100FetchedPages { get; set; }

    public double RelevantFetchedPagePercent { get; set; }

    public double FalsePositiveRate { get; set; }

    public double DuplicateRate { get; set; }

    public double PublicContactDiscoveryRate { get; set; }

    public double IndustryCoveragePercent { get; set; }

    public double CountryCoveragePercent { get; set; }

    public int LowYieldQueriesAvoided { get; set; }

    public int SourceSuccessCount { get; set; }

    public int SourceChallengeCount { get; set; }

    public int AdaptiveActionsAccepted { get; set; }

    public int AdaptiveActionsRejected { get; set; }

    public int OllamaCallsUsed { get; set; }
}

public interface ILocalAiResearchIntelligence
{
    Task<ResearchPlanResult> ProposeResearchPlanAsync(
        ResearchPlanRequest request,
        CancellationToken cancellationToken);

    Task<AiBatchSearchClassificationResult> ClassifySearchResultsBatchAsync(
        AiBatchSearchClassificationRequest request,
        CancellationToken cancellationToken);

    Task<AiEvidenceExtractionResult> ExtractBusinessEvidenceAsync(
        AiEvidenceExtractionRequest request,
        CancellationToken cancellationToken);
}
