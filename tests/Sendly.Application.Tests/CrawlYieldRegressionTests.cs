using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Entities;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class CrawlYieldRegressionTests
{
    [Theory]
    [InlineData("https://cdn.example.com/photo.jpg", false, "url.media_asset")]
    [InlineData("https://regymenfitness.co.nz/assets/contact-BQ2Ywf54.js", false, "url.script_asset")]
    [InlineData("https://ironhousegym.co.nz/wp-json/oembed/1.0/embed", false, "url.api_endpoint")]
    [InlineData("https://images.squarespace-cdn.com/content/v1/abc/image.png", false, "url.cdn_host")]
    [InlineData("https://www.abadc.com.sa/product-sitemap.xml", false, "url.media_asset")]
    [InlineData("https://kc.edu.sa/en/contactUs/_payload.json", false, "url.media_asset")]
    [InlineData("https://www.jetts.co.nz/clubs/auckland-cbd/", true, "url.accepted")]
    [InlineData("https://www.flexfitnessgym.co.nz/contact", true, "url.accepted")]
    [InlineData("https://www.oni.co.nz/about", true, "url.accepted")]
    [InlineData("https://kc.edu.sa/en/contactUs", true, "url.accepted")]
    public void UrlAdmission_filters_assets_and_keeps_useful_pages(
        string url,
        bool accepted,
        string reasonPrefix)
    {
        var decision = UrlAdmissionPolicy.Evaluate(url);
        Assert.Equal(accepted, decision.Accepted);
        Assert.StartsWith(reasonPrefix.Split('.')[0], decision.ReasonCode);
        if (accepted)
        {
            Assert.Equal(reasonPrefix, decision.ReasonCode);
        }
    }

    [Fact]
    public void OfficialDomainRecovery_keeps_school_domains_from_directory_pages()
    {
        var result = OfficialDomainRecovery.BuildCandidates(
            [
                "https://ksaschools.com/some-school",
                "https://example-school.edu.sa/contact",
                "https://real-academy.sa/about"
            ],
            parentUrl: "https://ksaschools.com/cities/riyadh",
            knownDomains: [],
            knownCanonicalUrls: [],
            industries: ["Education", "School"],
            countries: ["Saudi Arabia"],
            maxCandidates: 10);

        Assert.DoesNotContain(result.Candidates, x => x.Url.Contains("ksaschools.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, x => x.Url.Contains("example-school.edu.sa", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Candidates, x => x.Url.Contains("real-academy.sa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UrlAdmission_uses_content_type_when_extension_is_ambiguous()
    {
        var html = UrlAdmissionPolicy.Evaluate("https://example.com/download", "text/html");
        Assert.True(html.Accepted);

        var js = UrlAdmissionPolicy.Evaluate("https://example.com/download", "text/javascript");
        Assert.False(js.Accepted);
        Assert.Equal("url.content_type", js.ReasonCode);
    }

    [Fact]
    public void CrawlQueueScheduler_prefers_contact_pages_and_limits_domain_monopoly()
    {
        var pending = new List<DiscoveredUrl>();
        for (var i = 0; i < 20; i++)
        {
            pending.Add(new DiscoveredUrl
            {
                Url = $"https://www.jetts.co.nz/clubs/location-{i}/",
                NormalizedUrl = $"https://www.jetts.co.nz/clubs/location-{i}/",
                NormalizedDomain = "www.jetts.co.nz",
                SourceType = DiscoverySourceType.LinkExpansion,
                Status = DiscoveredUrlStatus.Pending,
                Depth = 1,
                PagePurpose = UrlPagePurpose.FranchiseLocation.ToString()
            });
        }

        pending.Add(new DiscoveredUrl
        {
            Url = "https://ironhousegym.co.nz/contact/",
            NormalizedUrl = "https://ironhousegym.co.nz/contact/",
            NormalizedDomain = "ironhousegym.co.nz",
            SourceType = DiscoverySourceType.SeedUrl,
            Status = DiscoveredUrlStatus.Pending,
            Depth = 0,
            PagePurpose = UrlPagePurpose.Contact.ToString()
        });

        pending.Add(new DiscoveredUrl
        {
            Url = "https://images.squarespace-cdn.com/x.png",
            NormalizedUrl = "https://images.squarespace-cdn.com/x.png",
            NormalizedDomain = "images.squarespace-cdn.com",
            SourceType = DiscoverySourceType.LinkExpansion,
            Status = DiscoveredUrlStatus.Pending,
            Depth = 2
        });

        var ranked = CrawlQueueScheduler.RankPending(
            pending,
            industries: ["Gym", "Fitness"],
            countries: ["New Zealand"],
            pagesFetchedPerDomain: new Dictionary<string, int>(),
            failedDomains: new HashSet<string>(),
            maxPagesPerDomain: 5,
            maxPerDomainInBatch: 2);

        Assert.Equal("https://ironhousegym.co.nz/contact/", ranked[0].Url.Url);
        var fairWindow = 1 + 2;
        var jettsInFairWindow = ranked.Take(fairWindow + 1)
            .Count(x => x.Domain.Contains("jetts", StringComparison.OrdinalIgnoreCase));
        Assert.True(jettsInFairWindow <= 2);
        Assert.Contains(ranked.Take(4), x => x.Domain.Contains("ironhouse", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdaptiveRecovery_triggers_on_low_qualified_yield_despite_high_volume()
    {
        var context = new UrlDiscoveryContext
        {
            MinCandidatesBeforeRecovery = 10,
            MaxPriorResearchSharePercent = 40,
            Industries = ["Gym"],
            IndustrySynonyms = ["Fitness", "Health club"],
            Countries = ["New Zealand"]
        };
        var current = new AutomaticDiscoveryResult
        {
            Candidates = Enumerable.Range(0, 80)
                .Select(i => new DiscoveredUrlCandidate
                {
                    Url = $"https://prior{i}.example/page",
                    SourceType = DiscoverySourceType.PriorResearch
                })
                .ToList(),
            ProviderStats =
            [
                new DiscoveryProviderStat { ProviderName = "PriorResearchUrls", Attempted = true, Succeeded = true, CandidatesReturned = 80 },
                new DiscoveryProviderStat { ProviderName = "DirectoryCatalog", Attempted = true, Succeeded = true, CandidatesReturned = 0 }
            ],
            RejectedFiltered = 40
        };
        var yield = new DiscoveryYieldSignals
        {
            RequestedProspects = 50,
            QualifiedProspects = 1,
            UniqueTargetBusinesses = 5,
            AttributedEmails = 1,
            HtmlFetchesSucceeded = 30,
            FetchesAttempted = 40,
            IrrelevantEntities = 20,
            EntitiesClassified = 33,
            Duplicates = 15,
            CandidatesProcessed = 40,
            FreshSourceUrls = 5,
            PriorSourceUrls = 75,
            AssetRejections = 40,
            UrlsDiscovered = 80,
            SourcesAttempted = 2,
            SourceFailures = 0
        };

        var action = AdaptiveDiscoveryRecovery.DecideFromOutcomes(
            context,
            current,
            yield,
            duplicateRatePercent: 30,
            failedFetchPercent: 20);

        Assert.NotNull(action);
        Assert.True(
            action!.Strategy is "TightenUrlFiltering"
                or "PreferFreshDiscoveryProviders"
                or "PreferOfficialCompanyDomains"
                or "YieldQualityRecovery"
                or "PrioritizeOfficialContactPages");
        AdaptiveDiscoveryRecovery.Apply(context, action);
        Assert.True(
            context.PreferFreshSources
            || context.PreferOfficialCompanyDomains
            || context.TightenUrlFiltering
            || context.EnableContactPageHints
            || context.PreferUnexploredProviders);
    }

    [Fact]
    public void EntityClassifier_rejects_listicles_keeps_directory_discovered_businesses_as_targets()
    {
        var listicle = EntityClassifier.Classify(
            "THE BEST 10 Gyms in AUCKLAND, NEW ZEALAND",
            "THE BEST 10 Gyms in AUCKLAND, NEW ZEALAND",
            "Our ranked list of gyms",
            "https://www.yelp.com/search?cflt=gyms");
        Assert.Equal(ResearchEntityType.PublisherEditorial, listicle.EntityType);
        Assert.False(EntityClassifier.IsAllowedTarget(listicle.EntityType, ResearchEntityType.TargetBusiness));

        var location = EntityClassifier.Classify(
            "Jetts Auckland CBD",
            "Jetts Auckland CBD Club",
            "Visit our Auckland CBD club location",
            "https://www.jetts.co.nz/clubs/auckland-cbd/");
        Assert.True(
            location.EntityType is ResearchEntityType.BusinessLocation or ResearchEntityType.TargetBusiness);
        Assert.True(EntityClassifier.IsAllowedTarget(location.EntityType, ResearchEntityType.TargetBusiness));
    }

    [Fact]
    public void ContactAttribution_rejects_cross_domain_operator_email()
    {
        var rejected = ContactAttributionHelper.Attribute(
            "ads@directory.example",
            "https://directory.example/gym/ironhouse",
            "https://ironhousegym.co.nz",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.Rejected, rejected.State);

        var same = ContactAttributionHelper.Attribute(
            "info@ironhousegym.co.nz",
            "https://ironhousegym.co.nz/contact/",
            "https://ironhousegym.co.nz",
            pageIsDirectory: false);
        Assert.Equal(ContactAttributionState.SameDomain, same.State);
    }

    [Fact]
    public void Qualification_distinguishes_missing_email_from_industry_mismatch()
    {
        var service = new ProspectQualificationService();
        var missingEmail = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Ironhouse Gym",
                Website = "https://ironhousegym.co.nz",
                Country = "New Zealand",
                Industry = "Gym",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 80
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["New Zealand"],
            Industries = ["Gym"]
        });
        Assert.False(missingEmail.IsQualified);
        Assert.Contains("publicBusinessEmail", missingEmail.MissingCriteria);
        Assert.Contains(missingEmail.Criteria, x => x.CriterionKey == "targetIndustry" && x.Passed);

        var industryFail = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Generic Co",
                Website = "https://generic.nz",
                Country = "New Zealand",
                Email = "hello@generic.nz",
                EntityType = ResearchEntityType.TargetBusiness,
                EntityTypeConfidence = 70,
                NameConfidence = 70,
                ContactAttributionState = ContactAttributionState.SameDomain
            },
            RequireEmail = true,
            MinQualificationScore = 60,
            Countries = ["New Zealand"],
            Industries = ["Gym", "Fitness"]
        });
        Assert.False(industryFail.IsQualified);
        Assert.Contains("targetIndustry", industryFail.MissingCriteria);
        Assert.DoesNotContain("Industry mismatch", industryFail.MatchingCriteria);
    }

    [Fact]
    public async Task AutomaticDiscovery_filters_polluted_urls_and_caps_prior_research()
    {
        var sources = new IUrlDiscoverySource[]
        {
            new FakeYieldSource(
                "PriorResearchUrls",
                true,
                Enumerable.Range(0, 30)
                    .Select(i => new DiscoveredUrlCandidate
                    {
                        Url = $"https://example.com/prior-{i}",
                        SourceType = DiscoverySourceType.PriorResearch
                    })
                    .Concat(
                    [
                        new DiscoveredUrlCandidate
                        {
                            Url = "https://images.squarespace-cdn.com/x.jpg",
                            SourceType = DiscoverySourceType.PriorResearch
                        },
                        new DiscoveredUrlCandidate
                        {
                            Url = "https://example.com/assets/app.js",
                            SourceType = DiscoverySourceType.PriorResearch
                        }
                    ])
                    .ToList()),
            new FakeYieldSource(
                "DirectoryCatalog",
                true,
                [
                    new DiscoveredUrlCandidate
                    {
                        Url = "https://example.com/fresh-gym/",
                        SourceType = DiscoverySourceType.DirectoryCatalog
                    }
                ])
        };

        var coordinator = new AutomaticDiscoveryCoordinator(
            sources,
            NullLogger<AutomaticDiscoveryCoordinator>.Instance);
        var context = new UrlDiscoveryContext
        {
            MaxUrls = 20,
            MaxPriorResearchSharePercent = 40,
            MaxCandidateDomains = 50,
            MaxDiscoveryStages = 2,
            MinCandidatesBeforeRecovery = 5,
            Countries = ["New Zealand"],
            Industries = ["Gym"]
        };

        var result = await coordinator.DiscoverAsync(context, CancellationToken.None);

        Assert.True(result.RejectedFiltered >= 2);
        Assert.DoesNotContain(result.Candidates, x => x.Url.Contains("squarespace", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Candidates, x => x.Url.Contains("/assets/app.js", StringComparison.OrdinalIgnoreCase));
        var maxPriorAllowed = (int)Math.Floor(context.MaxUrls * (context.MaxPriorResearchSharePercent / 100.0));
        var priorCount = result.Candidates.Count(x => x.SourceType == DiscoverySourceType.PriorResearch);
        Assert.True(priorCount <= Math.Max(maxPriorAllowed, 0));
        Assert.Contains(result.Candidates, x => x.Url.Contains("fresh-gym", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.Candidates.Count <= context.MaxUrls);
    }

    [Fact]
    public void DiscoveryYieldSignals_detects_underperformance()
    {
        var healthy = new DiscoveryYieldSignals
        {
            RequestedProspects = 50,
            QualifiedProspects = 20,
            FetchesAttempted = 40,
            HtmlFetchesSucceeded = 35,
            AttributedEmails = 18,
            UniqueTargetBusinesses = 22
        };
        Assert.False(healthy.IsUnderperforming());

        var collapsed = new DiscoveryYieldSignals
        {
            RequestedProspects = 50,
            QualifiedProspects = 1,
            FetchesAttempted = 40,
            HtmlFetchesSucceeded = 30,
            AttributedEmails = 1,
            UniqueTargetBusinesses = 10,
            PriorSourceUrls = 70,
            FreshSourceUrls = 5,
            AssetRejections = 50,
            UrlsDiscovered = 80
        };
        Assert.True(collapsed.IsUnderperforming());
    }

    private sealed class FakeYieldSource : IUrlDiscoverySource
    {
        private readonly bool _enabled;
        private readonly List<DiscoveredUrlCandidate> _candidates;

        public FakeYieldSource(string name, bool enabled, List<DiscoveredUrlCandidate> candidates)
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
            => Task.FromResult<IReadOnlyList<DiscoveredUrlCandidate>>(_candidates);
    }
}
