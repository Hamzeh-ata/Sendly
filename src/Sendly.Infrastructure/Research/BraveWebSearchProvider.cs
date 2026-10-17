using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class BraveWebSearchProvider : IWebSearchProvider
{
    private readonly IBraveSearchClient _client;
    private readonly BraveSearchOptions _braveOptions;
    private readonly ResearchOptions _researchOptions;
    private readonly ILogger<BraveWebSearchProvider> _logger;
    private int _requestsThisRun;

    public BraveWebSearchProvider(
        IBraveSearchClient client,
        IOptions<BraveSearchOptions> braveOptions,
        IOptions<ResearchOptions> researchOptions,
        ILogger<BraveWebSearchProvider> logger)
    {
        _client = client;
        _braveOptions = braveOptions.Value;
        _researchOptions = researchOptions.Value;
        _logger = logger;
    }

    public string Name => "Brave";

    public bool IsEnabled =>
        _researchOptions.EnableSearchProvider
        && _braveOptions.Enabled
        && !string.IsNullOrWhiteSpace(_braveOptions.ApiKey);

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(
        WebSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(request.Query))
        {
            return [];
        }

        var maxRequests = Math.Clamp(_braveOptions.MaxRequestsPerResearchRun, 1, 100);
        if (_requestsThisRun >= maxRequests)
        {
            _logger.LogWarning("Brave Search request cap reached for current process scope ({Max})", maxRequests);
            return [];
        }

        try
        {
            _requestsThisRun++;
            var count = Math.Clamp(
                request.MaxResults,
                1,
                Math.Clamp(_braveOptions.MaxResultsPerQuery, 1, 20));
            var country = request.Country;
            if (string.IsNullOrWhiteSpace(country) && request.Countries.Count > 0)
            {
                country = request.Countries[0];
            }

            var response = await _client.SearchAsync(
                new BraveSearchQuery
                {
                    Query = request.Query.Trim(),
                    Count = count,
                    Offset = Math.Max(0, request.Offset),
                    Country = country,
                    SearchLang = request.Language
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
                    Snippet = item.Description,
                    Provider = "Brave"
                });
            }

            _logger.LogInformation("Brave Search returned {Count} urls", hits.Count);
            return hits;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Brave Search provider failed");
            return [];
        }
    }
}
