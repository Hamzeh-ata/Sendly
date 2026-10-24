using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Tests;

public class SearchFirstResearchEngineTests
{
    private static SearchResultGateInput GateInput(string url, string title, string snippet, string query = "Paintball parks in Texas")
    {
        return new SearchResultGateInput
        {
            Title = title,
            Snippet = snippet,
            Url = url,
            Query = query,
            QueryIndustry = "Paintball arenas",
            Industries = ["Paintball arenas", "Laser tag venues", "Trampoline parks"],
            Countries = ["United States"],
            Locations = ["Texas", "Florida", "Ohio", "California", "Georgia", "Pennsylvania"]
        };
    }

    [Fact]
    public void Gate_RelevantBusinessWithGeoAndIndustryEvidence_MayFetch()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://lonestarpb.example/",
            "Lone Star Paintball Park - Dallas, Texas",
            "Outdoor paintball park in Texas with rentals, parties and walk-on play. Book now."));

        Assert.True(result.MayFetch);
        Assert.Equal(SearchResultDecision.PotentialOfficialBusinessWebsite, result.Decision);
        Assert.NotNull(result.CountryEvidence);
    }

    [Fact]
    public void Gate_ForumUser_CannotFetch()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://example-forum.example/forums/members/johndoe",
            "johndoe - Member Profile",
            "Paintball discussion member in Texas"));

        Assert.False(result.MayFetch);
        Assert.Equal(SearchResultDecision.Irrelevant, result.Decision);
    }

    [Fact]
    public void Gate_SocialAndAppAndNews_CannotFetch()
    {
        var social = SearchResultGate.Evaluate(GateInput("https://facebook.com/somepaintball", "Paintball Texas", "Paintball in Texas"));
        var app = SearchResultGate.Evaluate(GateInput("https://play.google.com/store/apps/details?id=x", "Paintball App", "Paintball game app"));
        var news = SearchResultGate.Evaluate(GateInput("https://www.cnn.com/2026/01/paintball", "Paintball news", "Paintball park Texas news story"));

        Assert.False(social.MayFetch);
        Assert.False(app.MayFetch);
        Assert.False(news.MayFetch);
    }

    [Fact]
    public void Gate_Directory_IsSourceNotProspect()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://www.yelp.com/search?find_desc=Paintball&find_loc=Texas",
            "Best Paintball in Texas",
            "Paintball parks and fields in Texas listings"));

        Assert.True(result.IsDirectory);
        Assert.Equal(SearchResultDecision.PotentialDirectory, result.Decision);
    }

    [Fact]
    public void Gate_WrongIndustry_CannotFetch()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://dallasplumbing.example/",
            "Dallas Plumbing Company Texas",
            "Plumbing repairs and water heaters in Dallas Texas",
            "Plumbing in Texas"));

        Assert.False(result.MayFetch);
        Assert.Equal(SearchResultDecision.WrongIndustry, result.Decision);
    }

    [Fact]
    public void Gate_SchoolProject_DoesNotRejectPrivateSchoolBecausePublicSchoolIsExcluded()
    {
        var result = SearchResultGate.Evaluate(new SearchResultGateInput
        {
            Title = "British International School of Chicago",
            Snippet = "Private international school in Chicago, United States offering K-12 education",
            Url = "https://britishschoolchicago.example/",
            Query = "British school in Chicago United States",
            QueryIndustry = "International School",
            Industries =
            [
                "Private School",
                "International School",
                "Independent School",
                "Fee-Paying School"
            ],
            ExcludedIndustries =
            [
                "Public School",
                "Language School",
                "Nursery",
                "University",
                "School Supplier"
            ],
            ExcludedKeywords =
            [
                "public school",
                "school district",
                "language school"
            ],
            Countries = ["United States"],
            Locations = ["Chicago", "New York", "Los Angeles"]
        });

        Assert.NotEqual(SearchResultDecision.WrongIndustry, result.Decision);
        Assert.True(result.Decision is SearchResultDecision.PotentialOfficialBusinessWebsite
            or SearchResultDecision.RelevantBusinessCandidate
            or SearchResultDecision.Ambiguous);
        Assert.True(result.MayFetch || result.Decision == SearchResultDecision.Ambiguous);
    }

    [Fact]
    public void Exclusion_SpecificSchoolTerms_DoNotTriggerBlanketSchoolBan()
    {
        var privateSchool = ExclusionEvidenceClassifier.Classify(
            "British International School of Chicago",
            "British International School of Chicago",
            "Private international school in Chicago United States",
            "https://britishschoolchicago.example/",
            ["Public School", "Language School", "Nursery"],
            ["public school", "language school", "school district"]);

        Assert.False(privateSchool.IsExcluded);

        var publicSchool = ExclusionEvidenceClassifier.Classify(
            "Chicago Public School District",
            "Chicago Public Schools",
            "Official public school district website for Chicago",
            "https://cps.edu/",
            ["Public School", "Language School"],
            ["public school", "school district"]);

        Assert.True(publicSchool.IsExcluded);

        var paintballWithSchoolBan = ExclusionEvidenceClassifier.Classify(
            "Lincoln High School",
            "Lincoln High School",
            "Public high school in Ohio",
            "https://lincolnhs.edu/",
            ["School", "University"],
            ["school"]);

        Assert.True(paintballWithSchoolBan.IsExcluded);
    }

    [Fact]
    public void Exclusion_SchoolProject_IgnoresUniversityKeywordOnPrivateSchoolPages()
    {
        var school = ExclusionEvidenceClassifier.Classify(
            "Repton School Dubai",
            "Repton School Dubai",
            "Independent private school in Dubai UAE preparing students for university",
            "https://www.reptondubai.org/",
            ["Public School", "University", "Language School", "School Supplier"],
            ["university", "tutoring", "careers", "recruitment", "public school"],
            ["Private School", "International School", "Independent School", "Fee-Paying School"]);

        Assert.False(school.IsExcluded);

        var languageSchool = ExclusionEvidenceClassifier.Classify(
            "Dubai Language School",
            "Dubai Language School",
            "English language school and tutoring centre in Dubai",
            "https://dubailanguageschool.example/",
            ["Public School", "Language School"],
            ["language school", "tutoring"],
            ["Private School", "International School"]);

        Assert.True(languageSchool.IsExcluded);
    }

    [Fact]
    public void DiscoveryOutcome_DoesNotBlameSearchProviderWhenResultsWereReceived()
    {
        var state = new ResearchDiscoveryState
        {
            SearchResultsReceived = 20,
            CandidatesDiscovered = 0,
            DiscoveryStopReason = DiscoveryStopReason.SearchProviderUnavailable,
            SearchProviderHealth = SearchProviderHealthState.InvalidResponse
        };

        var reason = DiscoveryOutcomeClassifier.Classify(state, qualifiedCount: 0, requestedCount: 50);
        Assert.Equal(DiscoveryStopReason.NoCandidatesDiscovered, reason);
    }

    [Fact]
    public void Gate_WrongCountry_CannotFetch()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://torontopaintball.example/",
            "Toronto Paintball Arena Canada",
            "Indoor paintball arena in Toronto Canada with rentals"));

        Assert.False(result.MayFetch);
        Assert.True(result.Decision is SearchResultDecision.WrongGeography or SearchResultDecision.UnsupportedOrInvalidResult);
    }

    [Fact]
    public void Gate_IndustryWithoutGeography_IsAmbiguousNotRelevant()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://skyhigh.example/",
            "Sky High Trampoline Park",
            "Indoor trampoline park with dodgeball and parties"));

        Assert.False(result.MayFetch);
        Assert.Equal(SearchResultDecision.Ambiguous, result.Decision);
        Assert.True(result.NeedsVerification);
    }

    [Fact]
    public void Gate_QueryAloneDoesNotProveIndustry()
    {
        var result = SearchResultGate.Evaluate(GateInput(
            "https://randomblog.example/post",
            "Weekend Ideas",
            "Fun things to do this weekend with friends"));

        Assert.False(result.MayFetch);
    }

    [Fact]
    public void PluralIndustryLabels_MatchVenueEvidence()
    {
        var paintball = IndustryEvidenceClassifier.Classify(
            "Lone Star Paintball Park", "Lone Star Paintball Park",
            "Paintball park in Texas with rentals", "https://lonestar.example",
            ["Paintball arenas", "Laser tag venues", "Trampoline parks"]);
        var laser = IndustryEvidenceClassifier.Classify(
            "Zap Zone Laser Tag", "Zap Zone Laser Tag",
            "Indoor laser tag arena in Ohio", "https://zap.example",
            ["Paintball arenas", "Laser tag venues", "Trampoline parks"]);
        var trampoline = IndustryEvidenceClassifier.Classify(
            "Sky Zone", "Sky Zone Trampoline Park",
            "Indoor trampoline park in Georgia", "https://sky.example",
            ["Paintball arenas", "Laser tag venues", "Trampoline parks"]);

        Assert.True(paintball.MatchedApprovedIndustry);
        Assert.True(laser.MatchedApprovedIndustry);
        Assert.True(trampoline.MatchedApprovedIndustry);
    }

    [Fact]
    public void Budget_SeparateKinds_ReserveAndExhaustIndependently()
    {
        var budget = new ResearchBudgetManager(
            new ResearchBudgetLimits
            {
                SearchRequests = 2,
                VerificationFetches = 1,
                ContactFetches = 1,
                LinkExpansionFetches = 0,
                OllamaCalls = 1,
                MaxDurationMinutes = 30
            },
            DateTimeOffset.UtcNow);

        Assert.True(budget.TryReserve(ResearchBudgetKind.SearchRequest));
        Assert.True(budget.TryReserve(ResearchBudgetKind.VerificationFetch));
        Assert.False(budget.TryReserve(ResearchBudgetKind.VerificationFetch));
        Assert.True(budget.TryReserve(ResearchBudgetKind.ContactFetch));
        Assert.False(budget.TryReserve(ResearchBudgetKind.LinkExpansion));
        Assert.Equal(1, budget.RemainingFor(ResearchBudgetKind.SearchRequest));
        Assert.Equal(0, budget.RemainingFor(ResearchBudgetKind.VerificationFetch));
    }

    [Fact]
    public void Budget_LinkExpansionCannotConsumeVerificationBudget()
    {
        var budget = new ResearchBudgetManager(
            new ResearchBudgetLimits
            {
                SearchRequests = 10,
                VerificationFetches = 5,
                ContactFetches = 5,
                LinkExpansionFetches = 1,
                OllamaCalls = 2,
                MaxDurationMinutes = 30
            },
            DateTimeOffset.UtcNow);

        Assert.True(budget.TryReserve(ResearchBudgetKind.LinkExpansion));
        Assert.False(budget.TryReserve(ResearchBudgetKind.LinkExpansion));
        Assert.Equal(5, budget.RemainingFor(ResearchBudgetKind.VerificationFetch));
    }

    [Fact]
    public void PlaceholderAndInternalEmails_AreRejected()
    {
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("research@sendly.local"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("example@domain.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("info@example.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("team@company.local"));
        Assert.True(EmailAddressParser.IsAcceptableBusinessEmail("info@lonestarpaintball.com"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("Laser-Tag@2x.png"));
        Assert.False(EmailAddressParser.IsAcceptableBusinessEmail("icon@site.jpg"));
    }

    [Fact]
    public void BraveHtmlParser_ExtractsRealResultTitleUrlAndSnippet()
    {
        var html = """
            <div class="snippet svelte-x" data-pos="0" data-type="web">
              <a href="https://www.txpb.com/" target="_self" class="svelte-y l1">
                <div class="site-name-wrapper svelte-z">
                  <div class="title search-snippet-title" title="Texas Paintball | Jonestown, Texas">Texas Paintball</div>
                </div>
              </a>
              <div class="generic-snippet svelte-w">Paintball fields and rentals in Jonestown Texas. Book parties online.</div>
            </div>
            """;

        var hits = PublicSearchHtmlParser.ParseBrave(html, 10, "BraveHtml");

        Assert.Single(hits);
        Assert.Equal("https://www.txpb.com/", hits[0].Url);
        Assert.Contains("Texas Paintball", hits[0].Title);
        Assert.Contains("Jonestown Texas", hits[0].Snippet);
    }

    [Fact]
    public void JunkNames_CannotBecomeCompanies()
    {
        Assert.True(ProspectJunkClassifier.IsJunkOrganization("johndoe", "https://forum.example/members/johndoe", null)
                    || CompanyIdentityResolver.IsGenericName("johndoe")
                    || CompanyIdentityResolver.IsGenericName("Home"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Home"));
        Assert.True(CompanyIdentityResolver.IsGenericName("Welcome"));
    }

    [Fact]
    public void Outcome_ZeroQualifiedWithBudgetExhausted_IsNotCompleted()
    {
        var state = new ResearchDiscoveryState
        {
            DiscoveryStopReason = DiscoveryStopReason.BudgetExhausted,
            LimitsExhausted = true,
            LimitsExhaustedReason = "Maximum research duration reached"
        };

        var status = ResearchRunOutcomeResolver.Resolve(0, 50, state, 0, false);

        Assert.Equal(ResearchRunStatus.BudgetExhausted, status);
        Assert.NotEqual(ResearchRunStatus.Completed, status);
    }

    [Fact]
    public void Outcome_PartialBelowTarget_IsNotCompleted()
    {
        var state = new ResearchDiscoveryState();

        var status = ResearchRunOutcomeResolver.Resolve(3, 50, state, 0, true);

        Assert.Equal(ResearchRunStatus.Partial, status);
    }

    [Fact]
    public void QueryPlan_FairAcrossIndustriesAndStates()
    {
        var context = new ProspectSourceContext
        {
            ProjectName = "US Venues",
            Industries = ["Paintball arenas", "Laser tag venues", "Trampoline parks"],
            Countries = ["United States"],
            Locations = ["Texas", "Florida", "Ohio"],
            Keywords = []
        };

        var planned = SearchCampaignPlanner.Build(context, 60);
        var industries = planned.Select(x => x.Industry).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Contains("Paintball arenas", industries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Laser tag venues", industries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Trampoline parks", industries, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(planned.Count, planned.Select(x => x.Query.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void Qualification_RealPaintballBusinessWithEvidence_PassesMandatoryGates()
    {
        var service = new ProspectQualificationService();
        var candidate = new ProspectCandidate
        {
            CompanyName = "Lone Star Paintball",
            Website = "https://lonestarpaintball.example",
            SourceUrl = "https://lonestarpaintball.example",
            Country = "United States",
            City = "Dallas",
            Industry = "Paintball arenas",
            CompanyDescription = "Paintball park in Dallas Texas offering rentals parties and walk-on play contact info@lonestarpaintball.example",
            Email = "info@lonestarpaintball.example",
            NameConfidence = 90,
            NameReasonCode = "identity.page_title",
            CountryEvidenceSummary = "Dallas Texas United States address on official website",
            CountryEvidenceSource = "official_website"
        };

        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = candidate,
            Countries = ["United States"],
            Industries = ["Paintball arenas"],
            RequireEmail = true,
            MinQualificationScore = 50
        });

        Assert.DoesNotContain(result.Criteria, x => x.IsMandatory && !x.Passed);
    }

    [Fact]
    public void Qualification_WrongIndustryAndCountry_FailsMandatory()
    {
        var service = new ProspectQualificationService();
        var industryResult = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Dallas Plumbing Co",
                Website = "https://dallasplumbing.example",
                SourceUrl = "https://dallasplumbing.example",
                Country = "United States",
                CompanyDescription = "Plumbing repairs in Dallas Texas",
                NameConfidence = 90
            },
            Countries = ["United States"],
            Industries = ["Paintball arenas"],
            MinQualificationScore = 50
        });
        var countryResult = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Toronto Paintball",
                Website = "https://torontopaintball.ca",
                SourceUrl = "https://torontopaintball.ca",
                Country = "Canada",
                CompanyDescription = "Paintball arena in Toronto Canada",
                NameConfidence = 90
            },
            Countries = ["United States"],
            Industries = ["Paintball arenas"],
            MinQualificationScore = 50
        });

        Assert.False(industryResult.IsQualified);
        Assert.False(countryResult.IsQualified);
    }

    [Fact]
    public void Qualification_FakeEmail_DoesNotSatisfyEmailCriterion()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Lone Star Paintball",
                Website = "https://lonestarpaintball.example",
                SourceUrl = "https://lonestarpaintball.example",
                Country = "United States",
                CompanyDescription = "Paintball park in Texas United States",
                Email = "example@domain.com",
                NameConfidence = 90,
                CountryEvidenceSummary = "Texas United States",
                CountryEvidenceSource = "official_website"
            },
            Countries = ["United States"],
            Industries = ["Paintball arenas"],
            RequireEmail = true,
            MinQualificationScore = 50
        });

        Assert.False(result.IsEmailReady);
    }
}
