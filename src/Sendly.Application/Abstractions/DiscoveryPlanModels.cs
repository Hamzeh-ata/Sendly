using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class DiscoveryPlan
{
    public List<string> TargetIndustries { get; set; } = [];

    public List<string> IndustrySynonyms { get; set; } = [];

    public List<string> TargetCountries { get; set; } = [];

    public List<string> CompanyCharacteristics { get; set; } = [];

    public List<string> MandatoryCriteria { get; set; } = [];

    public List<string> ProviderStages { get; set; } = [];

    public List<string> EligibleProviderNames { get; set; } = [];

    public int MaxCandidateDomains { get; set; }

    public int MaxUrls { get; set; }

    public int MaxPagesPerDomain { get; set; }

    public int MaxCrawlDepth { get; set; }

    public int MaxSourceExpansionDepth { get; set; }

    public int MaxArchiveRecords { get; set; }

    public int MaxDiscoveryStages { get; set; }

    public int MinCandidatesBeforeRecovery { get; set; }

    public string? Summary { get; set; }
}

public class DiscoveryStageResult
{
    public int StageNumber { get; set; }

    public string StageName { get; set; } = string.Empty;

    public List<string> ProvidersAttempted { get; set; } = [];

    public int CandidatesAdded { get; set; }

    public bool RecoveryTriggered { get; set; }

    public string? Notes { get; set; }
}

public class DiscoveryBudget
{
    public int MaxCandidateDomains { get; set; } = 100;

    public int MaxUrlsPerRun { get; set; } = 200;

    public int MaxDiscoveryCandidates { get; set; } = 200;

    public int MaxFetchesPerRun { get; set; } = 80;

    public int MaxPagesPerDomain { get; set; } = 5;

    public int MaxCrawlDepth { get; set; } = 4;

    public int MaxConcurrentRequests { get; set; } = 2;

    public int MaxSourceExpansionDepth { get; set; } = 1;

    public int MaxExternalExpansionsPerPage { get; set; } = 8;

    public int MaxLinksPerPage { get; set; } = 15;

    public int MaxLinksPerDomain { get; set; } = 20;

    public int SearchFetchBudgetReservePercent { get; set; } = 25;

    public int MaxPriorResearchSharePercent { get; set; } = 40;

    public int MaxArchiveRecords { get; set; } = 50;

    public int MaxDiscoveryStages { get; set; } = 3;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int MaxResearchDurationMinutes { get; set; } = 30;
}
