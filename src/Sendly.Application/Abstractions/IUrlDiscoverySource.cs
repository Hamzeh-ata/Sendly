using Sendly.Domain.Enums;

namespace Sendly.Application.Abstractions;

public class DiscoveredUrlCandidate
{
    public string Url { get; set; } = string.Empty;

    public DiscoverySourceType SourceType { get; set; }

    public string? SourceName { get; set; }

    public Guid? ParentDiscoveredUrlId { get; set; }

    public Guid? DiscoverySourceId { get; set; }

    public int Depth { get; set; }

    public string? LinkText { get; set; }

    public string? SurroundingContext { get; set; }

    public int ExpansionScore { get; set; }

    public int PriorityBoost { get; set; }

    public string? DiscoveryReasonCode { get; set; }

    public bool IsIndustryRelevant { get; set; }

    public bool IsSecondaryRegistry { get; set; }

    public string? EligibilityReasonCode { get; set; }
}

public class UrlDiscoveryContext
{
    public Guid ProjectId { get; set; }

    public Guid ResearchRunId { get; set; }

    public Guid? ResearchQueryId { get; set; }

    public Guid? ResearchQueryVersionId { get; set; }

    public int MaxUrls { get; set; } = 200;

    public int MaxPriorResearchSharePercent { get; set; } = 40;

    public int MaxCandidateDomains { get; set; } = 100;

    public int MaxArchiveRecords { get; set; } = 50;

    public int MaxDiscoveryStages { get; set; } = 3;

    public int MinCandidatesBeforeRecovery { get; set; } = 10;

    public int DiscoveryStage { get; set; } = 1;

    public bool EnableDirectoryDiscovery { get; set; } = true;

    public bool EnableSitemapDiscovery { get; set; } = true;

    public bool EnableLinkExpansion { get; set; } = true;

    public bool EnablePriorResearchUrls { get; set; } = true;

    public bool EnableWebArchiveDiscovery { get; set; } = true;

    public List<string> Countries { get; set; } = [];

    public List<string> Industries { get; set; } = [];

    public List<string> IndustrySynonyms { get; set; } = [];

    public DiscoveryPlan? Plan { get; set; }

    public bool PreferUnexploredProviders { get; set; }

    public bool DeprioritizeDeadDomains { get; set; }

    public bool EnableContactPageHints { get; set; }

    public bool TightenUrlFiltering { get; set; }

    public bool PreferOfficialCompanyDomains { get; set; }

    public bool PreferFreshSources { get; set; }

    public HashSet<string> ExploredProviderNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DeadDomains { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<SourceEligibilityDecision> SourceEligibilityDecisions { get; set; } = [];

    public SourceEligibilityResolveResult? LastSourceEligibility { get; set; }
}

public interface IUrlDiscoverySource
{
    string Name { get; }

    bool IsEnabled(UrlDiscoveryContext context);

    Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken);
}
