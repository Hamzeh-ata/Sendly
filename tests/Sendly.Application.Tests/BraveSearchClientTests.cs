using System.Net;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Sendly.Application.Tests;

public class BraveSearchClientTests
{
    [Fact]
    public async Task SearchAsync_SendsSubscriptionTokenHeader()
    {
        HttpRequestMessage? captured = null;
        var handler = CreateHandler((request, _) =>
        {
            captured = request;
            return JsonResponse("""
                {"web":{"results":[{"title":"Gym","url":"https://example.com","description":"A gym"}]}}
                """);
        });

        var client = CreateClient(handler, apiKey: "secret-key");
        var result = await client.SearchAsync(new BraveSearchQuery { Query = "gym Riyadh", Count = 5 }, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.True(captured!.Headers.TryGetValues("X-Subscription-Token", out var values));
        Assert.Equal("secret-key", values.Single());
        Assert.DoesNotContain("secret-key", captured.RequestUri!.Query, StringComparison.Ordinal);
        Assert.Single(result.Results);
        Assert.Equal("https://example.com", result.Results[0].Url);
        Assert.Equal("Brave", result.Results[0].SearchProvider);
    }

    [Fact]
    public async Task SearchAsync_UsesApiKeyFromOptions()
    {
        string? token = null;
        var handler = CreateHandler((request, _) =>
        {
            token = request.Headers.GetValues("X-Subscription-Token").Single();
            return JsonResponse("""{"web":{"results":[]}}""");
        });

        var client = CreateClient(handler, apiKey: "from-config");
        await client.SearchAsync(new BraveSearchQuery { Query = "test" }, CancellationToken.None);
        Assert.Equal("from-config", token);
    }

    [Fact]
    public async Task SearchAsync_NormalizesResults()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""
            {
              "query": { "more_results_available": true },
              "web": {
                "results": [
                  { "title": "One", "url": "https://one.test", "description": "Desc 1" },
                  { "title": "Two", "url": "https://two.test", "description": "Desc 2" }
                ]
              }
            }
            """));

        var client = CreateClient(handler);
        var result = await client.SearchAsync(new BraveSearchQuery { Query = "fitness" }, CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
        Assert.Equal("One", result.Results[0].Title);
        Assert.Equal("Desc 1", result.Results[0].Description);
        Assert.True(result.HasMoreResults);
        Assert.Equal("Brave", result.Provider);
    }

    [Fact]
    public async Task SearchAsync_Unauthorized_ThrowsSafeMessage()
    {
        var handler = CreateHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("nope")
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.SearchAsync(new BraveSearchQuery { Query = "gym" }, CancellationToken.None));

        Assert.Contains("unauthorized", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_TooManyRequests_RetriesThenSucceeds()
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

            return JsonResponse("""{"web":{"results":[{"title":"Ok","url":"https://ok.test","description":"ok"}]}}""");
        });

        var client = CreateClient(handler, maxRetry: 1, delayMs: 1);
        var result = await client.SearchAsync(new BraveSearchQuery { Query = "gym" }, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Single(result.Results);
    }

    [Fact]
    public async Task SearchAsync_ServerError_Retries()
    {
        var attempts = 0;
        var handler = CreateHandler((_, _) =>
        {
            attempts++;
            if (attempts < 3)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("down")
                };
            }

            return JsonResponse("""{"web":{"results":[]}}""");
        });

        var client = CreateClient(handler, maxRetry: 2, delayMs: 1);
        var result = await client.SearchAsync(new BraveSearchQuery { Query = "gym" }, CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task TestConnectionAsync_EmptyResults_StillSuccess()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"web":{"results":[]}}"""));
        var client = CreateClient(handler);
        var result = await client.TestConnectionAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("ConnectionSuccessful", result.Status);
    }

    [Fact]
    public async Task TestConnectionAsync_MissingKey_NotConfigured()
    {
        var handler = CreateHandler((_, _) => JsonResponse("""{"web":{"results":[]}}"""));
        var client = CreateClient(handler, apiKey: "");
        var result = await client.TestConnectionAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NotConfigured", result.Status);
    }

    [Fact]
    public async Task BraveWebSearchProvider_UsesClientWithoutBraveLeak()
    {
        var braveClient = new Mock<IBraveSearchClient>();
        braveClient
            .Setup(x => x.SearchAsync(It.IsAny<BraveSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BraveSearchResponse
            {
                Results =
                [
                    new BraveSearchResultItem
                    {
                        Title = "Gym",
                        Url = "https://gym.test",
                        Description = "Fitness",
                        SearchProvider = "Brave"
                    }
                ]
            });

        var provider = new BraveWebSearchProvider(
            braveClient.Object,
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions { Enabled = true, ApiKey = "secret", MaxResultsPerQuery = 10, MaxRequestsPerResearchRun = 5 }),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableSearchProvider = true }),
            NullLogger<BraveWebSearchProvider>.Instance);

        Assert.True(provider.IsEnabled);
        var hits = await provider.SearchAsync(new WebSearchRequest { Query = "gym Saudi Arabia", MaxResults = 5 }, CancellationToken.None);
        Assert.Single(hits);
        Assert.Equal("Brave", hits[0].Provider);
        Assert.Equal("https://gym.test", hits[0].Url);
    }

    private static BraveSearchClient CreateClient(
        HttpMessageHandler handler,
        string apiKey = "secret-key",
        int maxRetry = 2,
        int delayMs = 1)
    {
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("BraveSearch")).Returns(httpClient);
        return new BraveSearchClient(
            factory.Object,
            Microsoft.Extensions.Options.Options.Create(new BraveSearchOptions
            {
                Enabled = true,
                ApiKey = apiKey,
                MaxRetryCount = maxRetry,
                RequestDelayMilliseconds = delayMs,
                MaxResultsPerQuery = 10
            }),
            NullLogger<BraveSearchClient>.Instance);
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
}
