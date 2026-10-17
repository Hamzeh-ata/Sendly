using System.Text.Json;
using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class GoogleCustomSearchProvider : IWebSearchProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ResearchOptions _options;
    private readonly ILogger<GoogleCustomSearchProvider> _logger;

    public GoogleCustomSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<ResearchOptions> options,
        ILogger<GoogleCustomSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "GoogleCustomSearch";

    public bool IsEnabled =>
        _options.EnableSearchProvider
        && !string.IsNullOrWhiteSpace(_options.GoogleSearchApiKey)
        && !string.IsNullOrWhiteSpace(_options.GoogleSearchEngineId);

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
            var client = _httpClientFactory.CreateClient("GoogleSearch");
            var url =
                $"https://www.googleapis.com/customsearch/v1?key={Uri.EscapeDataString(_options.GoogleSearchApiKey!)}"
                + $"&cx={Uri.EscapeDataString(_options.GoogleSearchEngineId!)}"
                + $"&q={Uri.EscapeDataString(request.Query)}"
                + $"&num={Math.Clamp(request.MaxResults, 1, 10)}";
            using var response = await client.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google search failed with status {Status}", (int)response.StatusCode);
                return [];
            }

            return ParseGoogle(body);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Google search failed");
            return [];
        }
    }

    private static List<WebSearchHit> ParseGoogle(string body)
    {
        var result = new List<WebSearchHit>();
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in items.EnumerateArray())
        {
            var url = item.TryGetProperty("link", out var linkProp) ? linkProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            result.Add(new WebSearchHit
            {
                Url = url,
                Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
                Snippet = item.TryGetProperty("snippet", out var snippetProp) ? snippetProp.GetString() : null
            });
        }

        return result;
    }
}
