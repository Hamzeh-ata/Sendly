using System.Net;
using System.Text;
using Sendly.Infrastructure.Research;

namespace Sendly.Application.Tests;

public class RobotsTxtServiceTests
{
    [Fact]
    public async Task IsAllowedAsync_respects_disallow_rules()
    {
        var handler = new StubHandler("""
            User-agent: *
            Disallow: /private
            Allow: /
            """);
        var factory = new StubHttpClientFactory(handler);
        var service = new RobotsTxtService(factory);

        var allowed = await service.IsAllowedAsync(
            new Uri("https://example.com/about"),
            "SendlyResearchBot/1.0",
            CancellationToken.None);
        var blocked = await service.IsAllowedAsync(
            new Uri("https://example.com/private/data"),
            "SendlyResearchBot/1.0",
            CancellationToken.None);

        Assert.True(allowed);
        Assert.False(blocked);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body)
        {
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/plain")
            };
            return Task.FromResult(response);
        }
    }
}
