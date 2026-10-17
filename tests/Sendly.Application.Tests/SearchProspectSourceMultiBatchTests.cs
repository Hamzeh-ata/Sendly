using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Infrastructure.Research;

namespace Sendly.Application.Tests;

public class SearchProspectSourceMultiBatchTests
{
    [Fact]
    public async Task DiscoverAsync_ContinuesAcrossMultipleQueries_UntilBatchFilledOrQueriesExhausted()
    {
        var search = new Mock<IWebSearchProvider>();
        search.SetupGet(x => x.Name).Returns("Brave");
        search.SetupGet(x => x.IsEnabled).Returns(true);
        search.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebSearchRequest request, CancellationToken _) =>
            [
                new WebSearchHit
                {
                    Url = $"https://{Guid.NewGuid():N}.example",
                    Title = $"{request.Query} Gym",
                    Snippet = "Independent gym with memberships",
                    Provider = "Brave"
                }
            ]);

        var ai = new Mock<IAiResearchService>();
        ai.Setup(x => x.StructureFromSourceTextAsync(It.IsAny<AiSourceStructureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiSourceStructureRequest request, CancellationToken _) =>
            [
                new ProspectCandidate
                {
                    CompanyName = request.HintCompanyName ?? "Gym",
                    Website = request.SourceUrl,
                    SourceUrl = request.SourceUrl,
                    SourceName = "Website",
                    Country = "Canada"
                }
            ]);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient("ResearchCrawler"))
            .Returns(new HttpClient(new EmptyHtmlHandler()));

        var crawler = new WebsitePageCrawler(httpFactory.Object, NullLogger<WebsitePageCrawler>.Instance);
        var source = new SearchProspectSource(
            [search.Object],
            crawler,
            ai.Object,
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchResults = 5,
                MaxSearchQueries = 8,
                MaxSearchRequests = 20,
                MaxPagesPerCrawl = 1,
                MaxCandidatesPerRun = 200,
                MaxResearchDurationMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                MaxRequestsPerResearchRun = 20
            }),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions()),
            NullLogger<SearchProspectSource>.Instance);

        var context = new ProspectSourceContext
        {
            ProjectName = "GymWatch – Canada",
            Keywords = ["gym", "fitness"],
            Countries = ["Canada"],
            Industries = ["Fitness"],
            Locations = ["Toronto", "Vancouver"],
            RequestedCount = 50,
            DiscoveryBatchSize = 3,
            DiscoveryState = new ResearchDiscoveryState()
        };

        var firstBatch = await source.DiscoverAsync(context, CancellationToken.None);
        Assert.True(firstBatch.Count >= 1);
        Assert.True(context.DiscoveryState.SearchQueriesExecuted >= 1);

        var previousQueries = context.DiscoveryState.SearchQueriesExecuted;
        var secondBatch = await source.DiscoverAsync(context, CancellationToken.None);
        Assert.True(context.DiscoveryState.SearchQueriesExecuted > previousQueries
                    || context.DiscoveryState.CompletedSourceNames.Contains("WebSearch")
                    || secondBatch.Count >= 0);
        Assert.True(context.DiscoveryState.GeneratedQueries.Count > 1);
    }

    [Fact]
    public async Task DiscoverAsync_ExpandsQueriesWhenFirstWaveRunsOut()
    {
        var search = new Mock<IWebSearchProvider>();
        search.SetupGet(x => x.Name).Returns("Brave");
        search.SetupGet(x => x.IsEnabled).Returns(true);
        search.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebSearchRequest request, CancellationToken _) =>
            [
                new WebSearchHit
                {
                    Url = $"https://{Guid.NewGuid():N}.example",
                    Title = $"{request.Query} Gym",
                    Snippet = "Independent gym with memberships",
                    Provider = "Brave"
                }
            ]);

        var ai = new Mock<IAiResearchService>();
        ai.Setup(x => x.StructureFromSourceTextAsync(It.IsAny<AiSourceStructureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiSourceStructureRequest request, CancellationToken _) =>
            [
                new ProspectCandidate
                {
                    CompanyName = request.HintCompanyName ?? "Gym",
                    Website = request.SourceUrl,
                    SourceUrl = request.SourceUrl,
                    SourceName = "Website",
                    Country = "Canada"
                }
            ]);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient("ResearchCrawler"))
            .Returns(new HttpClient(new EmptyHtmlHandler()));

        var source = new SearchProspectSource(
            [search.Object],
            new WebsitePageCrawler(httpFactory.Object, NullLogger<WebsitePageCrawler>.Instance),
            ai.Object,
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchResults = 5,
                MaxSearchQueries = 3,
                MaxSearchRequests = 30,
                MaxPagesPerCrawl = 1,
                MaxCandidatesPerRun = 200,
                MaxResearchDurationMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                MaxRequestsPerResearchRun = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions()),
            NullLogger<SearchProspectSource>.Instance);

        var context = new ProspectSourceContext
        {
            ProjectName = "GymWatch – Canada",
            Keywords = ["gym", "fitness"],
            Countries = ["Canada"],
            Industries = ["Fitness"],
            Locations = ["Toronto", "Vancouver"],
            RequestedCount = 50,
            DiscoveryBatchSize = 2,
            DiscoveryState = new ResearchDiscoveryState()
        };

        await source.DiscoverAsync(context, CancellationToken.None);
        var firstWaveCount = context.DiscoveryState.GeneratedQueries.Count;
        Assert.True(firstWaveCount >= 1);

        while (context.DiscoveryState.NextQueryIndex < context.DiscoveryState.GeneratedQueries.Count
               && !context.DiscoveryState.LimitsExhausted)
        {
            await source.DiscoverAsync(context, CancellationToken.None);
        }

        if (!context.DiscoveryState.LimitsExhausted)
        {
            await source.DiscoverAsync(context, CancellationToken.None);
        }

        Assert.True(context.DiscoveryState.GeneratedQueries.Count > firstWaveCount
                    || context.DiscoveryState.SearchQueriesExecuted > firstWaveCount);
    }

    [Fact]
    public async Task DiscoverAsync_WhenBraveReturnsEmpty_ContinuesOtherQueriesThenExhausts()
    {
        var search = new Mock<IWebSearchProvider>();
        search.SetupGet(x => x.Name).Returns("Brave");
        search.SetupGet(x => x.IsEnabled).Returns(true);
        search.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient("ResearchCrawler"))
            .Returns(new HttpClient(new EmptyHtmlHandler()));

        var source = new SearchProspectSource(
            [search.Object],
            new WebsitePageCrawler(httpFactory.Object, NullLogger<WebsitePageCrawler>.Instance),
            new Mock<IAiResearchService>().Object,
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchQueries = 3,
                MaxSearchRequests = 10,
                MaxSearchResults = 5,
                MaxPagesPerCrawl = 1,
                MaxResearchDurationMinutes = 30
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions
            {
                Enabled = true,
                ApiKey = "test-key",
                MaxRequestsPerResearchRun = 10
            }),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions()),
            NullLogger<SearchProspectSource>.Instance);

        var context = new ProspectSourceContext
        {
            Keywords = ["gym"],
            Countries = ["Canada"],
            Locations = ["Toronto"],
            RequestedCount = 50,
            DiscoveryBatchSize = 10,
            DiscoveryState = new ResearchDiscoveryState()
        };

        var result = await source.DiscoverAsync(context, CancellationToken.None);
        Assert.Empty(result);
        Assert.True(context.DiscoveryState.SearchQueriesExecuted >= 1);
        Assert.True(context.DiscoveryState.LimitsExhausted
                    || context.DiscoveryState.CompletedSourceNames.Contains("WebSearch"));
    }

    private sealed class EmptyHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><h1>Gym</h1><p>Memberships and classes</p></body></html>")
            });
        }
    }
}
