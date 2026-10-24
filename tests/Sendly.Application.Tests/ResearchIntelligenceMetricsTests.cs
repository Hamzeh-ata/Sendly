using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class ResearchIntelligenceMetricsTests
{
    [Fact]
    public void Compute_Tracks_Quality_Rates_From_Run_State()
    {
        var state = new ResearchDiscoveryState
        {
            SearchRequestsSuccessful = 40,
            SearchRequestsFailed = 10,
            PagesFetched = 20,
            PagesClassifiedRelevant = 10,
            ProspectsSaved = 5,
            IdentityVerifiedBusinesses = 4,
            CandidatesDiscovered = 20,
            BusinessesRejected = 6,
            IrrelevantEntitiesRejected = 2,
            ProspectsSkippedAsDuplicates = 3,
            CompaniesIdentified = 8,
            ContactableProspects = 4,
            AdaptiveActionsAccepted = 7,
            AdaptiveActionsRejected = 3,
            OllamaCallsUsed = 9
        };
        state.LowYieldQueryKeys.Add("Gyms|discover_business");
        state.PlannedSearchQueries.Add(new PlannedSearchQuery
        {
            Query = "gyms Dubai United Arab Emirates",
            Industry = "Gyms",
            Country = "United Arab Emirates",
            Location = "Dubai"
        });
        state.PlannedSearchQueries.Add(new PlannedSearchQuery
        {
            Query = "gyms Abu Dhabi United Arab Emirates",
            Industry = "Gyms",
            Country = "United Arab Emirates",
            Location = "Abu Dhabi"
        });
        state.ExecutedSearchQueryKeys.Add(SearchCampaignPlanner.BuildKey(state.PlannedSearchQueries[0]));
        state.RelevantHitsByIndustry["Gyms"] = 2;
        state.FetchFailureReasons["captcha_challenge"] = 2;

        var metrics = ResearchIntelligenceMetrics.Compute(state);

        Assert.Equal(10, metrics.QualifiedBusinessesPer100SearchRequests);
        Assert.Equal(20, metrics.VerifiedBusinessesPer100FetchedPages);
        Assert.Equal(50, metrics.RelevantFetchedPagePercent);
        Assert.Equal(40, metrics.FalsePositiveRate);
        Assert.Equal(15, metrics.DuplicateRate);
        Assert.Equal(50, metrics.PublicContactDiscoveryRate);
        Assert.Equal(100, metrics.IndustryCoveragePercent);
        Assert.Equal(1, metrics.LowYieldQueriesAvoided);
        Assert.Equal(2, metrics.SourceChallengeCount);
        Assert.Equal(7, metrics.AdaptiveActionsAccepted);
        Assert.Equal(3, metrics.AdaptiveActionsRejected);
        Assert.Equal(9, metrics.OllamaCallsUsed);
    }

    [Fact]
    public void BuildCompactOutcomeSummary_Uses_Observed_Facts_Only()
    {
        var state = new ResearchDiscoveryState
        {
            SearchQueriesExecuted = 12,
            SearchRequestsSuccessful = 10,
            IrrelevantEntitiesRejected = 4,
            IdentityVerifiedBusinesses = 3,
            ProspectsSaved = 2,
            ProspectsSkippedAsDuplicates = 1
        };
        state.AbandonedSearchPatterns.Add("software for venues");
        var summary = ResearchIntelligenceMetrics.BuildCompactOutcomeSummary(
            state,
            new ResearchSpecification
            {
                ApprovedIndustries = ["Entertainment venues"],
                TargetCountries = ["United States"],
                Cities = ["Austin", "Dallas"]
            });

        Assert.Contains("queriesExecuted=12", summary);
        Assert.Contains("qualified=2", summary);
        Assert.Contains("abandonedPatterns=software for venues", summary);
        Assert.Contains("underCoveredLocations=Austin|Dallas", summary);
    }

    [Fact]
    public void AiClaimConflictsWithHardConstraints_Blocks_Wrong_Country_Claim()
    {
        var spec = new ResearchSpecification
        {
            ApprovedIndustries = ["Cleaning companies"],
            TargetCountries = ["Canada"],
            IsValid = true
        };

        Assert.True(ProjectEligibilityPolicy.AiClaimConflictsWithHardConstraints(
            spec,
            "Cleaning companies",
            "United States",
            claimedRelevant: true));
        Assert.False(ProjectEligibilityPolicy.AiClaimConflictsWithHardConstraints(
            spec,
            "Cleaning companies",
            "Canada",
            claimedRelevant: true));
    }
}
