using System.Net;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sendly.Application.Tests;

public class SafeHttpFetcherSsrfTests
{
    [Fact]
    public async Task FetchAsync_blocks_localhost()
    {
        var fetcher = new SafeHttpFetcher(
            new StubHttpClientFactory(),
            new AlwaysAllowRobots(),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions()),
            NullLogger<SafeHttpFetcher>.Instance);

        var result = await fetcher.FetchAsync(
            new SafeHttpFetchRequest { Url = new Uri("http://127.0.0.1/") },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.BlockedBySsrf);
    }

    private sealed class AlwaysAllowRobots : IRobotsTxtService
    {
        public Task<bool> IsAllowedAsync(Uri url, string userAgent, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyList<Uri>> GetSitemapUrlsAsync(
            Uri siteUrl,
            string userAgent,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Uri>>([]);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new HttpClientHandler { AllowAutoRedirect = false });
    }
}
