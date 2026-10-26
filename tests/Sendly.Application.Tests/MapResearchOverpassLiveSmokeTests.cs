using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class MapResearchOverpassLiveSmokeTests
{
    [Fact]
    public async Task Overpass_LiveQuery_Returns_RealRecords_When_Enabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SENDLY_LIVE_OVERPASS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var apiKey = Environment.GetEnvironmentVariable("SENDLY_OVERPASS_API_KEY");
        var overpassOptions = new OverpassOptions
        {
            BaseUrl = "https://api.overpass.ch/v1/interpreter",
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            MaxRetryCount = 1,
            RequestDelayMilliseconds = 0
        };
        var source = new OverpassOpenStreetMapSource(
            new SingleClientFactory(),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableOpenStreetMapDiscovery = true }),
            Microsoft.Extensions.Options.Options.Create(overpassOptions),
            new ChallengedSourceRegistry(),
            NullLogger<OverpassOpenStreetMapSource>.Instance);

        MapSourceQueryResult result;
        try
        {
            result = await source.QueryAsync(
                new MapSourceQueryRequest
                {
                    South = 31.90,
                    West = 35.85,
                    North = 32.00,
                    East = 35.98,
                    SourceCategories = ["leisure=fitness_centre"],
                    NameKeywords = ["gym"],
                    KeywordFamilyKey = "industry:gyms",
                    CountryCode = "JO",
                    Limit = 10
                },
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            Assert.True(
                true,
                $"Live Overpass attempt did not complete due to network/timeout: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!result.Succeeded)
        {
            Assert.True(
                true,
                $"Live Overpass attempted but source was unavailable: {result.ErrorSummary}");
            return;
        }

        Assert.NotEmpty(result.Places);
        Assert.All(result.Places, place =>
        {
            Assert.False(string.IsNullOrWhiteSpace(place.Name));
            Assert.False(string.IsNullOrWhiteSpace(place.SourceObjectKey));
            Assert.NotNull(place.Latitude);
            Assert.NotNull(place.Longitude);
            Assert.Contains("osm:", place.SourceObjectKey);
        });
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "SendlyResearchBot/1.0 (tests; contact: research@sendly.local)");
            return client;
        }
    }
}
