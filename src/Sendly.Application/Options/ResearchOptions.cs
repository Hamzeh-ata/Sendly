namespace Sendly.Application.Options;

public class ResearchOptions
{
    public const string SectionName = "Research";

    public int MaxCandidatesPerRun { get; set; } = 200;

    public int MaxDiscoveryCandidates { get; set; } = 200;

    public int MaxFetchesPerRun { get; set; } = 80;

    public int MaxExternalExpansionsPerPage { get; set; } = 8;

    public int MaxLinksPerPage { get; set; } = 15;

    public int MaxLinksPerDomain { get; set; } = 20;

    public int SearchFetchBudgetReservePercent { get; set; } = 25;

    public int MaxSearchIterations { get; set; } = 10;

    public int MaxPriorResearchSharePercent { get; set; } = 40;

    public int DefaultMinQualificationScore { get; set; } = 70;

    public int DefaultRequestedProspectCount { get; set; } = 50;

    public int ProviderTimeoutSeconds { get; set; } = 30;

    public bool EnableSeedProvider { get; set; }

    public bool EnableWebsiteCrawler { get; set; }

    public bool EnableSearchProvider { get; set; }

    public bool EnableIndependentCrawler { get; set; } = true;

    public bool EnableCommonCrawlDiscovery { get; set; } = true;

    public bool EnableOpenStreetMapDiscovery { get; set; } = true;

    public bool EnableHtmlSearchFallback { get; set; }

    public bool EnableDiscoverySourceExpansion { get; set; } = true;

    public string CrawlerUserAgent { get; set; } = "SendlyResearchBot/1.0 (+https://localhost; research@sendly.local)";

    public int MaxResponseBytes { get; set; } = 2_000_000;

    public int MaxRedirects { get; set; } = 5;

    public int MaxPagesPerCrawl { get; set; } = 12;

    public int MaxCandidateDomains { get; set; } = 100;

    public int MaxSourceExpansionDepth { get; set; } = 1;

    public int MaxArchiveRecords { get; set; } = 50;

    public int MaxDiscoveryStages { get; set; } = 3;

    public int MaxConcurrentRequests { get; set; } = 2;

    public int MaxDiscoverySourceRetries { get; set; } = 3;

    public int MaxSearchResults { get; set; } = 20;

    public int MaxSearchQueries { get; set; } = 50;

    public int MaxSearchRequests { get; set; } = 100;

    public int MaxResearchDurationMinutes { get; set; } = 30;

    public int EvidenceReuseMaxAgeDays { get; set; } = 30;

    public int MaxHostFetchFailuresBeforeAvoid { get; set; } = 2;

    public int DiscoveryOversampleFactor { get; set; } = 4;

    public int PagesPerSearchOffset { get; set; } = 1;

    public int MaxVerificationFetchesPerRun { get; set; } = 60;

    public int MaxContactFetchesPerRun { get; set; } = 40;

    public int MaxLinkExpansionFetchesPerRun { get; set; } = 10;

    public int MaxOllamaCallsPerRun { get; set; } = 40;

    public int MinSearchRelevanceScore { get; set; }

    public string? BingSearchApiKey { get; set; }

    public string? BingSearchEndpoint { get; set; } = "https://api.bing.microsoft.com/v7.0/search";

    public string? GoogleSearchApiKey { get; set; }

    public string? GoogleSearchEngineId { get; set; }

    public List<string> HostDenylist { get; set; } =
    [
        "facebook.com",
        "linkedin.com",
        "twitter.com",
        "x.com",
        "instagram.com",
        "youtube.com",
        "wikipedia.org",
        "reddit.com"
    ];

    public List<ResearchSeedCandidateOptions> SeedCandidates { get; set; } = [];

    public List<ResearchCrawlTargetOptions> CrawlTargets { get; set; } = [];
}

public class ResearchSeedCandidateOptions
{
    public string CompanyName { get; set; } = string.Empty;

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? City { get; set; }

    public string? Industry { get; set; }

    public string? CompanyDescription { get; set; }

    public string? CompanySize { get; set; }

    public string? ContactName { get; set; }

    public string? ContactRole { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? WhatsApp { get; set; }

    public string? LinkedInUrl { get; set; }

    public string? SourceUrl { get; set; }

    public string? SourceName { get; set; }
}

public class ResearchCrawlTargetOptions
{
    public string Url { get; set; } = string.Empty;

    public string? CompanyName { get; set; }

    public string? Country { get; set; }

    public string? Industry { get; set; }

    public string? SourceName { get; set; }
}
