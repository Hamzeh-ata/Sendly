using System.Net;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Sendly.Application.Tests;

public class SearXngSearchClientTests
{
    [Fact]
    public async Task SearchAsync_BuildsEndpointAndQueryParameters()
    {
        HttpRequestMessage? captured = null;
        var handler = CreateHandler((request, _) =>
        {
            captured = request;
            return JsonResponse("""{"results":[{"title":"School","url":"https://school.test","content":"Independent","engine":"google"}]}""");
        });

        var client = CreateClient(handler, "http://127.0.0.1:8080");
        var result = await client.SearchAsync(
            new SearXngSearchQuery { Query = "independent schools London", Count = 5, PageNumber = 2, Language = "en" },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("127.0.0.1", captured.RequestUri!.Host);
        Assert.EndsWith("/search", captured.RequestUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("q=independent%20schools%20London", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("format=json", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("pageno=2", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains("language=en", captured.RequestUri.Query, StringComparison.Ordinal);
        Assert.Single(result.Results);
        Assert.Equal("https://school.test", result.Results[0].Url);
        Assert.Equal("google", result.Results[0].Engine);
        Assert.Equal("SearXNG", result.Results[0].SearchProvider);
    }

    [Fact]
    public async Task SearchAsync_ParsesSuccessfulJson()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""
            {
              "results": [
                { "title": "One", "url": "https://one.test", "content": "A", "engine": "bing" },
                { "title": "Two", "url": "https://two.test", "content": "B", "engine": "duckduckgo" }
              ]
            }
            """));

        var client = CreateClient(handler);
        var result = await client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
        Assert.Equal("One", result.Results[0].Title);
        Assert.Equal("A", result.Results[0].Content);
        Assert.Equal("SearXNG", result.Provider);
    }

    [Fact]
    public async Task SearchAsync_EmptyResults_Succeeds()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"results":[]}"""));
        var client = CreateClient(handler);
        var result = await client.SearchAsync(new SearXngSearchQuery { Query = "rare query" }, CancellationToken.None);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task SearchAsync_InvalidJson_Throws()
    {
        var handler = CreateHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>nope</html>", Encoding.UTF8, "text/html")
        });
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_MissingResultsArray_Throws()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"query":"x"}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, CancellationToken.None));
        Assert.Contains("results", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_Http403_Throws()
    {
        var handler = CreateHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("denied")
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, CancellationToken.None));
        Assert.Contains("authorization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_Http429_RetriesThenSucceeds()
    {
        var attempts = 0;
        var handler = CreateHandler((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate")
                };
            }

            return JsonResponse("""{"results":[{"title":"Ok","url":"https://ok.test","content":"ok","engine":"google"}]}""");
        });

        var client = CreateClient(handler, maxRetry: 1, delayMs: 1);
        var result = await client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task SearchAsync_RespectsCancellation()
    {
        var handler = CreateHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return JsonResponse("""{"results":[]}""");
        });
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.SearchAsync(new SearXngSearchQuery { Query = "gym" }, cts.Token));
    }

    [Fact]
    public async Task SearchAsync_PaginationUsesPageNumber()
    {
        HttpRequestMessage? captured = null;
        var handler = CreateHandler((request, _) =>
        {
            captured = request;
            return JsonResponse("""{"results":[]}""");
        });
        var client = CreateClient(handler);
        await client.SearchAsync(new SearXngSearchQuery { Query = "schools", PageNumber = 3 }, CancellationToken.None);
        Assert.Contains("pageno=3", captured!.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestConnectionAsync_RequiresValidResultsStructure()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"ok":true}"""));
        var client = CreateClient(handler);
        var result = await client.TestConnectionAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("ConnectionFailed", result.Status);
    }

    [Fact]
    public async Task TestConnectionAsync_ValidEmptyResults_Succeeds()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"results":[]}"""));
        var client = CreateClient(handler);
        var result = await client.TestConnectionAsync(CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("ConnectionSuccessful", result.Status);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", false)]
    [InlineData("http://localhost:8080", false)]
    [InlineData("http://192.168.1.10:8080", false)]
    [InlineData("http://169.254.169.254/", true)]
    [InlineData("http://user:pass@searx.example/", true)]
    [InlineData("ftp://searx.example/", true)]
    public void ValidateBaseUrl_BlocksUnsafeDestinations(string url, bool alwaysBlocked)
    {
        if (alwaysBlocked)
        {
            Assert.Throws<InvalidOperationException>(() =>
                SearXngUrlSafety.ValidateAndNormalizeBaseUrl(url, allowPrivateNetworkHosts: true));
            return;
        }

        Assert.Throws<InvalidOperationException>(() =>
            SearXngUrlSafety.ValidateAndNormalizeBaseUrl(url, allowPrivateNetworkHosts: false));
        var allowed = SearXngUrlSafety.ValidateAndNormalizeBaseUrl(url, allowPrivateNetworkHosts: true);
        Assert.True(allowed.IsAbsoluteUri);
    }

    [Fact]
    public void ValidateBaseUrl_AllowsPublicHost()
    {
        var uri = SearXngUrlSafety.ValidateAndNormalizeBaseUrl("https://searx.example.org", false);
        Assert.Equal("https://searx.example.org/", uri.ToString());
    }

    [Fact]
    public async Task SearXngProvider_Only_ReturnsHits()
    {
        var client = new Mock<ISearXngSearchClient>();
        client.Setup(x => x.SearchAsync(It.IsAny<SearXngSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearXngSearchResponse
            {
                Results =
                [
                    new SearXngSearchResultItem
                    {
                        Title = "Gym",
                        Url = "https://gym.test",
                        Content = "Fitness",
                        Engine = "google"
                    }
                ]
            });

        var provider = new SearXngWebSearchProvider(
            client.Object,
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions
            {
                Enabled = true,
                BaseUrl = "https://searx.example",
                DefaultResultsPerQuery = 10,
                MaximumPagesPerQuery = 2,
                MaxRequestsPerResearchRun = 5
            }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<SearXngWebSearchProvider>.Instance);

        Assert.True(provider.IsEnabled);
        var hits = await provider.SearchAsync(new WebSearchRequest { Query = "gym", MaxResults = 5 }, CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal("SearXNG", hits[0].Provider);
    }

    [Fact]
    public async Task BothProviders_StopsAfterFirstProviderReturnsHits()
    {
        var brave = new Mock<IWebSearchProvider>();
        brave.SetupGet(x => x.Name).Returns("Brave");
        brave.SetupGet(x => x.IsEnabled).Returns(true);
        brave.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WebSearchHit { Url = "https://shared.test/page", Title = "Shared Brave", Provider = "Brave" },
                new WebSearchHit { Url = "https://brave-only.test", Title = "Brave Only", Provider = "Brave" }
            ]);

        var searx = new Mock<IWebSearchProvider>();
        searx.SetupGet(x => x.Name).Returns("SearXNG");
        searx.SetupGet(x => x.IsEnabled).Returns(true);
        searx.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WebSearchHit { Url = "https://searx-only.test", Title = "SearX Only", Provider = "SearXNG" }
            ]);

        var ai = new Mock<IAiResearchService>();
        ai.Setup(x => x.StructureFromSourceTextAsync(It.IsAny<AiSourceStructureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiSourceStructureRequest request, CancellationToken _) =>
            [
                new ProspectCandidate
                {
                    CompanyName = request.HintCompanyName ?? "Co",
                    Website = request.SourceUrl,
                    SourceUrl = request.SourceUrl,
                    SourceName = "Website"
                }
            ]);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient("ResearchCrawler"))
            .Returns(new HttpClient(new EmptyHtmlHandler()));

        var source = new SearchProspectSource(
            [brave.Object, searx.Object],
            new WebsitePageCrawler(httpFactory.Object, NullLogger<WebsitePageCrawler>.Instance),
            ai.Object,
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchQueries = 5,
                MaxSearchRequests = 20,
                MaxSearchResults = 10,
                MaxPagesPerCrawl = 1,
                MaxCandidatesPerRun = 50,
                MaxResearchDurationMinutes = 30,
                PagesPerSearchOffset = 1
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "k", MaxRequestsPerResearchRun = 20 }),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = true, BaseUrl = "https://searx.example", MaxRequestsPerResearchRun = 20 }),
            NullLogger<SearchProspectSource>.Instance);

        var context = new ProspectSourceContext
        {
            Keywords = ["gym"],
            Countries = ["Canada"],
            Locations = ["Toronto"],
            RequestedCount = 10,
            DiscoveryBatchSize = 10,
            DiscoveryState = new ResearchDiscoveryState()
        };

        var batch = await source.DiscoverAsync(context, CancellationToken.None);
        var websites = batch
            .Select(x => x.Website?.TrimEnd('/').ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        Assert.Contains(websites, x => x!.Contains("brave-only", StringComparison.Ordinal));
        Assert.DoesNotContain(websites, x => x!.Contains("searx-only", StringComparison.Ordinal));
        searx.Verify(
            x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BothProviders_FallsBackWhenFirstProviderReturnsEmpty()
    {
        var brave = new Mock<IWebSearchProvider>();
        brave.SetupGet(x => x.Name).Returns("Brave");
        brave.SetupGet(x => x.IsEnabled).Returns(true);
        brave.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var searx = new Mock<IWebSearchProvider>();
        searx.SetupGet(x => x.Name).Returns("SearXNG");
        searx.SetupGet(x => x.IsEnabled).Returns(true);
        searx.Setup(x => x.SearchAsync(It.IsAny<WebSearchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new WebSearchHit { Url = "https://searx-only.test", Title = "SearX Only", Provider = "SearXNG" }
            ]);

        var ai = new Mock<IAiResearchService>();
        ai.Setup(x => x.StructureFromSourceTextAsync(It.IsAny<AiSourceStructureRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiSourceStructureRequest request, CancellationToken _) =>
            [
                new ProspectCandidate
                {
                    CompanyName = request.HintCompanyName ?? "Co",
                    Website = request.SourceUrl,
                    SourceUrl = request.SourceUrl,
                    SourceName = "Website"
                }
            ]);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient("ResearchCrawler"))
            .Returns(new HttpClient(new EmptyHtmlHandler()));

        var source = new SearchProspectSource(
            [brave.Object, searx.Object],
            new WebsitePageCrawler(httpFactory.Object, NullLogger<WebsitePageCrawler>.Instance),
            ai.Object,
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions
            {
                EnableSearchProvider = true,
                MaxSearchQueries = 5,
                MaxSearchRequests = 20,
                MaxSearchResults = 10,
                MaxPagesPerCrawl = 1,
                MaxCandidatesPerRun = 50,
                MaxResearchDurationMinutes = 30,
                PagesPerSearchOffset = 1
            }),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "k", MaxRequestsPerResearchRun = 20 }),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = true, BaseUrl = "https://searx.example", MaxRequestsPerResearchRun = 20 }),
            NullLogger<SearchProspectSource>.Instance);

        var context = new ProspectSourceContext
        {
            Keywords = ["gym"],
            Countries = ["Canada"],
            Locations = ["Toronto"],
            RequestedCount = 10,
            DiscoveryBatchSize = 10,
            DiscoveryState = new ResearchDiscoveryState()
        };

        var batch = await source.DiscoverAsync(context, CancellationToken.None);
        Assert.Contains(batch, x => (x.Website ?? string.Empty).Contains("searx-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BraveOnly_ProviderEnabled_SearXngDisabled()
    {
        var brave = new BraveWebSearchProvider(
            Mock.Of<IBraveSearchClient>(),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "secret" }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<BraveWebSearchProvider>.Instance);
        var searx = new SearXngWebSearchProvider(
            Mock.Of<ISearXngSearchClient>(),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = false, BaseUrl = "https://searx.example" }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<SearXngWebSearchProvider>.Instance);

        Assert.True(brave.IsEnabled);
        Assert.False(searx.IsEnabled);
    }

    [Fact]
    public void SearXngOnly_ProviderEnabled_BraveDisabled()
    {
        var brave = new BraveWebSearchProvider(
            Mock.Of<IBraveSearchClient>(),
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = false, ApiKey = "secret" }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<BraveWebSearchProvider>.Instance);
        var searx = new SearXngWebSearchProvider(
            Mock.Of<ISearXngSearchClient>(),
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions { Enabled = true, BaseUrl = "https://searx.example" }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<SearXngWebSearchProvider>.Instance);

        Assert.False(brave.IsEnabled);
        Assert.True(searx.IsEnabled);
    }

    private static SearXngSearchClient CreateClient(
        HttpMessageHandler handler,
        string baseUrl = "http://127.0.0.1:8080",
        int maxRetry = 1,
        int delayMs = 1)
    {
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("SearXng")).Returns(httpClient);
        return new SearXngSearchClient(
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new SearXngOptions
            {
                Enabled = true,
                BaseUrl = baseUrl,
                DefaultLanguage = "en",
                DefaultResultsPerQuery = 10,
                MaximumPagesPerQuery = 5,
                MaxRetryCount = maxRetry,
                RequestDelayMilliseconds = delayMs,
                RequestTimeoutSeconds = 30,
                AllowPrivateNetworkHosts = true
            }),
            NullLogger<SearXngSearchClient>.Instance);
    }

    private static HttpMessageHandler CreateHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((request, token) =>
                Task.FromResult(responder(request, token)));
        return handler.Object;
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class EmptyHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><body><h1>Gym</h1></body></html>")
            });
        }
    }
}
