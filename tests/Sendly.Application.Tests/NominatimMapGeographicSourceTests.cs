using System.Net;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class NominatimMapGeographicSourceTests
{
    [Fact]
    public async Task QueryAsync_Parses_School_Results_From_Nominatim()
    {
        var json = """
            [
              {
                "osm_type": "way",
                "osm_id": 12345,
                "lat": "25.2789680",
                "lon": "55.3684963",
                "name": "Dubai National School",
                "display_name": "Dubai National School, Dubai, UAE",
                "category": "amenity",
                "type": "school",
                "extratags": { "website": "https://example-school.test", "phone": "+971500000000" },
                "address": { "city": "Dubai", "country": "United Arab Emirates", "road": "Street 5" }
              }
            ]
            """;
        var source = new NominatimMapGeographicSource(
            new FixedResponseClientFactory(json),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableOpenStreetMapDiscovery = true }),
            new ChallengedSourceRegistry(),
            NullLogger<NominatimMapGeographicSource>.Instance);

        var result = await source.QueryAsync(
            new MapSourceQueryRequest
            {
                South = 25.0,
                West = 55.1,
                North = 25.3,
                East = 55.4,
                SourceCategories = ["amenity=school"],
                KeywordFamilyKey = "industry:schools",
                CountryCode = "AE",
                Limit = 10
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Places);
        Assert.Equal("Dubai National School", result.Places[0].Name);
        Assert.Equal("osm:way/12345", result.Places[0].SourceObjectKey);
        Assert.Equal(25.2789680, result.Places[0].Latitude);
        Assert.Equal(55.3684963, result.Places[0].Longitude);
        Assert.Equal("https://example-school.test", result.Places[0].Website);
    }

    [Fact]
    public async Task Composite_Falls_Back_To_Nominatim_When_Overpass_Disabled()
    {
        var json = """
            [
              {
                "osm_type": "node",
                "osm_id": 99,
                "lat": "25.2",
                "lon": "55.3",
                "name": "Fallback School",
                "category": "amenity",
                "type": "school",
                "extratags": {},
                "address": { "city": "Dubai" }
              }
            ]
            """;
        var challenges = new ChallengedSourceRegistry();
        challenges.RecordChallenge(
            "OpenStreetMapOverpass",
            Domain.Enums.HttpAccessResponseKind.RateLimited,
            "forced",
            DateTimeOffset.UtcNow,
            429,
            TimeSpan.FromHours(1));

        var overpass = new OverpassOpenStreetMapSource(
            new FixedResponseClientFactory("[]"),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableOpenStreetMapDiscovery = true }),
            challenges,
            NullLogger<OverpassOpenStreetMapSource>.Instance);
        var nominatim = new NominatimMapGeographicSource(
            new FixedResponseClientFactory(json),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableOpenStreetMapDiscovery = true }),
            challenges,
            NullLogger<NominatimMapGeographicSource>.Instance);
        var composite = new CompositeMapGeographicSource(
            overpass,
            nominatim,
            NullLogger<CompositeMapGeographicSource>.Instance);

        Assert.True(composite.IsEnabled);
        var result = await composite.QueryAsync(
            new MapSourceQueryRequest
            {
                South = 25.0,
                West = 55.1,
                North = 25.3,
                East = 55.4,
                SourceCategories = ["amenity=school"],
                KeywordFamilyKey = "industry:schools",
                Limit = 5
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Places);
        Assert.Equal("Fallback School", result.Places[0].Name);
        Assert.Equal("OpenStreetMapNominatim", result.Places[0].SourceName);
    }

    private sealed class FixedResponseClientFactory : IHttpClientFactory
    {
        private readonly string _body;

        public FixedResponseClientFactory(string body)
        {
            _body = body;
        }

        public HttpClient CreateClient(string name)
        {
            var handler = new FixedHandler(_body);
            var client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        }
    }

    private sealed class FixedHandler : HttpMessageHandler
    {
        private readonly string _body;

        public FixedHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
