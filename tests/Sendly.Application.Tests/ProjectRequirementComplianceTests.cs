using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Domain.Services;

namespace Sendly.Application.Tests;

public class ProjectRequirementComplianceTests
{
    private static ResearchSpecification UaeActivitySpec()
        => new()
        {
            ApprovedIndustries = ["Paintball arenas", "Laser tag venues", "Trampoline parks"],
            IndustrySynonyms = ["paintball", "laser tag", "trampoline"],
            ExcludedIndustries = ["School", "Gaming cafe", "Software"],
            ExcludedKeywords = ["school", "gaming cafe"],
            TargetCountries = ["United Arab Emirates"],
            Cities = ["Dubai", "Abu Dhabi", "Sharjah"],
            GeographicAliases = ["UAE", "Dubai", "Abu Dhabi"],
            RequireOfficialWebsite = true,
            RequirePublicBusinessEmail = true,
            IsValid = true
        };

    [Fact]
    public void UAE_Only_Project_Does_Not_Qualify_USA_Only_Business()
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
                CompanyDescription = "Paintball park in Dallas Texas United States",
                CountryEvidenceSummary = "Dallas Texas United States",
                NameConfidence = 90
            },
            Countries = ["United Arab Emirates"],
            Industries = ["Paintball arenas"],
            MinQualificationScore = 50
        });
        Assert.False(result.IsQualified);
        Assert.Contains(result.Criteria, x => x.CriterionKey == "targetCountry" && !x.Passed);
    }

    [Fact]
    public void Paintball_Project_Rejects_Software_Vendor_For_Paintball_Venues()
    {
        var spec = UaeActivitySpec();
        spec.ExcludedIndustries = ["School", "Gaming cafe"];
        var decision = ProjectEligibilityPolicy.EvaluateCandidateEvidence(
            "ArenaBook CRM",
            "Booking software and SaaS platform for paintball arenas and trampoline parks",
            "https://arenabook.example",
            "Software for paintball operators",
            "Dubai United Arab Emirates",
            spec);
        Assert.False(decision.IsEligible);
        Assert.Equal(ResearchCandidateDisposition.RejectedVendorNotOperator, decision.Disposition);
    }

    [Fact]
    public void Explicit_Exclusion_Wins_Over_General_Keyword()
    {
        var hit = ProjectEligibilityPolicy.EvaluateSearchHit(
            new SearchResultGateInput
            {
                Title = "Dubai Gaming Cafe - VR and PC Gaming",
                Snippet = "Gaming cafe with trampoline party room in Dubai UAE",
                Url = "https://dubaigamingcafe.example/",
                Query = "trampoline parks Dubai UAE",
                QueryIndustry = "Trampoline parks",
                Industries = UaeActivitySpec().ApprovedIndustries,
                ExcludedIndustries = UaeActivitySpec().ExcludedIndustries,
                ExcludedKeywords = UaeActivitySpec().ExcludedKeywords,
                Countries = UaeActivitySpec().TargetCountries,
                Locations = UaeActivitySpec().Cities
            },
            UaeActivitySpec());
        Assert.False(hit.IsEligible);
        Assert.True(hit.Disposition is ResearchCandidateDisposition.RejectedExcludedCategory
            or ResearchCandidateDisposition.RejectedBeforeFetch
            or ResearchCandidateDisposition.RejectedWrongIndustry);
    }

    [Fact]
    public void Valid_Synonym_Can_Match_Approved_Industry()
    {
        var classification = IndustryEvidenceClassifier.Classify(
            "Sky High Dubai",
            "Sky High Dubai",
            "Indoor trampoline park with parties in Dubai UAE",
            null,
            ["Trampoline parks"],
            ["School"]);
        Assert.True(classification.MatchedApprovedIndustry);
    }

    [Fact]
    public void Directory_Is_Not_Saved_As_Target_Operator()
    {
        var result = SearchResultGate.Evaluate(new SearchResultGateInput
        {
            Title = "Best paintball arenas in UAE - directory",
            Snippet = "Listing of paintball venues across Dubai and Abu Dhabi",
            Url = "https://yellowpages.ae/paintball",
            Query = "paintball arenas UAE",
            QueryIndustry = "Paintball arenas",
            Industries = ["Paintball arenas"],
            Countries = ["United Arab Emirates"],
            Locations = ["Dubai"]
        });
        Assert.True(result.IsDirectory || result.Decision == SearchResultDecision.PotentialDirectory);
        Assert.NotEqual(SearchResultDecision.PotentialOfficialBusinessWebsite, result.Decision);
    }

    [Fact]
    public void Query_For_One_Industry_Does_Not_Drift()
    {
        var invalid = ProjectEligibilityPolicy.ValidateQuery(
            "private schools in Dubai UAE",
            UaeActivitySpec(),
            plannedIndustry: "Paintball arenas",
            plannedCountry: "United Arab Emirates");
        Assert.False(invalid.IsValid);

        var valid = ProjectEligibilityPolicy.ValidateQuery(
            "paintball arenas in Dubai UAE",
            UaeActivitySpec(),
            plannedIndustry: "Paintball arenas",
            plannedCountry: "United Arab Emirates");
        Assert.True(valid.IsValid);
    }

    [Fact]
    public void Ollama_Cannot_Override_Configured_Country_Or_Industry()
    {
        var conflicts = ProjectEligibilityPolicy.AiClaimConflictsWithHardConstraints(
            UaeActivitySpec(),
            claimedIndustry: "Private School",
            claimedCountry: "United States",
            claimedRelevant: true);
        Assert.True(conflicts);

        var ok = ProjectEligibilityPolicy.AiClaimConflictsWithHardConstraints(
            UaeActivitySpec(),
            claimedIndustry: "Paintball arenas",
            claimedCountry: "United Arab Emirates",
            claimedRelevant: true);
        Assert.False(ok);
    }

    [Fact]
    public void Uncertain_Country_Is_Not_Verified()
    {
        var decision = ProjectEligibilityPolicy.EvaluateCandidateEvidence(
            "Desert Paintball",
            "Indoor paintball arena for parties",
            "https://desertpaintball.example",
            "Fun activity venue",
            countryEvidence: null,
            UaeActivitySpec());
        Assert.False(decision.IsEligible);
        Assert.Equal("eligibility.country_unverified", decision.ReasonCode);
    }

    [Fact]
    public void Missing_Email_Does_Not_Make_Candidate_Unrelated()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Dubai Paintball Arena",
                Website = "https://dubaipaintball.example",
                SourceUrl = "https://dubaipaintball.example",
                Country = "United Arab Emirates",
                CompanyDescription = "Paintball arena in Dubai United Arab Emirates",
                CountryEvidenceSummary = "Dubai United Arab Emirates",
                NameConfidence = 90
            },
            Countries = ["United Arab Emirates"],
            Industries = ["Paintball arenas"],
            RequireEmail = true,
            MinQualificationScore = 50
        });
        Assert.DoesNotContain(result.Criteria, x => x.CriterionKey == "targetIndustry" && !x.Passed);
        Assert.DoesNotContain(result.Criteria, x => x.CriterionKey == "vendorNotOperator" && !x.Passed);
        Assert.False(result.IsEmailReady);
    }

    [Fact]
    public void Irrelevant_Result_Rejected_Before_Fetch()
    {
        var decision = ProjectEligibilityPolicy.EvaluateSearchHit(
            new SearchResultGateInput
            {
                Title = "Dallas Plumbing Company Texas",
                Snippet = "Plumbing repairs and water heaters",
                Url = "https://dallasplumbing.example/",
                Query = "paintball arenas Dubai UAE",
                QueryIndustry = "Paintball arenas",
                Industries = UaeActivitySpec().ApprovedIndustries,
                ExcludedIndustries = UaeActivitySpec().ExcludedIndustries,
                Countries = UaeActivitySpec().TargetCountries,
                Locations = UaeActivitySpec().Cities
            },
            UaeActivitySpec());
        Assert.False(decision.MayFetch);
        Assert.False(decision.IsEligible);
    }

    [Fact]
    public void Blocked_Provider_Does_Not_Globally_Stop_Other_Sources()
    {
        Assert.True(ResearchRunContinuationPolicy.IsSearchOnlyLimitReason("Search provider unavailable"));
        var state = new ResearchDiscoveryState
        {
            SearchLimitsExhausted = true,
            LimitsExhausted = false,
            UsefulPendingUrls = 3,
            PagesQueued = 2
        };
        Assert.True(ResearchRunContinuationPolicy.HasEligibleIndependentCrawlWork(state));
    }

    [Fact]
    public void Qualified_Counts_Exclude_Raw_And_Rejected()
    {
        var status = ResearchRunOutcomeResolver.Resolve(
            qualifiedCount: 2,
            requestedCount: 50,
            new ResearchDiscoveryState { CandidatesDiscovered = 40, RejectionReasons = { ["WrongIndustry"] = 20 } },
            providerFailureCount: 0,
            allSourcesCompleted: true);
        Assert.NotEqual(ResearchRunStatus.Completed, status);
    }

    [Fact]
    public void Planned_Query_Retains_Project_And_Version()
    {
        var planned = new PlannedSearchQuery
        {
            Query = "paintball arenas Dubai UAE",
            Industry = "Paintball arenas",
            Country = "United Arab Emirates",
            ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ResearchQueryVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222")
        };
        Assert.NotNull(planned.ProjectId);
        Assert.NotNull(planned.ResearchQueryVersionId);
    }

    [Fact]
    public void Coverage_Identifies_Missing_Combinations()
    {
        var state = new ResearchDiscoveryState
        {
            PlannedSearchQueries =
            [
                new PlannedSearchQuery { Query = "paintball arenas Dubai UAE", Industry = "Paintball arenas", Country = "United Arab Emirates" },
                new PlannedSearchQuery { Query = "laser tag Dubai UAE", Industry = "Laser tag venues", Country = "United Arab Emirates" },
                new PlannedSearchQuery { Query = "trampoline parks Dubai UAE", Industry = "Trampoline parks", Country = "United Arab Emirates" }
            ]
        };
        state.QueriesExecutedByIndustry["Paintball arenas"] = 3;
        state.RelevantHitsByIndustry["Paintball arenas"] = 2;
        var under = state.ResolveUnderCoveredIndustries();
        Assert.Contains("Laser tag venues", under);
        Assert.Contains("Trampoline parks", under);
    }

    [Fact]
    public void No_Invented_Contact_Is_Accepted_As_Verified()
    {
        var service = new ProspectQualificationService();
        var result = service.Evaluate(new QualificationEvaluationRequest
        {
            Candidate = new ProspectCandidate
            {
                CompanyName = "Dubai Paintball Arena",
                Website = "https://dubaipaintball.example",
                Email = "example@domain.com",
                Country = "United Arab Emirates",
                CompanyDescription = "Paintball arena Dubai UAE",
                CountryEvidenceSummary = "Dubai United Arab Emirates",
                NameConfidence = 90,
                ContactAttributionState = ContactAttributionState.Rejected,
                ContactAttributionReasonCode = "contact.invented"
            },
            Countries = ["United Arab Emirates"],
            Industries = ["Paintball arenas"],
            RequireEmail = true,
            MinQualificationScore = 50
        });
        Assert.False(result.IsEmailReady);
    }
}
