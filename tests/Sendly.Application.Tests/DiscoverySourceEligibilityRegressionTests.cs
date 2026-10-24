using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class DiscoverySourceEligibilityRegressionTests
{
    [Fact]
    public void A_UsPrivateSchoolQuery_DoesNotSelectCleaningAssociations()
    {
        var countries = new[] { "United States" };
        var industries = new[] { "Private School", "Independent School" };

        var issa = SourceEligibilityEvaluator.Evaluate(
            "ISSA Cleaning Association",
            "https://www.issa.com",
            "Association",
            ["United States", "USA", "US"],
            ["Cleaning", "Commercial Cleaning", "Janitorial", "Facility", "Sanitation"],
            countries,
            industries);

        var bscai = SourceEligibilityEvaluator.Evaluate(
            "BSCAI Cleaning Association",
            "https://www.bscai.org",
            "Association",
            ["United States", "USA", "US"],
            ["Cleaning", "Commercial Cleaning", "Building Service"],
            countries,
            industries);

        Assert.Equal(SourceEligibilityState.Ineligible, issa.State);
        Assert.Equal("source.industry_mismatch", issa.ReasonCode);
        Assert.False(issa.CountsAsRelevantIndustrySource);
        Assert.Equal(SourceEligibilityState.Ineligible, bscai.State);
        Assert.False(bscai.CountsAsRelevantIndustrySource);

        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(countries, industries);
        Assert.DoesNotContain(resolved.Entries, x =>
            x.BaseUrl.Contains("issa.com", StringComparison.OrdinalIgnoreCase)
            || x.BaseUrl.Contains("bscai.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Entries, x =>
            x.BaseUrl.Contains("nais.org", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Entries, x =>
            x.BaseUrl.Contains("privateschoolreview.com", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resolved.Entries, x =>
            x.BaseUrl.Contains("theinternationalschools.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_OpenCorporates_IsSecondaryNotIndustryDirectory()
    {
        var decision = SourceEligibilityEvaluator.Evaluate(
            "OpenCorporates Search Hub",
            "https://opencorporates.com",
            "Registry",
            ["United States"],
            ["Cleaning", "Education", "Business"],
            ["United States"],
            ["Commercial Cleaning"]);

        Assert.Equal(SourceEligibilityState.EligibleSecondary, decision.State);
        Assert.False(decision.CountsAsRelevantIndustrySource);
        Assert.True(decision.CanRevealTargetOrganizations);
    }

    [Fact]
    public void A_OpenCorporates_IsNotSelectedForArcheryClubViaFitnessSynonym()
    {
        var decision = SourceEligibilityEvaluator.Evaluate(
            "OpenCorporates Search Hub",
            "https://opencorporates.com",
            "Registry",
            ["United Arab Emirates"],
            ["Fitness", "Gym", "Retail", "Business"],
            ["United Arab Emirates"],
            ["Archery Club", "Sailing Center", "Escape Room"]);

        Assert.Equal(SourceEligibilityState.Ineligible, decision.State);
        Assert.False(decision.CanRevealTargetOrganizations);
        Assert.Null(IndustrySynonymCatalog.FindIndustryOverlap(
            ["Archery Club"],
            ["Fitness", "Gym", "fitness club"]));
    }

    [Fact]
    public void A_OpenCorporates_IsIneligibleForSchoolQueries()
    {
        var decision = SourceEligibilityEvaluator.Evaluate(
            "OpenCorporates Search Hub",
            "https://opencorporates.com",
            "Registry",
            ["United States"],
            ["Cleaning", "Education", "Business"],
            ["United States"],
            ["Private School"]);

        Assert.Equal(SourceEligibilityState.Ineligible, decision.State);
        Assert.Equal("source.registry_low_value_for_schools", decision.ReasonCode);
        Assert.False(decision.CanRevealTargetOrganizations);
    }

    [Fact]
    public void K_CleaningQuery_StillSelectsCleaningAssociations()
    {
        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(
            ["United States"],
            ["Commercial Cleaning"]);

        Assert.Contains(resolved.Entries, x => x.BaseUrl.Contains("issa.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(resolved.Entries, x => x.BaseUrl.Contains("bscai.org", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void K_SaudiEducation_StillSelectsMadares()
    {
        var resolved = SystemDirectoryCatalog.ResolveWithDecisions(
            ["Saudi Arabia"],
            ["Private School"]);

        Assert.Contains(resolved.Entries, x => x.BaseUrl.Contains("madares.sa", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resolved.Entries, x => x.BaseUrl.Contains("issa.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IndustrySynonym_PrivateSchoolMatchesEducationFamily()
    {
        var overlap = IndustrySynonymCatalog.FindIndustryOverlap(
            ["Private Primary School", "Independent School"],
            ["Education", "School"]);
        Assert.NotNull(overlap);
        Assert.Null(IndustrySynonymCatalog.FindIndustryOverlap(
            ["Private School"],
            ["Cleaning", "Janitorial", "Facility"]));
    }

    [Fact]
    public async Task C_ThreeIrrelevantSources_DoNotSatisfyEarlyExit()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource(
                "DirectoryCatalog",
                enabled: true,
                [
                    Secondary("https://www.issa.com", "ISSA"),
                    Secondary("https://www.bscai.org", "BSCAI"),
                    Secondary("https://opencorporates.com", "OpenCorporates")
                ]),
            new FakeDiscoverySource("CommonCrawlIndex", enabled: true, [])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var context = new UrlDiscoveryContext
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            MaxUrls = 50,
            MaxCandidateDomains = 50,
            MaxDiscoveryStages = 3,
            MinCandidatesBeforeRecovery = 10,
            Countries = ["United States"],
            Industries = ["Private School"],
            IndustrySynonyms = IndustrySynonymCatalog.Expand(["Private School"]),
            SourceEligibilityDecisions =
            [
                Ineligible("ISSA", "https://www.issa.com"),
                Ineligible("BSCAI", "https://www.bscai.org"),
                SecondaryDecision("OpenCorporates", "https://opencorporates.com")
            ]
        };

        var result = await coordinator.DiscoverAsync(context, CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Equal(DiscoveryStopReason.NoRelevantDiscoverySources, result.StopReason);
        Assert.Contains(result.Stages, x => x.RecoveryTriggered || x.StageNumber > 1);
    }

    [Fact]
    public async Task B_NoRelevantCatalog_StopsWithoutBurningCandidates()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeDiscoverySource("DirectoryCatalog", enabled: true, []),
            new FakeDiscoverySource("IndustryAssociationCatalog", enabled: true, []),
            new FakeDiscoverySource("PriorResearchUrls", enabled: true, []),
            new FakeDiscoverySource("SeedUrls", enabled: true, []),
            new FakeDiscoverySource("CommonCrawlIndex", enabled: true, [])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);

        var result = await coordinator.DiscoverAsync(
            new UrlDiscoveryContext
            {
                ProjectId = Guid.NewGuid(),
                ResearchRunId = Guid.NewGuid(),
                MaxUrls = 50,
                MaxDiscoveryStages = 3,
                MinCandidatesBeforeRecovery = 10,
                Countries = ["United States"],
                Industries = ["Private School"],
                Plan = new DiscoveryPlan
                {
                    EligibleProviderNames =
                    [
                        "SeedUrls", "PriorResearchUrls", "DirectoryCatalog",
                        "IndustryAssociationCatalog", "CommonCrawlIndex"
                    ]
                }
            },
            CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.True(
            result.StopReason is DiscoveryStopReason.NoCandidatesDiscovered
                or DiscoveryStopReason.NoRelevantDiscoverySources);
    }

    [Fact]
    public void E_ListingChrome_IsRejected_WhileRealOrgsRemain()
    {
        var html = """
                   <html><body>
                   <nav><a href="/membership">Membership</a><a href="/events">Events</a><a href="/store">Store</a></nav>
                   <div class="listing-card">
                     <h3><a href="/schools/bright-academy">Bright Academy</a></h3>
                     <a href="https://brightacademy.edu">Official site</a>
                     <p>Private independent school in Boston.</p>
                   </div>
                   <div class="listing-card">
                     <h3>Board of Directors</h3>
                     <a href="/about/board">Board</a>
                   </div>
                   <a href="tel:+61417740156">tel:+61417740156</a>
                   </body></html>
                   """;

        var listings = DirectoryListingExtractor.Extract(
            html,
            new Uri("https://directory.example/schools"));

        Assert.DoesNotContain(listings, x => x.Name.Equals("Membership", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Equals("Events", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Equals("Store", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Equals("Board of Directors", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(listings, x => x.Name.Contains("tel:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(listings, x => x.Name.Contains("Bright Academy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Coverage_IrrelevantSeeds_DoNotCountAsSufficient()
    {
        var eligibility = new SourceEligibilityResolveResult
        {
            EligiblePrimary = [],
            EligibleSecondary =
            [
                new SourceEligibilityDecision
                {
                    SourceName = "OpenCorporates",
                    State = SourceEligibilityState.EligibleSecondary,
                    CountsAsRelevantIndustrySource = false
                }
            ]
        };

        var result = new AutomaticDiscoveryResult
        {
            Candidates =
            [
                new DiscoveredUrlCandidate
                {
                    Url = "https://opencorporates.com",
                    IsSecondaryRegistry = true,
                    IsIndustryRelevant = false
                },
                new DiscoveredUrlCandidate
                {
                    Url = "https://www.issa.com",
                    IsIndustryRelevant = false
                },
                new DiscoveredUrlCandidate
                {
                    Url = "https://www.bscai.org",
                    IsIndustryRelevant = false
                }
            ]
        };

        Assert.False(DiscoveryCoverageEvaluator.HasSufficientRelevantCoverage(
            result,
            new UrlDiscoveryContext { MinCandidatesBeforeRecovery = 10 },
            eligibility));
    }

    private static DiscoveredUrlCandidate Secondary(string url, string name)
    {
        return new DiscoveredUrlCandidate
        {
            Url = url,
            SourceType = DiscoverySourceType.DirectoryCatalog,
            SourceName = name,
            Depth = 0,
            IsIndustryRelevant = false,
            IsSecondaryRegistry = true,
            EligibilityReasonCode = "source.eligible_secondary_registry"
        };
    }

    private static SourceEligibilityDecision Ineligible(string name, string url)
    {
        return new SourceEligibilityDecision
        {
            SourceName = name,
            SourceUrl = url,
            State = SourceEligibilityState.Ineligible,
            ReasonCode = "source.industry_mismatch",
            CountsAsRelevantIndustrySource = false
        };
    }

    private static SourceEligibilityDecision SecondaryDecision(string name, string url)
    {
        return new SourceEligibilityDecision
        {
            SourceName = name,
            SourceUrl = url,
            SourceKind = "Registry",
            State = SourceEligibilityState.EligibleSecondary,
            ReasonCode = "source.eligible_secondary_registry",
            CountsAsRelevantIndustrySource = false,
            CanRevealTargetOrganizations = true
        };
    }

    private sealed class FakeDiscoverySource : IUrlDiscoverySource
    {
        private readonly bool _enabled;
        private readonly IReadOnlyList<DiscoveredUrlCandidate> _candidates;

        public FakeDiscoverySource(string name, bool enabled, IReadOnlyList<DiscoveredUrlCandidate> candidates)
        {
            Name = name;
            _enabled = enabled;
            _candidates = candidates;
        }

        public string Name { get; }

        public bool IsEnabled(UrlDiscoveryContext context) => _enabled;

        public Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
            UrlDiscoveryContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(_candidates);
    }
}
