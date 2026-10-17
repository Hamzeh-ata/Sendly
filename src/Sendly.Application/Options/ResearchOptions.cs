namespace Sendly.Application.Options;

public class ResearchOptions
{
    public const string SectionName = "Research";

    public int MaxCandidatesPerRun { get; set; } = 200;

    public int DefaultMinQualificationScore { get; set; } = 70;

    public int DefaultRequestedProspectCount { get; set; } = 50;

    public int ProviderTimeoutSeconds { get; set; } = 30;

    public bool EnableSeedProvider { get; set; }

    public bool EnableWebsiteCrawler { get; set; }

    public bool EnableSearchProvider { get; set; } = true;

    public bool EnableHtmlSearchFallback { get; set; }

    public int MaxPagesPerCrawl { get; set; } = 5;

    public int MaxSearchResults { get; set; } = 20;

    public int MaxSearchQueries { get; set; } = 50;

    public int MaxSearchRequests { get; set; } = 100;

    public int MaxResearchDurationMinutes { get; set; } = 30;

    public int DiscoveryOversampleFactor { get; set; } = 4;

    public int PagesPerSearchOffset { get; set; } = 1;

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
