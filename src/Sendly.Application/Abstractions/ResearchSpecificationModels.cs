namespace Sendly.Application.Abstractions;

public class ResearchBudgetSpec
{
    public int MaxUrlsPerRun { get; set; } = 200;

    public int MaxPagesPerDomain { get; set; } = 5;

    public int MaxCrawlDepth { get; set; } = 4;

    public int MaxSearchQueries { get; set; } = 50;

    public int MaxSearchRequests { get; set; } = 100;

    public int MaxFetchesPerRun { get; set; } = 80;

    public int MaxConcurrentRequests { get; set; } = 2;

    public int MaxResearchDurationMinutes { get; set; } = 30;
}

public class ResearchSpecValidationIssue
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string Severity { get; set; } = "warning";
}

public class ResearchSpecification
{
    public string? OriginalBrief { get; set; }

    public string? ProjectGoal { get; set; }

    public string? ValueProposition { get; set; }

    public List<string> ApprovedIndustries { get; set; } = [];

    public List<string> IndustrySynonyms { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> ExcludedEntityTypes { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> TargetCountries { get; set; } = [];

    public List<string> Regions { get; set; } = [];

    public List<string> Cities { get; set; } = [];

    public List<string> GeographicAliases { get; set; } = [];

    public List<string> DesiredOrganizationTypes { get; set; } = [];

    public List<string> BusinessModels { get; set; } = [];

    public List<string> RequiredContactChannels { get; set; } = [];

    public List<string> RequiredCriteria { get; set; } = [];

    public List<string> OptionalCriteria { get; set; } = [];

    public List<string> MandatoryGates { get; set; } = [];

    public List<string> LanguageHints { get; set; } = [];

    public int MinQualificationScore { get; set; } = 70;

    public int TargetProspectCount { get; set; } = 50;

    public bool RequireOfficialWebsite { get; set; } = true;

    public bool RequirePublicBusinessEmail { get; set; } = true;

    public ResearchBudgetSpec Budgets { get; set; } = new();

    public List<ResearchSpecValidationIssue> ValidationIssues { get; set; } = [];

    public List<string> CorrectionReasons { get; set; } = [];

    public bool IsValid { get; set; } = true;
}

public class PlannedSearchQuery
{
    public string Query { get; set; } = string.Empty;

    public string Industry { get; set; } = string.Empty;

    public string? Location { get; set; }

    public string? Country { get; set; }

    public string Intent { get; set; } = SearchQueryIntents.DiscoverBusiness;

    public string Language { get; set; } = "en";

    public Guid? ProjectId { get; set; }

    public Guid? ResearchQueryVersionId { get; set; }

    public List<string> Keywords { get; set; } = [];

    public string Purpose { get; set; } = "discover_target_business";

    public string ExpectedResultType { get; set; } = "operating_business";

    public string? Provider { get; set; }

    public string ExecutionStatus { get; set; } = "planned";
}

public static class SearchQueryIntents
{
    public const string DiscoverBusiness = "discover_business";

    public const string Directory = "directory";

    public const string RegionalListing = "regional_listing";

    public const string Association = "association";

    public const string OfficialWebsite = "official_website";

    public const string ContactPage = "contact_page";

    public const string PublicEmail = "public_email";

    public const string BranchListing = "branch_listing";
}

public class IndustryClassificationResult
{
    public string? AssignedIndustry { get; set; }

    public bool MatchedApprovedIndustry { get; set; }

    public bool IsExcluded { get; set; }

    public string? ExcludedIndustry { get; set; }

    public string ReasonCode { get; set; } = "industry.unknown";

    public string Explanation { get; set; } = "Industry could not be independently determined from evidence.";
}

public class ExclusionClassificationResult
{
    public bool IsExcluded { get; set; }

    public string? MatchedTerm { get; set; }

    public string? EntityType { get; set; }

    public string ReasonCode { get; set; } = "exclusion.none";

    public string Explanation { get; set; } = "No exclusion evidence was found.";
}

public class ProjectUnderstandingRequest
{
    public string? ProjectName { get; set; }

    public string? Description { get; set; }

    public string? ResearchInstructions { get; set; }

    public List<string> ApprovedIndustries { get; set; } = [];

    public List<string> ExcludedIndustries { get; set; } = [];

    public List<string> Keywords { get; set; } = [];

    public List<string> ExcludedKeywords { get; set; } = [];

    public List<string> Countries { get; set; } = [];

    public List<string> TargetRoles { get; set; } = [];

    public string? QueryTargetIndustry { get; set; }

    public List<string> QueryBusinessCategories { get; set; } = [];

    public string? QueryTargetCountry { get; set; }

    public List<string> QueryCities { get; set; } = [];

    public List<string> QueryExclusionCriteria { get; set; } = [];

    public bool QueryRequireOfficialWebsite { get; set; } = true;

    public bool QueryRequirePublicBusinessEmail { get; set; } = true;

    public int MinQualificationScore { get; set; } = 70;

    public int TargetProspectCount { get; set; } = 50;

    public bool IncludeEmail { get; set; } = true;

    public bool IncludeWhatsApp { get; set; }

    public ResearchBudgetSpec? Budgets { get; set; }
}
