using System.Net.Http.Headers;
using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class BingWebSearchProvider : IWebSearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly ILogger<BingWebSearchProvider> _logger;

    public BingWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        ILogger<BingWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Bing";

    public bool IsEnabled =>
        _options.EnableSearchProvider && !string.IsNullOrWhiteSpace(_options.BingSearchApiKey);

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        try
        {
            var client = _httpClientFactory.CreateClient("BingSearch");
            var endpoint = string.IsNullOrWhiteSpace(_options.BingSearchEndpoint)
                ? "https://api.bing.microsoft.com/v7.0/search"
                : _options.BingSearchEndpoint;
            var url = $"{endpoint}?q={Uri.EscapeDataString(request.Query)}&count={Math.Clamp(request.MaxResults, 1, 50)}";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Add("Ocp-Apim-Subscription-Key", _options.BingSearchApiKey);
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bing search failed with status {Status}", (int)response.StatusCode);
                return [];
            }

            return ParseBing(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Bing search failed");
            return [];
        }
    }

    private static List<WebSearchHit> ParseBing(string body)
    {
        var result = new List<WebSearchHit>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("webPages", out var webPages)
            || !webPages.TryGetProperty("value", out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in values.EnumerateArray())
        {
            var url = item.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            result.Add(new WebSearchHit
            {
                Url = url,
                Title = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null,
                Snippet = item.TryGetProperty("snippet", out var snippetProp) ? snippetProp.GetString() : null
            });
        }

        return result;
    }
}
