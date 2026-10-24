using Microsoft.Extensions.Logging.Abstractions;
using Sendly.Application.Abstractions;
using Sendly.Application.Services;

namespace Sendly.Application.Tests;

public class AdaptiveResearchPlannerTests
{
    [Fact]
    public void ValidateAction_Rejects_Excluded_Industry_Query()
    {
        var spec = BuildGymSpec();
        var state = new ResearchDiscoveryState();
        var action = AdaptiveResearchPlanner.ValidateAction(
            new ResearchActionProposal
            {
                ActionType = ResearchActionTypes.ExactIndustryLocationSearch,
                Query = "private schools in Dubai United Arab Emirates",
                Purpose = "discover schools",
                TargetIndustry = "School",
                Geography = "United Arab Emirates",
                Reason = "wrong industry",
                Priority = 80
            },
            spec,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WebSearch" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            state);

        Assert.False(action.IsAccepted);
        Assert.Equal("query.industry_mismatch", action.RejectionReason);
    }

    [Fact]
    public void ValidateAction_Accepts_Project_Aligned_Query()
    {
        var spec = BuildGymSpec();
        var state = new ResearchDiscoveryState();
        var action = AdaptiveResearchPlanner.ValidateAction(
            new ResearchActionProposal
            {
                ActionType = ResearchActionTypes.ExactIndustryLocationSearch,
                Query = "fitness gyms in Dubai United Arab Emirates",
                Purpose = "discover gyms",
                TargetIndustry = "Gyms",
                Geography = "United Arab Emirates",
                Reason = "cover Dubai gyms",
                Priority = 70,
                PreferredSource = "WebSearch"
            },
            spec,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WebSearch" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            state);

        Assert.True(action.IsAccepted);
        Assert.Equal("Gyms", action.TargetIndustry);
        Assert.Contains(action.AddressesRequirements, x => x.StartsWith("industry:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateAction_Rejects_Abandoned_Pattern()
    {
        var spec = BuildGymSpec();
        var state = new ResearchDiscoveryState();
        state.AbandonedSearchPatterns.Add("software for gyms");
        var action = AdaptiveResearchPlanner.ValidateAction(
            new ResearchActionProposal
            {
                ActionType = ResearchActionTypes.AlternativeTerminologySearch,
                Query = "software for gyms Dubai United Arab Emirates",
                Purpose = "vendor search",
                TargetIndustry = "Gyms",
                Geography = "United Arab Emirates",
                Reason = "repeat low yield",
                Priority = 40
            },
            spec,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WebSearch" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            state);

        Assert.False(action.IsAccepted);
        Assert.Equal("AbandonedPattern", action.RejectionReason);
    }

    [Fact]
    public void ValidateAndAcceptActions_Different_Projects_Keep_Distinct_Strategies()
    {
        var gymSpec = BuildGymSpec();
        var laundrySpec = BuildLaundrySpec();
        var gymState = new ResearchDiscoveryState();
        var laundryState = new ResearchDiscoveryState();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WebSearch", "DirectoryCatalog" };

        var gymAccepted = AdaptiveResearchPlanner.ValidateAndAcceptActions(
            [
                new ResearchActionProposal
                {
                    ActionType = ResearchActionTypes.DirectoryListingSearch,
                    Query = "gym directory Dubai United Arab Emirates",
                    Purpose = "directory",
                    TargetIndustry = "Gyms",
                    Geography = "Dubai United Arab Emirates",
                    Reason = "directories for gyms",
                    Priority = 80,
                    PreferredSource = "DirectoryCatalog"
                }
            ],
            gymSpec,
            AdaptiveResearchPlanner.BuildDefaultSourceCatalog(true, true, true, true, true, true, true),
            gymState,
            4);

        var laundryAccepted = AdaptiveResearchPlanner.ValidateAndAcceptActions(
            [
                new ResearchActionProposal
                {
                    ActionType = ResearchActionTypes.LocalLanguageSearch,
                    Query = "laundromats Dubai United Arab Emirates",
                    Purpose = "local language",
                    TargetIndustry = "Laundromats",
                    Geography = "United Arab Emirates",
                    Reason = "laundry coverage",
                    Priority = 75
                },
                new ResearchActionProposal
                {
                    ActionType = ResearchActionTypes.ExactIndustryLocationSearch,
                    Query = "gyms Dubai United Arab Emirates",
                    Purpose = "wrong project industry",
                    TargetIndustry = "Gyms",
                    Geography = "United Arab Emirates",
                    Reason = "should reject for laundry project",
                    Priority = 20
                }
            ],
            laundrySpec,
            AdaptiveResearchPlanner.BuildDefaultSourceCatalog(true, true, true, true, true, true, true),
            laundryState,
            4);

        Assert.Single(gymAccepted);
        Assert.Equal(ResearchActionTypes.DirectoryListingSearch, gymAccepted[0].ActionType);
        Assert.Single(laundryAccepted);
        Assert.Equal(ResearchActionTypes.LocalLanguageSearch, laundryAccepted[0].ActionType);
        Assert.True(laundryState.AdaptiveActionsRejected >= 1);
        Assert.NotEqual(gymAccepted[0].Query, laundryAccepted[0].Query);
        _ = sources;
    }

    [Fact]
    public void ConvertToPlannedQueries_Maps_Action_Metadata()
    {
        var spec = BuildGymSpec();
        var context = new ProspectSourceContext
        {
            ProjectId = Guid.NewGuid(),
            ProjectName = "UAE Gyms",
            Industries = ["Gyms"],
            Countries = ["United Arab Emirates"],
            Locations = ["Dubai"]
        };
        var planned = AdaptiveResearchPlanner.ConvertToPlannedQueries(
            [
                new ResearchActionProposal
                {
                    ActionType = ResearchActionTypes.CoverageGapSearch,
                    Query = "gyms in Sharjah United Arab Emirates",
                    Purpose = "cover Sharjah",
                    TargetIndustry = "Gyms",
                    Geography = "Sharjah United Arab Emirates",
                    ExpectedResult = "operating_business",
                    Reason = "Sharjah under-covered",
                    IsAccepted = true,
                    AddressesRequirements = ["industry:Gyms", "geo:Sharjah"]
                }
            ],
            context,
            spec);

        Assert.Single(planned);
        Assert.Equal(SearchQueryIntents.DiscoverBusiness, planned[0].Intent);
        Assert.Equal("cover Sharjah", planned[0].Purpose);
        Assert.Equal("Gyms", planned[0].Industry);
    }

    [Fact]
    public async Task PlanAsync_Uses_Stub_Intelligence_And_Enforces_Budget()
    {
        var intelligence = new StubIntelligence(
            new ResearchPlanResult
            {
                IsValid = true,
                StrategySummary = "Prioritize Dubai gym directories then official websites.",
                AbandonedPatterns = ["software for gyms"],
                Actions =
                [
                    new ResearchActionProposal
                    {
                        ActionType = ResearchActionTypes.ExactIndustryLocationSearch,
                        Query = "gyms Dubai United Arab Emirates",
                        Purpose = "primary search",
                        TargetIndustry = "Gyms",
                        Geography = "United Arab Emirates",
                        Reason = "core coverage",
                        Priority = 90
                    },
                    new ResearchActionProposal
                    {
                        ActionType = ResearchActionTypes.ExactIndustryLocationSearch,
                        Query = "private schools Dubai United Arab Emirates",
                        Purpose = "wrong",
                        TargetIndustry = "School",
                        Geography = "United Arab Emirates",
                        Reason = "should reject",
                        Priority = 10
                    }
                ]
            });
        var planner = new AdaptiveResearchPlanner(NullLogger<AdaptiveResearchPlanner>.Instance, intelligence);
        var state = new ResearchDiscoveryState();
        var context = new ProspectSourceContext
        {
            ProjectName = "UAE Gyms",
            Industries = ["Gyms"],
            Countries = ["United Arab Emirates"],
            Locations = ["Dubai"],
            Specification = BuildGymSpec(),
            DiscoveryState = state
        };

        var planned = await planner.PlanAsync(
            context,
            state,
            AdaptiveResearchPlanner.BuildDefaultSourceCatalog(true, true, true, true, true, true, true),
            maxActions: 4,
            isFollowUp: true,
            remainingOllamaCalls: 3,
            remainingSearchRequests: 20,
            CancellationToken.None);

        Assert.Single(planned);
        Assert.Equal(1, state.AdaptiveActionsAccepted);
        Assert.True(state.AdaptiveActionsRejected >= 1);
        Assert.Contains("software for gyms", state.AbandonedSearchPatterns);
        Assert.Equal(1, state.OllamaCallsUsed);
        Assert.Equal("Prioritize Dubai gym directories then official websites.", state.LastAdaptiveStrategySummary);
    }

    [Fact]
    public async Task PlanAsync_Returns_Empty_When_Budget_Exhausted()
    {
        var intelligence = new StubIntelligence(new ResearchPlanResult { IsValid = true, Actions = [] });
        var planner = new AdaptiveResearchPlanner(NullLogger<AdaptiveResearchPlanner>.Instance, intelligence);
        var state = new ResearchDiscoveryState();
        var context = new ProspectSourceContext
        {
            ProjectName = "UAE Gyms",
            Industries = ["Gyms"],
            Countries = ["United Arab Emirates"],
            Specification = BuildGymSpec(),
            DiscoveryState = state
        };

        var planned = await planner.PlanAsync(
            context,
            state,
            AdaptiveResearchPlanner.BuildDefaultSourceCatalog(true, true, true, true, true, true, true),
            maxActions: 4,
            isFollowUp: true,
            remainingOllamaCalls: 0,
            remainingSearchRequests: 20,
            CancellationToken.None);

        Assert.Empty(planned);
        Assert.Equal(0, state.OllamaCallsUsed);
    }

    private static ResearchSpecification BuildGymSpec()
        => new()
        {
            ApprovedIndustries = ["Gyms", "Fitness centers"],
            IndustrySynonyms = ["gym", "fitness"],
            ExcludedIndustries = ["School", "Software"],
            ExcludedKeywords = ["school", "software"],
            TargetCountries = ["United Arab Emirates"],
            Cities = ["Dubai", "Abu Dhabi", "Sharjah"],
            GeographicAliases = ["UAE", "Dubai"],
            RequireOfficialWebsite = true,
            IsValid = true
        };

    private static ResearchSpecification BuildLaundrySpec()
        => new()
        {
            ApprovedIndustries = ["Laundromats", "Dry cleaners"],
            IndustrySynonyms = ["laundry", "مغسلة"],
            ExcludedIndustries = ["School"],
            TargetCountries = ["United Arab Emirates"],
            Cities = ["Dubai"],
            LanguageHints = ["ar"],
            IsValid = true
        };

    private sealed class StubIntelligence : ILocalAiResearchIntelligence
    {
        private readonly ResearchPlanResult _plan;

        public StubIntelligence(ResearchPlanResult plan)
        {
            _plan = plan;
        }

        public Task<ResearchPlanResult> ProposeResearchPlanAsync(
            ResearchPlanRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(_plan);

        public Task<AiBatchSearchClassificationResult> ClassifySearchResultsBatchAsync(
            AiBatchSearchClassificationRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new AiBatchSearchClassificationResult());

        public Task<AiEvidenceExtractionResult> ExtractBusinessEvidenceAsync(
            AiEvidenceExtractionRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult(new AiEvidenceExtractionResult());
    }
}
