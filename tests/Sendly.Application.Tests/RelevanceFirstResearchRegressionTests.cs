using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Application.Services.WebsiteInvestigation;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Tests;

public class RelevanceFirstResearchRegressionTests
{
    private static readonly List<string> VenueIndustries = ["paintball arena", "laser tag", "trampoline park"];
    private static readonly List<string> UnitedStates = ["United States"];

    [Fact]
    public void SearchAdmission_relevant_business_gets_priority_and_irrelevant_is_rejected()
    {
        var relevant = UrlAdmissionPolicy.Evaluate(
            "https://dallaspaintball.com/contact",
            countries: UnitedStates,
            industries: VenueIndustries);
        var social = UrlAdmissionPolicy.Evaluate(
            "https://facebook.com/dallaspaintball",
            countries: UnitedStates,
            industries: VenueIndustries);
        var forum = UrlAdmissionPolicy.Evaluate(
            "https://paintballforum.example.com/forum/members/player1",
            countries: UnitedStates,
            industries: VenueIndustries);
        var education = UrlAdmissionPolicy.Evaluate(
            "https://stateuniversity.edu/paintball-club",
            countries: UnitedStates,
            industries: VenueIndustries);

        Assert.True(relevant.Accepted);
        Assert.False(social.Accepted);
        Assert.Equal("url.social_platform", social.ReasonCode);
        Assert.False(forum.Accepted);
        Assert.False(education.Accepted);
        Assert.Equal("url.educational_unrelated", education.ReasonCode);

        var searchPriority = UrlPriorityScorer.Score(
            "https://dallaspaintball.com",
            DiscoverySourceType.WebSearch,
            VenueIndustries,
            UnitedStates,
            null,
            0,
            false);
        var expandedPriority = UrlPriorityScorer.Score(
            "https://dallaspaintball.com",
            DiscoverySourceType.LinkExpansion,
            VenueIndustries,
            UnitedStates,
            null,
            1,
            false);
        Assert.True(searchPriority.Total > expandedPriority.Total);
    }

    [Fact]
    public void Irrelevant_page_cannot_flood_queue_with_links()
    {
        var links = Enumerable.Range(1, 300)
            .Select(i => $"https://stateuniversity.edu/page-{i}")
            .ToList();
        var admitted = links
            .Select(x => UrlAdmissionPolicy.Evaluate(x, countries: UnitedStates, industries: VenueIndustries))
            .Count(x => x.Accepted);
        var capped = CrawlQueueScheduler.ApplyPerDomainCap(links, x => UrlCanonicalizer.ExtractDomain(x), 20);

        Assert.Equal(0, admitted);
        Assert.True(capped.Count <= 20);
        Assert.Equal(3, CrawlQueueScheduler.ResolveLinkExpansionCap(15, false));
    }

    [Fact]
    public void Relevant_business_page_can_produce_useful_contact_urls()
    {
        var contact = UrlAdmissionPolicy.Evaluate(
            "https://dallaspaintball.com/contact",
            countries: UnitedStates,
            industries: VenueIndustries);
        var score = UrlPriorityScorer.Score(
            "https://dallaspaintball.com/contact",
            DiscoverySourceType.LinkExpansion,
            VenueIndustries,
            UnitedStates,
            "dallaspaintball.com",
            1,
            false,
            UrlPagePurpose.Contact);

        Assert.True(contact.Accepted);
        Assert.True(UrlAdmissionPolicy.IsUsefulPendingPurpose(contact.PagePurpose));
        Assert.True(score.IsVerificationUseful);
        Assert.Equal(15, CrawlQueueScheduler.ResolveLinkExpansionCap(15, true));
    }

    [Fact]
    public void Single_domain_cannot_consume_entire_crawl_budget()
    {
        var urls = Enumerable.Range(1, 100)
            .Select(i => new DiscoveredUrlCandidateForTest($"https://same-domain.com/page-{i}"))
            .ToList();
        var capped = CrawlQueueScheduler.ApplyPerDomainCap(
            urls,
            x => UrlCanonicalizer.ExtractDomain(x.Url),
            5);
        var budget = DiscoveryBudgetResolver.Resolve(
            new ResearchOptions { MaxPagesPerCrawl = 4, MaxFetchesPerRun = 80 },
            null);

        Assert.Equal(5, capped.Count);
        Assert.Equal(4, budget.MaxPagesPerDomain);
        Assert.True(budget.MaxPagesPerDomain < budget.MaxFetchesPerRun);
    }

    [Fact]
    public void Search_executes_additional_wave_while_crawl_work_remains()
    {
        var state = new ResearchDiscoveryState
        {
            PagesQueued = 712,
            UsefulPendingUrls = 100,
            PagesFetched = 20,
            FetchBudgetLimit = 80,
            LastBudgetStopReason = ResearchRunContinuationPolicy.FetchBatchLimitReason,
            CrawlBatchesSinceSearch = 1
        };

        Assert.True(ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state));
        Assert.False(ResearchRunContinuationPolicy.ShouldDeferSearchForIndependentCrawl(state));
        Assert.True(ResearchRunContinuationPolicy.IsSearchDue(state));
    }

    [Fact]
    public void Challenged_search_provider_does_not_retry_forever_or_repeat_queries()
    {
        var state = new ResearchDiscoveryState
        {
            ConsecutiveProviderFailures = 2,
            SearchProviderHealth = SearchProviderHealthState.Unavailable
        };
        var planned = new List<PlannedSearchQuery>
        {
            new() { Query = "paintball arena Dallas United States", Industry = "paintball arena", Intent = SearchQueryIntents.DiscoverBusiness }
        };
        state.ExecutedSearchQueryKeys.Add(SearchCampaignPlanner.BuildKey(planned[0]));

        Assert.False(ResearchRunContinuationPolicy.ShouldRetrySearchProvider(state));
        Assert.Null(SearchCampaignPlanner.SelectNext(planned, state));
    }

    [Fact]
    public void Forum_username_cannot_become_organization()
    {
        Assert.True(ProspectJunkClassifier.IsJunkOrganization(
            "PlayerOne99",
            "https://paintballforum.example.com/forum/members/playerone99"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization(
            "Show posts by this member only",
            "https://paintballforum.example.com/forum/members/playerone99"));

        var html = """
            <html><body>
              <article class="listing-card">
                <h2>PlayerOne99</h2>
                <a href="/forum/members/playerone99">PlayerOne99</a>
              </article>
            </body></html>
            """;
        var listings = DirectoryListingExtractor.Extract(
            html,
            new Uri("https://paintballforum.example.com/forum/"));
        Assert.DoesNotContain(listings, x => x.Name.Contains("PlayerOne99", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generic_headings_and_social_platforms_cannot_become_qualified_businesses()
    {
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("Facebook", "https://facebook.com/example"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("Accueil", "https://example-site.com"));
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("Islam", "https://example-site.com/islam"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Accueil"));

        var result = EvaluateCandidate(new ProspectCandidate
        {
            CompanyName = "Facebook",
            Website = "https://facebook.com/example",
            Country = "United States",
            Industry = "paintball arena",
            CompanyDescription = "paintball arena United States",
            Email = "info@facebook.com",
            EntityType = ResearchEntityType.TargetBusiness,
            EntityTypeConfidence = 80,
            NameConfidence = 90
        });
        Assert.False(result.IsQualified);
    }

    [Fact]
    public void Real_local_paintball_venue_passes_entity_and_qualification_validation()
    {
        Assert.False(ProspectJunkClassifier.IsJunkOrganization(
            "Dallas Paintball Arena",
            "https://dallaspaintball.com"));

        var result = EvaluateCandidate(new ProspectCandidate
        {
            CompanyName = "Dallas Paintball Arena",
            Website = "https://dallaspaintball.com",
            Country = "United States",
            CountryEvidenceSource = "page_address",
            CountryEvidenceSummary = "123 Main Street, Dallas, TX 75201, United States",
            City = "Dallas",
            Industry = "paintball arena",
            CompanyDescription = "Paintball arena in Dallas, Texas, United States offering paintball arena games and parties.",
            Email = "info@dallaspaintball.com",
            EntityType = ResearchEntityType.TargetBusiness,
            EntityTypeConfidence = 85,
            NameConfidence = 92,
            NameReasonCode = "identity.json_ld_organization",
            ContactAttributionState = ContactAttributionState.SameDomain,
            ContactAttributionConfidence = 92,
            ContactAttributionReasonCode = "contact.same_domain",
            SourceUrl = "https://dallaspaintball.com"
        });

        Assert.True(result.IsQualified);
        Assert.True(result.IsIdentityVerified);
        Assert.True(result.Score >= 50);
    }

    [Fact]
    public void Business_outside_target_country_cannot_qualify_by_mentioning_target_country()
    {
        var result = EvaluateCandidate(new ProspectCandidate
        {
            CompanyName = "Toronto Paintball Arena",
            Website = "https://torontopaintball.ca",
            Country = "Canada",
            CountryEvidenceSource = "page_address",
            CountryEvidenceSummary = "100 King Street, Toronto, Canada",
            City = "Toronto",
            Industry = "paintball arena",
            CompanyDescription = "Paintball arena in Toronto, Canada. Popular with visitors from the United States.",
            Email = "info@torontopaintball.ca",
            EntityType = ResearchEntityType.TargetBusiness,
            EntityTypeConfidence = 85,
            NameConfidence = 92,
            NameReasonCode = "identity.json_ld_organization",
            ContactAttributionState = ContactAttributionState.SameDomain,
            ContactAttributionConfidence = 92,
            SourceUrl = "https://torontopaintball.ca"
        });

        Assert.False(result.IsQualified);
        Assert.Contains("targetCountry", result.MissingCriteria);
    }

    [Fact]
    public void Industry_mismatch_cannot_qualify_regardless_of_other_evidence()
    {
        var result = EvaluateCandidate(new ProspectCandidate
        {
            CompanyName = "Dallas Cleaning Pros",
            Website = "https://dallascleaningpros.com",
            Country = "United States",
            CountryEvidenceSource = "page_address",
            CountryEvidenceSummary = "Dallas, TX, United States",
            Industry = "cleaning",
            CompanyDescription = "Commercial cleaning company in Dallas, United States.",
            Email = "info@dallascleaningpros.com",
            EntityType = ResearchEntityType.TargetBusiness,
            EntityTypeConfidence = 90,
            NameConfidence = 95,
            NameReasonCode = "identity.json_ld_organization",
            ContactAttributionState = ContactAttributionState.SameDomain,
            ContactAttributionConfidence = 95,
            SourceUrl = "https://dallascleaningpros.com"
        });

        Assert.False(result.IsQualified);
        Assert.Contains("targetIndustry", result.MissingCriteria);
        Assert.True(result.Score < 50);
    }

    [Fact]
    public void Placeholder_and_internal_emails_are_rejected_for_qualification()
    {
        Assert.True(EmailAddressParser.IsValidEmail("example@domain.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("example@domain.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("info@example.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("research@sendly.local"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("info@company.local"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("not-an-email"));
        Assert.True(EmailAddressParser.IsAcceptableBusinessEmail("info@dallaspaintball.com"));

        var candidate = ValidVenueCandidate();
        candidate.Email = "example@domain.com";
        candidate.ContactAttributionState = ContactAttributionState.SameDomain;
        var result = EvaluateCandidate(candidate);
        Assert.Contains("publicBusinessEmail", result.MissingCriteria);
        Assert.False(result.IsEmailReady);
    }

    [Fact]
    public void Unqualified_candidate_is_not_treated_as_qualified_prospect()
    {
        var candidate = ValidVenueCandidate();
        candidate.CompanyName = "Dallas Cleaning Pros";
        candidate.Website = "https://dallascleaningpros.com";
        candidate.SourceUrl = "https://dallascleaningpros.com";
        candidate.Email = "info@dallascleaningpros.com";
        candidate.CompanyDescription = "Commercial cleaning company in Dallas, United States.";
        candidate.Industry = "cleaning";
        var result = EvaluateCandidate(candidate);

        Assert.False(result.IsQualified);
        var wouldBeQualifiedProspect = result.IsQualified && result.Score >= 50;
        Assert.False(wouldBeQualifiedProspect);
    }

    [Fact]
    public void Duplicate_business_from_different_urls_is_deduplicated_after_normalization()
    {
        var first = UrlCanonicalizer.Canonicalize("https://dallaspaintball.com/?utm_source=google#contact");
        var second = UrlCanonicalizer.Canonicalize("https://dallaspaintball.com/");
        Assert.Equal(second, first);

        var merged = ProspectCandidateDeduplicator.DeduplicateAndMerge(
        [
            new ProspectCandidate
            {
                CompanyName = "Dallas Paintball Arena",
                Website = "https://dallaspaintball.com",
                SourceUrl = "https://directory.example.com/dallas-paintball"
            },
            new ProspectCandidate
            {
                CompanyName = "Dallas Paintball Arena",
                Website = "https://www.dallaspaintball.com/contact",
                SourceUrl = "https://dallaspaintball.com/contact",
                Email = "info@dallaspaintball.com"
            }
        ]);

        Assert.Single(merged);
    }

    [Fact]
    public void Zero_qualified_with_exhausted_budget_is_not_completed()
    {
        var state = new ResearchDiscoveryState
        {
            PagesFetched = 80,
            FetchBudgetLimit = 80,
            CandidatesDiscovered = 599,
            CandidatesValidated = 26
        };

        var status = ResearchRunOutcomeResolver.Resolve(0, 50, state, 0, false);
        Assert.Equal(ResearchRunStatus.BudgetExhausted, status);
        Assert.NotEqual(ResearchRunStatus.Completed, status);
    }

    [Fact]
    public void Run_reaching_qualified_target_is_completed()
    {
        var status = ResearchRunOutcomeResolver.Resolve(50, 50, new ResearchDiscoveryState(), 0, true);
        Assert.Equal(ResearchRunStatus.Completed, status);
    }

    [Fact]
    public void Partial_run_reports_incomplete_target_and_preserves_useful_results()
    {
        var state = new ResearchDiscoveryState
        {
            CompaniesIdentified = 12,
            ProspectsSaved = 5,
            PagesFetched = 20,
            FetchBudgetLimit = 80
        };

        var status = ResearchRunOutcomeResolver.Resolve(5, 50, state, 0, true);
        Assert.Equal(ResearchRunStatus.Partial, status);
        Assert.True(ResearchRunOutcomeResolver.IsTerminal(status));
        Assert.Equal(12, state.CompaniesIdentified);
        Assert.Equal(5, state.ProspectsSaved);
    }

    [Fact]
    public void Budget_accounting_reserves_search_capacity_across_interleaved_stages()
    {
        Assert.Equal(60, ResearchRunContinuationPolicy.ResolveLinkExpansionFetchCap(0, 80, 25, true));
        Assert.Equal(80, ResearchRunContinuationPolicy.ResolveLinkExpansionFetchCap(0, 80, 25, false));

        var state = new ResearchDiscoveryState { FetchBudgetLimit = 80, PagesFetched = 60 };
        Assert.False(ResearchRunOutcomeResolver.IsBudgetExhausted(state));
        state.PagesFetched = 80;
        Assert.True(ResearchRunOutcomeResolver.IsBudgetExhausted(state));
    }

    [Fact]
    public void All_target_industries_receive_fair_discovery_opportunities()
    {
        var planned = new List<PlannedSearchQuery>
        {
            new() { Query = "paintball arena United States", Industry = "paintball arena", Intent = SearchQueryIntents.DiscoverBusiness },
            new() { Query = "laser tag United States", Industry = "laser tag", Intent = SearchQueryIntents.DiscoverBusiness },
            new() { Query = "trampoline park United States", Industry = "trampoline park", Intent = SearchQueryIntents.DiscoverBusiness }
        };
        var state = new ResearchDiscoveryState();
        state.QueriesExecutedByIndustry["paintball arena"] = 3;
        state.ExecutedSearchQueryKeys.Add(SearchCampaignPlanner.BuildKey(planned[0]));

        var next = SearchCampaignPlanner.SelectNext(planned, state);
        Assert.NotNull(next);
        Assert.NotEqual("paintball arena", next!.Industry);
    }

    [Fact]
    public void Existing_settings_remain_compatible_with_new_budget_controls()
    {
        var options = new ResearchOptions
        {
            MaxCandidatesPerRun = 200,
            MaxFetchesPerRun = 80,
            MaxPagesPerCrawl = 12
        };
        var budget = DiscoveryBudgetResolver.Resolve(options, null);

        Assert.Equal(80, budget.MaxFetchesPerRun);
        Assert.Equal(15, budget.MaxLinksPerPage);
        Assert.Equal(20, budget.MaxLinksPerDomain);
        Assert.Equal(25, budget.SearchFetchBudgetReservePercent);
    }

    private static QualificationEvaluationResult EvaluateCandidate(ProspectCandidate candidate)
    {
        var service = new ProspectQualificationService();
        return service.Evaluate(new QualificationEvaluationRequest
        {
            ProjectId = Guid.NewGuid(),
            ResearchRunId = Guid.NewGuid(),
            Candidate = candidate,
            RequireEmail = true,
            MinQualificationScore = 50,
            Countries = UnitedStates,
            Industries = VenueIndustries
        });
    }

    private static ProspectCandidate ValidVenueCandidate()
    {
        return new ProspectCandidate
        {
            CompanyName = "Dallas Paintball Arena",
            Website = "https://dallaspaintball.com",
            Country = "United States",
            CountryEvidenceSource = "page_address",
            CountryEvidenceSummary = "123 Main Street, Dallas, TX 75201, United States",
            City = "Dallas",
            Industry = "paintball arena",
            CompanyDescription = "Paintball arena in Dallas, Texas, United States offering paintball arena games.",
            Email = "info@dallaspaintball.com",
            EntityType = ResearchEntityType.TargetBusiness,
            EntityTypeConfidence = 85,
            NameConfidence = 92,
            NameReasonCode = "identity.json_ld_organization",
            ContactAttributionState = ContactAttributionState.SameDomain,
            ContactAttributionConfidence = 92,
            ContactAttributionReasonCode = "contact.same_domain",
            SourceUrl = "https://dallaspaintball.com"
        };
    }

    private sealed class DiscoveredUrlCandidateForTest
    {
        public DiscoveredUrlCandidateForTest(string url)
        {
            Url = url;
        }

        public string Url { get; }
    }
}
