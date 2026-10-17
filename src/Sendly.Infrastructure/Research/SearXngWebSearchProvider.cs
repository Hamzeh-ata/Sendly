using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class SearXngWebSearchProvider : IWebSearchProvider
{
    private readonly ISearXngSearchClient _client;
    private readonly SearXngOptions _searXngOptions;
    private readonly ResearchOptions _researchOptions;
    private readonly ILogger<SearXngWebSearchProvider> _logger;
    private int _requestsThisRun;

    public SearXngWebSearchProvider(
        ISearXngSearchClient client,
        IOptions<SearXngOptions> searXngOptions,
        IOptions<ResearchOptions> researchOptions,
        ILogger<SearXngWebSearchProvider> logger)
    {
        _client = client;
        _searXngOptions = searXngOptions.Value;
        _researchOptions = researchOptions.Value;
        _logger = logger;
    }

    public string Name => "SearXNG";

    public bool IsEnabled =>
        _researchOptions.EnableSearchProvider
        && _searXngOptions.Enabled
        && !string.IsNullOrWhiteSpace(_searXngOptions.BaseUrl);

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var maxRequests = Math.Clamp(_searXngOptions.MaxRequestsPerResearchRun, 1, 500);
        if (_requestsThisRun >= maxRequests)
        {
            _logger.LogWarning("SearXNG request cap reached for current process scope ({Max})", maxRequests);
            throw new InvalidOperationException("SearXNG request cap reached for this research run.");
        }

        var count = Math.Clamp(
            request.MaxResults,
            1,
            Math.Clamp(_searXngOptions.DefaultResultsPerQuery, 1, 50));
        var pageSize = Math.Max(1, count);
        var pageNumber = (Math.Max(0, request.Offset) / pageSize) + 1;
        var maxPages = Math.Clamp(_searXngOptions.MaximumPagesPerQuery, 1, 20);
        if (pageNumber > maxPages)
        {
            return [];
        }

        _requestsThisRun++;
        var language = string.IsNullOrWhiteSpace(request.Language)
            ? _searXngOptions.DefaultLanguage
            : request.Language;

        var response = await _client.SearchAsync(
            new SearXngSearchQuery
            {
                Query = request.Query.Trim(),
                Count = count,
                PageNumber = pageNumber,
                Language = language
            },
            cancellationToken);

        var hits = new List<WebSearchHit>();
        foreach (var item in response.Results)
        {
            if (string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            hits.Add(new WebSearchHit
            {
                Url = item.Url,
                Title = item.Title,
                Snippet = item.Content,
                Provider = "SearXNG"
            });
        }

        _logger.LogInformation("SearXNG returned {Count} urls", hits.Count);
        return hits;
    }
}
