using System.Net;
using System.Text;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Infrastructure.Research;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sendly.Application.Tests;

public class OverpassOpenStreetMapSourceTests
{
    [Fact]
    public async Task QueryAsync_Uses_Configured_Endpoint_And_ApiKey_Header()
    {
        var json = """
            {
              "elements": [
                {
                  "type": "node",
                  "id": 42,
                  "lat": 47.605,
                  "lon": -122.335,
                  "tags": { "name": "Harbor Cafe", "amenity": "cafe" }
                }
              ]
            }
            """;
        var handler = new CapturingHandler(json);
        var source = CreateSource(
            handler,
            new OverpassOptions
            {
                BaseUrl = "https://api.overpass.ch/v1/interpreter",
                ApiKey = "ovp_test_key",
                MaxRetryCount = 0,
                RequestDelayMilliseconds = 0
            });

        var result = await source.QueryAsync(
            new MapSourceQueryRequest
            {
                South = 47.60,
                West = -122.34,
                North = 47.61,
                East = -122.33,
                SourceCategories = ["amenity=cafe"],
                Limit = 5
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(result.Places);
        Assert.Equal("Harbor Cafe", result.Places[0].Name);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://api.overpass.ch/v1/interpreter", handler.LastRequest!.RequestUri!.ToString());
        Assert.True(handler.LastRequest.Headers.TryGetValues("X-API-Key", out var keys));
        Assert.Equal("ovp_test_key", keys.Single());
        Assert.DoesNotContain("ovp_test_key", handler.LastRequest.RequestUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_Retries_Transient_Failures_With_Backoff()
    {
        var json = """
            {
              "elements": [
                {
                  "type": "node",
                  "id": 7,
                  "lat": 47.605,
                  "lon": -122.335,
                  "tags": { "name": "Retry Cafe", "amenity": "cafe" }
                }
              ]
            }
            """;
        var handler = new SequenceHandler(json);
        var source = CreateSource(
            handler,
            new OverpassOptions
            {
                BaseUrl = "https://api.overpass.ch/v1/interpreter",
                ApiKey = "ovp_test_key",
                MaxRetryCount = 2,
                RequestDelayMilliseconds = 0
            });

        var result = await source.QueryAsync(
            new MapSourceQueryRequest
            {
                South = 47.60,
                West = -122.34,
                North = 47.61,
                East = -122.33,
                SourceCategories = ["amenity=cafe"],
                Limit = 5
            },
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal("Retry Cafe", result.Places[0].Name);
    }

    [Fact]
    public async Task QueryAsync_Does_Not_Treat_Server_Error_As_Success()
    {
        var handler = new CapturingHandler("upstream error", HttpStatusCode.InternalServerError);
        var source = CreateSource(
            handler,
            new OverpassOptions
            {
                BaseUrl = "https://api.overpass.ch/v1/interpreter",
                ApiKey = "ovp_test_key",
                MaxRetryCount = 0,
                RequestDelayMilliseconds = 0
            });

        var result = await source.QueryAsync(
            new MapSourceQueryRequest
            {
                South = 47.60,
                West = -122.34,
                North = 47.61,
                East = -122.33,
                SourceCategories = ["amenity=cafe"],
                Limit = 5
            },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(result.Places);
        Assert.Contains("500", result.ErrorSummary);
    }

    private static OverpassOpenStreetMapSource CreateSource(HttpMessageHandler handler, OverpassOptions options)
    {
        return new OverpassOpenStreetMapSource(
            new HandlerClientFactory(handler),
            Microsoft.Extensions.Options.Options.Create(new ResearchOptions { EnableOpenStreetMapDiscovery = true }),
            Microsoft.Extensions.Options.Options.Create(options),
            new ChallengedSourceRegistry(),
            NullLogger<OverpassOpenStreetMapSource>.Instance);
    }

    private sealed class HandlerClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public HandlerClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;

        public CapturingHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly string _successBody;

        public SequenceHandler(string successBody)
        {
            _successBody = successBody;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited", Encoding.UTF8, "text/plain")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_successBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
