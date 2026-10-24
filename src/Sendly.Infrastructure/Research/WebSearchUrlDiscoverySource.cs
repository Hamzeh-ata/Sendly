using Sendly.Application.Abstractions;
using Sendly.Application.Options;
using Sendly.Application.Services;
using Sendly.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sendly.Infrastructure.Research;

public class WebSearchUrlDiscoverySource : IUrlDiscoverySource
{
    private readonly IEnumerable<IWebSearchProvider> _providers;
    private readonly ResearchOptions _options;
    private readonly ILogger<WebSearchUrlDiscoverySource> _logger;

    public WebSearchUrlDiscoverySource(
        IEnumerable<IWebSearchProvider> providers,
        IOptions<ResearchOptions> options,
        ILogger<WebSearchUrlDiscoverySource> logger)
    {
        _providers = providers;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "WebSearchDiscovery";

    public bool IsEnabled(UrlDiscoveryContext context)
        => _options.EnableSearchProvider && _providers.Any(x => x.IsEnabled);

    public async Task<IReadOnlyList<DiscoveredUrlCandidate>> DiscoverAsync(
        UrlDiscoveryContext context,
        CancellationToken cancellationToken)
    {
        var sourceContext = BuildSourceContext(context);
        var plan = ResearchSearchQueryGenerator.GeneratePlan(sourceContext, 8);
        var candidates = new List<DiscoveredUrlCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var planned in plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hits = await SearchProvidersAsync(planned.Query, sourceContext, cancellationToken);
            AddHits(candidates, seen, hits, planned.Query);
            if (candidates.Count >= context.MaxUrls)
            {
                break;
            }
        }

        return candidates;
    }

    private async Task<List<WebSearchHit>> SearchProvidersAsync(
        string query,
        ProspectSourceContext sourceContext,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            if (!provider.IsEnabled)
            {
                continue;
            }

            try
            {
                var hits = await ExecuteProviderAsync(provider, query, sourceContext, cancellationToken);
                if (hits.Count > 0)
                {
                    return hits;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Discovery search provider {Provider} failed; trying next provider", provider.Name);
            }
        }

        return [];
    }

    private static async Task<List<WebSearchHit>> ExecuteProviderAsync(
        IWebSearchProvider provider,
        string query,
        ProspectSourceContext sourceContext,
        CancellationToken cancellationToken)
    {
        var request = new WebSearchRequest
        {
            Query = query,
            MaxResults = 10,
            Keywords = sourceContext.Keywords,
            Countries = sourceContext.Countries,
            Country = sourceContext.Countries.FirstOrDefault()
        };
        if (provider is IDiagnosticWebSearchProvider diagnostic)
        {
            var outcome = await diagnostic.SearchDetailedAsync(request, cancellationToken);
            return outcome.Hits.ToList();
        }

        var hits = await provider.SearchAsync(request, cancellationToken);
        return hits.ToList();
    }

    private static void AddHits(
        List<DiscoveredUrlCandidate> candidates,
        HashSet<string> seen,
        List<WebSearchHit> hits,
        string query)
    {
        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.Url) || !seen.Add(hit.Url.Trim().ToLowerInvariant()))
            {
                continue;
            }

            candidates.Add(new DiscoveredUrlCandidate
            {
                Url = hit.Url,
                SourceType = DiscoverySourceType.WebSearch,
                SourceName = hit.Provider ?? "Web search",
                LinkText = hit.Title,
                SurroundingContext = hit.Snippet,
                DiscoveryReasonCode = "web_search_hit",
                IsIndustryRelevant = true,
                PriorityBoost = 20
            });
        }
    }

    private static ProspectSourceContext BuildSourceContext(UrlDiscoveryContext context)
    {
        var industries = new List<string>();
        industries.AddRange(context.Industries);
        industries.AddRange(context.IndustrySynonyms);
        return new ProspectSourceContext
        {
            ProjectId = context.ProjectId,
            ResearchRunId = context.ResearchRunId,
            Countries = context.Countries.ToList(),
            Industries = industries,
            Locations = context.Countries.ToList()
        };
    }
}
