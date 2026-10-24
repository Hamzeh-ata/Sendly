using System.Net;
using Sendly.Application.Abstractions;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Sendly.Infrastructure.Research;

namespace Sendly.Application.Tests;

public class ZeroBudgetDiscoveryTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, "<html><body>results</body></html>", false, HttpAccessResponseKind.ValidResults)]
    [InlineData(HttpStatusCode.OK, "", false, HttpAccessResponseKind.InvalidHtml)]
    [InlineData(HttpStatusCode.Forbidden, "forbidden", false, HttpAccessResponseKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, "slow down", false, HttpAccessResponseKind.RateLimited)]
    [InlineData(HttpStatusCode.OK, "please complete the captcha challenge", false, HttpAccessResponseKind.CaptchaChallenge)]
    [InlineData(HttpStatusCode.OK, "checking your browser before you continue", false, HttpAccessResponseKind.BotVerification)]
    [InlineData(HttpStatusCode.OK, "Accept all cookies to continue", false, HttpAccessResponseKind.ConsentInterstitial)]
    [InlineData(HttpStatusCode.Unauthorized, "please log in", false, HttpAccessResponseKind.LoginRequired)]
    public void Classifier_DistinguishesAccessOutcomes(
        HttpStatusCode status,
        string body,
        bool timedOut,
        HttpAccessResponseKind expected)
    {
        var result = HttpAccessResponseClassifier.Classify(status, body, timedOut);
        Assert.Equal(expected, result.Kind);
    }

    [Fact]
    public void Classifier_DoesNotTreatChallengeHtmlAsValidResults()
    {
        var result = HttpAccessResponseClassifier.Classify(
            HttpStatusCode.OK,
            "<html>cf-challenge verify you are human</html>",
            parsedResultCount: 5);
        Assert.True(result.IsChallenge);
        Assert.NotEqual(HttpAccessResponseKind.ValidResults, result.Kind);
    }

    [Fact]
    public void ChallengedSourceRegistry_DisablesAndSkipsRetries()
    {
        var registry = new ChallengedSourceRegistry();
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        registry.RecordChallenge(
            "HtmlSearch:MojeekHtml",
            HttpAccessResponseKind.CaptchaChallenge,
            "blocked",
            now,
            403);

        Assert.True(registry.IsTemporarilyDisabled("HtmlSearch:MojeekHtml", now.AddMinutes(1)));
        Assert.False(registry.IsTemporarilyDisabled("HtmlSearch:BraveHtml", now.AddMinutes(1)));
        Assert.Contains(registry.ListDisabledKeys(now.AddMinutes(1)), x => x == "HtmlSearch:MojeekHtml");

        registry.RecordSuccess("HtmlSearch:MojeekHtml");
        Assert.False(registry.IsTemporarilyDisabled("HtmlSearch:MojeekHtml", now.AddMinutes(1)));
    }

    [Fact]
    public void DiscoveryOutcome_AllSourcesUnavailable_ReportsSearchProviderFailureOnlyWhenNoResults()
    {
        var noResults = new ResearchDiscoveryState
        {
            SearchResultsReceived = 0,
            CandidatesDiscovered = 0,
            SearchProviderHealth = SearchProviderHealthState.InvalidResponse,
            DiscoveryStopReason = DiscoveryStopReason.SearchProviderUnavailable
        };
        Assert.Equal(
            DiscoveryStopReason.SearchProviderUnavailable,
            DiscoveryOutcomeClassifier.Classify(noResults, 0, 50));

        var withResults = new ResearchDiscoveryState
        {
            SearchResultsReceived = 12,
            CandidatesDiscovered = 0,
            SearchProviderHealth = SearchProviderHealthState.InvalidResponse,
            DiscoveryStopReason = DiscoveryStopReason.SearchProviderUnavailable
        };
        Assert.Equal(
            DiscoveryStopReason.NoCandidatesDiscovered,
            DiscoveryOutcomeClassifier.Classify(withResults, 0, 50));
    }

    [Fact]
    public void RunStatus_IsNotCompletedWhenQualifiedBelowTarget()
    {
        var state = new ResearchDiscoveryState { LimitsExhausted = true };
        var status = ResearchRunOutcomeResolver.Resolve(
            qualifiedCount: 3,
            requestedCount: 50,
            state,
            providerFailureCount: 1,
            allSourcesCompleted: true);
        Assert.NotEqual(ResearchRunStatus.Completed, status);
        Assert.True(status is ResearchRunStatus.Partial or ResearchRunStatus.BudgetExhausted);
    }

    [Fact]
    public void RunStatus_CompletedOnlyWhenTargetReached()
    {
        var status = ResearchRunOutcomeResolver.Resolve(
            qualifiedCount: 50,
            requestedCount: 50,
            new ResearchDiscoveryState(),
            providerFailureCount: 0,
            allSourcesCompleted: true);
        Assert.Equal(ResearchRunStatus.Completed, status);
    }

    [Fact]
    public void Continuation_SearchProviderUnavailable_IsSearchOnlyLimit()
    {
        Assert.True(ResearchRunContinuationPolicy.IsSearchOnlyLimitReason("Search provider unavailable"));
    }

    [Fact]
    public void Gate_RejectsUnrelatedIndustry_ForZeroBudgetPath()
    {
        var result = SearchResultGate.Evaluate(new SearchResultGateInput
        {
            Title = "Dallas Plumbing Company",
            Snippet = "Plumbing repairs in Dallas Texas",
            Url = "https://dallasplumbing.example/",
            Query = "private schools UAE",
            QueryIndustry = "Private School",
            Industries = ["Private School", "International School"],
            Countries = ["United Arab Emirates"]
        });
        Assert.Equal(SearchResultDecision.WrongIndustry, result.Decision);
        Assert.False(result.MayFetch);
    }
}
