using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Domain.Entities;

namespace Sendly.Application.Services;

public static class DiscoveryBudgetResolver
{
    public static DiscoveryBudget Resolve(ResearchOptions options, ResearchQueryVersion? version)
    {
        var maxUrls = version?.MaxUrlsPerRun > 0
            ? version.MaxUrlsPerRun
            : options.MaxCandidatesPerRun;
        maxUrls = Math.Clamp(maxUrls, 1, Math.Max(1, options.MaxCandidatesPerRun));

        var maxPages = version?.MaxPagesPerDomain > 0
            ? version.MaxPagesPerDomain
            : options.MaxPagesPerCrawl;
        maxPages = Math.Clamp(maxPages, 1, 50);

        var maxDepth = version?.MaxCrawlDepth >= 0
            ? version.MaxCrawlDepth
            : 4;
        maxDepth = Math.Clamp(maxDepth, 0, 8);

        var concurrent = version?.MaxConcurrentRequests > 0
            ? version.MaxConcurrentRequests
            : options.MaxConcurrentRequests;
        concurrent = Math.Clamp(concurrent, 1, 8);

        var maxFetches = Math.Clamp(options.MaxFetchesPerRun, 10, 500);
        var maxDiscovery = Math.Clamp(
            options.MaxDiscoveryCandidates > 0 ? options.MaxDiscoveryCandidates : maxUrls,
            10,
            Math.Max(maxUrls * 2, 50));

        return new DiscoveryBudget
        {
            MaxCandidateDomains = Math.Clamp(options.MaxCandidateDomains, 10, 500),
            MaxUrlsPerRun = maxUrls,
            MaxDiscoveryCandidates = maxDiscovery,
            MaxFetchesPerRun = maxFetches,
            MaxPagesPerDomain = maxPages,
            MaxCrawlDepth = maxDepth,
            MaxConcurrentRequests = concurrent,
            MaxSourceExpansionDepth = Math.Clamp(options.MaxSourceExpansionDepth, 0, 2),
            MaxExternalExpansionsPerPage = Math.Clamp(options.MaxExternalExpansionsPerPage, 0, 25),
            MaxLinksPerPage = Math.Clamp(options.MaxLinksPerPage, 0, 50),
            MaxLinksPerDomain = Math.Clamp(options.MaxLinksPerDomain, 1, 200),
            SearchFetchBudgetReservePercent = Math.Clamp(options.SearchFetchBudgetReservePercent, 0, 80),
            MaxPriorResearchSharePercent = Math.Clamp(options.MaxPriorResearchSharePercent, 0, 100),
            MaxArchiveRecords = Math.Clamp(options.MaxArchiveRecords, 0, 200),
            MaxDiscoveryStages = Math.Clamp(options.MaxDiscoveryStages, 1, 5),
            RequestTimeoutSeconds = Math.Clamp(
                version?.RequestTimeoutSeconds > 0
                    ? version.RequestTimeoutSeconds
                    : options.ProviderTimeoutSeconds,
                5,
                120),
            MaxResearchDurationMinutes = Math.Clamp(options.MaxResearchDurationMinutes, 1, 180)
        };
    }
}
