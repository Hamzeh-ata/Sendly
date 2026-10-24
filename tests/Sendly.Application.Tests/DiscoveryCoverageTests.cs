using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;

namespace Sendly.Application.Tests;

public class DiscoveryCoverageTests
{
    [Fact]
    public void IndustrySynonymCatalog_Expand_IncludesArabicCleaningTerms()
    {
        var expanded = IndustrySynonymCatalog.Expand(["Commercial cleaning"]);

        Assert.Contains(expanded, x => x.Contains("نظافة", StringComparison.Ordinal));
        Assert.Contains(expanded, x => x.Contains("cleaning", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiscoveryQueryPlanner_Build_DoesNotDropMandatoryCriteria()
    {
        var version = new ResearchQueryVersion
        {
            TargetIndustry = "Cleaning",
            TargetCountry = "Saudi Arabia",
            RequireOfficialWebsite = true,
            RequirePublicBusinessEmail = true
        };
        var budget = DiscoveryBudgetResolver.Resolve(new ResearchOptions(), version);
        var plan = DiscoveryQueryPlanner.Build(
            version,
            new ProspectSourceContext { IncludeEmail = true },
            budget,
            ["DirectoryCatalog", "PriorResearchUrls"]);

        Assert.Contains("Official website", plan.MandatoryCriteria);
        Assert.Contains("Public business email", plan.MandatoryCriteria);
        Assert.Equal("Saudi Arabia", Assert.Single(plan.TargetCountries));
        Assert.True(plan.IndustrySynonyms.Count > 0);
        Assert.False(string.IsNullOrWhiteSpace(plan.Summary));
    }

    [Fact]
    public void DiscoveryBudgetResolver_ClampsToConfiguredCaps()
    {
        var options = new ResearchOptions
        {
            MaxCandidatesPerRun = 50,
            MaxCandidateDomains = 20,
            MaxDiscoveryStages = 9,
            MaxSourceExpansionDepth = 5,
            MaxArchiveRecords = 500
        };
        var version = new ResearchQueryVersion
        {
            MaxUrlsPerRun = 999,
            MaxPagesPerDomain = 100,
            MaxCrawlDepth = 20
        };

        var budget = DiscoveryBudgetResolver.Resolve(options, version);

        Assert.Equal(50, budget.MaxUrlsPerRun);
        Assert.Equal(50, budget.MaxPagesPerDomain);
        Assert.Equal(8, budget.MaxCrawlDepth);
        Assert.Equal(20, budget.MaxCandidateDomains);
        Assert.Equal(5, budget.MaxDiscoveryStages);
        Assert.Equal(2, budget.MaxSourceExpansionDepth);
        Assert.Equal(200, budget.MaxArchiveRecords);
    }

    [Fact]
    public void DiscoveryOutcomeClassifier_NoCandidates_ReturnsNoCandidatesDiscovered()
    {
        var state = new ResearchDiscoveryState
        {
            AutomaticDiscoveryProviderStats =
            [
                new DiscoveryProviderStat { ProviderName = "DirectoryCatalog", Attempted = true, Succeeded = true }
            ]
        };

        var reason = DiscoveryOutcomeClassifier.Classify(state, qualifiedCount: 0, requestedCount: 10);

        Assert.Equal(DiscoveryStopReason.NoCandidatesDiscovered, reason);
        Assert.Contains("No candidate URLs", DiscoveryOutcomeClassifier.Describe(reason));
    }

    [Fact]
    public void DiscoveryOutcomeClassifier_AllProvidersFailed_ReturnsProvidersFailed()
    {
        var state = new ResearchDiscoveryState
        {
            AutomaticDiscoveryProviderStats =
            [
                new DiscoveryProviderStat
                {
                    ProviderName = "DirectoryCatalog",
                    Attempted = true,
                    Succeeded = false
                },
                new DiscoveryProviderStat
                {
                    ProviderName = "CommonCrawlIndex",
                    Attempted = true,
                    Succeeded = false
                }
            ]
        };

        var reason = DiscoveryOutcomeClassifier.Classify(state, 0, 10);

        Assert.Equal(DiscoveryStopReason.ProvidersFailed, reason);
    }

    [Fact]
    public void DiscoverySourceExpansion_LooksLikeDirectory_DetectsSignals()
    {
        Assert.True(DiscoverySourceExpansionService.LooksLikeDirectory("https://example.com/members"));
        Assert.True(DiscoverySourceExpansionService.LooksLikeDirectory("https://example.com/دليل-شركات"));
        Assert.False(DiscoverySourceExpansionService.LooksLikeDirectory("https://example.com/about"));
    }

    [Fact]
    public void DiscoverySourceRegistry_ComputePriorityScore_RewardsYieldAndFreshness()
    {
        var weak = new DiscoverySource
        {
            AttemptCount = 10,
            SuccessCount = 1,
            CandidatesDiscovered = 1,
            UniqueCompaniesDiscovered = 1,
            QualifiedProspectsYielded = 0,
            FreshnessScore = 10
        };
        var strong = new DiscoverySource
        {
            AttemptCount = 10,
            SuccessCount = 9,
            CandidatesDiscovered = 40,
            UniqueCompaniesDiscovered = 20,
            QualifiedProspectsYielded = 12,
            FreshnessScore = 80,
            SourceType = DiscoverySourceType.IndustryAssociation
        };

        Assert.True(
            DiscoverySourceRegistryService.ComputePriorityScore(strong)
            > DiscoverySourceRegistryService.ComputePriorityScore(weak));
    }
}
